using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentifierSync
{
    public class ConfirmSync
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        private bool _IsCorpIDSyncEnabled;
        private int _SyncIntervalHours;
        private int _MaxCorpIDsAllowed;

        public ConfirmSync(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<ConfirmSync>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;
        }

        public void GetEnvironmentVariables()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            //
            // Get CorpID sync flag
            //
            _IsCorpIDSyncEnabled = false;
            bool result = bool.TryParse(Environment.GetEnvironmentVariable("EnableCorpIDSync"), out _IsCorpIDSyncEnabled);
            if (!result)
            {
                _logger.DSLogError("EnableCorpIDSync not set or not a valid boolean. Defaulting to disabled.", fullMethodName);
            }

            //
            // Get sync interval hours
            //
            _SyncIntervalHours = 0;
            if (!int.TryParse(Environment.GetEnvironmentVariable("SyncIntervalHours"), out _SyncIntervalHours))
            {
                _logger.DSLogError("SyncIntervalHours is not set or not a valid integer. Defaulting to 0.", fullMethodName);
            }
            else
            {
                _logger.DSLogInformation($"Using SyncIntervalHours: {_SyncIntervalHours}.", fullMethodName);
            }

            //
            // Get maximum allowed Corporate ID entries
            //
            _MaxCorpIDsAllowed = 10000;
            string maxCorpIDsString = Environment.GetEnvironmentVariable("MAX_CORPIDS_ALLOWED");
            if (!int.TryParse(maxCorpIDsString, out int max) || max <= 0)
            {
                _logger.DSLogError($"MAX_CORPIDS_ALLOWED is not set or invalid. Using default value: {_MaxCorpIDsAllowed}.", fullMethodName);
            }
            else
            {
                _MaxCorpIDsAllowed = max;
                _logger.DSLogInformation($"Maximum allowed Corporate Identifiers for the tenant is set to: {_MaxCorpIDsAllowed}.", fullMethodName);
            }
        }

        [Function("ConfirmSync")]
        public async Task Run([TimerTrigger("%NewConfirmSyncTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}", fullMethodName);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}", fullMethodName);
            }

            GetEnvironmentVariables();

            //
            //  We don't need to keep going if syncing is disabled
            //
            if (!_IsCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }

            //
            // Validate sync interval. If invalid (0 from failed parse), log and exit.
            //
            if (_SyncIntervalHours <= 0)
            {
                _logger.DSLogError("SyncIntervalHours is not set or invalid. Exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Checking devices last synced over {_SyncIntervalHours} hours ago.", fullMethodName);

            //
            // Only checking devices that are in tags with sync enabled.
            // Get list of tags with sync enabled. If none found, log and exit.
            //
            List<string> tagsWithSyncEnabled = await _dbService.GetSyncingDeviceTags();
            if (tagsWithSyncEnabled.Count == 0)
            {
                _logger.DSLogInformation("No tags with sync enabled found. No work to do. Function is exiting.", fullMethodName);
                return;
            }

            //
            // Get all devices that were synced before the cutoff time, and filter to only those in sync-enabled tags.
            //
            List<Device> candidates = await _dbService.GetSyncedDevicesSyncedBefore(DateTime.UtcNow.AddHours(-_SyncIntervalHours));
            List<Device> devicesToCheck = candidates
                .Where(d => d.Tags.Count > 0 && tagsWithSyncEnabled.Contains(d.Tags[0]))
                .ToList();

            _logger.DSLogInformation($"Found {devicesToCheck.Count} Synced devices in sync-enabled tags to confirm.", fullMethodName);

            //
            //  Track the count of devices in various states for logging
            //
            int countCorpIDsFound = 0;
            int countCorpIDsReAdded = 0;
            int countCorpIDsReAddFailed = 0;

            foreach (Device device in devicesToCheck)
            {
                _logger.DSLogInformation($"-----Confirming corporate identifier for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                // Boolean used to keep track of scenarios
                bool corpIDFound = false;
                bool corpIDReAdded = false;
                bool corpIDReAddFailed = false;

                // Check whether the Corp ID still exists in Graph
                if (!string.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    corpIDFound = await _graphBetaService.CorporateIdentifierExists(device.CorporateIdentityID);
                }
                else
                {
                    _logger.DSLogWarning("Device does not have a CorporateIdentityID stored in DB. Not sure how we got here. Will attempt to re-add.", fullMethodName);
                }

                if (corpIDFound)
                {
                    _logger.DSLogInformation("Corporate Identifier confirmed present.", fullMethodName);
                    countCorpIDsFound++;
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                }
                else
                {
                    // Re-add the missing Corp ID
                    _logger.DSLogInformation("Corporate Identifier not found, re-adding.", fullMethodName);

                    string identifier;
                    if (device.OS == DeviceOS.Windows || device.OS == DeviceOS.Unknown)
                    {
                        string escapedMake = "\"" + device.Make + "\"";
                        string escapedModel = "\"" + device.Model + "\"";
                        identifier = $"{escapedMake},{escapedModel},{device.SerialNumber}";
                    }
                    else
                    {
                        identifier = device.SerialNumber;
                    }

                    try
                    {
                        ImportedDeviceIdentityType corpIDType = CorpIDUtilities.GetCorpIDTypeForOS(device.OS);
                        ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(corpIDType, identifier);

                        device.CorporateIdentityID = deviceIdentity.Id;
                        device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                        device.LastCorpIdentitySync = DateTime.UtcNow;
                        device.Status = DeviceStatus.Synced;
                        device.CorpIDFailureCount = 0;
                        corpIDReAdded = true;
                        countCorpIDsReAdded++;
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Error re-adding corporate identifier for device {device.Make} {device.Model} {device.SerialNumber}: ", ex, fullMethodName);
                        // Reset to Added so AddNewDevices will retry
                        device.CorporateIdentityID = null;

                        // Not checking against max since this should only ever be the first failure
                        device.CorpIDFailureCount++;
                        device.Status = DeviceStatus.Added;
                        corpIDReAddFailed = true;
                        countCorpIDsReAddFailed++;
                    }
                }

                //
                //  Update device entry
                //
                try
                {
                    await _dbService.UpdateDevice(device);
                    _logger.DSLogInformation($"Updated device {device.Make} {device.Model} {device.SerialNumber} in Delegation Station.", fullMethodName);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.DSLogWarning($"Device {device.Id} {device.Make} {device.Model} {device.SerialNumber} was deleted during ConfirmSync.", fullMethodName);

                    if (corpIDFound)
                    {
                        // We only updated LastCorpIdentitySync — no CorpID to roll back
                        countCorpIDsFound--;
                    }
                    else if (corpIDReAddFailed)
                    {
                        // The CorpID re-add failed — no CorpID to roll back
                        // Rolling back count to prevent over-releasing CorpIDs (assume taken care of where device was deleted)
                        countCorpIDsReAddFailed--;
                    }
                    else if (corpIDReAdded)
                    {
                        // We re-added a Corp ID but the device is gone — roll back
                        var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                        if (rollbackResult == DeleteCorpIdResult.Error)
                        {
                            _logger.DSLogError($"Failed to roll back Corp ID {device.CorporateIdentityID}. Manual cleanup required.", fullMethodName);
                        }
                        else
                        {
                            // Success or NotFound — Corp ID is confirmed gone from Graph
                            _logger.DSLogInformation($"Rolled back re-added Corp ID {device.CorporateIdentityID}.", fullMethodName);
                        }
                        countCorpIDsReAdded--;
                    }
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    if (corpIDFound)
                    {
                        // We only updated timestamp — no action necessary
                        countCorpIDsFound--;
                        _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} was modified by another function.  " +
                            $"CorpID entry was already detected, so skipping device record update.", fullMethodName);
                    }
                    if (corpIDReAddFailed)
                    {
                        // Since we weren't able to add CorpID, no rollback necessary
                        countCorpIDsReAddFailed--;
                    }
                    if (corpIDReAdded)
                    {
                        // We re-added a Corp ID to Graph but another function modified the device concurrently.
                        // Read fresh state to determine whether rollback is needed.
                        _logger.DSLogWarning($"Device {device.Id} was modified concurrently after Corp ID re-add. Reading fresh state to determine rollback.", fullMethodName);
                        try
                        {
                            Device? freshDevice = await _dbService.GetDevice(device.Id, device.PartitionKey);
                            if (freshDevice == null)
                            {
                                _logger.DSLogWarning($"Device {device.Id} no longer exists. Rolling back re-added Corp ID.", fullMethodName);
                                var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                                if (rollbackResult == DeleteCorpIdResult.Error)
                                {
                                    _logger.DSLogError($"Failed to roll back Corp ID {device.CorporateIdentityID}. Manual cleanup required.", fullMethodName);
                                }
                                else
                                {
                                    // Success or NotFound — Corp ID is confirmed gone from Graph
                                    _logger.DSLogInformation($"Rolled back re-added Corp ID {device.CorporateIdentityID}.", fullMethodName);
                                }
                                countCorpIDsReAdded--;
                            }
                            else if (freshDevice.Status == DeviceStatus.Deleting || freshDevice.Status == DeviceStatus.NotSyncing)
                            {
                                // Device is heading for deletion or is unsyncing — roll back
                                _logger.DSLogWarning($"Device {device.Id} is now {freshDevice.Status}. Rolling back re-added Corp ID.", fullMethodName);
                                var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                                if (rollbackResult == DeleteCorpIdResult.Error)
                                {
                                    _logger.DSLogError($"Failed to roll back Corp ID {device.CorporateIdentityID}. Manual cleanup required.", fullMethodName);
                                }
                                else
                                {
                                    // Success or NotFound — Corp ID is confirmed gone from Graph
                                    _logger.DSLogInformation($"Rolled back re-added Corp ID {device.CorporateIdentityID}.", fullMethodName);
                                }
                                countCorpIDsReAdded--;
                            }
                            else
                            {
                                // Another instance of this function is likely running concurrently —
                                // the Corp ID is valid and the device record will be updated by the other instance.
                                // Do NOT decrement: the Corp ID is still active and tracked in the counter.
                                _logger.DSLogInformation($"Device {device.Id} is {freshDevice.Status}. Corp ID valid, no rollback needed.", fullMethodName);
                            }
                        }
                        catch (Exception readEx)
                        {
                            _logger.DSLogException($"Could not read fresh device {device.Id} to determine rollback. Manual review may be required.", readEx, fullMethodName);
                            countCorpIDsReAdded--;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If we get a different error, there's no clear path of what to do so just log it
                    _logger.DSLogException($"Failed to update device record {device.Make} {device.Model} {device.SerialNumber}. CorporateIdentifier details may be out of sync.", ex, fullMethodName);
                }
            }

            _logger.DSLogInformation($"ConfirmSync completed. Processed {countCorpIDsFound + countCorpIDsReAdded + countCorpIDsReAddFailed} devices: {countCorpIDsFound} found, {countCorpIDsReAdded} re-added, " +
                                     $"{countCorpIDsReAddFailed} failed to re-add.", fullMethodName);

            //
            // Update CorpID counter for failed re-adds
            //
            if (countCorpIDsReAddFailed > 0)
            {
                var capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);
                try
                {
                    await capacityManager.ReleaseCorpIDs(countCorpIDsReAddFailed, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to release {countCorpIDsReAddFailed} CorpID slots for failed re-adds. Manual correction may be required.", ex, fullMethodName);
                }
            }
        }
    }
}

using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentifierSync
{
    public class NewConfirmSync
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        public NewConfirmSync(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<NewConfirmSync>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;
        }

        [Function("NewConfirmSync")]
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

            //
            //  Check whether syncing is enabled and get sync interval from environment variables. If not enabled or invalid, log and exit.
            //
            bool isCorpIDSyncEnabled = false;
            bool result = bool.TryParse(Environment.GetEnvironmentVariable("EnableCorpIDSync", EnvironmentVariableTarget.Process), out isCorpIDSyncEnabled);
            if (!result)
            {
                _logger.DSLogError("EnableCorpIDSync not set or not a valid boolean. Disabling sync.", fullMethodName);
            }
            else if (!isCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("EnableCorpIDSync set to false. Disabling sync.", fullMethodName);
            }

            if (!isCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }


            //
            // Get sync interval hours and calculate cutoff time for devices to check based on last sync time. If invalid, log and exit.
            //
            int intervalHours = 0;
            if (!int.TryParse(Environment.GetEnvironmentVariable("SyncIntervalHours", EnvironmentVariableTarget.Process), out intervalHours))
            {
                _logger.DSLogError("SyncIntervalHours is not set or not a valid integer. Exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Checking devices last synced over {intervalHours} hours ago.", fullMethodName);


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
            // Get all devies that were synced before the cutoff time, and filter to only those in sync-enabled tags.
            //
            List<Device> candidates = await _dbService.GetSyncedDevicesSyncedBefore(DateTime.UtcNow.AddHours(-intervalHours));
            List<Device> devicesToCheck = candidates
                .Where(d => d.Tags.Count > 0 && tagsWithSyncEnabled.Contains(d.Tags[0]))
                .ToList();

            _logger.DSLogInformation($"Found {devicesToCheck.Count} Synced devices in sync-enabled tags to confirm.", fullMethodName);

            //
            //  Track the count of devices in various states for logging
            //
            //int deviceCount = 0;
            int devicesFound = 0;
            int devicesReadded = 0;
            int devicesFailedReAdd = 0;

            foreach (Device device in devicesToCheck)
            {
                _logger.DSLogInformation($"-----Confirming corporate identifier for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                // corpIDFound will be used to track if entry is still present in Intune
                bool corpIDFound = false;

                // Check whether the Corp ID still exists in Graph
                if (!string.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    corpIDFound = await _graphBetaService.CorporateIdentifierExists(device.CorporateIdentityID);
                }

                if (corpIDFound)
                {
                    _logger.DSLogInformation("Corporate Identifier confirmed present.", fullMethodName);
                    devicesFound++;
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
                        devicesReadded++;
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Error re-adding corporate identifier for device {device.Make} {device.Model} {device.SerialNumber}: ", ex, fullMethodName);
                        // Reset to Added so AddNewDevices will retry
                        device.CorporateIdentityID = null;

                        // Not checking against max since this should only ever be the first failure
                        device.CorpIDFailureCount++;
                        device.Status = DeviceStatus.Added;
                        devicesFailedReAdd++;
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
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to update device record {device.Make} {device.Model} {device.SerialNumber} in Delegation Station. CorporateIdentifier details may be out of sync.", ex, fullMethodName);
                }
            }

            _logger.DSLogInformation($"NewConfirmSync completed. Processed {devicesFound + devicesReadded + devicesFailedReAdd} devices: {devicesFound} found, {devicesReadded} re-added, " +
                                     $"{devicesFailedReAdd} failed to re-add.", fullMethodName);

            //
            // Update CorpID counter for failed re-adds
            //
            if (devicesFailedReAdd > 0)
            {
                int totalCap = int.Parse(Environment.GetEnvironmentVariable("CORP_ID_TOTAL_CAP") ?? "320000");
                var capacityManager = new CorpIdCapacityManager(_dbService, _logger, totalCap);
                await capacityManager.ReleaseCorpIDs(devicesFailedReAdd, CancellationToken.None);
            }
        }
    }
}

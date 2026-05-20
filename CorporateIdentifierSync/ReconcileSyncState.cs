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
    public class ReconcileSyncState
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        private bool _IsCorpIDSyncEnabled;
        private int _BatchSize;
        private int _MaxCorpIDsAllowed;

        public ReconcileSyncState(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<ReconcileSyncState>();
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
            // Get batch size for reconciling devices
            //
            _BatchSize = 1000;
            string batchSizeString = Environment.GetEnvironmentVariable("ReconcileSyncBatchSize");
            if (!int.TryParse(batchSizeString, out int bs) || bs <= 0)
            {
                _logger.DSLogWarning($"ReconcileSyncBatchSize is not set or invalid. Using default value: {_BatchSize}.", fullMethodName);
            }
            else
            {
                _BatchSize = bs;
                _logger.DSLogInformation($"Using ReconcileSyncBatchSize: {_BatchSize}.", fullMethodName);
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

        [Function("ReconcileSyncState")]
        public async Task Run([TimerTrigger("%ReconcileSyncStateTriggerTime%")] TimerInfo myTimer)
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
            // Check if syncing is enabled via environment variable before doing any work
            //

            if (!_IsCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled. No work to do. Function is exiting.", fullMethodName);
                return;
            }

            //
            // Remove any devices from CorpID that are synced, but are no longer in a tag set to sync
            //
            int corpIDsRemoved = await RemoveSyncedDevicesInDisabledTagsAsync(_BatchSize);
            _logger.DSLogInformation($"Removed Corp IDs for {corpIDsRemoved} devices in disabled tags.", fullMethodName);

            //
            // Add any devices to CorpID that are not syncing, but are in a tag set to sync - up to the batch size and available capacity
            //
            int corpIDsAdded = await AddNotSyncingDevicesInEnabledTagsAsync(_BatchSize);
            _logger.DSLogInformation($"Added Corp IDs for {corpIDsAdded} devices in enabled tags.", fullMethodName);

            _logger.DSLogInformation("ReconcileSyncState completed.", fullMethodName);
        }

        private async Task<int> RemoveSyncedDevicesInDisabledTagsAsync(int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("--- Section 1: Removing Corp IDs for Synced devices in disabled tags. ---", fullMethodName);

            List<string> tagsWithSyncDisabled = await _dbService.GetNonSyncingDeviceTags();
            _logger.DSLogInformation($"Found {tagsWithSyncDisabled.Count} tags with sync disabled.", fullMethodName);

            int updatedCount = 0;
            int corpIDsRemoved = 0;
            int failureCount = 0;

            List<Device> devicesToUnsync = await _dbService.GetSyncedDevicesInTags(tagsWithSyncDisabled, batchSize);

            _logger.DSLogInformation($"Found {devicesToUnsync.Count} Synced devices in disabled tags.", fullMethodName);

            foreach (Device device in devicesToUnsync)
            {
                _logger.DSLogInformation($"-----Removing Corp ID for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                bool hadCorpID = !string.IsNullOrEmpty(device.CorporateIdentityID);
                bool graphDeleteSucceeded = false;

                if (hadCorpID)
                {
                    try
                    {
                        DeleteCorpIdResult graphDeleteResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                        graphDeleteSucceeded = graphDeleteResult == DeleteCorpIdResult.Success || graphDeleteResult == DeleteCorpIdResult.NotFound;
                        if (graphDeleteSucceeded)
                        {
                            _logger.DSLogInformation($"Removed Corp ID from Graph for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                        }
                        else
                        {
                            _logger.DSLogError($"Error removing Corp ID from Graph for device {device.Id}.", fullMethodName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Error removing Corp ID from Graph for device {device.Id}: ", ex, fullMethodName);
                    }
                }
                else
                {
                    graphDeleteSucceeded = true;
                }

                if (graphDeleteSucceeded)
                {
                    device.Status = DeviceStatus.NotSyncing;
                    device.CorporateIdentityID = string.Empty;
                    device.CorporateIdentity = string.Empty;
                    device.LastCorpIdentitySync = DateTime.UtcNow;

                    try
                    {
                        await _dbService.UpdateDevice(device);
                        updatedCount++;
                        if (hadCorpID)
                        {
                            corpIDsRemoved++;
                        }
                    }
                    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // DeviceDeletion already deleted this device — it owns capacity release
                        _logger.DSLogInformation($"Device {device.Id} was deleted. DeviceDeletion owns capacity release.", fullMethodName);
                    }
                    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                    {
                        // Corp ID was deleted from Graph but another function updated this device concurrently.
                        // Must retry with a fresh read to ensure NotSyncing status is written —
                        // otherwise device remains Synced pointing to a deleted Corp ID.
                        _logger.DSLogWarning($"Device {device.Id} was modified concurrently after Corp ID removal. Retrying status update.", fullMethodName);
                        try
                        {
                            Device? freshDevice = await _dbService.GetDevice(device.Id, device.PartitionKey);
                            if (freshDevice == null || freshDevice.Status == DeviceStatus.Deleting || freshDevice.Status == DeviceStatus.NotSyncing)
                            {
                                // Device was deleted between the 412 and this read — DeviceDeletion owns cleanup
                                _logger.DSLogInformation($"Device {device.Id} was modified and is now in state {freshDevice.Status.ToString()}.  No action necessary as CorpID is already removed.", fullMethodName);
                            }
                            else  // status is Synced, Added or Failed
                            {
                                // If another process re-added a Corp ID (e.g., ConfirmSync), we must delete it before unsync
                                if (!string.IsNullOrEmpty(freshDevice.CorporateIdentityID)
                                    && freshDevice.CorporateIdentityID != device.CorporateIdentityID)
                                {
                                    _logger.DSLogWarning($"Device {device.Id} has a new Corp ID {freshDevice.CorporateIdentityID} (original was {device.CorporateIdentityID}). Deleting new Corp ID from Graph before unsync.", fullMethodName);
                                    var deleteResult = await _graphBetaService.DeleteCorporateIdentifier(freshDevice.CorporateIdentityID);
                                    if (deleteResult != DeleteCorpIdResult.Success && deleteResult != DeleteCorpIdResult.NotFound)
                                    {
                                        _logger.DSLogError($"Failed to delete new Corp ID {freshDevice.CorporateIdentityID} for device {device.Id}. Skipping to avoid orphan.", fullMethodName);
                                        failureCount++;
                                        continue;
                                    }
                                }

                                freshDevice.Status = DeviceStatus.NotSyncing;
                                freshDevice.CorporateIdentityID = string.Empty;
                                freshDevice.CorporateIdentity = string.Empty;
                                freshDevice.LastCorpIdentitySync = DateTime.UtcNow;
                                await _dbService.UpdateDevice(freshDevice);
                                updatedCount++;
                                if (hadCorpID) corpIDsRemoved++;
                            }
                        }
                        catch (Exception retryEx)
                        {
                            _logger.DSLogException($"Retry failed for device {device.Id}. Device may be Synced with a deleted Corp ID — manual review required.", retryEx, fullMethodName);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Don't count toward ReleaseCorpIDs — can't confirm we own the outcome
                        _logger.DSLogException($"Failed to update device {device.Id} after Corp ID removal. Not releasing capacity for this device.", ex, fullMethodName);
                    }
                }
                else
                {
                    failureCount++;
                    _logger.DSLogError($"Corp ID removal from Graph failed for device {device.Id}. Device status not changed.", fullMethodName);
                }
            }

            _logger.DSLogInformation($"Section 1 complete. Updated {updatedCount} devices, removed {corpIDsRemoved} Corp IDs from Graph. Failures: {failureCount}.", fullMethodName);

            if (corpIDsRemoved > 0)
            {
                try
                {
                    //// use object
                    //var counter = await _dbService.GetCorpIDCounter();
                    //if (corpIDsRemoved > counter.CorpIDCount)
                    //{
                    //    _logger.DSLogError($"Drift detected: Attempting to decrement counter by {corpIDsRemoved} but current count is {counter.CorpIDCount}.", fullMethodName);
                    //}
                    //counter.CorpIDCount = Math.Max(0, counter.CorpIDCount - corpIDsRemoved);
                    //await _dbService.SetCorpIDCounter(counter);
                    //_logger.DSLogInformation($"Decremented CorpIDCounter by {corpIDsRemoved} to {counter.CorpIDCount}.", fullMethodName);
                    CorpIdCapacityManager capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);
                    int available = await capacityManager.ReleaseCorpIDs(corpIDsRemoved,CancellationToken.None);

                    _logger.DSLogInformation($"Released {corpIDsRemoved} from Capacity Manager.  {available} CorpIDs remain.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Failed to update CorpIDCounter after Section 1 removals.", ex, fullMethodName);
                }
            }

            return corpIDsRemoved;
        }

        private async Task<int> AddNotSyncingDevicesInEnabledTagsAsync(int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("--- Section 2: Adding Corp IDs for NotSyncing devices in enabled tags. ---", fullMethodName);

            List<string> tagsWithSyncEnabled = await _dbService.GetSyncingDeviceTags();
            _logger.DSLogInformation($"Found {tagsWithSyncEnabled.Count} tags with sync enabled.", fullMethodName);

            if (tagsWithSyncEnabled.Count == 0)
            {
                _logger.DSLogInformation("No tags with sync enabled. Skipping Section 2.", fullMethodName);
                return 0;
            }

            var capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);

            int availableSlots;
            try
            {
                availableSlots = await capacityManager.GetAvailableCorpIDCount(CancellationToken.None);
                _logger.DSLogInformation($"Available Corp ID slots: {availableSlots}.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Exiting.  Failed to retrieve available Corp ID count.", ex, fullMethodName);
                return 0;
            }

            if (availableSlots <= 0)
            {
                _logger.DSLogWarning("No available Corp ID slots. Skipping Section 2.", fullMethodName);
                return 0;
            }

            int effectiveBatchSize = Math.Min(batchSize, availableSlots);
            _logger.DSLogInformation($"Effective batch size for Section 2: {effectiveBatchSize} (min of batchSize {batchSize} and available slots {availableSlots}).", fullMethodName);

            List<Device> devicesToSync = await _dbService.GetNotSyncingDevicesInTags(tagsWithSyncEnabled, effectiveBatchSize);

            _logger.DSLogInformation($"Found {devicesToSync.Count} NotSyncing devices in enabled tags to add.", fullMethodName);


            if (devicesToSync.Count == 0)
            {
                _logger.DSLogInformation("No NotSyncing devices in enabled tags found. Skipping Section 2 processing.", fullMethodName);
                return 0;
            }

            // Reserve slots atomically — returned value may be less than requested if capacity tightened since the availability check
            int actualReserved;
            try
            {
                actualReserved = await capacityManager.ReserveCorpIDs(devicesToSync.Count, CancellationToken.None);
                _logger.DSLogInformation($"Requested {devicesToSync.Count} slots, reserved {actualReserved}.", fullMethodName);
            }
            catch(Exception ex)
            {
                _logger.DSLogException("Exiting. Failed to reserve Corp ID slots.", ex, fullMethodName);
                return 0;
            }

            if (actualReserved == 0)
            {
                _logger.DSLogWarning("No Corp ID slots could be reserved.", fullMethodName);
                return 0;
            }

            // Trim the work list if fewer slots were granted than devices found
            if (actualReserved < devicesToSync.Count)
            {
                _logger.DSLogInformation($"Trimming device list from {devicesToSync.Count} to {actualReserved} due to available capacity.", fullMethodName);
                devicesToSync = devicesToSync.Take(actualReserved).ToList();
            }

            int addedCount = 0;

            foreach (Device device in devicesToSync)
            {
                _logger.DSLogInformation($"-----Adding Corp ID for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                if (device.OS == null)
                {
                    device.OS = DeviceOS.Unknown;
                }

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
                    device.Status = DeviceStatus.Synced;
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                    device.CorpIDFailureCount = 0;
                    addedCount++;

                    _logger.DSLogInformation($"Added Corp ID for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Error adding Corp ID for device {device.Id}: ", ex, fullMethodName);
                    // Reset to Added so AddNewDevices will retry
                    device.CorporateIdentityID = string.Empty;
                    device.Status = DeviceStatus.Added;

                    //not checking against max here since this should only ever be the first failure
                    device.CorpIDFailureCount++;
                    device.LastCorpIdentitySync = DateTime.MinValue;
                }

                bool graphAddSucceeded = !string.IsNullOrEmpty(device.CorporateIdentityID);

                try
                {
                    await _dbService.UpdateDevice(device);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.DSLogWarning($"Device {device.Id} was deleted during processing.", fullMethodName);

                    if (graphAddSucceeded)
                    {
                        // Corp ID was added to Graph but device is gone — roll back
                        var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                        if (rollbackResult == DeleteCorpIdResult.Error)
                        {
                            _logger.DSLogError($"Failed to roll back Corp ID {device.CorporateIdentityID}. Manual cleanup required.", fullMethodName);
                        }
                        else
                        {
                            // Success or NotFound — Corp ID is confirmed gone from Graph
                            _logger.DSLogInformation($"Rolled back Corp ID {device.CorporateIdentityID} from Graph.", fullMethodName);
                        }
                        addedCount--; // Don't count toward CommitCorpIDCount
                    }
                    // If Graph add failed, nothing to roll back — device is gone anyway
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    // Device was modified concurrently between our read and our write-back.
                    _logger.DSLogWarning($"Device {device.Id} was modified concurrently during Corp ID add. Reconciling.", fullMethodName);

                    if (!graphAddSucceeded)
                    {
                        // Nothing was added to Graph — nothing to reconcile or roll back.
                        // The concurrent writer's state stands. Don't count toward capacity.
                        _logger.DSLogInformation($"Graph add did not succeed for device {device.Id}; no rollback needed.", fullMethodName);
                        continue;
                    }

                    // We did add a Corp ID to Graph. Re-read the device to decide what to do.
                    Device? freshDevice = null;
                    try
                    {
                        freshDevice = await _dbService.GetDevice(device.Id, device.PartitionKey);
                    }
                    catch (Exception readEx)
                    {
                        _logger.DSLogException($"Failed to re-read device {device.Id} after 412. Corp ID {device.CorporateIdentityID} may be orphaned — manual review required.", readEx, fullMethodName);
                        continue;
                    }

                    // Device gone or marked for deletion → roll back the Corp ID we just created.
                    if (freshDevice == null || freshDevice.Status == DeviceStatus.Deleting)
                    {
                        string state = freshDevice == null ? "deleted" : "Deleting";
                        _logger.DSLogInformation($"Device {device.Id} is {state} after 412. Rolling back Corp ID {device.CorporateIdentityID}.", fullMethodName);

                        var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                        if (rollbackResult == DeleteCorpIdResult.Error)
                        {
                            _logger.DSLogError($"Failed to roll back Corp ID {device.CorporateIdentityID} for device {device.Id}. Manual cleanup required.", fullMethodName);
                        }
                        else
                        {
                            _logger.DSLogInformation($"Rolled back Corp ID {device.CorporateIdentityID} from Graph.", fullMethodName);
                            addedCount--;
                        }
                    }

                    // Device was set NotSyncing concurrently (tag disabled, or unsynced by another process).
                    // The Corp ID we added is now an orphan with respect to system intent — roll it back.
                    // We deliberately do NOT overwrite the fresh device: its NotSyncing state is authoritative.
                    else if (freshDevice.Status == DeviceStatus.NotSyncing)
                    {
                        _logger.DSLogWarning($"Device {device.Id} is NotSyncing after 412. Rolling back newly added Corp ID {device.CorporateIdentityID} to honor current sync intent.", fullMethodName);

                        var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                        if (rollbackResult == DeleteCorpIdResult.Error)
                        {
                            _logger.DSLogError($"Failed to roll back Corp ID {device.CorporateIdentityID} for NotSyncing device {device.Id}. Manual cleanup required.", fullMethodName);
                        }
                        else
                        {
                            _logger.DSLogInformation($"Rolled back Corp ID {device.CorporateIdentityID} from Graph for NotSyncing device {device.Id}.", fullMethodName);
                            addedCount--;
                        }
                        continue;
                    }

                    // Device is already Synced — another process (e.g., ConfirmSync) won the race
                    // and successfully wrote the device. Since Graph rejects duplicate identifiers,
                    // the existing Corp ID on the fresh device is effectively ours. Nothing to roll back,
                    // and nothing to write. The slot we reserved is legitimately consumed.
                    else if (freshDevice.Status == DeviceStatus.Synced)
                    {
                        _logger.DSLogInformation($"Device {device.Id} is already Synced after 412 (Corp ID {freshDevice.CorporateIdentityID}). No rollback or update needed.", fullMethodName);
                        addedCount--;
                        continue;
                    }

                    // Status is Added or Failed — apply our Corp ID details onto the fresh device and retry the write,
                    // but only if our Corp ID is still present in Graph. If something concurrent removed it,
                    // skip the device update to avoid leaving a Synced device pointing at a missing Corp ID.
                    else
                    {
                        bool corpIdStillPresent;
                        try
                        {
                            corpIdStillPresent = await _graphBetaService.CorporateIdentifierExists(device.CorporateIdentityID);
                        }
                        catch (Exception existsEx)
                        {
                            _logger.DSLogException($"Failed to verify Corp ID {device.CorporateIdentityID} presence in Graph for device {device.Id}. Skipping write-back — manual review required.", existsEx, fullMethodName);
                            addedCount--;
                            continue;
                        }

                        if (!corpIdStillPresent)
                        {
                            _logger.DSLogWarning($"Corp ID {device.CorporateIdentityID} for device {device.Id} is no longer present in Graph. Skipping device update; fresh state (status {freshDevice.Status}) is left untouched.", fullMethodName);
                            addedCount--;
                            continue;
                        }

                        freshDevice.CorporateIdentityID = device.CorporateIdentityID;
                        freshDevice.CorporateIdentity = device.CorporateIdentity;
                        freshDevice.Status = DeviceStatus.Synced;
                        freshDevice.LastCorpIdentitySync = device.LastCorpIdentitySync;
                        freshDevice.CorpIDFailureCount = 0;

                        try
                        {
                            await _dbService.UpdateDevice(freshDevice);
                            _logger.DSLogInformation($"Applied Corp ID {device.CorporateIdentityID} to fresh device {device.Id}.", fullMethodName);
                        }
                        catch (Exception retryEx)
                        {
                            _logger.DSLogException($"Retry update failed for device {device.Id} after 412. Corp ID {device.CorporateIdentityID} may be orphaned — manual review required.", retryEx, fullMethodName);
                        }
                    }
                }
            }

            // Commit: moves actualReserved out of CorpIDReserve and records addedCount in CorpIDCount.
            // Any slots reserved but not added (failures) are released automatically by CommitCorpIDCount.
            try
            {
                int available = await capacityManager.CommitCorpIDCount(actualReserved, addedCount, CancellationToken.None);
                _logger.DSLogInformation($"Section 2 complete. Committed {addedCount} Corp IDs. Released {actualReserved - addedCount} unused reserved slots. {available} CorpIDs now available.", fullMethodName);
            }
            catch(Exception ex)
            {
                _logger.DSLogException("Failed to commit Corp ID count after additions. Manual resolution may be required.", ex, fullMethodName);
            }

            return addedCount;
        }
    }
}

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
    public class ReconcileSyncState
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        public ReconcileSyncState(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<ReconcileSyncState>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;
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

            //
            // Check if syncing is enabled via environment variable before doing any work
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
                _logger.DSLogInformation("Syncing not enabled. No work to do. Function is exiting.", fullMethodName);
                return;
            }

            //
            // Determine batch size for processing devices, with a default and logging for visibility
            //
            int batchSize = 1000;
            string batchSizeString = Environment.GetEnvironmentVariable("ReconcileSyncBatchSize", EnvironmentVariableTarget.Process);
            if (!int.TryParse(batchSizeString, out int bs) || bs <= 0)
            {
                _logger.DSLogWarning($"ReconcileSyncBatchSize is not set or invalid. Using default value: {batchSize}.", fullMethodName);
            }
            else
            {
                batchSize = bs;
                _logger.DSLogInformation($"Using ReconcileSyncBatchSize: {batchSize}.", fullMethodName);
            }

            //
            // Remove any devices from CorpID that are synced, but are no longer in a tag set to sync
            //
            int corpIDsRemoved = await RemoveSyncedDevicesInDisabledTagsAsync(batchSize);
            _logger.DSLogInformation($"Removed Corp IDs for {corpIDsRemoved} devices in disabled tags.", fullMethodName);

            //
            // Add any devices to CorpID that are not syncing, but are in a tag set to sync - up to the batch size and available capacity
            //
            int corpIDsAdded = await AddNotSyncingDevicesInEnabledTagsAsync(batchSize);
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
            int corpIDsRemovedFromGraph = 0;
            int failureCount = 0;

            List<Device> devicesToUnsync = await _dbService.GetSyncedDevicesInTags(tagsWithSyncDisabled, batchSize);

            _logger.DSLogInformation($"Found {devicesToUnsync.Count} Synced devices in disabled tags.", fullMethodName);

            foreach (Device device in devicesToUnsync)
            {
                _logger.DSLogInformation($"-----Removing Corp ID for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                bool deleted = false;

                if (!string.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    try
                    {
                        deleted = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                        if (deleted)
                        {
                            corpIDsRemovedFromGraph++;
                            _logger.DSLogInformation($"Removed Corp ID from Graph for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Error removing Corp ID from Graph for device {device.Id}: ", ex, fullMethodName);
                    }
                }
                else
                {
                    // No Corp ID in Graph to remove — still update the device status
                    deleted = true;
                }

                if (deleted)
                {
                    device.Status = DeviceStatus.NotSyncing;
                    device.CorporateIdentityID = string.Empty;
                    device.CorporateIdentity = string.Empty;
                    device.LastCorpIdentitySync = DateTime.UtcNow;

                    try
                    {
                        await _dbService.UpdateDevice(device);
                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Failed to update device {device.Id} in Delegation Station after Corp ID removal.", ex, fullMethodName);
                    }
                }
                else
                {
                    failureCount++;
                    _logger.DSLogError($"Corp ID removal from Graph failed for device {device.Id}. Device status not changed.", fullMethodName);
                }
            }

            _logger.DSLogInformation($"Section 1 complete. Updated {updatedCount} devices, removed {corpIDsRemovedFromGraph} Corp IDs from Graph. Failures: {failureCount}.", fullMethodName);

            if (corpIDsRemovedFromGraph > 0)
            {
                try
                {
                    // use object
                    var counter = await _dbService.GetCorpIDCounter();
                    if (corpIDsRemovedFromGraph > counter.CorpIDCount)
                    {
                        _logger.DSLogError($"Drift detected: Attempting to decrement counter by {corpIDsRemovedFromGraph} but current count is {counter.CorpIDCount}.", fullMethodName);
                    }
                    counter.CorpIDCount = Math.Max(0, counter.CorpIDCount - corpIDsRemovedFromGraph);
                    await _dbService.SetCorpIDCounter(counter);
                    _logger.DSLogInformation($"Decremented CorpIDCounter by {corpIDsRemovedFromGraph} to {counter.CorpIDCount}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Failed to update CorpIDCounter after Section 1 removals.", ex, fullMethodName);
                }
            }

            return corpIDsRemovedFromGraph;
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

            int totalCap = int.Parse(Environment.GetEnvironmentVariable("CORP_ID_TOTAL_CAP") ?? "320000");
            var capacityManager = new CorpIdCapacityManager(_dbService, _logger, totalCap);

            int availableSlots = await capacityManager.GetAvailableCorpIDCount(CancellationToken.None);
            _logger.DSLogInformation($"Available Corp ID slots after Section 1: {availableSlots}.", fullMethodName);

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
            int actualReserved = await capacityManager.ReserveCorpIDs(devicesToSync.Count, CancellationToken.None);
            _logger.DSLogInformation($"Requested {devicesToSync.Count} slots, reserved {actualReserved}.", fullMethodName);

            if (actualReserved == 0)
            {
                _logger.DSLogWarning("No Corp ID slots could be reserved. Skipping Section 2.", fullMethodName);
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
                    device.CorporateIdentityID = null;
                    device.Status = DeviceStatus.Added;

                    //not checking against max here since this should only ever be the first failure
                    device.CorpIDFailureCount++;
                    device.LastCorpIdentitySync = DateTime.MinValue;
                }

                try
                {
                    await _dbService.UpdateDevice(device);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to update device {device.Make} {device.Model} {device.SerialNumber} in Delegation Station after Corp ID add.", ex, fullMethodName);
                }
            }

            // Commit: moves actualReserved out of CorpIDReserve and records addedCount in CorpIDCount.
            // Any slots reserved but not added (failures) are released automatically by CommitCorpIDCount.
            await capacityManager.CommitCorpIDCount(actualReserved, addedCount, CancellationToken.None);
            _logger.DSLogInformation($"Section 2 complete. Committed {addedCount} Corp IDs. Released {actualReserved - addedCount} unused reserved slots.", fullMethodName);

            return addedCount;
        }
    }
}

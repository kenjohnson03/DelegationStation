using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentifierSync
{
    public class AddNewDevices
    {
        private readonly ILogger<AddNewDevices> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        public AddNewDevices(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<AddNewDevices>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;

        }


        [Function("AddNewDevices")]
        public async Task Run([TimerTrigger("%AddDevicesTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation("Next timer schedule at: " + myTimer.ScheduleStatus.Next, fullMethodName);
            }

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

            int batchSize = 5000;
            string batchSizeString = Environment.GetEnvironmentVariable("AddDeviceBatchSize", EnvironmentVariableTarget.Process);
            if (!int.TryParse(batchSizeString, out int bs) || bs <= 0)
            {
                _logger.DSLogError($"BatchSize is not set or invalid. Using default value: {batchSize}.", fullMethodName);
            }
            else
            {
                batchSize = bs;
                _logger.DSLogInformation($"Using BatchSize: {batchSize}.", fullMethodName);
            }

            int totalCap = int.Parse(Environment.GetEnvironmentVariable("CORP_ID_TOTAL_CAP") ?? "320000");
            var capacityManager = new CorpIdCapacityManager(_dbService, _logger, totalCap);

            int availableCorpIDs = await capacityManager.GetAvailableCorpIDCount(CancellationToken.None);
            _logger.DSLogInformation($"Available Corporate ID slots: {availableCorpIDs}.", fullMethodName);

            if (availableCorpIDs <= 0)
            {
                _logger.DSLogWarning("No available Corporate ID slots. No work to do. Function is exiting.", fullMethodName);
                return;
            }

            int effectiveBatchSize = Math.Min(batchSize, availableCorpIDs);
            _logger.DSLogInformation($"Effective batch size (min of BatchSize {batchSize} and available slots {availableCorpIDs}): {effectiveBatchSize}.", fullMethodName);

            //Reserve Corporate ID slots for this batch
            int reservedSlots = await capacityManager.ReserveCorpIDs(effectiveBatchSize, CancellationToken.None);
            if (reservedSlots == 0)
            {
                _logger.DSLogWarning("No available Corporate ID slots at reservation time.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Successfully reserved {reservedSlots} Corporate ID slots for this batch.", fullMethodName);
            if(effectiveBatchSize!= reservedSlots)
            {
                effectiveBatchSize = reservedSlots;
            }

            // Get All Devices without Corporate Identifier values or fields
            List<Device> devicesToMigrate = new List<Device>();
            try
            {
                devicesToMigrate = await _dbService.GetAddedDevices(effectiveBatchSize);
                _logger.DSLogInformation($"Found {devicesToMigrate.Count} devices to migrate.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogError($"Exiting. Error getting devices to migrate: {ex.Message}", fullMethodName);
                return;
            }


            // For each device set blank Corporate Identifier values
            int deviceCount = 0;
            int devicesSynced = 0;
            int totalDevices = devicesToMigrate.Count;
            foreach (Device device in devicesToMigrate)
            {

                // Set OS if not set
                if(device.OS == null)
                {
                    device.OS = DeviceOS.Unknown;
                }


                // Get Device Tag sync setting
                bool isCorpIDSyncEnabledForTag = false;
                if (device.Tags.Count == 0)
                {
                    _logger.DSLogError($"Device {device.Make} {device.Model} {device.SerialNumber} has no tags. Skipping.", fullMethodName);
                    continue;
                }
                else
                {
                    DeviceTag tag = await _dbService.GetDeviceTag(device.Tags[0]);
                    isCorpIDSyncEnabledForTag = tag.CorpIDSyncEnabled;
                }


                // Add the Corporate Identifier
                try
                {
                    if (isCorpIDSyncEnabledForTag)
                    {
                        _logger.DSLogInformation($"-----Adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                        string identifier = "";
                        if ((device.OS == DeviceOS.Windows) || (device.OS == DeviceOS.Unknown))
                        {

                            // Putting make and model in quotes to handle commas
                            string escapedMake = "\"" + device.Make + "\"";
                            string escapedModel = "\"" + device.Model + "\"";
                            identifier = $"{escapedMake},{escapedModel},{device.SerialNumber}";
                        }
                        else
                        {
                            identifier = device.SerialNumber;
                        }

                        ImportedDeviceIdentityType corpIDType = CorpIDUtilities.GetCorpIDTypeForOS(device.OS);
                        ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(corpIDType, identifier);
                        devicesSynced++;

                        // Set the Corporate Identifier values
                        device.CorporateIdentityID = deviceIdentity.Id;
                        device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                        device.Status = DeviceStatus.Synced;
                        device.LastCorpIdentitySync = DateTime.UtcNow;

                    }
                    else
                    {
                        _logger.DSLogInformation($"Device {device.Make} {device.Model} {device.SerialNumber} tag {device.Tags[0]} is not enabled for sync.", fullMethodName);
                        device.Status = DeviceStatus.NotSyncing;
                        device.LastCorpIdentitySync = DateTime.UtcNow;
                    }

                    // Update the DB entry with the new Corporate Identifier info
                    await _dbService.UpdateDevice(device);
                    deviceCount++;

                    _logger.DSLogInformation($"Successfully added Corporate Identifier for device {deviceCount}/{totalDevices}:  {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Error adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}: ", ex, fullMethodName);
                }
            }

            // Release any unused reserved Corporate ID slots
            int nowAvailable = await capacityManager.CommitCorpIDCount(effectiveBatchSize, devicesSynced, CancellationToken.None);
            _logger.DSLogInformation($"Successfully migrated {deviceCount} devices. {devicesSynced} devices were synced with Corporate Identifiers.", fullMethodName);
            if (nowAvailable > 0)
            {
                _logger.DSLogInformation($"Current available Corporate ID slots: {nowAvailable}.", fullMethodName);
            }
            else
            {
                _logger.DSLogWarning($"Current available Corporate ID slots: {nowAvailable}.", fullMethodName);
            }
        }
    }
}

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

        private bool _IsCorpIDSyncEnabled;
        private int _BatchSize;
        private int _MaxCorpIDsAllowed;
        private int _MaxCorpIDRetries;

        public AddNewDevices(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<AddNewDevices>();
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
                _logger.DSLogError("EnableCorpIDSync not set or not a valid boolean.  Defaulting to disabled.", fullMethodName);
            }

            //
            // Get batch size for adding new devcies
            //
            _BatchSize = 5000;
            string batchSizeString = Environment.GetEnvironmentVariable("AddDeviceBatchSize");
            if (!int.TryParse(batchSizeString, out int bs) || bs <= 0)
            {
                _logger.DSLogError($"BatchSize is not set or invalid. Using default value: {_BatchSize}.", fullMethodName);
            }
            else
            {
                _BatchSize = bs;
                _logger.DSLogInformation($"Using BatchSize: {_BatchSize}.", fullMethodName);
            }

            //
            // Getting Max Allowed Corporate ID entries
            //
            _MaxCorpIDsAllowed = 10000;
            string maxCorpIDsString = Environment.GetEnvironmentVariable("MAX_CORPIDS_ALLOWED");
            if(!int.TryParse(maxCorpIDsString, out int max) || max <= 0)
            {
                _logger.DSLogError($"Max Corp IDS Allowed is not set or invalid.  Using default value: {_MaxCorpIDsAllowed}.", fullMethodName);
            }
            else
            {
                _MaxCorpIDsAllowed = max;
                _logger.DSLogInformation($"Maximum allowed Corporate Identifers for the tenant is set to: {_MaxCorpIDsAllowed}", fullMethodName);
            }

            //
            // Set max times we'll retry adding a device to Corporate Identifiers before marking it as Failed
            //
            _MaxCorpIDRetries = 10;
            string maxRetriesString = Environment.GetEnvironmentVariable("MAX_CORPID_RETRIES");
            if (!int.TryParse(maxRetriesString, out int retries) || retries <= 0)
            {
                _logger.DSLogError($"MAX_CORPID_RETRIES is not set or invalid. Using default value: {_MaxCorpIDRetries}.", fullMethodName);
            }
            else
            {
                _MaxCorpIDRetries = retries;
                _logger.DSLogInformation($"Max Corporate Identifier retries before marking device as Failed: {_MaxCorpIDRetries}.", fullMethodName);
            }
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

            GetEnvironmentVariables();

            //
            //  We don't need to keep going if either sync is disabled
            //
            if (!_IsCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }


            //
            // First process all devices added that aren't to be synced
            //
            List<string> nonSyncingTagIDs = await _dbService.GetNonSyncingDeviceTags();
            List<Device> notSyncingDevices = await _dbService.GetAddedDevicesNotSyncing(nonSyncingTagIDs, _BatchSize);
            _logger.DSLogInformation("Processing devices in tags that aren't set to sync to CorpIDs first", fullMethodName);
            foreach (Device device in notSyncingDevices)
            {
                try
                {
                    _logger.DSLogInformation($"Device {device.Make} {device.Model} {device.SerialNumber} tag {device.Tags[0]} is not enabled for sync.", fullMethodName);
                    device.Status = DeviceStatus.NotSyncing;
                    device.LastCorpIdentitySync = DateTime.UtcNow;

                    // Update the DB entry with the new Corporate Identifier info
                    await _dbService.UpdateDevice(device);

                }
                catch (Exception dbEx)
                {
                    _logger.DSLogException($"Device entry not updated for non-syncing device (should be retried next run):  {device.Make} {device.Model} {device.SerialNumber}", dbEx, fullMethodName);
                }
            }

            //
            // Now process devices to sync (which will be limited by batch settings)
            // CorpID Capacity Manager will be used to ensure we stay under configured limit
            //
            var capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);

            int availableCorpIDs = await capacityManager.GetAvailableCorpIDCount(CancellationToken.None);
            _logger.DSLogInformation($"Available Corporate ID slots: {availableCorpIDs}.", fullMethodName);


            // If we're out of CorpIds, it's time to exit
            // IMPORTANT:  Any devices not processed because we're out of CorpIDs won't get counted as retries.  Do we like this?
            if (availableCorpIDs <= 0)
            {
                _logger.DSLogWarning("No available Corporate ID slots. Function is exiting.", fullMethodName);
                return;
            }

            int requestSize = Math.Min(_BatchSize, availableCorpIDs);
            _logger.DSLogInformation($"Reserving {requestSize} CorporateID entries", fullMethodName);

            //Reserve Corporate ID slots for this batch
            int reservedSlots = await capacityManager.ReserveCorpIDs(requestSize, CancellationToken.None);
            if (reservedSlots == 0)
            {
                _logger.DSLogWarning("No available Corporate ID slots at reservation time.  Function is exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Successfully reserved {reservedSlots} Corporate ID slots for this batch.", fullMethodName);

            // Get All Devices without Corporate Identifier values or fields
            List<Device> devicesToMigrate = new List<Device>();
            List<string> syncingTagIDs = await _dbService.GetSyncingDeviceTags();
            try
            {
                devicesToMigrate = await _dbService.GetAddedDevicesToSync(syncingTagIDs, reservedSlots);
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
                        device.CorpIDFailureCount = 0;

                    }
                    else
                    {
                        _logger.DSLogInformation($"Device {device.Make} {device.Model} {device.SerialNumber} tag {device.Tags[0]} is not enabled for sync.", fullMethodName);
                        device.Status = DeviceStatus.NotSyncing;
                        device.LastCorpIdentitySync = DateTime.UtcNow;
                    }

                    deviceCount++;

                    _logger.DSLogInformation($"Successfully added Corporate Identifier for device {deviceCount}/{totalDevices}:  {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Error adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}: ", ex, fullMethodName);
                    device.CorpIDFailureCount++;
                    if(device.CorpIDFailureCount > _MaxCorpIDRetries)
                    {
                        _logger.DSLogError($"Device {device.Make} {device.Model} {device.SerialNumber} has exceeded max Corporate Identifier retries. Marking as Failed.", fullMethodName);
                        device.Status = DeviceStatus.Failed;

                    }
                    else
                    {
                        _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} has failed to sync Corporate Identifier {device.CorpIDFailureCount} times. It will be retried in the next sync cycle.", fullMethodName);
                }

                try
                {
                    // Update the DB entry with the new Corporate Identifier info
                    await _dbService.UpdateDevice(device);

                }
                catch(Exception dbEx)
                {
                    _logger.DSLogException($"Device entry not updated - CorpIDStatus may not be in sync:  {device.Make} {device.Model} {device.SerialNumber}", dbEx, fullMethodName);
                }
            }

            // Release any unused reserved Corporate ID slots
            int nowAvailable = await capacityManager.CommitCorpIDCount(reservedSlots, devicesSynced, CancellationToken.None);
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

}}

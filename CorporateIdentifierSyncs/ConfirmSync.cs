using CorporateIdentifierSync.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;

namespace CorporateIdentifierSync
{
    public class ConfirmSync
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        public ConfirmSync(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<ConfirmSync>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;
        }

        [Function("ConfirmSync")]
        public async Task Run([TimerTrigger("%ConfirmSyncTriggerTime%")] TimerInfo myTimer)
        {

            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}", fullMethodName);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}", fullMethodName);
            }

            int deviceCount = 0;
            int intervalHours = 0;
            bool result = int.TryParse(Environment.GetEnvironmentVariable("SyncIntervalHours", EnvironmentVariableTarget.Process), out intervalHours);
            if (!result)
            {
                _logger.DSLogError("SyncIntervalHours is not set or not a valid integer. Exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Checking devices last checked over {intervalHours} hours ago.", fullMethodName);


            // Get all devices with sync date older than X
            List<Device> devicesToCheck = await _dbService.GetDevicesSyncedBefore(DateTime.UtcNow.AddHours(-intervalHours));
            _logger.DSLogInformation($"Found {devicesToCheck.Count} devices to check.", fullMethodName);

            foreach (Device device in devicesToCheck)
            {
                _logger.DSLogInformation($"-----Confirming corporate identifier is synced for {device.Make} {device.Model} {device.SerialNumber} -----", fullMethodName);

                // Query to see if device is still in CorporateIdentifiers
                var corpIDFoundOrUpdated = false;
                if (!String.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    corpIDFoundOrUpdated = await _graphBetaService.CorporateIdentifierExists(device.CorporateIdentityID);
                }

                // If not found, add it back
                if (!corpIDFoundOrUpdated)
                {
                    _logger.DSLogInformation("Corporate Identifier not found, adding back to CorporateIdentifiers", fullMethodName);

                    string identifier = $"{device.Make},{device.Model},{device.SerialNumber}";
                    try
                    {
                        ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(identifier);
                        device.CorporateIdentityID = deviceIdentity.Id;
                        device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                        corpIDFoundOrUpdated = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogError($"Error adding corporate identifier for device {device.Id}: {ex.Message}", fullMethodName);
                    }

                }
                else
                {
                    _logger.DSLogInformation("Corporate Identifier found, updating sync date", fullMethodName);
                }

                if (corpIDFoundOrUpdated)
                {
                    // Update the sync date and status
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                    device.Status = Device.DeviceStatus.Synced;
                    _logger.DSLogInformation($"New sync date: {device.LastCorpIdentitySync}", fullMethodName);

                    // Update device entry in DS
                    try
                    {
                        await _dbService.UpdateDevice(device);
                        deviceCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogError($"Error updating device {device.Id}: {ex.Message}", fullMethodName);
                    }
                }

            } 

            _logger.LogInformation($"Successfully updated {deviceCount} devices.", fullMethodName);
        }
    }
}

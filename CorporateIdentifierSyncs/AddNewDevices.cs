using CorporateIdentifierSync.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;

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
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation("Next timer schedule at: " + myTimer.ScheduleStatus.Next, fullMethodName);
            }

            // Get All Devices without Corporate Identifier values or fields
            List<Device> devicesToMigrate = new List<Device>();
            try
            {
                devicesToMigrate = await _dbService.GetDevicesWithoutCorpIdentity();
                _logger.DSLogInformation($"Found {devicesToMigrate.Count} devices to migrate.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogError($"Exiting. Error getting devices to migrate: {ex.Message}", fullMethodName);
                return;
            }


            // For each device set blank Corporate Identifier values
            int deviceCount = 0;
            foreach (Device device in devicesToMigrate)
            {
                _logger.DSLogInformation($"-----Adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);
                string identifier = $"{device.Make},{device.Model},{device.SerialNumber}";

                // Add the Corporate Identifier
                try
                {
                    ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(identifier);

                    // Set the Corporate Identifier values
                    device.CorporateIdentityID = deviceIdentity.Id;
                    device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                    device.Status = Device.DeviceStatus.Synced;
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                    device.CorporateIdentityType = "manufacturerModelSerial";

                    // Update the DB entry with the new Corporate Identifier info
                    await _dbService.UpdateDevice(device);
                    deviceCount++;

                    _logger.DSLogInformation($"Successfully added Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogError($"Error adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}: {ex.Message}", fullMethodName);
                }
            }

            _logger.DSLogInformation($"Successfully migrated {deviceCount} devices.", fullMethodName);
        }
    }
}

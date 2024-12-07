using CorporateIdentiferSync.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentiferSync
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
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Admin, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Get All Devices without Corporate Identifier values or fields
            List<Device> devicesToMigrate = await _dbService.GetDevicesWithoutCorpIdentity();
            _logger.LogInformation($"Found {devicesToMigrate.Count} devices to migrate.");


            // For each device set blank Corporate Identifier values
            int deviceCount = 0;
            foreach (Device device in devicesToMigrate)
            {
                string identifier = $"{device.Make},{device.Model},{device.SerialNumber}";

                // Add the Corporate Identifier
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
            }

            return new OkObjectResult($"Successfully migrated {deviceCount} devices.");
        }
    }
}

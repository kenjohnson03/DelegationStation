using CorporateIdentiferSync.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;

namespace CorporateIdentiferSync
{
    public class DeviceDeletion
    {
        private readonly ILogger<DeviceDeletion> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphService _graphService;
        private readonly IGraphBetaService _graphBetaService;

        public DeviceDeletion(ILogger<DeviceDeletion> logger, ICosmosDbService dbService, IGraphService graphService, IGraphBetaService graphBetaService)
        {
            _logger = logger;
            _dbService = dbService;
            _graphService = graphService;
            _graphBetaService = graphBetaService;
        }

        [Function("DeviceDeletion")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("C# HTTP trigger function processed a request.", fullMethodName);

            List<Device> devicesToDelete = await _dbService.GetDevicesMarkedForDeletion();
            _logger.DSLogInformation($"Found {devicesToDelete.Count} devices to delete.", fullMethodName);


            // For each set blank Corporate Identifier values
            int deviceCount = 0;
            foreach (Device device in devicesToDelete)
            {
                _logger.DSLogInformation($"Deleting device {device.Id}.", fullMethodName);


                // Delete from Managed Devices
                bool delManagedDevice = false;
                ManagedDevice managedDevice = null;
                try
                {
                    managedDevice = await _graphService.GetManagedDevice(device.Make, device.Model, device.SerialNumber);

                    if (managedDevice != null)
                    {
                        _logger.DSLogInformation($"Found managed device {managedDevice.Id} in Intune that matches device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                        delManagedDevice = await _graphService.DeleteManagedDevice(managedDevice.Id);
                    }
                    else
                    {
                        // Setting as deleted since it's not present
                        delManagedDevice = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.DSLogError($"Unable to delete managed device:  {device.Make} {device.Model} {device.SerialNumber}", fullMethodName);
                    delManagedDevice = false;
                }

                if (delManagedDevice)
                {
                    // Delete from Corporate Identifiers
                    bool delCorpID = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);

                    // Delete from Delegation Station
                    if (delCorpID)
                    {
                        await _dbService.DeleteDevice(device);
                        deviceCount++;
                    }
                    else
                    {
                        _logger.DSLogError($"Deletion from Corporate Identifiers failed for device {device.Id}.  Not deleting from Delegation Station", fullMethodName);
                    }
                }
                else
                {
                    _logger.DSLogError($"Deletion from Intune failed for device {device.Id} (managedDevice ID: {managedDevice.Id}.  Not deleting from Delegation Station", fullMethodName);

                }
            }

            _logger.DSLogInformation($"Successfully deleted {deviceCount} devices.", fullMethodName);
            return new OkObjectResult("Welcome to Azure Functions!");
        }
    }
}

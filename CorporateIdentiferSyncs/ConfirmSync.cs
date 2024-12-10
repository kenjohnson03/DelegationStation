using CorporateIdentiferSync.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;

namespace CorporateIdentiferSync
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
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Admin, "get", "post")] HttpRequest req)
        {
            //public void Run([TimerTrigger("0 */1 * * * *")] TimerInfo myTimer public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Admin, "get", "post")] HttpRequest req)

            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}", fullMethodName);
            int deviceCount = 0;

            //if (myTimer.ScheduleStatus is not null)
            //{
            //    _logger.DSLogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}", fullMethodName);
            //}

            // Get all devices with sync date older than X
            // TBD:  make date configurable
            //List<Device> devicesToCheck = await _dbService.GetDevicesSyncedBefore(DateTime.UtcNow.AddDays(-1));
            List<Device> devicesToCheck = await _dbService.GetDevicesSyncedBefore(DateTime.UtcNow);
            _logger.DSLogInformation($"Found {devicesToCheck.Count} devices to check.", fullMethodName);

            foreach (Device device in devicesToCheck)
            {
                // Query to see if device is still in CorporateIdentifiers
                var corpIDFound = false;
                corpIDFound = await _graphBetaService.CorporateIdentifierExists(device.CorporateIdentityID);

                // If not, add it back
                if (!corpIDFound)
                {
                    // Add back to CorporateIdentifiers
                    string identifier = $"{device.Make},{device.Model},{device.SerialNumber}";
                    ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(identifier);
                    _logger.DSLogInformation("Corporate Identifier not found, adding back to CorporateIdentifiers", fullMethodName);

                    // TBD:  what happens if adding it fails?
                    device.CorporateIdentityID = deviceIdentity.Id;
                    device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                }
                else
                {
                    _logger.DSLogInformation("Corporate Identifier found, updating sync date", fullMethodName);
                }

                // Update the sync date and status
                device.LastCorpIdentitySync = DateTime.UtcNow;
                device.Status = Device.DeviceStatus.Synced;
                _logger.DSLogInformation($"New sync date: {device.LastCorpIdentitySync}", fullMethodName);


                // Update device entry in DS
                await _dbService.UpdateDevice(device);
                deviceCount++;
            }

            return new OkObjectResult($"Successfully updated {deviceCount} devices.");
        }
    }
}

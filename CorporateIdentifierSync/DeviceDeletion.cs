using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;

namespace CorporateIdentifierSync
{
    public class DeviceDeletion
    {
        private readonly ILogger<DeviceDeletion> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphService _graphService;
        private readonly IGraphBetaService _graphBetaService;

        private int _MaxCorpIDsAllowed;

        public DeviceDeletion(ILogger<DeviceDeletion> logger, ICosmosDbService dbService, IGraphService graphService, IGraphBetaService graphBetaService)
        {
            _logger = logger;
            _dbService = dbService;
            _graphService = graphService;
            _graphBetaService = graphBetaService;
        }

        public void GetEnvironmentVariables()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;


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

        [Function("DeviceDeletion")]
        public async Task Run([TimerTrigger("%DeleteDevicesTriggerTime%")] TimerInfo myTimer)
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
            // Get All devices marked for deletion
            //
            List<Device> devicesToDelete = await _dbService.GetDevicesMarkedForDeletion();
            _logger.DSLogInformation($"Found {devicesToDelete.Count} devices to delete.", fullMethodName);

            int deletedDeviceCount = 0;
            int corpIDsDeletedCount = 0;
            foreach (Device device in devicesToDelete)
            {
                _logger.DSLogInformation($"-----Deleting device {device.Id}.-----", fullMethodName);

                bool delCorpID = false;

                // If present, delete from Corporate Identifiers
                if (!String.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    DeleteCorpIdResult deleteResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                    switch (deleteResult)
                    {
                        case DeleteCorpIdResult.Success:
                            delCorpID = true;
                            corpIDsDeletedCount++;
                            _logger.DSLogInformation($"Successfully deleted Corporate Identifier: {device.CorporateIdentity}", fullMethodName);
                            break;

                        case DeleteCorpIdResult.NotFound:
                            // Already removed from Graph — safe to proceed with Cosmos deletion, but don't
                            // increment corpIDsDeletedCount since there is nothing to release from the counter.
                            delCorpID = true;
                            _logger.DSLogWarning($"Corporate Identifier {device.CorporateIdentityID} was not found in Graph. Proceeding with Cosmos deletion for device {device.Id}.", fullMethodName);
                            break;

                        case DeleteCorpIdResult.Error:
                            delCorpID = false;
                            _logger.DSLogError($"Could not delete CorpID for: {device.Make} {device.Model} {device.SerialNumber} (ID: {device.CorporateIdentityID})", fullMethodName);
                            break;
                    }
                }
                else
                {
                    _logger.DSLogInformation($"Device not synced yet. No Corp Identifier to delete.  {device.Make} {device.Model} {device.SerialNumber}", fullMethodName);
                    delCorpID = true;
                }

                // Delete from Delegation Station
                if (delCorpID)
                {
                    try
                    {
                        await _dbService.DeleteDevice(device);
                        deletedDeviceCount++;
                        _logger.DSLogInformation($"Successfully deleted device from Delegation Station: {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Deletion from Delegation Station failed for device {device.Id}.", ex, fullMethodName);
                    }
                }
                else
                {
                    _logger.DSLogError($"Deletion from Corporate Identifiers failed for device {device.Id}.  Not deleting from Delegation Station", fullMethodName);
                }
            }

            _logger.DSLogInformation($"Successfully deleted {deletedDeviceCount} devices from Delegation Station.", fullMethodName);

            if (corpIDsDeletedCount > 0)
            {
                _logger.DSLogInformation($"Successfully deleted {corpIDsDeletedCount} Corporate Identifiers.", fullMethodName);
                var capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);

                try
                {
                    int available = await capacityManager.ReleaseCorpIDs(corpIDsDeletedCount, CancellationToken.None);
                    _logger.DSLogInformation($"Available CorpIDs after release: {available}", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Failed to release CorpIDs after deletions.", ex, fullMethodName);
                }
            }
        }
    }
}

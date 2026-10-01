using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Device = DelegationStationShared.Models.Device;
using SystemSettings = DelegationSharedLibrary.Models.SystemSettings;

namespace CorporateIdentifierSync
{
    /// <summary>
    /// Expires devices that were successfully processed more than ProcessedDevicesExpiredAfterDays ago
    /// by removing their Corporate Identifier and marking them as Expired.
    /// </summary>
    public class DeviceExpiration
    {
        private readonly ILogger<DeviceExpiration> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;
        private readonly IFunctionSingletonLock _singletonLock;

        private int _MaxCorpIDsAllowed;

        public DeviceExpiration(
            ILogger<DeviceExpiration> logger,
            ICosmosDbService dbService,
            IGraphBetaService graphBetaService,
            IFunctionSingletonLock singletonLock)
        {
            _logger = logger;
            _dbService = dbService;
            _graphBetaService = graphBetaService;
            _singletonLock = singletonLock;
        }

        public static string GetExpiredReason(int ProcessedDevicesExpiredAfterDays)
            => $"Device was expired since it was processed over {ProcessedDevicesExpiredAfterDays} days ago.";

        public void GetEnvironmentVariables()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _MaxCorpIDsAllowed = 10000;
            string? maxCorpIDsString = Environment.GetEnvironmentVariable("MAX_CORPIDS_ALLOWED");
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

        [Function("DeviceExpiration")]
        public async Task Run([TimerTrigger("%ExpireDevicesTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            await using var handle = await _singletonLock.TryAcquireAsync(nameof(DeviceExpiration));
            if (handle is null)
            {
                _logger.DSLogWarning("Another instance of DeviceExpiration is already running. Exiting.", fullMethodName);
                return;
            }

            _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);
            if (myTimer?.ScheduleStatus is not null)
            {
                _logger.DSLogInformation("Next timer schedule at: " + myTimer.ScheduleStatus.Next, fullMethodName);
            }

            GetEnvironmentVariables();
            await ExpireProcessedDevices();
        }

        /// <summary>
        /// Finds devices with ProcessingStatus == Processed whose SuccessfullyProcessedUTC is older than
        /// ProcessedDevicesExpiredAfterDays, removes their Corporate Identifier, and marks them Expired.
        /// </summary>
        public async Task ExpireProcessedDevices()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            //
            // Get the inactivity threshold from System Settings
            //
            SystemSettings? settings;
            try
            {
                settings = await _dbService.GetSystemSettings();
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to retrieve SystemSettings. Exiting function.", ex, fullMethodName);
                return;
            }

            if (settings is null)
            {
                settings = new SystemSettings();
                _logger.DSLogWarning($"SystemSettings not found. Using default ProcessedDevicesExpiredAfterDays: {settings.ProcessedDevicesExpiredAfterDays}.", fullMethodName);
            }

            int expiredAfterDays = settings.ProcessedDevicesExpiredAfterDays;
            if (expiredAfterDays <= 0)
            {
                _logger.DSLogError($"ProcessedDevicesExpiredAfterDays is invalid ({expiredAfterDays}). Must be greater than 0. Exiting function.", fullMethodName);
                return;
            }

            DateTime cutoff = DateTime.UtcNow.AddDays(-expiredAfterDays);
            string expiredReason = GetExpiredReason(expiredAfterDays);

            //
            // Get all devices eligible for expiration
            //
            List<Device> devicesToExpire;
            try
            {
                devicesToExpire = await _dbService.GetProcessedDevicesToExpire(cutoff);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to retrieve processed devices to expire. Exiting function.", ex, fullMethodName);
                return;
            }

            _logger.DSLogInformation($"Found {devicesToExpire.Count} devices processed over {expiredAfterDays} days ago (before {cutoff:o}).", fullMethodName);
            if (devicesToExpire.Count == 0)
            {
                return;
            }

            int expiredDeviceCount = 0;
            int failedDeviceCount = 0;
            int corpIDsDeletedCount = 0;
            foreach (Device device in devicesToExpire)
            {
                string deviceDesc = $"{device.Make} {device.Model} {device.SerialNumber}";
                _logger.DSLogInformation($"-----Expiring device {deviceDesc}.-----", fullMethodName);

                //
                // Mark device for expiration (preserve original mark on retries)
                //
                try
                {
                    device.MarkedForExpirationUTC ??= DateTime.UtcNow;
                    await _dbService.UpdateDevice(device);
                }
                catch (Exception ex)
                {
                    failedDeviceCount++;
                    _logger.DSLogException($"Failed to mark device {deviceDesc} for expiration. Skipping; will retry on next run.", ex, fullMethodName);
                    continue;
                }

                //
                // Remove Corporate Identifier
                //
                bool corpIDRemoved;
                if (string.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    corpIDRemoved = true;
                    _logger.DSLogInformation($"Device {deviceDesc} has no Corporate Identifier to remove.", fullMethodName);
                }
                else
                {
                    DeleteCorpIdResult deleteResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                    switch (deleteResult)
                    {
                        case DeleteCorpIdResult.Success:
                            corpIDRemoved = true;
                            corpIDsDeletedCount++;
                            _logger.DSLogInformation($"Successfully deleted Corporate Identifier {device.CorporateIdentityID} for device {deviceDesc}.", fullMethodName);
                            break;

                        case DeleteCorpIdResult.NotFound:
                            // Already gone from Graph; nothing to release from the counter.
                            corpIDRemoved = true;
                            _logger.DSLogWarning($"Corporate Identifier {device.CorporateIdentityID} was not found in Graph for device {deviceDesc}. Treating as removed.", fullMethodName);
                            break;

                        default:
                            corpIDRemoved = false;
                            _logger.DSLogError($"Could not delete Corporate Identifier {device.CorporateIdentityID} for device {deviceDesc}.", fullMethodName);
                            break;
                    }
                }

                //
                // Record outcome on the device
                //
                if (corpIDRemoved)
                {
                    device.ExpiredUTC = DateTime.UtcNow;
                    device.Status = DeviceStatus.Expired;
                    device.ExpiredReason = expiredReason;
                    device.CorporateIdentityID = string.Empty;
                    device.CorporateIdentity = string.Empty;
                }
                else
                {
                    device.Status = DeviceStatus.ExpirationFailed;
                }

                try
                {
                    await _dbService.UpdateDevice(device);
                    if (corpIDRemoved)
                    {
                        expiredDeviceCount++;
                        _logger.DSLogInformation($"Device {deviceDesc} marked as Expired.", fullMethodName);
                    }
                    else
                    {
                        failedDeviceCount++;
                        _logger.DSLogWarning($"Device {deviceDesc} marked as ExpirationFailed. Will retry on next run.", fullMethodName);
                    }
                }
                catch (Exception ex)
                {
                    // If the CorpID was removed, the next run will get NotFound from Graph and complete expiration.
                    failedDeviceCount++;
                    _logger.DSLogException($"Failed to update expiration status for device {deviceDesc}. Will retry on next run.", ex, fullMethodName);
                }
            }

            _logger.DSLogInformation($"Expired {expiredDeviceCount} devices. {failedDeviceCount} devices failed expiration.", fullMethodName);

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
                    _logger.DSLogException("Failed to release CorpIDs after expirations.", ex, fullMethodName);
                }
            }
        }
    }
}

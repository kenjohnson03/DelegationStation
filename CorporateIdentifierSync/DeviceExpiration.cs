using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Device = DelegationStationShared.Models.Device;

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

        // TODO: Move BatchSize (ExpireDevicesBatchSize), MaxExpirationRetries (MAX_EXPIRATION_RETRIES)
        // and RetryBatchPercent into SystemSettings once the SystemSettings DB code is available.
        // They are currently read from environment variables / constants.
        private int _BatchSize = DefaultBatchSize;
        private int _MaxExpirationRetries = DefaultMaxExpirationRetries;

        internal const int DefaultBatchSize = 1000;
        internal const int DefaultMaxExpirationRetries = 10;

        internal int BatchSize => _BatchSize;
        internal int MaxExpirationRetries => _MaxExpirationRetries;

        // Share of each batch available to retries so new devices always get most of the batch
        // and retries are never starved.
        internal const int RetryBatchPercent = 20;

        // TODO: Temporary static value until SystemSettings is read from the DB.
        internal const int TempProcessedDevicesExpiredAfterDays = 180;

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
        {
            return $"Device was expired since it was processed over {ProcessedDevicesExpiredAfterDays} days ago.";
        }

        // TODO: Move all of these settings (MAX_CORPIDS_ALLOWED, ExpireDevicesBatchSize,
        // MAX_EXPIRATION_RETRIES) into SystemSettings in the DB once that code is available.
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

            _BatchSize = DefaultBatchSize;
            string? batchSizeString = Environment.GetEnvironmentVariable("ExpireDevicesBatchSize");
            if (!int.TryParse(batchSizeString, out int bs) || bs <= 0)
            {
                _logger.DSLogWarning($"ExpireDevicesBatchSize is not set or invalid. Using default value: {_BatchSize}.", fullMethodName);
            }
            else
            {
                _BatchSize = bs;
                _logger.DSLogInformation($"Using ExpireDevicesBatchSize: {_BatchSize}.", fullMethodName);
            }

            _MaxExpirationRetries = DefaultMaxExpirationRetries;
            string? maxRetriesString = Environment.GetEnvironmentVariable("MAX_EXPIRATION_RETRIES");
            if (!int.TryParse(maxRetriesString, out int mr) || mr < 0)
            {
                _logger.DSLogWarning($"MAX_EXPIRATION_RETRIES is not set or invalid. Using default value: {_MaxExpirationRetries}.", fullMethodName);
            }
            else
            {
                _MaxExpirationRetries = mr;
                _logger.DSLogInformation($"Using MAX_EXPIRATION_RETRIES: {_MaxExpirationRetries}.", fullMethodName);
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

            // TODO: Temporary workaround. Replace with SystemSettings.ProcessedDevicesExpiredAfterDays
            // once the SystemSettings DB access code is available.
            int expiredAfterDays = TempProcessedDevicesExpiredAfterDays;
            _logger.DSLogInformation($"Using ProcessedDevicesExpiredAfterDays: {expiredAfterDays}.", fullMethodName);

            DateTime cutoff = DateTime.UtcNow.AddDays(-expiredAfterDays);
            string expiredReason = GetExpiredReason(expiredAfterDays);

            //
            // Build this run's batch: previously failed devices (capped share), then new devices.
            //
            // maxRetryDevicesInBatch is the max number of previously-failed devices (retries) to include in this
            // batch, NOT a per-device retry count limit (see _MaxExpirationRetries for that).
            // The rest of the batch is filled with new expirations.
            int maxRetryDevicesInBatch = Math.Max(1, _BatchSize * RetryBatchPercent / 100);
            List<Device> retryDevices = new List<Device>();
            try
            {
                retryDevices = await _dbService.GetProcessedDevicesToRetryExpiration(cutoff, maxRetryDevicesInBatch);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to retrieve devices pending expiration retry. Continuing with new devices.", ex, fullMethodName);
            }

            int newLimit = _BatchSize - retryDevices.Count;
            List<Device> newDevices = new List<Device>();
            try
            {
                newDevices = await _dbService.GetProcessedDevicesToExpire(cutoff, newLimit);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to retrieve processed devices to expire.", ex, fullMethodName);
            }

            _logger.DSLogInformation($"Batch size {_BatchSize}: {newDevices.Count} new and {retryDevices.Count} retry devices processed over {expiredAfterDays} days ago (before {cutoff:o}).", fullMethodName);

            List<Device> devicesToExpire = retryDevices.Concat(newDevices).ToList();
            if (devicesToExpire.Count == 0)
            {
                return;
            }

            int expiredDeviceCount = 0;
            int failedDeviceCount = 0;
            int expirationFailedCount = 0;
            int corpIDsDeletedCount = 0;
            foreach (Device device in devicesToExpire)
            {
                string deviceDesc = $"{device.Make} {device.Model} {device.SerialNumber}";
                _logger.DSLogInformation($"-----Expiring device {deviceDesc}.-----", fullMethodName);

                //
                // Mark device for expiration (retries are already marked; keep original timestamp).
                // Persisted with the outcome update below to avoid an extra DB write.
                //
                if (device.MarkedForExpirationUTC is null)
                {
                    device.MarkedForExpirationUTC = DateTime.UtcNow;
                }
                else
                {
                    _logger.DSLogInformation($"Retrying expiration for device {deviceDesc} (previous failures: {device.ExpirationFailureCount}).", fullMethodName);
                }

                //
                // Remove Corporate Identifier
                //
                bool corpIDRemoved;
                if (string.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    corpIDRemoved = true;
                    _logger.DSLogWarning($"Device {deviceDesc} is Synced but has no Corporate Identifier stored in DB. This is unexpected; treating as removed.", fullMethodName);
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
                // If removal failed, leave the device Synced (with MarkedForExpirationUTC set) so it is
                // retried, until it exceeds MAX_EXPIRATION_RETRIES and is moved to ExpirationFailed.
                //
                if (!corpIDRemoved)
                {
                    failedDeviceCount++;
                    device.ExpirationFailureCount++;
                    bool retriesExhausted = device.ExpirationFailureCount > _MaxExpirationRetries;
                    if (retriesExhausted)
                    {
                        device.Status = DeviceStatus.ExpirationFailed;
                    }

                    try
                    {
                        await _dbService.UpdateDevice(device);
                        if (retriesExhausted)
                        {
                            expirationFailedCount++;
                            _logger.DSLogError($"Device {deviceDesc} exceeded max expiration retries ({_MaxExpirationRetries}). Marked as ExpirationFailed. Corporate Identifier {device.CorporateIdentityID} requires manual cleanup.", fullMethodName);
                        }
                        else
                        {
                            _logger.DSLogWarning($"Corporate Identifier removal failed for device {deviceDesc} ({device.ExpirationFailureCount} failures). Leaving device Synced; will retry on next run.", fullMethodName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Failed to record expiration failure for device {deviceDesc}. Will retry on next run.", ex, fullMethodName);
                    }
                    continue;
                }

                device.ExpiredUTC = DateTime.UtcNow;
                device.Status = DeviceStatus.Expired;
                device.ExpiredReason = expiredReason;
                device.CorporateIdentityID = string.Empty;
                device.CorporateIdentity = string.Empty;

                try
                {
                    await _dbService.UpdateDevice(device);
                    expiredDeviceCount++;
                    _logger.DSLogInformation($"Device {deviceDesc} marked as Expired.", fullMethodName);
                }
                catch (Exception ex)
                {
                    // If the CorpID was removed, the next run will get NotFound from Graph and complete expiration.
                    failedDeviceCount++;
                    _logger.DSLogException($"Failed to update expiration status for device {deviceDesc}. Will retry on next run.", ex, fullMethodName);
                }
            }

            _logger.DSLogInformation($"Expired {expiredDeviceCount} devices. {failedDeviceCount} devices failed expiration, {expirationFailedCount} of which exceeded max retries and were marked ExpirationFailed.", fullMethodName);

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

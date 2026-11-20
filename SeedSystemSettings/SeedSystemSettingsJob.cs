using System.Net;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace SeedSystemSettings
{
    public class SeedSystemSettingsJob
    {
        private readonly Container _container;
        private readonly ILogger<SeedSystemSettingsJob> _logger;

        public SeedSystemSettingsJob(Container container, ILoggerFactory loggerFactory)
        {
            _container = container;
            _logger = loggerFactory.CreateLogger<SeedSystemSettingsJob>();
        }

        public async Task RunAsync()
        {
            _logger.LogInformation("Checking for existing system settings...");
            var query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = \"SystemSettings\"");
            using var iterator = _container.GetItemQueryIterator<SystemSettings>(
                query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("SystemSettings") });
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                if (response.Any())
                {
                    _logger.LogInformation("System settings already exist. Skipping seed without modifying them.");
                    return;
                }
            }

            var settings = new SystemSettings
            {
                MaxCorpIdsAllowed = ReadMaxCorpIdsAllowed(),
                CorpIdWarningThresholdPercent = ReadWarningThresholdPercent(),
                ExpireProcessedAfterDays = 180,
                ExpireUnprocessedAfterDays = 180
            };

            try
            {
                await _container.CreateItemAsync(settings, new PartitionKey(settings.PartitionKey));
                _logger.LogInformation("System settings created successfully: {Settings}", settings);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.LogInformation("System settings were created by another process. Skipping seed without modifying them.");
            }
        }

        private int ReadMaxCorpIdsAllowed()
        {
            string? value = Environment.GetEnvironmentVariable("MAX_CORPIDS_ALLOWED");
            if (int.TryParse(value, out int max) && max > 0)
            {
                return max;
            }

            _logger.LogWarning("MAX_CORPIDS_ALLOWED is not set or invalid. Using default value: 10000.");
            return 10000;
        }

        private int ReadWarningThresholdPercent()
        {
            string? value = Environment.GetEnvironmentVariable("CORPID_WARNING_THRESHOLD_PERCENT");
            if (!int.TryParse(value, out int threshold))
            {
                _logger.LogWarning("CORPID_WARNING_THRESHOLD_PERCENT is not set or invalid. Using default value: 90.");
                return 90;
            }

            int clamped = Math.Clamp(threshold, 1, 100);
            if (clamped != threshold)
            {
                _logger.LogWarning("CORPID_WARNING_THRESHOLD_PERCENT value {Threshold} was clamped to {Clamped}.",
                    threshold, clamped);
            }
            return clamped;
        }
    }
}

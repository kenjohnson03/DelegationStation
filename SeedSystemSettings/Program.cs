using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace SeedSystemSettings
{
    public class Program
    {
        static async Task<int> Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddApplicationInsightsWebJobs(o =>
                    o.ConnectionString = Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING"));
            });
            var logger = loggerFactory.CreateLogger<SeedSystemSettingsJob>();

            try
            {
                string? endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT");
                string? connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
                if (string.IsNullOrEmpty(endpoint) && string.IsNullOrEmpty(connectionString))
                {
                    logger.LogError("Missing required environment variable COSMOS_ENDPOINT or COSMOS_CONNECTION_STRING.");
                    return 1;
                }

                string databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "";
                string containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME") ?? "";
                if (string.IsNullOrEmpty(databaseName))
                {
                    databaseName = "DelegationStationData";
                    logger.LogInformation("COSMOS_DATABASE_NAME is not set. Using {DatabaseName}.", databaseName);
                }
                if (string.IsNullOrEmpty(containerName))
                {
                    containerName = "DeviceData";
                    logger.LogInformation("COSMOS_CONTAINER_NAME is not set. Using {ContainerName}.", containerName);
                }

                logger.LogInformation("Connecting to Cosmos DB using {Authentication}.",
                    string.IsNullOrEmpty(connectionString) ? "Managed Identity" : "Connection String");
                using var client = !string.IsNullOrEmpty(connectionString)
                    ? new CosmosClient(connectionString)
                    : new CosmosClient(endpoint!, new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
                var job = new SeedSystemSettingsJob(client.GetContainer(databaseName, containerName), loggerFactory);
                await job.RunAsync();
                return 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed system settings.");
                return 1;
            }
        }
    }
}

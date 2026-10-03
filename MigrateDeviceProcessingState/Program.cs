using System.Runtime.InteropServices;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace MigrateDeviceProcessingState;

public static class Program
{
    public static async Task<int> Main()
    {
        string? insightsConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(insightsConnectionString))
            insightsConnectionString = Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(insightsConnectionString))
        {
            Console.Error.WriteLine("Configure APPLICATIONINSIGHTS_CONNECTION_STRING (or APPINSIGHTS_CONNECTION_STRING) " +
                "with the webapp's Application Insights connection string. No devices were changed.");
            return 1;
        }

        using var logging = new MigrationLogging(insightsConnectionString);
        var loggerFactory = logging.Factory;
        var logger = loggerFactory.CreateLogger<MigrationJob>();
        using var shutdown = new CancellationTokenSource();
        using var monitorStop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += handler;
        using var sigterm = !OperatingSystem.IsWindows()
            ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; shutdown.Cancel(); })
            : null;
        Task monitor = MonitorShutdownAsync(shutdown, monitorStop.Token);
        try
        {
            var options = MigrationOptions.FromEnvironment();
            string cloud = Environment.GetEnvironmentVariable("AzureEnvironment") ?? "AzurePublicCloud";
            if (cloud != "AzurePublicCloud" && cloud != "AzureUSGovernment" && cloud != "AzureUSDoD")
                throw new InvalidOperationException("AzureEnvironment must be AzurePublicCloud, AzureUSGovernment or AzureUSDoD.");
            string endpoint = Environment.GetEnvironmentVariable("GraphEndpoint") ??
                (cloud == "AzurePublicCloud" ? "https://graph.microsoft.com/" :
                 cloud == "AzureUSDoD" ? "https://dod-graph.microsoft.us/" : "https://graph.microsoft.us/");
            var graphUri = new Uri(endpoint.TrimEnd('/') + "/");
            if (graphUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("GraphEndpoint must use HTTPS.");

            var credential = new ManagedIdentityCredential(new ManagedIdentityCredentialOptions(ManagedIdentityId.SystemAssigned)
            {
                AuthorityHost = cloud == "AzurePublicCloud"
                    ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            });
            string? connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
            using var cosmos = !string.IsNullOrWhiteSpace(connectionString)
                ? new CosmosClient(connectionString)
                : new CosmosClient(MigrationOptions.Required("COSMOS_ENDPOINT"), credential);
            var container = cosmos.GetContainer(
                Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "DelegationStationData",
                Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME") ?? "DeviceData");
            using var http = new HttpClient();
            var job = new MigrationJob(new CosmosDeviceStore(container),
                new GraphIntuneReader(http, credential, graphUri, options.GraphMaxRetries,
                    loggerFactory.CreateLogger<GraphIntuneReader>()), options, logger);
            RunCounts counts = await job.RunAsync(shutdown.Token);
            return counts.Errors == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration failed. Correct configuration or service errors and resume with the same MigrationID.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            monitorStop.Cancel();
            await monitor;
            bool flushed = await logging.FlushAsync(CancellationToken.None);
            if (!flushed)
                Console.Error.WriteLine("Application Insights flush did not complete successfully; check console logs for this run.");
        }
    }

    private static async Task MonitorShutdownAsync(CancellationTokenSource shutdown, CancellationToken stop)
    {
        string? shutdownFile = Environment.GetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE");
        if (string.IsNullOrEmpty(shutdownFile))
            return;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (File.Exists(shutdownFile))
                {
                    shutdown.Cancel();
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Normal cleanup after the scheduled batch finishes.
        }
    }
}

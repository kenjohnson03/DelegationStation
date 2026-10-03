using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;

namespace MigrateDeviceProcessingState;

public sealed class MigrationLogging : IDisposable
{
    private readonly TelemetryClient telemetry;
    public ILoggerFactory Factory { get; }

    public MigrationLogging(string connectionString, ITelemetryChannel? channel = null)
    {
        TelemetryClient? client = null;
        Factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddApplicationInsights(configuration =>
            {
                configuration.ConnectionString = connectionString;
                if (channel != null)
                    configuration.TelemetryChannel = channel;
                configuration.TelemetryInitializers.Add(new MigrationTelemetryInitializer());
                client = new TelemetryClient(configuration);
            }, options => options.IncludeScopes = true);
            builder.AddFilter<ApplicationInsightsLoggerProvider>("", LogLevel.Information);
        });
        telemetry = client ?? throw new InvalidOperationException("Application Insights telemetry was not initialized.");
    }

    public Task<bool> FlushAsync(CancellationToken cancellationToken) => telemetry.FlushAsync(cancellationToken);

    public void Dispose() => Factory.Dispose();

    private sealed class MigrationTelemetryInitializer : ITelemetryInitializer
    {
        public void Initialize(ITelemetry telemetry) =>
            telemetry.Context.Cloud.RoleName = "MigrateDeviceProcessingState";
    }
}

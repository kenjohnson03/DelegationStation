using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;

namespace MigrateDeviceProcessingState.Tests;

public class MigrationLoggingTests
{
    [Fact]
    public void InformationSummariesAndExceptionsGoToConfiguredTelemetryResourceWithMigrationScope()
    {
        var channel = new Channel();
        using var logging = new MigrationLogging(
            "InstrumentationKey=00000000-0000-0000-0000-000000000001", channel);
        var logger = logging.Factory.CreateLogger<MigrationJob>();
        using (logger.BeginScope(new Dictionary<string, object> { ["MigrationID"] = "event" }))
        {
            logger.LogInformation("Migration {MigrationID} summary: checked={Checked}", "event", 7);
            logger.LogError(new IOException("test failure"), "Device patch failed");
        }

        var trace = Assert.Single(channel.Items.OfType<TraceTelemetry>());
        Assert.Contains("checked=7", trace.Message);
        Assert.Equal(SeverityLevel.Information, trace.SeverityLevel);
        Assert.Equal("event", trace.Properties["MigrationID"]);
        Assert.Single(channel.Items.OfType<ExceptionTelemetry>());
        Assert.All(channel.Items, item =>
        {
            Assert.Equal("MigrateDeviceProcessingState", item.Context.Cloud.RoleName);
            Assert.Equal("00000000-0000-0000-0000-000000000001", item.Context.InstrumentationKey);
        });
    }

    private sealed class Channel : ITelemetryChannel
    {
        public List<ITelemetry> Items { get; } = [];
        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; } = "";
        public void Send(ITelemetry item) => Items.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }
}

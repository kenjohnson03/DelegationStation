using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using DelegationStationShared.Models;

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

    [Fact]
    public async Task SuccessfulDeviceProcessingIsLoggedToApplicationInsights()
    {
        var channel = new Channel();
        using var logging = new MigrationLogging(
            "InstrumentationKey=00000000-0000-0000-0000-000000000001", channel);
        var enrolled = DateTimeOffset.UtcNow.AddYears(-1);
        var device = new Device { Make = "Dell", Model = "Model", SerialNumber = "Serial" };
        var options = new MigrationOptions("event", 1, 180, 2);
        var job = new MigrationJob(new Store(device), new Reader(device, enrolled),
            options, logging.Factory.CreateLogger<MigrationJob>());

        RunCounts result = await job.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Updated);
        Assert.Contains(channel.Items.OfType<TraceTelemetry>(),
            trace => trace.Message.Contains("processing fields updated and marked with MigrationID"));
        Assert.Contains(channel.Items.OfType<TraceTelemetry>(),
            trace => trace.Message.Contains("qualified for Processed state"));
    }

    private sealed class Store(Device device) : IDeviceStore
    {
        public Task<IReadOnlyList<Device>> GetBatchAsync(MigrationOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Device>>([device]);

        public Task<bool> PatchAsync(Device device, string migrationID, DateTime? enrollmentUtc) =>
            Task.FromResult(true);
    }

    private sealed class Reader(Device device, DateTimeOffset enrolled) : IIntuneReader
    {
        public Task<IntuneDevice?> GetMatchAsync(Device requestedDevice, CancellationToken cancellationToken) =>
            Task.FromResult<IntuneDevice?>(new(device.Make, device.Model, device.SerialNumber, enrolled,
                DateTimeOffset.UtcNow.AddSeconds(-1)));
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

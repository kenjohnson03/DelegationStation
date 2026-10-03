using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigrateDeviceProcessingState.Tests;

public class MigrationJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly MigrationOptions Options = new("processing-2026", 3, 180, 2);

    [Theory]
    [InlineData(-180, true)]
    [InlineData(-179, true)]
    [InlineData(0, true)]
    [InlineData(-181, false)]
    [InlineData(1, false)]
    public async Task SyncWindowIsInclusiveAndExcludesFutureSync(int days, bool qualifies)
    {
        var device = DeviceFor("one");
        var enrolled = new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.FromHours(3));
        var store = new Store(device);
        var reader = new Reader(device, enrolled, Now.AddDays(days));
        RunCounts counts = await Job(store, reader).RunAsync(CancellationToken.None);

        Assert.Equal(1, counts.Checked);
        Assert.Equal(1, counts.Matched);
        Assert.Equal(1, counts.Marked);
        Assert.Equal(qualifies ? 1 : 0, counts.Qualified);
        Assert.Equal(qualifies ? enrolled.UtcDateTime : (DateTime?)null, store.Patches.Single().Enrollment);
        Assert.Equal(Options.MigrationID, store.Patches.Single().MigrationID);
        Assert.Equal(days < -180 ? 1 : 0, counts.Stale);
        Assert.Equal(days > 0 ? 1 : 0, counts.FutureSync);
    }

    [Fact]
    public async Task ConfiguredWindowIsUsed()
    {
        var device = DeviceFor("one");
        var store = new Store(device);
        var reader = new Reader(device, Now.AddDays(-5), Now.AddDays(-31));
        var job = new MigrationJob(store, reader, Options with { MaxIntuneSyncAgeDays = 30 },
            NullLogger<MigrationJob>.Instance, new Clock());
        Assert.Equal(1, (await job.RunAsync(CancellationToken.None)).Stale);
        Assert.Null(store.Patches.Single().Enrollment);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MissingDatesAreNotQualified(bool enrollmentMissing, bool syncMissing)
    {
        var device = DeviceFor("one");
        var store = new Store(device);
        var reader = new Reader(device, enrollmentMissing ? null : Now, syncMissing ? null : Now);
        var counts = await Job(store, reader).RunAsync(CancellationToken.None);
        Assert.Equal(1, counts.MissingDates);
        Assert.Equal(0, counts.Updated);
        Assert.Null(store.Patches.Single().Enrollment);
    }

    [Fact]
    public async Task ResumeSkipsMarkedAndFullyPopulatedDevicesAndHonorsBatchSize()
    {
        var marked = DeviceFor("marked");
        marked.MigrationID = Options.MigrationID;
        var processed = DeviceFor("processed");
        processed.SuccessfullyProcessedUTC = Now.UtcDateTime;
        processed.LastProcessingAttemptUTC = Now.UtcDateTime;
        processed.ProcessingStatus = ProcessingStatus.Processed;
        var partial = DeviceFor("partial");
        partial.ProcessingStatus = ProcessingStatus.Processed;
        var store = new Store(marked, processed, partial, DeviceFor("two"), DeviceFor("three"), DeviceFor("four"));
        var reader = new Reader();
        var job = Job(store, reader);

        Assert.Equal(3, (await job.RunAsync(CancellationToken.None)).NotFound);
        Assert.Equal(1, (await job.RunAsync(CancellationToken.None)).NotFound);
        Assert.Equal(0, (await job.RunAsync(CancellationToken.None)).Checked);
        Assert.Equal(4, store.Patches.Count);
        Assert.Equal(2, reader.Calls);
        Assert.DoesNotContain(store.Patches, p => p.Device == processed || p.Device == marked);

        var newMigration = new MigrationJob(store, reader, Options with { MigrationID = "next-event" },
            NullLogger<MigrationJob>.Instance, new Clock());
        Assert.Equal(3, (await newMigration.RunAsync(CancellationToken.None)).Checked);
    }

    [Fact]
    public async Task PatchFailureRemainsRetryableAndDoesNotStopOtherDevices()
    {
        var failing = DeviceFor("one");
        var store = new Store(failing, DeviceFor("two")) { FailID = failing.Id };
        var job = Job(store, new Reader());
        var counts = await job.RunAsync(CancellationToken.None);
        Assert.Equal(2, counts.Checked);
        Assert.Equal(1, counts.Errors);
        Assert.Equal(1, counts.Marked);
        Assert.Null(failing.MigrationID);
        store.FailID = null;
        Assert.Equal(1, (await job.RunAsync(CancellationToken.None)).Marked);
    }

    [Fact]
    public async Task ConflictDoesNotMarkDevice()
    {
        var device = DeviceFor("one");
        var store = new Store(device) { Conflict = true };
        var counts = await Job(store, new Reader()).RunAsync(CancellationToken.None);
        Assert.Equal(1, counts.Conflicts);
        Assert.Equal(0, counts.Marked);
        Assert.Null(device.MigrationID);
    }

    [Fact]
    public async Task CancellationFinishesCurrentDeviceThenStops()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new Store(DeviceFor("one"), DeviceFor("two")) { DuringPatch = cancellation.Cancel };
        var counts = await Job(store, new Reader()).RunAsync(cancellation.Token);
        Assert.Equal(1, counts.Checked);
        Assert.Equal(1, counts.Marked);
        Assert.Single(store.Patches);
    }

    [Fact]
    public async Task IncompleteGraphScanNeverMarksDevices()
    {
        var store = new Store(DeviceFor("one"));
        var reader = new Reader { Failure = new HttpRequestException("Graph page failed") };
        await Assert.ThrowsAsync<HttpRequestException>(() => Job(store, reader).RunAsync(CancellationToken.None));
        Assert.Empty(store.Patches);
    }

    private static MigrationJob Job(Store store, Reader reader) =>
        new(store, reader, Options, NullLogger<MigrationJob>.Instance, new Clock());

    private static Device DeviceFor(string serial) => new() { Make = "Dell", Model = "Model", SerialNumber = serial };

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store(params Device[] devices) : IDeviceStore
    {
        public List<(Device Device, string MigrationID, DateTime? Enrollment)> Patches { get; } = [];
        public Guid? FailID { get; set; }
        public bool Conflict { get; set; }
        public Action? DuringPatch { get; set; }

        public Task<IReadOnlyList<Device>> GetBatchAsync(MigrationOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Device>>(devices.Where(d => d.MigrationID != options.MigrationID &&
                (d.SuccessfullyProcessedUTC == null || d.LastProcessingAttemptUTC == null || d.ProcessingStatus == null))
                .OrderBy(d => d.ModifiedUTC).Take(options.BatchSize).ToList());
        }

        public Task<bool> PatchAsync(Device device, string migrationID, DateTime? enrollmentUtc)
        {
            DuringPatch?.Invoke();
            if (FailID == device.Id)
                throw new IOException("Patch failed");
            if (Conflict)
                return Task.FromResult(false);
            Patches.Add((device, migrationID, enrollmentUtc));
            device.MigrationID = migrationID;
            return Task.FromResult(true);
        }
    }

    private sealed class Reader : IIntuneReader
    {
        private readonly Dictionary<DeviceKey, IntuneDevice> matches = [];
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Reader() { }
        public Reader(Device device, DateTimeOffset? enrollment, DateTimeOffset? sync) =>
            matches[DeviceKey.FromCosmos(device)] = new(device.Make, device.Model, device.SerialNumber, enrollment, sync);

        public Task<IReadOnlyDictionary<DeviceKey, IntuneDevice>> GetMatchesAsync(
            IReadOnlySet<DeviceKey> keys, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure != null)
                throw Failure;
            return Task.FromResult<IReadOnlyDictionary<DeviceKey, IntuneDevice>>(matches);
        }
    }
}

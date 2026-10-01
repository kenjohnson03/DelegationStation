using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.DeviceExpirationTests;

[Collection("EnvVarTests")]
public class DeviceExpirationTests
{
    #region helpers
    private static DeviceExpiration CreateSut(
        FakeDbService? dbService = null,
        FakeGraphBetaService? graphBetaService = null,
        IFunctionSingletonLock? singletonLock = null)
    {
        return new DeviceExpiration(
            NullLogger<DeviceExpiration>.Instance,
            dbService ?? new FakeDbService(),
            graphBetaService ?? new FakeGraphBetaService(),
            singletonLock ?? new FakeSingletonLock(new FakeAsyncDisposable()));
    }

    private static Device CreateProcessedDevice(string corpIdentityID = "corp-id-1")
    {
        return new Device
        {
            Make = "Make",
            Model = "Model",
            SerialNumber = Guid.NewGuid().ToString(),
            Status = DeviceStatus.Synced,
            ProcessingStatus = ProcessingStatus.Processed,
            SuccessfullyProcessedUTC = DateTime.UtcNow.AddDays(-400),
            CorporateIdentity = "Make,Model,SN",
            CorporateIdentityID = corpIdentityID
        };
    }
    #endregion helpers

    [Fact]
    public async Task Run_WhenSingletonLockNotAcquired_ExitsWithoutQueryingDatabase()
    {
        var dbService = new FakeDbService();
        var sut = CreateSut(dbService: dbService, singletonLock: new FakeSingletonLock(handle: null));

        await sut.Run(new TimerInfo());

        Assert.Equal(0, dbService.GetDevicesCallCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_UsesProcessedDevicesExpiredAfterDaysForCutoff()
    {
        var dbService = new FakeDbService();
        var sut = CreateSut(dbService: dbService);
        int days = DeviceExpiration.TempProcessedDevicesExpiredAfterDays;

        DateTime before = DateTime.UtcNow.AddDays(-days);
        await sut.ExpireProcessedDevices();
        DateTime after = DateTime.UtcNow.AddDays(-days);

        Assert.Equal(1, dbService.GetDevicesCallCount);
        Assert.InRange(dbService.LastCutoff!.Value, before, after);
    }

    [Fact]
    public async Task ExpireProcessedDevices_WhenGetDevicesThrows_DoesNotDeleteCorpIDs()
    {
        var dbService = new FakeDbService { GetDevicesException = new Exception("boom") };
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(0, dbService.UpdateDeviceCallCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_OnCorpIDDeleteSuccess_MarksDeviceExpiredAndReleasesCounter()
    {
        var dbService = new FakeDbService
        {
            Counter = new CorpIDCounter(5)
        };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.Equal(1, graph.DeleteCallCount);
        Assert.NotNull(device.MarkedForExpirationUTC);
        Assert.NotNull(device.ExpiredUTC);
        Assert.True(device.ExpiredUTC >= device.MarkedForExpirationUTC);
        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.Equal($"Device was expired since it was processed over {DeviceExpiration.TempProcessedDevicesExpiredAfterDays} days ago.", device.ExpiredReason);
        Assert.Equal(string.Empty, device.CorporateIdentityID);
        Assert.Equal(2, dbService.UpdateDeviceCallCount);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_MarksForExpirationBeforeDeletingCorpID()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService();
        graph.OnDelete = () => Assert.Equal(1, dbService.UpdateDeviceCallCount);
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.NotNull(dbService.FirstUpdateMarkedForExpirationUTC);
        Assert.Null(dbService.FirstUpdateExpiredUTC);
    }

    [Fact]
    public async Task ExpireProcessedDevices_OnCorpIDNotFound_MarksExpiredWithoutReleasingCounter()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.NotFound };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.NotNull(device.ExpiredUTC);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_WithNoCorpID_MarksExpiredWithoutCallingGraph()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice(corpIdentityID: string.Empty);
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.NotNull(device.ExpiredUTC);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_OnCorpIDDeleteError_MarksExpirationFailed()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Error };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.Equal(DeviceStatus.ExpirationFailed, device.Status);
        Assert.NotNull(device.MarkedForExpirationUTC);
        Assert.Null(device.ExpiredUTC);
        Assert.Equal(string.Empty, device.ExpiredReason);
        Assert.Equal("corp-id-1", device.CorporateIdentityID);
        Assert.Equal(2, dbService.UpdateDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_OnRetry_PreservesOriginalMarkedForExpirationUTC()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice();
        DateTime originalMark = DateTime.UtcNow.AddDays(-2);
        device.MarkedForExpirationUTC = originalMark;
        device.Status = DeviceStatus.ExpirationFailed;
        dbService.DevicesToReturn.Add(device);
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireProcessedDevices();

        Assert.Equal(originalMark, device.MarkedForExpirationUTC);
        Assert.Equal(DeviceStatus.Expired, device.Status);
    }

    [Fact]
    public async Task ExpireProcessedDevices_WhenMarkUpdateFails_SkipsCorpIDDeletion()
    {
        var dbService = new FakeDbService { UpdateDeviceException = new Exception("412") };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(1, dbService.UpdateDeviceCallCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_WhenFinalUpdateFails_StillReleasesDeletedCorpIDs()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5), FailUpdateOnCall = 2 };
        dbService.DevicesToReturn.Add(CreateProcessedDevice());
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireProcessedDevices();

        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireProcessedDevices_MultipleDevices_ReleasesOnlySuccessfulDeletes()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(10) };
        var ok1 = CreateProcessedDevice("ok-1");
        var bad = CreateProcessedDevice("bad");
        var ok2 = CreateProcessedDevice("ok-2");
        dbService.DevicesToReturn.AddRange(new[] { ok1, bad, ok2 });
        var graph = new FakeGraphBetaService
        {
            ResultsById = new Dictionary<string, DeleteCorpIdResult> { ["bad"] = DeleteCorpIdResult.Error }
        };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireProcessedDevices();

        Assert.Equal(DeviceStatus.Expired, ok1.Status);
        Assert.Equal(DeviceStatus.ExpirationFailed, bad.Status);
        Assert.Equal(DeviceStatus.Expired, ok2.Status);
        Assert.Equal(1, dbService.TrySetCorpIDCounterCallCount);
        Assert.Equal(8, dbService.Counter.CorpIDCount);
    }

    // ====================================================================
    // Inner fakes
    // ====================================================================

    private sealed class FakeAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSingletonLock : IFunctionSingletonLock
    {
        private readonly IAsyncDisposable? _handle;
        public FakeSingletonLock(IAsyncDisposable? handle) => _handle = handle;
        public Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken cancellationToken = default)
            => Task.FromResult(_handle);
    }

    private sealed class FakeGraphBetaService : IGraphBetaService
    {
        public DeleteCorpIdResult DeleteResult { get; set; } = DeleteCorpIdResult.Success;
        public Dictionary<string, DeleteCorpIdResult> ResultsById { get; set; } = new();
        public Action? OnDelete { get; set; }
        public int DeleteCallCount { get; private set; }

        public Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID)
        {
            DeleteCallCount++;
            OnDelete?.Invoke();
            return Task.FromResult(ResultsById.TryGetValue(identifierID, out var r) ? r : DeleteResult);
        }

        public Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier)
            => throw new NotImplementedException();
        public Task<bool> CorporateIdentifierExists(string identiferID) => throw new NotImplementedException();
        public Task<int> GetCorporateDeviceIdentifierCountAsync() => throw new NotImplementedException();
    }

    private sealed class FakeDbService : ICosmosDbService
    {
        public List<Device> DevicesToReturn { get; set; } = new();
        public Exception? GetDevicesException { get; set; }
        public Exception? UpdateDeviceException { get; set; }
        public int? FailUpdateOnCall { get; set; }
        public CorpIDCounter Counter { get; set; } = new CorpIDCounter(0);

        public int GetDevicesCallCount { get; private set; }
        public int UpdateDeviceCallCount { get; private set; }
        public int TrySetCorpIDCounterCallCount { get; private set; }
        public DateTime? LastCutoff { get; private set; }
        public DateTime? FirstUpdateMarkedForExpirationUTC { get; private set; }
        public DateTime? FirstUpdateExpiredUTC { get; private set; }

        public Task<List<Device>> GetProcessedDevicesToExpire(DateTime processedBeforeUTC)
        {
            GetDevicesCallCount++;
            LastCutoff = processedBeforeUTC;
            if (GetDevicesException is not null) throw GetDevicesException;
            return Task.FromResult(DevicesToReturn);
        }

        public Task UpdateDevice(Device device)
        {
            UpdateDeviceCallCount++;
            if (UpdateDeviceCallCount == 1)
            {
                FirstUpdateMarkedForExpirationUTC = device.MarkedForExpirationUTC;
                FirstUpdateExpiredUTC = device.ExpiredUTC;
            }
            if (UpdateDeviceException is not null) throw UpdateDeviceException;
            if (FailUpdateOnCall == UpdateDeviceCallCount) throw new Exception("Simulated update failure");
            return Task.CompletedTask;
        }

        public Task<CorpIDCounter> GetCorpIDCounter() => Task.FromResult(Counter);

        public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
        {
            TrySetCorpIDCounterCallCount++;
            Counter = counter;
            return Task.FromResult(true);
        }

        // Not used by DeviceExpiration
        public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
        public Task DeleteDevice(Device device) => throw new NotImplementedException();
        public Task<Device?> GetDevice(Guid id, string partitionKey) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
        public Task<List<string>> GetSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<string>> GetNonSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize) => throw new NotImplementedException();
        public Task<int> GetSyncedDeviceCountAsync() => throw new NotImplementedException();
    }
}

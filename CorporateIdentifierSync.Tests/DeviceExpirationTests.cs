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

    private static Device CreateUnprocessedDevice(string corpIdentityID = "corp-id-1")
    {
        return new Device
        {
            Make = "Make",
            Model = "Model",
            SerialNumber = Guid.NewGuid().ToString(),
            Status = DeviceStatus.Synced,
            ProcessingStatus = null,
            ModifiedUTC = DateTime.UtcNow.AddDays(-400),
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
        Assert.Equal(0, dbService.GetRetryDevicesCallCount);
    }

    [Fact]
    public async Task ExpireDevices_CalculatesBothCutoffsFromCurrentSettings()
    {
        var dbService = new FakeDbService();
        var sut = CreateSut(dbService: dbService);
        int processedDays = DeviceExpiration.TempProcessedDevicesExpiredAfterDays;
        int unprocessedDays = DeviceExpiration.TempUnprocessedDevicesExpiredAfterDays;

        DateTime start = DateTime.UtcNow;
        await sut.ExpireDevices();
        DateTime end = DateTime.UtcNow;

        Assert.Equal(1, dbService.GetDevicesCallCount);
        Assert.InRange(dbService.LastProcessedCutoff!.Value, start.AddDays(-processedDays), end.AddDays(-processedDays));
        Assert.InRange(dbService.LastAddedCutoff!.Value, start.AddDays(-unprocessedDays), end.AddDays(-unprocessedDays));
        Assert.Equal(dbService.LastProcessedCutoff, dbService.LastRetryProcessedCutoff);
        Assert.Equal(dbService.LastAddedCutoff, dbService.LastRetryAddedCutoff);
    }

    [Fact]
    public async Task ExpireDevices_UnprocessedDevice_MarksExpiredWithUnprocessedReason()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateUnprocessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(1, graph.DeleteCallCount);
        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.NotNull(device.ExpiredUTC);
        Assert.Equal($"Device was expired since it was not processed within {DeviceExpiration.TempUnprocessedDevicesExpiredAfterDays} days of being added.", device.ExpiredReason);
        Assert.Equal(string.Empty, device.CorporateIdentityID);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_WhenGetDevicesThrows_DoesNotDeleteCorpIDs()
    {
        var dbService = new FakeDbService { GetDevicesException = new Exception("boom") };
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(0, dbService.UpdateDeviceCallCount);
    }

    [Fact]
    public async Task ExpireDevices_OnCorpIDDeleteSuccess_MarksDeviceExpiredAndReleasesCounter()
    {
        var dbService = new FakeDbService
        {
            Counter = new CorpIDCounter(5)
        };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(1, graph.DeleteCallCount);
        Assert.NotNull(device.MarkedForExpirationUTC);
        Assert.NotNull(device.ExpiredUTC);
        Assert.True(device.ExpiredUTC >= device.MarkedForExpirationUTC);
        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.Equal($"Device was expired since it was processed over {DeviceExpiration.TempProcessedDevicesExpiredAfterDays} days ago.", device.ExpiredReason);
        Assert.Equal(string.Empty, device.CorporateIdentityID);
        Assert.Equal(1, dbService.UpdateDeviceCallCount);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_PersistsMarkedForExpirationInSingleUpdateAfterDeletingCorpID()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService();
        graph.OnDelete = () => Assert.Equal(0, dbService.UpdateDeviceCallCount);
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(1, dbService.UpdateDeviceCallCount);
        Assert.NotNull(dbService.FirstUpdateMarkedForExpirationUTC);
        Assert.NotNull(dbService.FirstUpdateExpiredUTC);
    }

    [Fact]
    public async Task ExpireDevices_OnCorpIDNotFound_MarksExpiredWithoutReleasingCounter()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.NotFound };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.NotNull(device.ExpiredUTC);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_WithNoCorpID_MarksExpiredWithoutCallingGraph()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice(corpIdentityID: string.Empty);
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.NotNull(device.ExpiredUTC);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    [Fact]
    public async Task ExpireDevices_OnCorpIDDeleteError_LeavesDeviceSyncedForRetry()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Error };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(DeviceStatus.Synced, device.Status);
        Assert.NotNull(device.MarkedForExpirationUTC);
        Assert.Null(device.ExpiredUTC);
        Assert.Equal(string.Empty, device.ExpiredReason);
        Assert.Equal("corp-id-1", device.CorporateIdentityID);
        Assert.Equal(1, device.ExpirationFailureCount);
        Assert.Equal(1, dbService.UpdateDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }


    [Fact]
    public async Task ExpireDevices_OnRetry_PreservesOriginalMarkedForExpirationUTC()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice();
        DateTime originalMark = DateTime.UtcNow.AddDays(-2);
        device.MarkedForExpirationUTC = originalMark;
        device.ExpirationFailureCount = 1;
        dbService.RetryDevicesToReturn.Add(device);
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireDevices();

        Assert.Equal(originalMark, device.MarkedForExpirationUTC);
        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.Equal(1, dbService.UpdateDeviceCallCount);
    }

    [Fact]
    public async Task ExpireDevices_OnRetryFailureBelowMax_IncrementsCountAndStaysSynced()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice();
        device.MarkedForExpirationUTC = DateTime.UtcNow.AddDays(-1);
        device.ExpirationFailureCount = DeviceExpiration.DefaultMaxExpirationRetries - 1;
        dbService.RetryDevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Error };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(DeviceExpiration.DefaultMaxExpirationRetries, device.ExpirationFailureCount);
        Assert.Equal(DeviceStatus.Synced, device.Status);
        Assert.Equal(1, dbService.UpdateDeviceCallCount);
    }

    [Fact]
    public async Task ExpireDevices_WhenRetriesExceedMax_MarksExpirationFailed()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        device.MarkedForExpirationUTC = DateTime.UtcNow.AddDays(-1);
        device.ExpirationFailureCount = DeviceExpiration.DefaultMaxExpirationRetries;
        dbService.RetryDevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Error };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(DeviceExpiration.DefaultMaxExpirationRetries + 1, device.ExpirationFailureCount);
        Assert.Equal(DeviceStatus.ExpirationFailed, device.Status);
        Assert.Null(device.ExpiredUTC);
        Assert.Equal("corp-id-1", device.CorporateIdentityID);
        Assert.Equal(1, dbService.UpdateDeviceCallCount);
        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_UsesBatchSizeSplitBetweenRetryAndNewDevices()
    {
        var dbService = new FakeDbService();
        for (int i = 0; i < 3; i++)
        {
            var retry = CreateProcessedDevice($"retry-{i}");
            retry.MarkedForExpirationUTC = DateTime.UtcNow.AddDays(-1);
            retry.ExpirationFailureCount = 1;
            dbService.RetryDevicesToReturn.Add(retry);
        }
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireDevices();

        int expectedRetryLimit = DeviceExpiration.DefaultBatchSize * DeviceExpiration.RetryBatchPercent / 100;
        Assert.Equal(expectedRetryLimit, dbService.LastRetryBatchSize);
        Assert.Equal(DeviceExpiration.DefaultBatchSize - 3, dbService.LastNewBatchSize);
    }

    [Fact]
    public async Task ExpireDevices_RespectsBatchSizeFromEnvironment()
    {
        Environment.SetEnvironmentVariable("ExpireDevicesBatchSize", "10");
        try
        {
            var dbService = new FakeDbService();
            for (int i = 0; i < 5; i++)
            {
                var retry = CreateProcessedDevice($"retry-{i}");
                retry.MarkedForExpirationUTC = DateTime.UtcNow.AddDays(-1);
                retry.ExpirationFailureCount = 1;
                dbService.RetryDevicesToReturn.Add(retry);
            }
            for (int i = 0; i < 20; i++)
            {
                dbService.DevicesToReturn.Add(CreateProcessedDevice($"new-{i}"));
            }
            var graph = new FakeGraphBetaService();
            var sut = CreateSut(dbService: dbService, graphBetaService: graph);
            sut.GetEnvironmentVariables();

            await sut.ExpireDevices();

            // 20% of 10 = 2 retries, remaining 8 new devices.
            Assert.Equal(2, dbService.LastRetryBatchSize);
            Assert.Equal(8, dbService.LastNewBatchSize);
            Assert.Equal(10, graph.DeleteCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ExpireDevicesBatchSize", null);
        }
    }

    [Fact]
    public async Task ExpireDevices_WhenRetryQueryThrows_StillProcessesNewDevices()
    {
        var dbService = new FakeDbService { GetRetryDevicesException = new Exception("boom") };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireDevices();

        Assert.Equal(DeviceStatus.Expired, device.Status);
        Assert.Equal(DeviceExpiration.DefaultBatchSize, dbService.LastNewBatchSize);
    }

    [Theory]
    [InlineData(null, null, DeviceExpiration.DefaultBatchSize, DeviceExpiration.DefaultMaxExpirationRetries)]
    [InlineData("abc", "xyz", DeviceExpiration.DefaultBatchSize, DeviceExpiration.DefaultMaxExpirationRetries)]
    [InlineData("0", "-1", DeviceExpiration.DefaultBatchSize, DeviceExpiration.DefaultMaxExpirationRetries)]
    [InlineData("250", "3", 250, 3)]
    [InlineData("250", "0", 250, 0)]
    public void GetEnvironmentVariables_ParsesBatchSizeAndMaxRetries(string? batch, string? retries, int expectedBatch, int expectedRetries)
    {
        Environment.SetEnvironmentVariable("ExpireDevicesBatchSize", batch);
        Environment.SetEnvironmentVariable("MAX_EXPIRATION_RETRIES", retries);
        try
        {
            var sut = CreateSut();
            sut.GetEnvironmentVariables();

            Assert.Equal(expectedBatch, sut.BatchSize);
            Assert.Equal(expectedRetries, sut.MaxExpirationRetries);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ExpireDevicesBatchSize", null);
            Environment.SetEnvironmentVariable("MAX_EXPIRATION_RETRIES", null);
        }
    }

    [Fact]
    public async Task ExpireDevices_NewDevice_DoesNotUpdateDbBeforeDeletingCorpID()
    {
        var dbService = new FakeDbService();
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        int updatesBeforeDelete = -1;
        var graph = new FakeGraphBetaService();
        graph.OnDelete = () => updatesBeforeDelete = dbService.UpdateDeviceCallCount;
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(1, graph.DeleteCallCount);
        Assert.Equal(0, updatesBeforeDelete);
    }

    [Fact]
    public async Task ExpireDevices_WhenFinalUpdateFails_DoesNotReleaseDeletedCorpID()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5), FailUpdateOnCall = 1 };
        dbService.DevicesToReturn.Add(CreateProcessedDevice());
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireDevices();

        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    private static Microsoft.Azure.Cosmos.CosmosException Cosmos(System.Net.HttpStatusCode code)
        => new("simulated", code, 0, "activity", 0);

    [Fact]
    public async Task ExpireDevices_ReReadShowsRecentlyProcessed_SkipsWithoutDeleting()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var queried = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(queried);
        var fresh = CreateProcessedDevice();
        fresh.Id = queried.Id;
        fresh.SuccessfullyProcessedUTC = DateTime.UtcNow;
        dbService.OnGetDevice = _ => fresh;
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(0, dbService.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.Synced, fresh.Status);
        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_ReReadShowsRecentEnrollment_UnprocessedDeviceSkipped()
    {
        var dbService = new FakeDbService();
        var device = CreateUnprocessedDevice();
        device.LastSeenEnrollmentUTC = DateTime.UtcNow.AddDays(-1);
        dbService.DevicesToReturn.Add(device);
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(DeviceStatus.Synced, device.Status);
    }

    [Fact]
    public async Task ExpireDevices_ReReadReturnsNull_Skips()
    {
        var dbService = new FakeDbService { OnGetDevice = _ => null };
        dbService.DevicesToReturn.Add(CreateProcessedDevice());
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(0, graph.DeleteCallCount);
        Assert.Equal(0, dbService.UpdateDeviceCallCount);
    }

    [Fact]
    public async Task ExpireDevices_ReReadThrows_SkipsWithoutDeleting()
    {
        var dbService = new FakeDbService { OnGetDevice = _ => throw new Exception("read failed") };
        dbService.DevicesToReturn.Add(CreateProcessedDevice());
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(0, graph.DeleteCallCount);
    }

    [Fact]
    public async Task ExpireDevices_PreconditionFailed_StillEligible_RetriesAndExpires()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var fresh = CreateProcessedDevice();
        fresh.Id = device.Id;
        int reads = 0;
        dbService.OnGetDevice = _ => ++reads == 1 ? device : fresh;
        dbService.OnUpdate = d => ReferenceEquals(d, device) ? Cosmos(System.Net.HttpStatusCode.PreconditionFailed) : null;
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireDevices();

        Assert.Equal(2, dbService.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.Expired, fresh.Status);
        Assert.Equal(string.Empty, fresh.CorporateIdentityID);
        Assert.Equal(device.MarkedForExpirationUTC, fresh.MarkedForExpirationUTC);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    private static (FakeDbService db, Device fresh) SetupReEnrolledDuringExpiration(Func<Device, Exception?>? onFreshUpdate = null)
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var fresh = CreateProcessedDevice();
        fresh.Id = device.Id;
        fresh.ProcessingStatus = null;
        fresh.SuccessfullyProcessedUTC = null;
        fresh.LastSeenEnrollmentUTC = DateTime.UtcNow;
        fresh.OS = DeviceOS.Windows;
        int reads = 0;
        dbService.OnGetDevice = _ => ++reads == 1 ? device : fresh;
        dbService.OnUpdate = d => ReferenceEquals(d, device)
            ? Cosmos(System.Net.HttpStatusCode.PreconditionFailed)
            : onFreshUpdate?.Invoke(d);
        return (dbService, fresh);
    }

    [Fact]
    public async Task ExpireDevices_PreconditionFailed_DeviceReEnrolled_ReAddsCorpIDAndDoesNotRelease()
    {
        var (dbService, fresh) = SetupReEnrolledDuringExpiration();
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(1, graph.AddCallCount);
        Assert.Equal(2, dbService.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.Synced, fresh.Status);
        Assert.Equal("readded-id", fresh.CorporateIdentityID);
        Assert.NotNull(fresh.LastCorpIdentitySync);
        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_PreconditionFailed_DeviceReEnrolled_ReAddFails_LeavesForConfirmSync()
    {
        var (dbService, fresh) = SetupReEnrolledDuringExpiration();
        var graph = new FakeGraphBetaService { AddException = new Exception("graph down") };
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(1, dbService.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.Synced, fresh.Status);
        Assert.Equal("corp-id-1", fresh.CorporateIdentityID);
        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_PreconditionFailed_DeviceReEnrolled_ReAddSaveFails_RollsBack()
    {
        var (dbService, _) = SetupReEnrolledDuringExpiration(_ => new Exception("save failed"));
        var graph = new FakeGraphBetaService();
        var sut = CreateSut(dbService: dbService, graphBetaService: graph);

        await sut.ExpireDevices();

        Assert.Equal(1, graph.AddCallCount);
        Assert.Equal(new[] { "corp-id-1", "readded-id" }, graph.DeletedIds);
        Assert.Equal(5, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public void GetCorpIdentifier_FormatsByOS()
    {
        var device = new Device { Make = "M", Model = "X", SerialNumber = "SN", OS = DeviceOS.Windows };
        Assert.Equal("\"M\",\"X\",SN", CorpIDUtilities.GetCorpIdentifier(device));
        device.OS = DeviceOS.iOS;
        Assert.Equal("SN", CorpIDUtilities.GetCorpIdentifier(device));
    }

    [Theory]
    [InlineData(DeviceStatus.Deleting)]
    [InlineData(DeviceStatus.NonSyncing)]
    public async Task ExpireDevices_PreconditionFailed_DeviceNoLongerSynced_ReleasesWithoutUpdate(DeviceStatus freshStatus)
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        var device = CreateProcessedDevice();
        dbService.DevicesToReturn.Add(device);
        var fresh = CreateProcessedDevice();
        fresh.Id = device.Id;
        fresh.Status = freshStatus;
        int reads = 0;
        dbService.OnGetDevice = _ => ++reads == 1 ? device : fresh;
        dbService.OnUpdate = _ => Cosmos(System.Net.HttpStatusCode.PreconditionFailed);
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireDevices();

        Assert.Equal(1, dbService.UpdateDeviceCallCount);
        Assert.Equal(freshStatus, fresh.Status);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    [Fact]
    public async Task ExpireDevices_UpdateNotFound_ReleasesDeletedCorpID()
    {
        var dbService = new FakeDbService { Counter = new CorpIDCounter(5) };
        dbService.DevicesToReturn.Add(CreateProcessedDevice());
        dbService.OnUpdate = _ => Cosmos(System.Net.HttpStatusCode.NotFound);
        var sut = CreateSut(dbService: dbService);

        await sut.ExpireDevices();

        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    [Theory]
    [InlineData(DeviceStatus.Synced, ProcessingStatus.Processed, -400, null, null, true)]
    [InlineData(DeviceStatus.Synced, ProcessingStatus.Processed, -1, null, null, false)]
    [InlineData(DeviceStatus.Expired, ProcessingStatus.Processed, -400, null, null, false)]
    [InlineData(DeviceStatus.Synced, null, null, -400, null, true)]
    [InlineData(DeviceStatus.Synced, null, null, -400, -400, true)]
    [InlineData(DeviceStatus.Synced, null, null, -400, -1, false)]
    [InlineData(DeviceStatus.Synced, null, null, -1, null, false)]
    public void IsEligibleForExpiration_MatchesQueryRules(DeviceStatus status, ProcessingStatus? processing, int? processedDaysAgo, int? modifiedDaysAgo, int? enrolledDaysAgo, bool expected)
    {
        var now = DateTime.UtcNow;
        var device = new Device
        {
            Status = status,
            ProcessingStatus = processing,
            SuccessfullyProcessedUTC = processedDaysAgo is null ? null : now.AddDays(processedDaysAgo.Value),
            ModifiedUTC = modifiedDaysAgo is null ? now : now.AddDays(modifiedDaysAgo.Value),
            LastSeenEnrollmentUTC = enrolledDaysAgo is null ? null : now.AddDays(enrolledDaysAgo.Value)
        };

        Assert.Equal(expected, DeviceExpiration.IsEligibleForExpiration(device, now.AddDays(-180), now.AddDays(-180)));
    }

    [Fact]
    public async Task ExpireDevices_MultipleDevices_ReleasesOnlySuccessfulDeletes()
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

        await sut.ExpireDevices();

        Assert.Equal(DeviceStatus.Expired, ok1.Status);
        Assert.Equal(DeviceStatus.Synced, bad.Status);
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
            DeletedIds.Add(identifierID);
            OnDelete?.Invoke();
            return Task.FromResult(ResultsById.TryGetValue(identifierID, out var r) ? r : DeleteResult);
        }

        public Exception? AddException { get; set; }
        public int AddCallCount { get; private set; }
        public string? LastAddedIdentifier { get; private set; }
        public List<string> DeletedIds { get; } = new();

        public Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier)
        {
            AddCallCount++;
            LastAddedIdentifier = identifier;
            if (AddException is not null) throw AddException;
            return Task.FromResult(new ImportedDeviceIdentity { Id = "readded-id", ImportedDeviceIdentifier = identifier });
        }
        public Task<bool> CorporateIdentifierExists(string identiferID) => throw new NotImplementedException();
        public Task<int> GetCorporateDeviceIdentifierCountAsync() => throw new NotImplementedException();
    }

    private sealed class FakeDbService : ICosmosDbService
    {
        public List<Device> DevicesToReturn { get; set; } = new();
        public List<Device> RetryDevicesToReturn { get; set; } = new();
        public Exception? GetDevicesException { get; set; }
        public Exception? GetRetryDevicesException { get; set; }
        public Exception? UpdateDeviceException { get; set; }
        public int? FailUpdateOnCall { get; set; }
        public Func<Device, Exception?>? OnUpdate { get; set; }
        public Func<Guid, Device?>? OnGetDevice { get; set; }
        public int GetDeviceCallCount { get; private set; }
        public CorpIDCounter Counter { get; set; } = new CorpIDCounter(0);

        public int GetDevicesCallCount { get; private set; }
        public int GetRetryDevicesCallCount { get; private set; }
        public int? LastNewBatchSize { get; private set; }
        public int? LastRetryBatchSize { get; private set; }
        public int UpdateDeviceCallCount { get; private set; }
        public int TrySetCorpIDCounterCallCount { get; private set; }
        public DateTime? LastProcessedCutoff { get; private set; }
        public DateTime? LastAddedCutoff { get; private set; }
        public DateTime? LastRetryProcessedCutoff { get; private set; }
        public DateTime? LastRetryAddedCutoff { get; private set; }
        public DateTime? FirstUpdateMarkedForExpirationUTC { get; private set; }
        public DateTime? FirstUpdateExpiredUTC { get; private set; }

        public Task<List<Device>> GetDevicesToExpire(DateTime processedBeforeUTC, DateTime addedBeforeUTC, int batchSize)
        {
            GetDevicesCallCount++;
            LastProcessedCutoff = processedBeforeUTC;
            LastAddedCutoff = addedBeforeUTC;
            LastNewBatchSize = batchSize;
            if (GetDevicesException is not null) throw GetDevicesException;
            return Task.FromResult(DevicesToReturn.Where(d => d.ExpirationFailureCount == 0).Take(Math.Max(0, batchSize)).ToList());
        }

        public Task<List<Device>> GetDevicesToRetryExpiration(DateTime processedBeforeUTC, DateTime addedBeforeUTC, int batchSize)
        {
            GetRetryDevicesCallCount++;
            LastRetryProcessedCutoff = processedBeforeUTC;
            LastRetryAddedCutoff = addedBeforeUTC;
            LastRetryBatchSize = batchSize;
            if (GetRetryDevicesException is not null) throw GetRetryDevicesException;
            return Task.FromResult(RetryDevicesToReturn.Where(d => d.ExpirationFailureCount > 0).Take(Math.Max(0, batchSize)).ToList());
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
            var updateEx = OnUpdate?.Invoke(device);
            if (updateEx is not null) throw updateEx;
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
        public Task<Device?> GetDevice(Guid id, string partitionKey)
        {
            GetDeviceCallCount++;
            if (OnGetDevice is not null) return Task.FromResult(OnGetDevice(id));
            return Task.FromResult(DevicesToReturn.Concat(RetryDevicesToReturn).FirstOrDefault(d => d.Id == id));
        }
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

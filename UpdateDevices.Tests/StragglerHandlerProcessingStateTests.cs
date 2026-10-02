using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using UpdateDevices.Interfaces;
using UpdateDevices.Models;
using Device = DelegationStationShared.Models.Device;
using ManagedDevice = Microsoft.Graph.Models.ManagedDevice;

namespace UpdateDevices.Tests;

public class StragglerHandlerProcessingStateTests
{
    private const string TagId = "tag-1";
    private const string ManagedDeviceId = "11111111-1111-1111-1111-111111111111";
    private const string EntraObjectId = "22222222-2222-2222-2222-222222222222";
    private static readonly DateTimeOffset BaselineEnrollment = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

    private sealed record ProcessingStateSnapshot(
        ProcessingStatus? ProcessingStatus,
        DateTime? LastProcessingAttemptUTC,
        DateTime? SuccessfullyProcessedUTC,
        DateTime? LastSeenEnrollmentUTC,
        DateTime? MarkedForExpirationUTC);

    private sealed class FakeDbService : ICosmosDbService
    {
        public Device? Device { get; set; }
        public DeviceTag? Tag { get; set; }
        public List<Straggler> Stragglers { get; } = new();
        public List<ProcessingStateSnapshot> ProcessingUpdates { get; } = new();
        public int DeletedStragglerCount { get; private set; }
        public int UpdatedStragglerErrorCount { get; private set; }
        public int GetDeviceTagCallCount { get; private set; }
        public Func<Device, bool>? OnUpdateDeviceProcessingState { get; set; }

        public Task<FunctionSettings> GetFunctionSettings() => throw new NotImplementedException();
        public Task UpdateFunctionSettings(DateTime thisRun) => throw new NotImplementedException();
        public Task<Device> GetDevice(string make, string model, string serialNumber) => Task.FromResult(Device!);

        public Task<DeviceTag> GetDeviceTag(string tagId)
        {
            GetDeviceTagCallCount++;
            return Task.FromResult(Tag!);
        }

        public Task AddOrUpdateStraggler(ManagedDevice managedDevice) => throw new NotImplementedException();
        public Task<Straggler> GetStraggler(string managedDeviceId) => throw new NotImplementedException();
        public Task<List<Straggler>> GetStragglerList(int minCount) => Task.FromResult(Stragglers.ToList());
        public Task UpdateStraggler(Straggler straggler) => throw new NotImplementedException();

        public Task DeleteStraggler(Straggler straggler)
        {
            DeletedStragglerCount++;
            Stragglers.Remove(straggler);
            return Task.CompletedTask;
        }

        public Task UpdateStragglerAsErrored(Straggler straggler)
        {
            UpdatedStragglerErrorCount++;
            return Task.CompletedTask;
        }

        public Task<List<Straggler>> GetStragglersProcessedByUD(int minCount) => throw new NotImplementedException();

        public Task<bool> UpdateDeviceProcessingState(Device device)
        {
            if (OnUpdateDeviceProcessingState != null && !OnUpdateDeviceProcessingState(device))
            {
                return Task.FromResult(false);
            }

            ProcessingUpdates.Add(new ProcessingStateSnapshot(
                device.ProcessingStatus,
                device.LastProcessingAttemptUTC,
                device.SuccessfullyProcessedUTC,
                device.LastSeenEnrollmentUTC, device.MarkedForExpirationUTC));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeGraphService : IGraphService
    {
        public ManagedDevice ManagedDevice { get; set; } = CreateManagedDevice();
        public List<DeviceUpdateAction> GroupsAdded { get; } = new();
        public List<DeviceUpdateAction> AdminUnitsAdded { get; } = new();
        public List<List<DeviceUpdateAction>> AttributeUpdates { get; } = new();
        public int GetDeviceObjectIdCallCount { get; private set; }
        public Func<DeviceUpdateAction, bool> GroupResult { get; set; } = _ => true;
        public Func<DeviceUpdateAction, bool> AdminUnitResult { get; set; } = _ => true;
        public Func<List<DeviceUpdateAction>, bool> AttributeResult { get; set; } = _ => true;

        public Task<ManagedDevice> GetManagedDevice(string deviceID) => Task.FromResult(ManagedDevice);
        public Task<List<ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime) => throw new NotImplementedException();

        public Task<string> GetDeviceObjectID(string azureADDeviceID)
        {
            GetDeviceObjectIdCallCount++;
            return Task.FromResult(EntraObjectId);
        }

        public Task<bool> AddDeviceToAzureADGroup(string deviceId, string deviceObjectId, DeviceUpdateAction group)
        {
            GroupsAdded.Add(group);
            return Task.FromResult(GroupResult(group));
        }

        public Task<bool> AddDeviceToAzureAdministrativeUnit(string deviceId, string deviceObjectId, DeviceUpdateAction adminUnit)
        {
            AdminUnitsAdded.Add(adminUnit);
            return Task.FromResult(AdminUnitResult(adminUnit));
        }

        public Task<bool> UpdateAttributesOnDeviceAsync(string deviceId, string deviceObjectId, List<DeviceUpdateAction> deviceUpdateActions)
        {
            AttributeUpdates.Add(deviceUpdateActions);
            return Task.FromResult(AttributeResult(deviceUpdateActions));
        }
    }

    private sealed class FakeGraphBetaService : IGraphBetaService
    {
        public List<(string DeviceId, string Hostname)> Renames { get; } = new();
        public Func<string, string, Task<bool>> OnSetDeviceName { get; set; } = (_, _) => Task.FromResult(true);

        public Task<bool> SetDeviceName(string managedDeviceID, string newHostname)
        {
            Renames.Add((managedDeviceID, newHostname));
            return OnSetDeviceName(managedDeviceID, newHostname);
        }
    }

    private sealed class TestContext
    {
        public FakeDbService Db { get; } = new();
        public FakeGraphService Graph { get; } = new();
        public FakeGraphBetaService GraphBeta { get; } = new();

        public TestContext(DeviceTag tag)
        {
            Db.Tag = tag;
            Db.Device = CreateDbDevice();
            Db.Stragglers.Add(new Straggler
            {
                ManagedDeviceID = ManagedDeviceId,
                EnrollmentDateTime = BaselineEnrollment.UtcDateTime,
            });
        }

        public Task RunAsync()
        {
            var loggerFactory = LoggerFactory.Create(_ => { });
            var handler = new StragglerHandler(loggerFactory, Db, Graph, GraphBeta);
            var timer = new TimerInfo { ScheduleStatus = new ScheduleStatus { Next = DateTime.UtcNow.AddHours(1) } };
            return handler.RunAsync(timer);
        }
    }

    private static Device CreateDbDevice() => new()
    {
        Make = "Contoso",
        Model = "Laptop 1",
        SerialNumber = "SN123",
        PreferredHostname = "HOST-001",
        Tags = new List<string> { TagId },
        OS = DeviceOS.Windows,
    };

    private static ManagedDevice CreateManagedDevice() => new()
    {
        Id = ManagedDeviceId,
        Manufacturer = "Contoso",
        Model = "Laptop 1",
        SerialNumber = "SN123",
        AzureADDeviceId = "33333333-3333-3333-3333-333333333333",
        DeviceName = "DESKTOP-OLD",
        EnrolledDateTime = BaselineEnrollment,
    };

    private static DeviceTag CreateTag(params DeviceUpdateAction[] actions) => new()
    {
        Name = "Test Tag",
        UpdateActions = actions.ToList(),
        DeviceRenameEnabled = false,
    };

    private static DeviceUpdateAction Group(string name) =>
        new(Guid.NewGuid(), DeviceUpdateActionType.Group, name, Guid.NewGuid().ToString());

    private static DeviceUpdateAction AdminUnit(string name) =>
        new(Guid.NewGuid(), DeviceUpdateActionType.AdministrativeUnit, name, Guid.NewGuid().ToString());

    private static DeviceUpdateAction Attribute(string name, string value) =>
        new(Guid.NewGuid(), DeviceUpdateActionType.Attribute, name, value);

    private static void AssertMarkedProcessed(FakeDbService db)
    {
        ProcessingStateSnapshot update = Assert.Single(db.ProcessingUpdates);
        Assert.Equal(ProcessingStatus.Processed, update.ProcessingStatus);
        Assert.NotNull(update.LastProcessingAttemptUTC);
        Assert.Equal(update.LastProcessingAttemptUTC, update.SuccessfullyProcessedUTC);
        Assert.Equal(update.SuccessfullyProcessedUTC!.Value.AddDays(
            DelegationSharedLibrary.Models.SystemSettings.DefaultProcessedDevicesExpiredAfterDays), update.MarkedForExpirationUTC);
    }

    private static void AssertAttemptRecordedButNotProcessed(FakeDbService db)
    {
        ProcessingStateSnapshot update = Assert.Single(db.ProcessingUpdates);
        Assert.NotEqual(ProcessingStatus.Processed, update.ProcessingStatus);
        Assert.NotNull(update.LastProcessingAttemptUTC);
        Assert.Null(update.SuccessfullyProcessedUTC);
        Assert.Equal(db.Device!.MarkedForExpirationUTC, update.MarkedForExpirationUTC);
    }

    public enum FailingAction
    {
        Group,
        AdministrativeUnit,
        Attribute,
    }

    [Fact]
    public async Task Run_AllConfiguredActionsSucceed_MarksProcessedAndDeletesStraggler()
    {
        var context = new TestContext(CreateTag(
            Group("Group A"),
            AdminUnit("AU A"),
            Attribute("ExtensionAttribute1", "value-1")));

        await context.RunAsync();

        AssertMarkedProcessed(context.Db);
        Assert.Equal(BaselineEnrollment.UtcDateTime, Assert.Single(context.Db.ProcessingUpdates).LastSeenEnrollmentUTC);
        Assert.Single(context.Graph.GroupsAdded);
        Assert.Single(context.Graph.AdminUnitsAdded);
        List<DeviceUpdateAction> attributes = Assert.Single(context.Graph.AttributeUpdates);
        Assert.Equal("ExtensionAttribute1", Assert.Single(attributes).Name);
        Assert.Equal(1, context.Db.DeletedStragglerCount);
        Assert.Empty(context.Db.Stragglers);
        Assert.Equal(0, context.Db.UpdatedStragglerErrorCount);
    }

    [Theory]
    [InlineData(FailingAction.Group)]
    [InlineData(FailingAction.AdministrativeUnit)]
    [InlineData(FailingAction.Attribute)]
    public async Task Run_ActionReturnsFalse_RecordsFailedAttemptAndRetainsStragglerForRetry(FailingAction failing)
    {
        var context = new TestContext(CreateTag(Group("Group A"), AdminUnit("AU A"), Attribute("ExtensionAttribute1", "value-1")));
        switch (failing)
        {
            case FailingAction.Group:
                context.Graph.GroupResult = _ => false;
                break;
            case FailingAction.AdministrativeUnit:
                context.Graph.AdminUnitResult = _ => false;
                break;
            case FailingAction.Attribute:
                context.Graph.AttributeResult = _ => false;
                break;
        }

        await context.RunAsync();

        AssertAttemptRecordedButNotProcessed(context.Db);
        Assert.Equal(BaselineEnrollment.UtcDateTime, Assert.Single(context.Db.ProcessingUpdates).LastSeenEnrollmentUTC);
        Assert.Single(context.Graph.GroupsAdded);
        Assert.Single(context.Graph.AdminUnitsAdded);
        Assert.Single(context.Graph.AttributeUpdates);
        Assert.Equal(0, context.Db.DeletedStragglerCount);
        Assert.Equal(1, context.Db.UpdatedStragglerErrorCount);
        Assert.Single(context.Db.Stragglers);
    }

    [Fact]
    public async Task Run_DeviceHasNoTags_RecordsFailedAttemptAndDoesNotCallGraphActions()
    {
        var context = new TestContext(CreateTag(Group("Group A")));
        context.Db.Device!.Tags = new List<string>();

        await context.RunAsync();

        AssertAttemptRecordedButNotProcessed(context.Db);
        Assert.Equal(0, context.Graph.GetDeviceObjectIdCallCount);
        Assert.Equal(0, context.Db.GetDeviceTagCallCount);
        Assert.Empty(context.Graph.GroupsAdded);
        Assert.Empty(context.Graph.AdminUnitsAdded);
        Assert.Empty(context.Graph.AttributeUpdates);
        Assert.Equal(1, context.Db.UpdatedStragglerErrorCount);
    }

    [Fact]
    public async Task Run_NoAttributeActions_DoesNotCallAttributeUpdate()
    {
        var context = new TestContext(CreateTag(Group("Group A")));

        await context.RunAsync();

        AssertMarkedProcessed(context.Db);
        Assert.Empty(context.Graph.AttributeUpdates);
    }

    [Fact]
    public async Task Run_TagHasNoUpdateActions_MarksDeviceProcessed()
    {
        var context = new TestContext(CreateTag());

        await context.RunAsync();

        AssertMarkedProcessed(context.Db);
        Assert.Empty(context.Graph.GroupsAdded);
        Assert.Empty(context.Graph.AdminUnitsAdded);
        Assert.Empty(context.Graph.AttributeUpdates);
        Assert.Equal(1, context.Db.DeletedStragglerCount);
        Assert.Empty(context.Db.Stragglers);
    }

    [Fact]
    public async Task Run_DeviceIsDeleting_SkipsActionsAndProcessingState()
    {
        DeviceTag tag = CreateTag(Group("Group A"));
        tag.DeviceRenameEnabled = true;
        var context = new TestContext(tag);
        context.Db.Device!.Status = DeviceStatus.Deleting;

        await context.RunAsync();

        Assert.Empty(context.Db.ProcessingUpdates);
        Assert.Empty(context.Graph.GroupsAdded);
        Assert.Equal(0, context.Graph.GetDeviceObjectIdCallCount);
        Assert.Equal(0, context.Db.GetDeviceTagCallCount);
        Assert.Empty(context.GraphBeta.Renames);
        Assert.Equal(1, context.Db.DeletedStragglerCount);
        Assert.Empty(context.Db.Stragglers);
    }

    [Fact]
    public async Task Run_RenameConfigured_AppliesRenameAndMarksProcessed()
    {
        DeviceTag tag = CreateTag(Group("Group A"));
        tag.DeviceRenameEnabled = true;
        var context = new TestContext(tag);

        await context.RunAsync();

        AssertMarkedProcessed(context.Db);
        Assert.Equal((ManagedDeviceId, "HOST-001"), Assert.Single(context.GraphBeta.Renames));
    }

    [Fact]
    public async Task Run_RenameReturnsFalse_OtherActionsCanStillMarkDeviceProcessed()
    {
        DeviceTag tag = CreateTag(Group("Group A"));
        tag.DeviceRenameEnabled = true;
        var context = new TestContext(tag);
        context.GraphBeta.OnSetDeviceName = (_, _) => Task.FromResult(false);

        await context.RunAsync();

        AssertMarkedProcessed(context.Db);
        Assert.Single(context.Graph.GroupsAdded);
        Assert.Single(context.GraphBeta.Renames);
        Assert.Equal(1, context.Db.DeletedStragglerCount);
        Assert.Empty(context.Db.Stragglers);
    }

    [Fact]
    public async Task Run_EnrollmentTimestampChanged_ClearsPriorSuccessAndStoresCurrentEnrollment()
    {
        var context = new TestContext(CreateTag(Group("Group A")));
        context.Db.Device!.ProcessingStatus = ProcessingStatus.Processed;
        context.Db.Device.SuccessfullyProcessedUTC = DateTime.UtcNow.AddDays(-1);
        DateTime scheduledExpiration = DateTime.UtcNow.AddDays(179);
        context.Db.Device.MarkedForExpirationUTC = scheduledExpiration;
        context.Db.Device.LastSeenEnrollmentUTC = BaselineEnrollment.UtcDateTime;
        context.Graph.ManagedDevice.EnrolledDateTime = BaselineEnrollment.AddDays(45);
        context.Graph.GroupResult = _ => false;

        await context.RunAsync();

        ProcessingStateSnapshot update = Assert.Single(context.Db.ProcessingUpdates);
        Assert.Null(update.ProcessingStatus);
        Assert.Equal(scheduledExpiration, update.MarkedForExpirationUTC);
        Assert.Null(update.SuccessfullyProcessedUTC);
        Assert.Equal(BaselineEnrollment.AddDays(45).UtcDateTime, update.LastSeenEnrollmentUTC);
        Assert.NotNull(update.LastProcessingAttemptUTC);
    }

    [Fact]
    public async Task Run_OlderEnrollmentThanStored_RemovesStragglerWithoutUpdatingDevice()
    {
        DeviceTag tag = CreateTag(Group("Group A"), AdminUnit("AU A"), Attribute("ExtensionAttribute1", "value-1"));
        tag.DeviceRenameEnabled = true;
        var context = new TestContext(tag);
        Device storedDevice = context.Db.Device!;
        DateTime newerEnrollment = BaselineEnrollment.AddHours(4).UtcDateTime;
        DateTime lastAttempt = newerEnrollment.AddMinutes(1);
        DateTime successfulProcessing = newerEnrollment.AddMinutes(2);
        storedDevice.LastSeenEnrollmentUTC = newerEnrollment;
        storedDevice.LastProcessingAttemptUTC = lastAttempt;
        storedDevice.SuccessfullyProcessedUTC = successfulProcessing;
        storedDevice.ProcessingStatus = ProcessingStatus.Processed;

        await context.RunAsync();

        Assert.Empty(context.Db.Stragglers);
        Assert.Equal(1, context.Db.DeletedStragglerCount);
        Assert.Equal(0, context.Db.UpdatedStragglerErrorCount);
        Assert.Empty(context.Db.ProcessingUpdates);
        Assert.Equal(newerEnrollment, storedDevice.LastSeenEnrollmentUTC);
        Assert.Equal(lastAttempt, storedDevice.LastProcessingAttemptUTC);
        Assert.Equal(successfulProcessing, storedDevice.SuccessfullyProcessedUTC);
        Assert.Equal(ProcessingStatus.Processed, storedDevice.ProcessingStatus);
        Assert.Equal(0, context.Graph.GetDeviceObjectIdCallCount);
        Assert.Equal(0, context.Db.GetDeviceTagCallCount);
        Assert.Empty(context.Graph.GroupsAdded);
        Assert.Empty(context.Graph.AdminUnitsAdded);
        Assert.Empty(context.Graph.AttributeUpdates);
        Assert.Empty(context.GraphBeta.Renames);
    }

    [Fact]
    public async Task Run_NewerEnrollmentSavedDuringAttempt_DoesNotTreatRejectedStateWriteAsSuccess()
    {
        var context = new TestContext(CreateTag(Group("Group A")));
        DateTime newerEnrollment = BaselineEnrollment.AddHours(4).UtcDateTime;
        context.Db.OnUpdateDeviceProcessingState = device =>
        {
            context.Db.Device!.LastSeenEnrollmentUTC = newerEnrollment;
            return false;
        };

        await context.RunAsync();

        Assert.Empty(context.Db.ProcessingUpdates);
        Assert.Single(context.Db.Stragglers);
        Assert.Equal(0, context.Db.DeletedStragglerCount);
        Assert.Equal(1, context.Db.UpdatedStragglerErrorCount);
        Assert.Equal(newerEnrollment, context.Db.Device!.LastSeenEnrollmentUTC);

        await context.RunAsync();

        Assert.Empty(context.Db.ProcessingUpdates);
        Assert.Empty(context.Db.Stragglers);
        Assert.Equal(1, context.Db.DeletedStragglerCount);
        Assert.Equal(1, context.Db.UpdatedStragglerErrorCount);
        Assert.Single(context.Graph.GroupsAdded);
    }

    [Fact]
    public async Task Run_ManagedDeviceWithoutEnrollmentTimestamp_RecordsNoProcessingStateAndRetries()
    {
        var context = new TestContext(CreateTag(Group("Group A")));
        context.Graph.ManagedDevice.EnrolledDateTime = null;

        await context.RunAsync();

        Assert.Empty(context.Db.ProcessingUpdates);
        Assert.Equal(0, context.Graph.GetDeviceObjectIdCallCount);
        Assert.Equal(0, context.Db.GetDeviceTagCallCount);
        Assert.Equal(1, context.Db.UpdatedStragglerErrorCount);
        Assert.Equal(0, context.Db.DeletedStragglerCount);
        Assert.Single(context.Db.Stragglers);
    }
}

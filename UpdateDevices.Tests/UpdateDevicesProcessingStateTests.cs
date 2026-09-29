using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateDevices.Interfaces;
using UpdateDevices.Models;
using Device = DelegationStationShared.Models.Device;
using ManagedDevice = Microsoft.Graph.Models.ManagedDevice;

namespace UpdateDevices.Tests;

public class UpdateDevicesProcessingStateTests
{
    private const string TagId = "tag-1";
    private const string ManagedDeviceId = "11111111-1111-1111-1111-111111111111";
    private const string EntraObjectId = "22222222-2222-2222-2222-222222222222";

    // ─── fakes ──────────────────────────────────────────────────────────────

    private sealed record ProcessingStateSnapshot(
        string SerialNumber,
        ProcessingStatus? ProcessingStatus,
        DateTime? LastProcessingAttemptUTC,
        DateTime? SuccessfullyProcessedUTC);

    private sealed class FakeDbService : ICosmosDbService
    {
        public Device? Device { get; set; }
        public DeviceTag? Tag { get; set; }
        public List<ProcessingStateSnapshot> ProcessingUpdates { get; } = new();
        public DateTime? SavedLastRun { get; private set; }

        public Task<FunctionSettings> GetFunctionSettings() => Task.FromResult(new FunctionSettings());

        public Task UpdateFunctionSettings(DateTime thisRun)
        {
            SavedLastRun = thisRun;
            return Task.CompletedTask;
        }

        public Func<string, string, string, Task<Device>>? OnGetDevice { get; set; }
        public int GetDeviceTagCallCount { get; private set; }

        public Task<Device> GetDevice(string make, string model, string serialNumber) =>
            OnGetDevice != null ? OnGetDevice(make, model, serialNumber) : Task.FromResult(Device!);

        public Task<bool> UpdateDeviceProcessingState(Device device)
        {
            ProcessingUpdates.Add(new ProcessingStateSnapshot(
                device.SerialNumber,
                device.ProcessingStatus, device.LastProcessingAttemptUTC, device.SuccessfullyProcessedUTC));
            return Task.FromResult(true);
        }

        public Task<DeviceTag> GetDeviceTag(string tagId)
        {
            GetDeviceTagCallCount++;
            return Task.FromResult(Tag!);
        }

        public Task AddOrUpdateStraggler(ManagedDevice managedDevice) => throw new NotImplementedException();
        public Task<Straggler> GetStraggler(string managedDeviceId) => throw new NotImplementedException();
        public Task<List<Straggler>> GetStragglerList(int minCount) => throw new NotImplementedException();
        public Task UpdateStraggler(Straggler straggler) => throw new NotImplementedException();
        public Task DeleteStraggler(Straggler straggler) => throw new NotImplementedException();
        public Task UpdateStragglerAsErrored(Straggler straggler) => throw new NotImplementedException();
        public Task<List<Straggler>> GetStragglersProcessedByUD(int minCount) => throw new NotImplementedException();
    }

    private sealed class FakeGraphService : IGraphService
    {
        public List<ManagedDevice> ManagedDevices { get; } = new();
        public List<DeviceUpdateAction> GroupsAdded { get; } = new();
        public List<DeviceUpdateAction> AdminUnitsAdded { get; } = new();
        public List<List<DeviceUpdateAction>> AttributeUpdates { get; } = new();
        public int GetDeviceObjectIdCallCount { get; private set; }

        public Func<DeviceUpdateAction, bool> GroupResult { get; set; } = _ => true;
        public Func<DeviceUpdateAction, bool> AdminUnitResult { get; set; } = _ => true;
        public Func<List<DeviceUpdateAction>, bool> AttributeResult { get; set; } = _ => true;

        public int TotalActionCalls => GroupsAdded.Count + AdminUnitsAdded.Count + AttributeUpdates.Count;

        public Task<List<ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime) => Task.FromResult(ManagedDevices);

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

        public Task<ManagedDevice> GetManagedDevice(string deviceID) => throw new NotImplementedException();
    }

    private sealed class FakeGraphBetaService : IGraphBetaService
    {
        public Func<string, string, Task<bool>> OnSetDeviceName { get; set; } = (_, _) => Task.FromResult(true);
        public int SetDeviceNameCallCount { get; private set; }

        public Task<bool> SetDeviceName(string managedDeviceID, string newHostname)
        {
            SetDeviceNameCallCount++;
            return OnSetDeviceName(managedDeviceID, newHostname);
        }
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private sealed class TestContext
    {
        public FakeDbService Db { get; } = new();
        public FakeGraphService Graph { get; } = new();
        public FakeGraphBetaService GraphBeta { get; } = new();

        public TestContext(DeviceTag tag)
        {
            Db.Tag = tag;
            Db.Device = CreateDbDevice("SN123");
            Graph.ManagedDevices.Add(CreateManagedDevice(ManagedDeviceId, "SN123"));
        }

        public Task RunAsync()
        {
            var sut = new global::UpdateDevices.UpdateDevices(NullLoggerFactory.Instance, Db, Graph, GraphBeta);
            var timer = new TimerInfo { ScheduleStatus = new ScheduleStatus { Next = DateTime.UtcNow.AddHours(1) } };
            return sut.Run(timer);
        }
    }

    private static Device CreateDbDevice(string serialNumber) => new Device
    {
        Make = "Contoso",
        Model = "Laptop 1",
        SerialNumber = serialNumber,
        PreferredHostname = "HOST-001",
        Tags = new List<string> { TagId },
        OS = DeviceOS.Windows,
    };

    private static ManagedDevice CreateManagedDevice(string id, string serialNumber) => new ManagedDevice
    {
        Id = id,
        Manufacturer = "Contoso",
        Model = "Laptop 1",
        SerialNumber = serialNumber,
        AzureADDeviceId = "33333333-3333-3333-3333-333333333333",
        DeviceName = "DESKTOP-OLD",
    };

    private static DeviceTag CreateTag(params DeviceUpdateAction[] actions) => new DeviceTag
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
    }

    private static void AssertAttemptRecordedButNotProcessed(FakeDbService db)
    {
        ProcessingStateSnapshot update = Assert.Single(db.ProcessingUpdates);
        Assert.NotEqual(ProcessingStatus.Processed, update.ProcessingStatus);
        Assert.NotNull(update.LastProcessingAttemptUTC);
        Assert.Null(update.SuccessfullyProcessedUTC);
    }

    // ─── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_AllConfiguredActionsSucceed_MarksDeviceProcessed()
    {
        var context = new TestContext(CreateTag(
            Group("Group A"),
            Group("Group B"),
            AdminUnit("AU A"),
            Attribute("ExtensionAttribute1", "value-1")));

        await context.RunAsync();

        AssertMarkedProcessed(context.Db);
        Assert.Equal(2, context.Graph.GroupsAdded.Count);
        Assert.Single(context.Graph.AdminUnitsAdded);
        List<DeviceUpdateAction> attributes = Assert.Single(context.Graph.AttributeUpdates);
        Assert.Equal("ExtensionAttribute1", Assert.Single(attributes).Name);
        Assert.NotNull(context.Db.SavedLastRun);
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
    }

    public enum RenameFailure
    {
        SetDeviceNameReturnsFalse,
        SetDeviceNameThrows,
        InvalidDeviceNameRegex,
    }

    [Theory]
    [InlineData(RenameFailure.SetDeviceNameReturnsFalse)]
    [InlineData(RenameFailure.SetDeviceNameThrows)]
    [InlineData(RenameFailure.InvalidDeviceNameRegex)]
    public async Task Run_RenameFailsButOtherActionsSucceed_MarksDeviceProcessed(RenameFailure failure)
    {
        DeviceTag tag = CreateTag(Group("Group A"), AdminUnit("AU A"), Attribute("ExtensionAttribute1", "value-1"));
        tag.DeviceRenameEnabled = true;

        var context = new TestContext(tag);
        switch (failure)
        {
            case RenameFailure.SetDeviceNameReturnsFalse:
                context.GraphBeta.OnSetDeviceName = (_, _) => Task.FromResult(false);
                break;
            case RenameFailure.SetDeviceNameThrows:
                context.GraphBeta.OnSetDeviceName = (_, _) => throw new InvalidOperationException("Graph rename failed");
                break;
            case RenameFailure.InvalidDeviceNameRegex:
                tag.DeviceNameRegex = "[";
                break;
        }

        await context.RunAsync();

        AssertMarkedProcessed(context.Db);
        Assert.Single(context.Graph.GroupsAdded);
        Assert.Single(context.Graph.AdminUnitsAdded);
        Assert.Single(context.Graph.AttributeUpdates);
        Assert.Equal(failure == RenameFailure.InvalidDeviceNameRegex ? 0 : 1, context.GraphBeta.SetDeviceNameCallCount);
    }

    public enum FailingAction
    {
        Group,
        AdministrativeUnit,
        Attribute,
    }

    [Theory]
    [InlineData(FailingAction.Group)]
    [InlineData(FailingAction.AdministrativeUnit)]
    [InlineData(FailingAction.Attribute)]
    public async Task Run_AnActionReturnsFalse_RecordsAttemptButNotProcessed(FailingAction failing)
    {
        DeviceUpdateAction failingGroup = Group("Group A");
        var context = new TestContext(CreateTag(
            failingGroup,
            Group("Group B"),
            AdminUnit("AU A"),
            Attribute("ExtensionAttribute1", "value-1")));

        switch (failing)
        {
            case FailingAction.Group:
                context.Graph.GroupResult = g => g.Id != failingGroup.Id;
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
        // The failure doesn't stop the remaining actions from being applied.
        Assert.Equal(2, context.Graph.GroupsAdded.Count);
        Assert.Single(context.Graph.AdminUnitsAdded);
        Assert.Single(context.Graph.AttributeUpdates);
    }

    [Fact]
    public async Task Run_DeviceHasNoTags_RecordsAttemptButNotProcessed()
    {
        var context = new TestContext(CreateTag(Group("Group A")));
        context.Db.Device!.Tags = new List<string>();

        await context.RunAsync();

        AssertAttemptRecordedButNotProcessed(context.Db);
        Assert.Equal(0, context.Db.GetDeviceTagCallCount);
        Assert.Equal(0, context.Graph.TotalActionCalls);
    }

    [Fact]
    public async Task Run_DeviceIsDeleting_AppliesNoActionsAndDoesNotWriteProcessingState()
    {
        DeviceTag tag = CreateTag(Group("Group A"), AdminUnit("AU A"), Attribute("ExtensionAttribute1", "value-1"));
        tag.DeviceRenameEnabled = true;
        var context = new TestContext(tag);
        context.Db.Device!.Status = DeviceStatus.Deleting;

        await context.RunAsync();

        Assert.Empty(context.Db.ProcessingUpdates);
        Assert.Equal(0, context.Graph.GetDeviceObjectIdCallCount);
        Assert.Equal(0, context.Db.GetDeviceTagCallCount);
        Assert.Equal(0, context.Graph.TotalActionCalls);
        Assert.Equal(0, context.GraphBeta.SetDeviceNameCallCount);
    }

    [Fact]
    public async Task Run_OneDeviceThrowsUnexpectedly_ContinuesWithNextDeviceAndSavesLastRun()
    {
        var context = new TestContext(CreateTag(Group("Group A")));
        context.Graph.ManagedDevices.Clear();
        context.Graph.ManagedDevices.Add(CreateManagedDevice(ManagedDeviceId, "SN-THROWS"));
        context.Graph.ManagedDevices.Add(CreateManagedDevice("44444444-4444-4444-4444-444444444444", "SN-OK"));
        context.Db.OnGetDevice = (_, _, serialNumber) => serialNumber == "SN-THROWS"
            ? throw new InvalidOperationException("Cosmos unavailable")
            : Task.FromResult(CreateDbDevice(serialNumber));

        await context.RunAsync();

        ProcessingStateSnapshot update = Assert.Single(context.Db.ProcessingUpdates);
        Assert.Equal("SN-OK", update.SerialNumber);
        Assert.Equal(ProcessingStatus.Processed, update.ProcessingStatus);
        Assert.Single(context.Graph.GroupsAdded);
        Assert.NotNull(context.Db.SavedLastRun);
    }
}

using DelegationStation.Interfaces;
using DelegationStation.Services;
using DelegationSharedLibrary.Models;
using DelegationStationShared.Models;
using AdministrativeUnit = Microsoft.Graph.Models.AdministrativeUnit;
using Group = Microsoft.Graph.Models.Group;

namespace DelegationStation.Tests.Fakes;

internal sealed class FakeDeviceDBService : IDeviceDBService
{
    public Func<Device, Task<Device>> AddOrUpdateDeviceAsyncHandler { get; set; } =
        device => Task.FromResult(device);
    public Func<IEnumerable<string>, Device, int, int, Task<List<Device>>> GetDevicesAsyncHandler { get; set; } =
        (_, _, _, _) => Task.FromResult(new List<Device>());
    public Func<IEnumerable<string>, Device, int, int, Task<List<Device>>> GetDevicesSearchAsyncHandler { get; set; } =
        (_, _, _, _) => Task.FromResult(new List<Device>());
    public Func<IEnumerable<string>, Device, Task<int>> GetDeviceSearchCountAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(0);
    public Func<string, string, string, Task<Device?>> GetDeviceAsyncHandler { get; set; } =
        (_, _, _) => Task.FromResult<Device?>(null);
    public Func<string, Task<List<Device>>> GetDevicesByTagAsyncHandler { get; set; } =
        _ => Task.FromResult(new List<Device>());
    public Func<Device, Task> MarkDeviceToDeleteAsyncHandler { get; set; } =
        _ => Task.CompletedTask;

    public Task<Device> AddOrUpdateDeviceAsync(Device device) => AddOrUpdateDeviceAsyncHandler(device);

    public Task<List<Device>> GetDevicesAsync(
        IEnumerable<string> groupIds, Device device, int pageSize = 10, int page = 0) =>
        GetDevicesAsyncHandler(groupIds, device, pageSize, page);

    public Task<List<Device>> GetDevicesSearchAsync(
        IEnumerable<string> groupIds, Device device, int pageSize = 10, int page = 0) =>
        GetDevicesSearchAsyncHandler(groupIds, device, pageSize, page);

    public Task<int> GetDeviceSearchCountAsync(IEnumerable<string> groupIds, Device device) =>
        GetDeviceSearchCountAsyncHandler(groupIds, device);

    public Task<Device?> GetDeviceAsync(string make, string model, string serialNumber) =>
        GetDeviceAsyncHandler(make, model, serialNumber);

    public Task<List<Device>> GetDevicesByTagAsync(string tagId) => GetDevicesByTagAsyncHandler(tagId);

    public Task MarkDeviceToDeleteAsync(Device device) => MarkDeviceToDeleteAsyncHandler(device);
}

internal sealed class FakeDeviceTagDBService : IDeviceTagDBService
{
    public DeviceTagSearch CurrentSearch { get; set; } = new();
    public Func<IEnumerable<string>, string, Task<List<DeviceTag>>> GetDeviceTagsAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(new List<DeviceTag>());
    public Func<DeviceTag, Task<DeviceTag>> AddOrUpdateDeviceTagAsyncHandler { get; set; } =
        deviceTag => Task.FromResult(deviceTag);
    public Func<string, Task<DeviceTag>> GetDeviceTagAsyncHandler { get; set; } =
        _ => Task.FromResult(new DeviceTag());
    public Func<DeviceTag, Task> DeleteDeviceTagAsyncHandler { get; set; } =
        _ => Task.CompletedTask;
    public Func<string, Task<int>> GetDeviceCountByTagIdAsyncHandler { get; set; } =
        _ => Task.FromResult(0);
    public Func<IEnumerable<string>, int, int, string, Task<List<DeviceTag>>> GetDeviceTagsByPageAsyncHandler { get; set; } =
        (_, _, _, _) => Task.FromResult(new List<DeviceTag>());
    public Func<IEnumerable<string>, string, Task<int>> GetDeviceTagCountAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(0);
    public Func<string, Task<List<DeviceTag>>> GetTagsSearchAsyncHandler { get; set; } =
        _ => Task.FromResult(new List<DeviceTag>());

    public Task<List<DeviceTag>> GetDeviceTagsAsync(IEnumerable<string> groupIds, string name = null) =>
        GetDeviceTagsAsyncHandler(groupIds, name);

    public Task<DeviceTag> AddOrUpdateDeviceTagAsync(DeviceTag deviceTag) =>
        AddOrUpdateDeviceTagAsyncHandler(deviceTag);

    public Task<DeviceTag> GetDeviceTagAsync(string tagId) => GetDeviceTagAsyncHandler(tagId);

    public Task DeleteDeviceTagAsync(DeviceTag deviceTag) => DeleteDeviceTagAsyncHandler(deviceTag);

    public Task<int> GetDeviceCountByTagIdAsync(string tagId) =>
        GetDeviceCountByTagIdAsyncHandler(tagId);

    public Task<List<DeviceTag>> GetDeviceTagsByPageAsync(
        IEnumerable<string> groupIds, int pageNumber, int pageSize, string name = null) =>
        GetDeviceTagsByPageAsyncHandler(groupIds, pageNumber, pageSize, name);

    public Task<int> GetDeviceTagCountAsync(IEnumerable<string> groupIds, string name = null) =>
        GetDeviceTagCountAsyncHandler(groupIds, name);

    public Task<List<DeviceTag>> GetTagsSearchAsync(string name) => GetTagsSearchAsyncHandler(name);
}

internal sealed class FakeCorpIdDBService : ICorpIdDBService
{
    public Func<Task<CorpIDCounter?>> GetCorpIDCounterAsyncHandler { get; set; } =
        () => Task.FromResult<CorpIDCounter?>(null);

    public Task<CorpIDCounter?> GetCorpIDCounterAsync() => GetCorpIDCounterAsyncHandler();
}

internal sealed class FakeRoleDBService : IRoleDBService
{
    public Func<Role, Task<Role>> AddOrUpdateRoleAsyncHandler { get; set; } =
        role => Task.FromResult(role);
    public Func<Task<List<Role>>> GetRolesAsyncHandler { get; set; } =
        () => Task.FromResult(new List<Role>());
    public Func<string, Task<Role>> GetRoleAsyncHandler { get; set; } =
        _ => Task.FromResult(new Role());
    public Func<Role, Task> DeleteRoleAsyncHandler { get; set; } =
        _ => Task.CompletedTask;

    public Task<Role> AddOrUpdateRoleAsync(Role role) => AddOrUpdateRoleAsyncHandler(role);
    public Task<List<Role>> GetRolesAsync() => GetRolesAsyncHandler();
    public Task<Role> GetRoleAsync(string roleId) => GetRoleAsyncHandler(roleId);
    public Task DeleteRoleAsync(Role role) => DeleteRoleAsyncHandler(role);
}

internal sealed class FakeGraphService : IGraphService
{
    public Func<string, Task<string>> GetSecurityGroupNameHandler { get; set; } =
        groupId => Task.FromResult(groupId);
    public Func<string, Task<List<AdministrativeUnit>>> SearchAdministrativeUnitAsyncHandler { get; set; } =
        _ => Task.FromResult(new List<AdministrativeUnit>());
    public Func<string, Task<List<Group>>> SearchGroupAsyncHandler { get; set; } =
        _ => Task.FromResult(new List<Group>());

    public Task<string> GetSecurityGroupName(string groupId) =>
        GetSecurityGroupNameHandler(groupId);

    public Task<List<AdministrativeUnit>> SearchAdministrativeUnitAsync(string query) =>
        SearchAdministrativeUnitAsyncHandler(query);

    public Task<List<Group>> SearchGroupAsync(string query) => SearchGroupAsyncHandler(query);
}

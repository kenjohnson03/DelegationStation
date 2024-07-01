using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface IDeviceTagDBService
    {
        Task<List<DeviceTag>> GetDeviceTagsAsync(IEnumerable<string> groupIds);
        Task<DeviceTag> AddOrUpdateDeviceTagAsync(DeviceTag deviceTag);
        Task<DeviceTag> GetDeviceTagAsync(string tagId);
        Task DeleteDeviceTagAsync(DeviceTag deviceTag);
        Task<int> GetDeviceCountByTagIdAsync(string tagId);
        Task<List<DeviceTag>> GetDeviceTagsByPageAsync(IEnumerable<string> groupIds, int pageNumber, int pageSize);
        Task<int> GetDeviceTagCountAsync(IEnumerable<string> groupIds);
    }
}
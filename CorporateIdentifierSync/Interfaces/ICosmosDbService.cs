using CorporateIdentifierSync.Models;
using DelegationStationShared.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface ICosmosDbService
    {
        Task<List<Device>> GetAddedDevices(int batchSize);

        Task<List<Device>> GetDevicesMarkedForDeletion();

        Task UpdateDevice(Device device);

        Task DeleteDevice(Device device);

        Task<List<Device>> GetDevicesSyncedBefore(DateTime date);
        Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date);

        Task<DeviceTag> GetDeviceTag(string id);

        Task<List<string>> GetSyncingDeviceTags();

        Task<List<string>> GetNonSyncingDeviceTags();

        Task<CorpIDCounter> GetCorpIDCounter();

        Task SetCorpIDCounter(CorpIDCounter counter);

        Task<List<Device>> GetSyncedDevices(int batchSize);

        Task<List<Device>> GetNotSyncingDevices(int batchSize);
    }
}

using DelegationStationShared.Models;
using Microsoft.Graph.Models;

namespace UpdateDevices.Interfaces
{
    public interface IGraphService
    {
        Task<ManagedDevice> GetManagedDevice(string deviceID);
        Task<bool> AddDeviceToAzureADGroup(string deviceId, string deviceObjectId, DeviceUpdateAction group);
        Task<bool> AddDeviceToAzureAdministrativeUnit(string deviceId, string deviceObjectId, DeviceUpdateAction adminUnit);
        Task<bool> UpdateAttributesOnDeviceAsync(string deviceId, string deviceObjectId, List<DeviceUpdateAction> deviceUpdateActions);
        Task<List<ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime);
        Task<string> GetDeviceObjectID(string azureADDeviceID);

    }
}

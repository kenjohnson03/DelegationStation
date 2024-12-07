using DelegationStationShared.Models;

namespace CorporateIdentiferSync.Interfaces
{
    public interface ICosmosDbService
    {
        Task<List<Device>> GetDevicesWithoutCorpIdentity();

        Task<List<Device>> GetDevicesMarkedForDeletion();

        Task UpdateDevice(Device device);

        Task DeleteDevice(Device device);


    }
}

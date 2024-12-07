using DelegationStationShared.Models;

namespace CorporateIdentiferSync.Interfaces
{
  public interface ICosmosDbService
    {
         Task<List<Device>> GetDevicesWithoutCorpIdentity();

         Task UpdateDevice(Device device);

    
    }
}

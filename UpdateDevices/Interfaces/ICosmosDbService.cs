using DelegationStationShared.Models;
using System.Threading.Tasks;
using UpdateDevices.Models;

namespace UpdateDevices.Interfaces
{
  public interface ICosmosDbService
  {
    Task<FunctionSettings> GetFunctionSettings();
    Task UpdateFunctionSettings();

    Task<Device> GetDevice(string make, string model, string serialNumber);

    Task<DeviceTag> GetDeviceTag(string tagId);

  }
}

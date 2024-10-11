using DelegationStationShared.Models;
using System;
using System.Threading.Tasks;
using UpdateDevices.Models;

namespace UpdateDevices.Interfaces
{
  public interface ICosmosDbService
  {
    Task<FunctionSettings> GetFunctionSettings();
    Task UpdateFunctionSettings(DateTime thisRun);

    Task<Device> GetDevice(string make, string model, string serialNumber);

    Task<DeviceTag> GetDeviceTag(string tagId);

  }
}

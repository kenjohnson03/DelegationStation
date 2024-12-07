using Microsoft.Graph.Models;

namespace CorporateIdentiferSync.Interfaces
{
    public interface IGraphService
    {
        Task<ManagedDevice> GetManagedDevice(string make, string model, string serialNum);

        Task<bool> DeleteManagedDevice(string managedDeviceID);
    }
}

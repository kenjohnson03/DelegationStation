using Microsoft.Graph.Models;

namespace DelegationStation.Interfaces
{
    public interface IGraphService
    {
        Task<string> GetSecurityGroupName(string groupId);
        Task<List<AdministrativeUnit>> SearchAdministrativeUnitAsync(string query);
        Task<List<Group>> SearchGroupAsync(string query);
        Task<bool> DeleteManagedDevice(string ID);
        Task<ManagedDevice> GetManagedDevice(string manufacturer, string model, string serialNum);
    }
}

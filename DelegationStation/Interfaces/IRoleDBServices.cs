using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface IRoleDBService
    {
        Task<Role> AddOrUpdateRoleAsync(Role role);
        Task<List<Role>> GetRolesAsync();
        Task<Role> GetRoleAsync(string roleId);

        Task DeleteRoleAsync(Role role);
    }
}
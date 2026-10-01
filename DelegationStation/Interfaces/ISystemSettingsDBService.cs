using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface ISystemSettingsDBService
    {
        Task<SystemSettings?> GetSystemSettingsAsync();
        Task<SystemSettings> AddOrUpdateSystemSettingsAsync(SystemSettings systemSettings);
    }
}

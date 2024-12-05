using Microsoft.Graph.Beta.Models;

namespace DelegationStation.Interfaces
{
    public interface IGraphBetaService
    {
        Task<ImportedDeviceIdentity> AddCorporateIdentifer(string identifier);
        Task<bool> DeleteCorporateIdentifier(string ID);

    }
}

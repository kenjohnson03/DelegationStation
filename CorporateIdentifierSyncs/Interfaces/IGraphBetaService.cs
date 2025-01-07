using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface IGraphBetaService
    {
        Task<ImportedDeviceIdentity> AddCorporateIdentifier(string identifier);

        Task<bool> DeleteCorporateIdentifier(string identifierID);

        Task<bool> CorporateIdentifierExists(string identiferID);
    }
}

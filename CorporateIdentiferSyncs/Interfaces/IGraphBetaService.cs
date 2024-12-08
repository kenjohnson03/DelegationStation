using Microsoft.Graph.Beta.Models;

namespace CorporateIdentiferSync.Interfaces
{
    public interface IGraphBetaService
    {
        Task<ImportedDeviceIdentity> AddCorporateIdentifier(string identifier);

        Task<bool> DeleteCorporateIdentifier(string identifierID);

        Task<bool> CorporateIdentifierExists(string identiferID);
    }
}

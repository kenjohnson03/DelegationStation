using Azure.Identity;
using Microsoft.Graph;

namespace DelegationStation.Services
{
    public interface IGraphService
    {
        Task<string> GetSecurityGroupName(string groupId);
    }
    public class GraphService : IGraphService
    {
        private readonly ILogger<GraphService> _logger;
        private GraphServiceClient _graphClient;

        public GraphService(IConfiguration configuration, ILogger<GraphService> logger) 
        {
            this._logger = logger;

            var options = new TokenCredentialOptions
            {
                AuthorityHost = configuration.GetSection("AzureEnvironment").Value == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var clientSecretCredential = new ClientSecretCredential(
                    configuration.GetSection("AzureAd:TenantId").Value,
                    configuration.GetSection("AzureAd:ClientId").Value,
                    configuration.GetSection("AzureApp:ClientSecret").Value,
                    options
                );
            
            this._graphClient = new GraphServiceClient(clientSecretCredential);
        }

        public async Task<string> GetSecurityGroupName(string groupId)
        {
            if (groupId == null)
            {
                throw new Exception("GraphService GetSecurityGroupName was sent null Group Id");
            }

            if (!System.Text.RegularExpressions.Regex.Match(groupId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"GraphService GetSecurityGroupName groupId did not match GUID format {groupId}");
            }

            var group = await _graphClient.Groups[groupId].GetAsync();
            string name = group?.DisplayName ?? "";
            return name;
        }
    }
}

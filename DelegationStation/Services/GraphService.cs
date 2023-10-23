using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace DelegationStation.Services
{
    public interface IGraphService
    {
        Task<string> GetSecurityGroupName(string groupId);
        Task<List<AdministrativeUnit>> SearchAdministrativeUnitAsync(string query);
        Task<List<Group>> SearchGroupAsync(string query);
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

        public async Task<List<Group>> SearchGroupAsync(string query)
        {
            if (query == null)
            {
                throw new Exception("GraphService SearchGroupAsync was sent null query");
            }

            if(System.Text.RegularExpressions.Regex.Match(query, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                var groupsById = await _graphClient.Groups.GetAsync((requestConfiguration) =>
                {
                    requestConfiguration.QueryParameters.Filter = $"id eq {query}";
                    requestConfiguration.QueryParameters.Orderby = new string[] { "displayName" };
                    requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "description" };
                    requestConfiguration.QueryParameters.Top = 5;
                    requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                });

                if (groupsById == null)
                {
                    return new List<Group>();
                }

                return groupsById.Value ?? new List<Group>();
            }

            var groups = await _graphClient.Groups.GetAsync((requestConfiguration) =>
            {
                requestConfiguration.QueryParameters.Search = $"\"displayName:{query}\" OR \"description:{query}\"";
                requestConfiguration.QueryParameters.Orderby = new string[] { "displayName" };
                requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "description" };
                requestConfiguration.QueryParameters.Top = 5;
                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");                
            });

            if (groups == null)
            {
                return new List<Group>();
            }
            return groups.Value ?? new List<Group>();
        }

        public async Task<List<AdministrativeUnit>> SearchAdministrativeUnitAsync(string query)
        {
            if (query == null)
            {
                throw new Exception("GraphService SearchAdministrativeUnitAsync was sent null query");
            }

            var administrativeUnits = await _graphClient.Directory.AdministrativeUnits.GetAsync((requestConfiguration) =>
            {
                requestConfiguration.QueryParameters.Search = $"\"displayName:{query}\" OR \"description:{query}\"";
                requestConfiguration.QueryParameters.Orderby = new string[] { "displayName" };
                requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "description" };
                requestConfiguration.QueryParameters.Top = 5;
                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
            });
            if (administrativeUnits == null)
            {
                return new List<AdministrativeUnit>();
            }
            return administrativeUnits.Value ?? new List<AdministrativeUnit>();
        }
    }
}

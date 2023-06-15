using DelegationStation.Models;
using Microsoft.Azure.Cosmos;
using System.Configuration;

namespace DelegationStation.Services
{
    public interface IRoleDBService
    {
        Task<Role> AddOrUpdateRoleAsync(Role role);
        Task<List<Role>> GetRolesAsync();
        Task<Role> GetRoleAsync(string roleId);
    }
    public class RoleDBService : IRoleDBService
    {
        private readonly ILogger<DeviceDBService> _logger;
        private readonly Container _container;
        private string? _DefaultGroup;

        public RoleDBService(IConfiguration configuration, ILogger<DeviceDBService> logger)
        {
            this._logger = logger;
            CosmosClient client = new(
                connectionString: configuration.GetSection("COSMOS_CONNECTION_STRING").Value!
            );
            ConfigureCosmosDatabase(client, "DelegationStation", "Role");
            this._container = client.GetContainer("DelegationStation", "Role");
            _DefaultGroup = configuration.GetSection("DefaultAdminGroupObjectId").Value;
        }

        public async void ConfigureCosmosDatabase(CosmosClient client, string databaseName, string containerName)
        {
            DatabaseResponse database = await client.CreateDatabaseIfNotExistsAsync(databaseName);
            await database.Database.CreateContainerIfNotExistsAsync(containerName, "/PartitionKey");
        }

        public async Task<List<Role>> GetRolesAsync()
        {
            List<Role> roles = new List<Role>();
            string query = $"SELECT * FROM r WHERE r.PartitionKey = \"{typeof(Role).Name}\"";

            QueryDefinition q = new QueryDefinition(query);

            var queryIterator = this._container.GetItemQueryIterator<Role>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                roles.AddRange(response.ToList());
            }

            return roles;
        }

        public async Task<Role> AddOrUpdateRoleAsync(Role role)
        {
            ItemResponse<Role> response = await this._container.UpsertItemAsync<Role>(role);
            return response;
        }

        public async Task<Role> GetRoleAsync(string roleId)
        {
            if(roleId == null)
            {
                throw new Exception("RoleDBService GetRoleAsync was sent null roleId");
            }

            if(!System.Text.RegularExpressions.Regex.Match(roleId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync deviceId did not match GUID format {roleId}");
            }

            ItemResponse<Role> response = await this._container.ReadItemAsync<Role>(roleId, new PartitionKey(roleId));
            return response;
        }
    }
}
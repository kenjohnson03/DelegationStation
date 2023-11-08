using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using System.Configuration;

namespace DelegationStation.Services
{
    public interface IRoleDBService
    {
        Task<Role> AddOrUpdateRoleAsync(Role role);
        Task<List<Role>> GetRolesAsync();
        Task<Role> GetRoleAsync(string roleId);

        Task DeleteRoleAsync(Role role);
    }
    public class RoleDBService : IRoleDBService
    {
        private readonly ILogger<DeviceDBService> _logger;
        private readonly Container _container;
        private string? _DefaultGroup;

        public RoleDBService(IConfiguration configuration, ILogger<DeviceDBService> logger)
        {
            this._logger = logger;

            if (configuration == null)
            {
                throw new Exception("DeviceDBService appsettings configuration is null.");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONNECTION_STRING").Value))
            {
                throw new Exception("DeviceDBService appsettings COSMOS_CONNECTION_STRING is null or empty");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("DefaultAdminGroupObjectId").Value))
            {
                throw new Exception("DefaultAdminGroupObjectId appsettings is null or empty");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value))
            {
                _logger.LogInformation("COSMOS_DATABASE_NAME is null or empty, using default value of DelegationStationData");
            }

            string dbName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value) ? "DelegationStationData" : configuration.GetSection("COSMOS_DATABASE_NAME").Value!;

            CosmosClient client = new(
                connectionString: configuration.GetSection("COSMOS_CONNECTION_STRING").Value!
            );
            ConfigureCosmosDatabase(client, "DelegationStation", dbName);
            this._container = client.GetContainer("DelegationStation", dbName);
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
            if(role == null)
            {
                throw new Exception("RoleDBService AddOrUpdateRoleAsync was sent null role");
            }

            role.Attributes.Where(a => a == AllowedAttributes.All).ToList().ForEach(a => role.Attributes.Remove(a));
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

            ItemResponse<Role> response = await this._container.ReadItemAsync<Role>(roleId, new PartitionKey(typeof(Role).Name));
            return response;
        }

        public async Task DeleteRoleAsync(Role role)
        {
            ItemResponse<Role> response = await this._container.DeleteItemAsync<Role>(role.Id.ToString(), new PartitionKey(typeof(Role).Name));
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"RoleDBService DeleteRoleAsync failed to delete role {role.Id}");
            }
        }
    }
}
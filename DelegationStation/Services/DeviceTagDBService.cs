using Azure.Core;
using Azure.Identity;
using DelegationStation.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Identity;
using Microsoft.Identity.Web;

namespace DelegationStation.Services
{
    public interface IDeviceTagDBService
    {
        Task<List<DeviceTag>> GetDeviceTagsAsync(IEnumerable<string> groupIds);
        Task<DeviceTag> AddOrUpdateDeviceTagAsync(DeviceTag deviceTag);
        Task<DeviceTag> GetDeviceTagAsync(string tagId);
        Task DeleteDeviceTagAsync(DeviceTag deviceTag);
    }
    public class DeviceTagDBService : IDeviceTagDBService
    {
        private readonly ILogger? _logger;
        private readonly Container _container;
        private string? _DefaultGroup;

        public DeviceTagDBService(IConfiguration configuration)
        {
            CosmosClient client = new(
                connectionString: configuration.GetSection("COSMOS_CONNECTION_STRING").Value!
            );
            ConfigureCosmosDatabase(client, "DelegationStation", "Device");
            this._container = client.GetContainer("DelegationStation", "Device");
            _DefaultGroup = configuration.GetSection("DefaultAdminGroupObjectId").Value;
        }

        public async void ConfigureCosmosDatabase(CosmosClient client, string databaseName, string containerName)
        {
            DatabaseResponse database = await client.CreateDatabaseIfNotExistsAsync(databaseName);
            await database.Database.CreateContainerIfNotExistsAsync(containerName, "/PartitionKey");
        }

        public async Task<List<DeviceTag>> GetDeviceTagsAsync(IEnumerable<string> groupIds)
        {
            List<DeviceTag> deviceTags = new List<DeviceTag>();

            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                throw new Exception("DeviceTagDBService GetDeviceTagsAsync no valid group ids sent.");
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\"");
            }
            else
            {
                sb.Append("SELECT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

                foreach (string groupId in groupIds)
                {
                    sb.Append($"CONTAINS(r.SecurityGroupId, @arg{argCount}, true) ");
                    if (groupId != groupIds.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                sb.Append(")");
            }          
            

            argCount = 0;
            QueryDefinition q = new QueryDefinition(sb.ToString());

            if(!groupIds.Contains(_DefaultGroup)) 
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }            
            
            var queryIterator = this._container.GetItemQueryIterator<DeviceTag>(q);
            while(queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                deviceTags.AddRange(response.ToList());
            }
            
            return deviceTags;
        }

        public async Task<DeviceTag> GetDeviceTagAsync(string tagId)
        {
            if (tagId == null)
            {
                throw new Exception("DeviceDBService GetDeviceAsync was sent null tagId");
            }

            if (!System.Text.RegularExpressions.Regex.Match(tagId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync tagId did not match GUID format {tagId}");
            }

            ItemResponse<DeviceTag> response = await this._container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
            return response.Resource;
        }

        public async Task<DeviceTag> AddOrUpdateDeviceTagAsync(DeviceTag deviceTag)
        {
            ItemResponse<DeviceTag> response = await this._container.UpsertItemAsync<DeviceTag>(deviceTag);
            return response;
        }

        public async Task DeleteDeviceTagAsync(DeviceTag deviceTag)
        {
            await this._container.DeleteItemAsync<DeviceTag>(deviceTag.Id.ToString(), new PartitionKey(deviceTag.PartitionKey));
        }
    }
}
using DelegationStation.Models;
using Microsoft.Azure.Cosmos;
using System.Configuration;

namespace DelegationStation.Services
{
    public interface IDeviceDBService
    {
        Task<Device> AddOrUpdateDeviceAsync(Device device);
        Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds);

    }
    public class DeviceDBService : IDeviceDBService
    {
        private readonly ILogger<DeviceDBService> _logger;
        private readonly Container _container;
        private string? _DefaultGroup;

        public DeviceDBService(IConfiguration configuration, ILogger<DeviceDBService> logger)
        {
            this._logger = logger;
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

        public async Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds)
        {
            List<Device> devices = new List<Device>();

            List<DeviceTag> deviceTags = new List<DeviceTag>();
            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if(groupIds.Count() < 1)
            {
                throw new Exception("DeviceDBService GetDevicesAsync no valid group ids sent.");
            }

            // Get tags that the user has access to
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

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }

            var queryIterator = this._container.GetItemQueryIterator<DeviceTag>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                deviceTags.AddRange(response.ToList());
            }


            // Get Devices with the tags the user has access to
            sb = new System.Text.StringBuilder();
            argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM d WHERE d.Type = \"Device\"");
            }
            else
            {
                sb.Append("SELECT * FROM d WHERE d.Type = \"Device\" AND (");

                foreach (DeviceTag tag in deviceTags)
                {
                    sb.Append($"ARRAY_CONTAINS(d.Tags, @arg{argCount}, true) ");
                    if (tag != deviceTags.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                sb.Append(")");
            }


            argCount = 0;
            q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (DeviceTag tag in deviceTags)
                {
                    q.WithParameter($"@arg{argCount}", tag.Id);
                    argCount++;
                }
            }

            var deviceQueryIterator = this._container.GetItemQueryIterator<Device>(q);
            while (deviceQueryIterator.HasMoreResults)
            {
                var response = await deviceQueryIterator.ReadNextAsync();
                devices.AddRange(response.ToList());
            }

            return devices;
        }

        public async Task<Device> AddOrUpdateDeviceAsync(Device device)
        {
            ItemResponse<Device> response = await this._container.UpsertItemAsync<Device>(device);
            return response;
        }

        public async Task<Device> GetDeviceAsync(string deviceId)
        {
            if(deviceId == null)
            {
                throw new Exception("DeviceDBService GetDeviceAsync was sent null deviceId");
            }

            if(!System.Text.RegularExpressions.Regex.Match(deviceId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync deviceId did not match GUID format {deviceId}");
            }

            ItemResponse<Device> response = await this._container.ReadItemAsync<Device>(deviceId, new PartitionKey(deviceId));
            return response;
        }
    }
}
﻿using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;
using DelegationStationShared.Models;

namespace CorporateIdentifierSync.Services
{
    internal class CosmosDbService : ICosmosDbService
    {
        private readonly ILogger<CosmosDbService> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _container;

        public CosmosDbService(ILogger<CosmosDbService> logger)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger = logger;

            string containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME", EnvironmentVariableTarget.Process);
            string databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME", EnvironmentVariableTarget.Process);
            var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING", EnvironmentVariableTarget.Process);

            if (string.IsNullOrEmpty(containerName))
            {
                _logger.DSLogWarning("COSMOS_CONTAINER_NAME is null or empty, using default value of DeviceData", fullMethodName);
                containerName = "DeviceData";
            }
            if (string.IsNullOrEmpty(databaseName))
            {
                _logger.DSLogWarning("COSMOS_DATABASE_NAME is null or empty, using default value of DelegationStationData", fullMethodName);
                databaseName = "DelegationStationData";
            }
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.DSLogError("Cannot connect to CosmosDB. Missing required environment variable COSMOS_CONNECTION_STRING", fullMethodName);
                return;
            }

            try
            {
                if (_cosmosClient == null)
                {
                    _cosmosClient = new CosmosClient(connectionString);
                    _container = _cosmosClient.GetContainer(databaseName, containerName);
                }
                else if (_container == null)
                {
                    _container = _cosmosClient.GetContainer(databaseName, containerName);
                }

            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to connect to CosmosDB", ex, fullMethodName);
            }

            _logger.DSLogInformation("Connected to Cosmos DB database " + databaseName + " container " + containerName + ".", fullMethodName);
        }



        public async Task<List<Device>> GetAddedDevices()
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            // To ensure previously added devices are processed, check for devices without status as well as those set to Added
            // to handle initially large number of devices and our large bulk uploads, limiting processing to 10K devices at a time
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" " +
                "AND (NOT IS_DEFINED(c.Status) OR (c.Status = @status)) " +
                "OFFSET 0 LIMIT 10000");
            query.WithParameter("@status", Device.DeviceStatus.Added);

            var queryIterator = _container.GetItemQueryIterator<Device>(query);

            List<Device> devices = new List<Device>();
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                devices.AddRange(response.ToList());
            }

            return devices;

        }

        public async Task<List<Device>> GetDevicesMarkedForDeletion()
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND c.Status = @status");
            query.WithParameter("@status", Device.DeviceStatus.Deleting);


            var queryIterator = _container.GetItemQueryIterator<Device>(query);

            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for devices missing CorporateIdentityID.\n", ex, fullMethodName);
            }

            return devices;
        }

        public async Task UpdateDevice(Device device)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Updating device " + device.Id + ".", fullMethodName);

            ItemResponse<Device> response = null;
            response = await _container.UpsertItemAsync(device);
            _logger.DSLogInformation("Updated device " + device.Id + ".", fullMethodName);

            return;
        }

        public async Task DeleteDevice(Device device)
        {
            await _container.DeleteItemAsync<Device>(device.Id.ToString(), new PartitionKey(device.PartitionKey));
        }

        public async Task<List<Device>> GetDevicesSyncedBefore(DateTime date)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            // Only return devices that are in Synced or NotSyncing status
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND NOT (c.Status = @deleting)" +
                " AND NOT (c.Status = @added) AND c.LastCorpIdentitySync <= @date");
            query.WithParameter("@deleting", Device.DeviceStatus.Deleting);
            query.WithParameter("@added", Device.DeviceStatus.Added);
            query.WithParameter("@date", date);
            var queryIterator = _container.GetItemQueryIterator<Device>(query);
            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for devices missing CorporateIdentityID.\n", ex, fullMethodName);
            }
            return devices;

        }

        public async Task<DelegationStationShared.Models.DeviceTag> GetDeviceTag(string id)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (id == null)
            {
                throw new Exception($"{fullMethodName} was sent null tag ID");
            }

            if (!System.Text.RegularExpressions.Regex.Match(id, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"{fullMethodName} tag ID did not match GUID format {id}");
            }

            ItemResponse<DeviceTag> response = await this._container.ReadItemAsync<DeviceTag>(id, new PartitionKey("DeviceTag"));
            return response.Resource;

        }

        public async Task<List<string>> GetSyncEnabledDeviceTags()
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey= \"DeviceTag\" AND c.CorpIDSyncEnabled=true");
            var queryIterator = _container.GetItemQueryIterator<DeviceTag>(query);
            List<string> tagIDs = new List<string>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    tagIDs.AddRange(response.Select(t => t.Id.ToString()).ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for devices missing CorporateIdentityID.\n", ex, fullMethodName);
            }
            return tagIDs;

        }
    }
}

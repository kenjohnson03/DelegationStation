using DelegationSharedLibrary;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using CorporateIdentiferSync.Interfaces;
using DelegationStation.CorporateIdentiferSyncs.Extensions;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentiferSync.Services
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

   

        public async Task<List<Device>> GetDevicesWithoutCorpIdentity()
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;


            // TBD:  Should we check more of the fields?  
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND (NOT IS_DEFINED(c.CorporateIdentityID) OR c.CorporateIdentityID = \"\")");

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

            _logger.LogInformation("Setting CorporateIdentifier fields for device " + device.Id + ".", fullMethodName);

            ItemResponse<Device> response = null;
            try
            {
                response = await _container.UpsertItemAsync(device);
                _logger.LogInformation("Updated device " + device.Id + ".", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update device " + device.Id + ".\n" + ex.Message, fullMethodName);
                return;
            }

            return;
        }


    }
}

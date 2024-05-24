using DelegationSharedLibrary.Models;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    public interface IPreloadDBService
    {
      Task<List<string>> GetPreLoadItems(string type);
    }

    public class PreloadDBService : IPreloadDBService
    {
      private readonly ILogger<PreloadDBService> _logger;
      private readonly Container _container;
      private string? _DefaultGroup;

      public PreloadDBService(IConfiguration configuration, ILogger<PreloadDBService> logger)
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
        if (string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONTAINER_NAME").Value))
        {
          _logger.LogInformation("COSMOS_CONTAINER_NAME is null or empty, using default value of DeviceData");
        }

        string dbName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value) ? "DelegationStationData" : configuration.GetSection("COSMOS_DATABASE_NAME").Value!;
        string containerName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONTAINER_NAME").Value) ? "DeviceData" : configuration.GetSection("COSMOS_CONTAINER_NAME").Value!;

        CosmosClient client = new(
            connectionString: configuration.GetSection("COSMOS_CONNECTION_STRING").Value!
        );
        ConfigureCosmosDatabase(client, dbName, containerName);
        this._container = client.GetContainer(dbName, containerName);
        _DefaultGroup = configuration.GetSection("DefaultAdminGroupObjectId").Value;
      }

      public async void ConfigureCosmosDatabase(CosmosClient client, string databaseName, string containerName)
      {
        DatabaseResponse database = await client.CreateDatabaseIfNotExistsAsync(databaseName);
        await database.Database.CreateContainerIfNotExistsAsync(containerName, "/PartitionKey");
      }


      public async Task<List<string>> GetPreLoadItems(string type)
      {
        List<PreLoads> preLoads = new List<PreLoads>();
        List<string> list = new List<string>();

        QueryDefinition q = new QueryDefinition("SELECT * FROM d WHERE d.PartitionKey = \"PreLoads\" AND d.Name = @name");
        q.WithParameter("@name", type);

        var queryIterator = this._container.GetItemQueryIterator<PreLoads>(q);
        while (queryIterator.HasMoreResults)
        {
          var qIresponse = await queryIterator.ReadNextAsync();
          preLoads.AddRange(qIresponse.ToList());
        }
        if (preLoads.Count == 0)
        {
          return list;
        }
        else
        {
          var firstPreLoad = preLoads.FirstOrDefault();
          if (firstPreLoad != null)
          {
            list.AddRange(firstPreLoad.Values);
          }
        }

        return list;
      }

    }
  }
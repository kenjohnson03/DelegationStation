using DelegationSharedLibrary;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using DelegationStationShared.Models;

namespace DelegationStation.WhiteSpaceCleaner
{
  public class WhiteSpaceCleaner
  {
    private readonly ILogger _logger;
    private static Container? _container = null;

    public WhiteSpaceCleaner(ILoggerFactory loggerFactory)
    {
      _logger = loggerFactory.CreateLogger<WhiteSpaceCleaner>();
    }

    internal void Run()
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      _logger.DSLogInformation("WhiteSpaceCleaner starting....", fullMethodName);


      ConnectToCosmosDb();
      if (_container == null)
      {
        _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
        Environment.Exit(1);
      }

      int result = UpdateDevices();
      _logger.DSLogInformation($"WhiteSpaceCleaner done:  Updated {result} devices.", fullMethodName);
      
    }

    private void ConnectToCosmosDb()
    {

      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      _logger.DSLogInformation("Connecting to Cosmos DB...", fullMethodName);

      //string? containerName = System.Configuration.ConfigurationManager.AppSettings["COSMOS_CONTAINER_NAME"];
      //string? databaseName = System.Configuration.ConfigurationManager.AppSettings["COSMOS_DATABASE_NAME"];
      //var connectionString = System.Configuration.ConfigurationManager.AppSettings["COSMOS_CONNECTION_STRING"];
      string? containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME");
      string? databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME");
      var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");

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
      if (String.IsNullOrEmpty(connectionString))
      {
        _logger.DSLogError("Cannot connect to CosmosDB. Missing required environment variable COSMOS_CONNECTION_STRING", fullMethodName);
        return;
      }

      try
      {
        CosmosClient client = new(connectionString: connectionString);
        _container = client.GetContainer(databaseName, containerName);
      }
      catch (Exception ex)
      {
        _logger.DSLogException($"Failed to connect to CosmosDB", ex, fullMethodName);
      }

      _logger.DSLogInformation($"Connected to Cosmos DB database {databaseName} container {containerName}.", fullMethodName);
    }
    private int UpdateDevices()
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = GetType().Name;
      string fullMethodName = className + "." + methodName;

      int deviceUpdated = 0;

      // Search CosmosDB for device with exact match on Make, Model, SerialNumber
      QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND (STARTSWITH(c.Make,\" \") OR ENDSWITH(c.Make,\" \")" +
        " OR STARTSWITH(c.Model,\" \") OR ENDSWITH(c.Model,\" \") OR STARTSWITH(c.SerialNumber,\" \") OR ENDSWITH(c.SerialNumber,\" \"))");
      var queryIterator = _container.GetItemQueryIterator<Device>(query);


      while (queryIterator.HasMoreResults)
      {
        var response = queryIterator.ReadNextAsync().Result;
        _logger.DSLogInformation($"Retrieved devices found with whitespace in Make/Model/SerialNumber field: {response.Count}", fullMethodName);

        foreach (var device in response)
        {
          _logger.DSLogInformation($"Retrieved device {device.Id} with whitespace in Make/Model/SerialNumber:", fullMethodName);
          _logger.DSLogInformation($"Device {device.Id} Before Update - Make: '{device.Make}' Model: '{device.Model}' SerialNumber: '{device.SerialNumber}'", fullMethodName);
          // Fix device
          device.Make = device.Make.Trim();
          device.Model = device.Model.Trim();
          device.SerialNumber = device.SerialNumber.Trim();

          _container.ReplaceItemAsync(device, device.Id.ToString()).Wait();
          _logger.DSLogInformation($"Device {device.Id} After Update -  Make: '{device.Make}' Model: '{device.Model}' SerialNumber: '{device.SerialNumber}'", fullMethodName);
          deviceUpdated++;
        }
      }

      return deviceUpdated;

    }
  }
}

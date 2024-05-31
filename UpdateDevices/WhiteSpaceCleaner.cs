using DelegationSharedLibrary;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using UpdateDevices.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace UpdateDevices
{
  public class WhiteSpaceCleaner
  {
    private readonly ILogger<WhiteSpaceCleaner> _logger;
    private static Microsoft.Azure.Cosmos.Container _container = null;

    public WhiteSpaceCleaner(ILogger<WhiteSpaceCleaner> logger)
    {
      _logger = logger;
    }

    [Function("WhiteSpaceCleaner")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Admin, "post")] HttpRequest req)
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      _logger.DSLogInformation("C# HTTP trigger function processed a request.", fullMethodName);


      ConnectToCosmosDb();
      if (_container == null)
      {
        _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
        var errorObjectResult = new ObjectResult("Server Error");
        errorObjectResult.StatusCode = StatusCodes.Status500InternalServerError;
        return errorObjectResult;
      }

      int result = await UpdateDevices();
      _logger.DSLogInformation($"Updated {result} devices." , fullMethodName);
      
      string responseMessage = $"Updated {result} devices.";
      return new OkObjectResult(responseMessage);
    }

    private void ConnectToCosmosDb()
    {

      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      _logger.DSLogInformation("Connecting to Cosmos DB...", fullMethodName);

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

    private async Task<int> UpdateDevices()
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
        var response = await queryIterator.ReadNextAsync();
        _logger.DSLogInformation($"Retrieved devices found with whitespace in Make/Model/SerialNumber field: {response.Count}",  fullMethodName);

        foreach (var device in response)
        {
          _logger.DSLogInformation($"Retrieved device {device.Id} with whitespace in Make/Model/SerialNumber:", fullMethodName);
          _logger.DSLogInformation($"Device {device.Id} Before Update - Make: '{device.Make}' Model: '{device.Model}' SerialNumber: '{device.SerialNumber}'", fullMethodName);
          // Fix device
          device.Make = device.Make.Trim();
          device.Model = device.Model.Trim();
          device.SerialNumber = device.SerialNumber.Trim();
          await _container.ReplaceItemAsync(device, device.Id.ToString());
          _logger.DSLogInformation($"Device {device.Id} After Update -  Make: '{device.Make}' Model: '{device.Model}' SerialNumber: '{device.SerialNumber}'", fullMethodName);
          deviceUpdated++;
        }
      }

      return deviceUpdated;

    }
  }
}

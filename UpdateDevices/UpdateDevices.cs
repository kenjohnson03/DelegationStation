using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using System.Text;
using Microsoft.Graph;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using System.Linq;
using UpdateDevices.Models;
using Microsoft.Graph.Models;
using System.Text.RegularExpressions;
using DelegationStationShared.Models;
using DelegationSharedLibrary;
using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Tokens;
using UpdateDevices.Extensions;



namespace UpdateDevices
{
  public class UpdateDevices
    {
        private static string _guidRegex = "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$";
        private static Microsoft.Azure.Cosmos.Container _container = null;
        private GraphServiceClient _graphClient;

        private readonly ILogger _logger;

        public UpdateDevices(ILoggerFactory loggerFactory)
        {
          _logger = loggerFactory.CreateLogger<UpdateDevices>();
        }

        [Function("UpdateDevices")]
        public async Task Run([TimerTrigger("%TriggerTime%")] TimerInfo timerInfo)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}",fullMethodName);
            _logger.DSLogInformation($"Next timer schedule at: {timerInfo.ScheduleStatus.Next}", fullMethodName);


            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.",fullMethodName);
                return;
            }

            ConnectToGraph();
            if (_graphClient == null)
            {
                _logger.DSLogError("Failed to connect to Graph, exiting.",fullMethodName);
                return;
            }


            FunctionSettings settings = await GetFunctionSettings();
            DateTime lastRun = settings.LastRun == null ? DateTime.UtcNow.AddDays(-300) : ((DateTime)settings.LastRun).AddHours(-1);
            List<Microsoft.Graph.Models.ManagedDevice> devices = await GetNewDeviceManagementObjectsAsync(lastRun);
            if (devices == null)
            {
                _logger.DSLogError("Failed to get new devices, exiting", fullMethodName);
                return;
            }
            foreach (Microsoft.Graph.Models.ManagedDevice device in devices)
            {
                await RunDeviceUpdateActionsAsync(device);
            }
            await UpdateFunctionSettings();
        }

        private async Task RunDeviceUpdateActionsAsync(Microsoft.Graph.Models.ManagedDevice device)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Processing enrolled device: '{device.Id}' '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'.", fullMethodName);

            List<DeviceUpdateAction> actions = new List<DeviceUpdateAction>();

            var defaultActionDisable = Environment.GetEnvironmentVariable("DefaultActionDisable", EnvironmentVariableTarget.Process);

            if (String.IsNullOrEmpty(defaultActionDisable))
            {
                _logger.DSLogWarning("DefaultActionDisable environment variable not set. Defaulting to false.",fullMethodName);
            }

            // Search CosmosDB for device with exact match on Make, Model, SerialNumber
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND c.Make = @manufacturer AND c.Model = @model AND c.SerialNumber = @serialNumber")
                .WithParameter("@manufacturer", device.Manufacturer.Trim())
                .WithParameter("@model", device.Model.Trim())
                .WithParameter("@serialNumber", device.SerialNumber.Trim());
            var queryIterator = _container.GetItemQueryIterator<DelegationStationShared.Models.Device>(query);

            List<DelegationStationShared.Models.Device> deviceResults = new List<DelegationStationShared.Models.Device>();
            try
            {

                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    deviceResults.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Failure querying Cosmos DB for device '{device.Id}' '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'.\n", ex, fullMethodName );
            }

            if (deviceResults.Count < 1)
            {
                _logger.LogWarning($"Did not find any matching devices in DB for: '{device.Id}' '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'.", fullMethodName);

                // TODO make personal / add to group / update attribute
                if (defaultActionDisable == "true")
                {
                    _logger.DSLogInformation($"DefaultActionDisable is true. Disabling device in AAD '{device.AzureADDeviceId}' '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'", fullMethodName);
                    await UpdateAttributesOnDeviceAsync(device.Id, device.AzureADDeviceId, new List<DeviceUpdateAction> { new DeviceUpdateAction() { ActionType = DeviceUpdateActionType.Attribute, Name = "AccountEnabled", Value = "false" } });
                }
                return;
            }

            DelegationStationShared.Models.Device d = deviceResults.FirstOrDefault();
            _logger.DSLogInformation($"Found matching device in DB for: '{device.Id}' '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'.", fullMethodName);

            //Get device object ID from Graph which is needed for update actions
            var deviceObjectID = "";
            try
            {
      
              var deviceObj = await _graphClient.Devices.GetAsync((requestConfiguration) =>
              {
                requestConfiguration.QueryParameters.Filter = $"deviceId eq '{device.AzureADDeviceId}'";
                requestConfiguration.QueryParameters.Select = new string[] { "id" };
              });
              deviceObjectID = deviceObj.Value.FirstOrDefault().Id;

            }
            catch (Exception ex)
            {
              _logger.DSLogException($"Failed to retrieve graph device ID using .\n", ex, fullMethodName);
              return;
            }
            if (deviceObjectID.IsNullOrEmpty())
            {
              _logger.DSLogError("Failed to retrieve graph device ID using .\n",fullMethodName);
              return;
            }

            _logger.DSLogInformation($"Retrieved Entra Object ID '{deviceObjectID}' for device. DeviceID: '{device.AzureADDeviceId}', ManagedDeviceID: '{device.Id}'",fullMethodName);

            foreach (string tagId in d.Tags)
            {
                DeviceTag tag = new DeviceTag();
                try
                {
                    ItemResponse<DeviceTag> tagResponse = await _container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
                    tag = tagResponse.Resource;
                    _logger.DSLogInformation($"Device {device.Id} is assigned to: {tag.Name}",fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Get tag {tagId} failed.\n", ex, fullMethodName);
                }

                //
                // To prevent PAWs from being updated, check the enrollment user and ensure there is a match to permitted regex
                // Intended to protect against PAW users using this to apply changes to their PAW
                // Allow any where the user is not set
                // 
                try
                {
                    if (!string.IsNullOrEmpty(tag.AllowedUserPrincipalName))
                    {
                        // If the user principal name is not in the allowed list, skip the tag
                        if (!Regex.IsMatch(device.UserPrincipalName, tag.AllowedUserPrincipalName))
                        {
                            _logger.DSLogWarning($"Primary user {device.UserPrincipalName} on ManagedDevice Id {device.Id} does not match Tag {tag.Name} allowed user principal names regex '{tag.AllowedUserPrincipalName}'.", fullMethodName);
                            return;
                        }
                    }
                } 
                catch (Exception ex)
                {
                    _logger.DSLogException($"UserPrincipalName {device.UserPrincipalName} on ManagedDevice Id {device.Id} on {tag.Id} allowed user principal names {tag.AllowedUserPrincipalName}.", ex, fullMethodName);
                    return;
                }

                if(tag.UpdateActions == null || tag.UpdateActions.Count < 1)
        {
                    _logger.DSLogWarning($"No update actions configured for {tag.Name}.  No updates applied for device {device.Id}.", fullMethodName);
                    return;
                }


                //
                // Applying update actions based on tag
                // 
                _logger.DSLogInformation($"Apply update actions to device {device.Id} configured for tag {tag.Name}...",fullMethodName);
                

                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.AdministrativeUnit))
                {
                    try
                    {
                        await AddDeviceToAzureAdministrativeUnit(device.Id, deviceObjectID, deviceUpdateAction);
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Unable to add Device {device.Id} (as {deviceObjectID}) to Administrative Unit: {deviceUpdateAction.Name} ({deviceUpdateAction.Value}).", ex, fullMethodName);
                    }
                }

                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Group))
                {
                    try
                    {
                        await AddDeviceToAzureADGroup(device.Id, deviceObjectID, deviceUpdateAction);
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Unable to add device {device.Id} (as {deviceObjectID}) to Group: {deviceUpdateAction.Name} ({deviceUpdateAction.Value}).", ex, fullMethodName);
                    }
                }

                try
                { 
                    var attributeList = tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Attribute).ToList();
                    await UpdateAttributesOnDeviceAsync(device.Id, deviceObjectID, attributeList);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Unable to update attributes for device {device.Id} (as {deviceObjectID}).", ex, fullMethodName);
                }                
            }
        }


        private async Task UpdateAttributesOnDeviceAsync(string deviceId, string objectDeviceId, List<DeviceUpdateAction> updateActions)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (string.IsNullOrEmpty(deviceId) || updateActions == null)
            {
                _logger.DSLogError($"DeviceId or updateActions is null or empty. DeviceId: {objectDeviceId};", fullMethodName);
                return;
            }
            if (Regex.IsMatch(deviceId, _guidRegex) == false)
            {
                _logger.DSLogError($"DeviceId is not a valid GUID. DeviceId: {objectDeviceId}", fullMethodName);
                return;
            }
            if (updateActions.Count < 1)
            {
                _logger.DSLogWarning($"No update actions configured for {objectDeviceId}", fullMethodName);
                return;
            }

            var requestBody = new Microsoft.Graph.Models.Device
            {
                AdditionalData = new Dictionary<string, object>
                    {
                        {
                            "extensionAttributes" , new
                            {
                                ExtensionAttribute1 = updateActions.Where(a => a.Name == "ExtensionAttribute1").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute2 = updateActions.Where(a => a.Name == "ExtensionAttribute2").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute3 = updateActions.Where(a => a.Name == "ExtensionAttribute3").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute4 = updateActions.Where(a => a.Name == "ExtensionAttribute4").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute5 = updateActions.Where(a => a.Name == "ExtensionAttribute5").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute6 = updateActions.Where(a => a.Name == "ExtensionAttribute6").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute7 = updateActions.Where(a => a.Name == "ExtensionAttribute7").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute8 = updateActions.Where(a => a.Name == "ExtensionAttribute8").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute9 = updateActions.Where(a => a.Name == "ExtensionAttribute9").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute10 = updateActions.Where(a => a.Name == "ExtensionAttribute10").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute11 = updateActions.Where(a => a.Name == "ExtensionAttribute11").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute12 = updateActions.Where(a => a.Name == "ExtensionAttribute12").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute13 = updateActions.Where(a => a.Name == "ExtensionAttribute13").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute14 = updateActions.Where(a => a.Name == "ExtensionAttribute14").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute15 = updateActions.Where(a => a.Name == "ExtensionAttribute15").FirstOrDefault()?.Value ?? "",
                            }
                        }
                    }
            };

            try
            {
                var result = await _graphClient.Devices[$"{objectDeviceId}"].PatchAsync(requestBody);
                _logger.DSLogAudit($"Applied Attributes to Device {deviceId}: [{string.Join(", ", updateActions.Select(a => a.Name + ": " + a.Value))}]", fullMethodName);
            }
            catch(Exception ex)
            {
                _logger.DSLogException($"Unable to apply ExtensionAttributes to DeviceId {deviceId}: [{string.Join(", ", updateActions.Select(a => a.Name + ": " + a.Value))}]", ex, fullMethodName);
            }
        }

        private async Task AddDeviceToAzureADGroup(string deviceId, string deviceObjectId, DeviceUpdateAction group)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint", EnvironmentVariableTarget.Process);

            string groupId = group.Value;

            if (string.IsNullOrEmpty(deviceObjectId))
            {
                _logger.DSLogError($"DeviceObjectId is null or empty.", fullMethodName);
                return;
            }
            if (string.IsNullOrEmpty(groupId))
            {
                _logger.DSLogError($"GroupId is null or empty.", fullMethodName);
                return;
            }
            if (Regex.IsMatch(deviceObjectId, _guidRegex) == false)
            {
                _logger.DSLogError($"DeviceId is not a valid GUID. DeviceId: {deviceId}", fullMethodName);
                return;
            }
            if (Regex.IsMatch(groupId, _guidRegex) == false)
            {
                _logger.DSLogError($"GroupId is not a valid GUID. GroupId: {groupId}", fullMethodName);
                return;
            }

            try
            {
                var requestBody = new ReferenceCreate
                {
                    OdataId = $"{graphEndpoint}v1.0/devices/{deviceObjectId}"
                };
                await _graphClient.Groups[$"{groupId}"].Members.Ref.PostAsync(requestBody);
                _logger.DSLogAudit($"Added DeviceId {deviceId} (as Object ID {deviceObjectId}) to Group {group.Name} ({groupId}).", fullMethodName);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("already exist"))
                {
                    _logger.DSLogInformation($"DeviceId {deviceId} (as Object ID {deviceObjectId}) already exists in Group {group.Name} ({groupId}).", fullMethodName);
                }
                else
                {
                    _logger.DSLogException($"Unable to add DeviceId {deviceId} (as Object ID {deviceObjectId}) to Group {group.Name} ({groupId}).", ex, fullMethodName);
                }
            }

        }

        private async Task AddDeviceToAzureAdministrativeUnit(string deviceId, string deviceObjectId, DeviceUpdateAction adminUnit)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint", EnvironmentVariableTarget.Process);
            string auId = adminUnit.Value;

            if (string.IsNullOrEmpty(deviceObjectId))
            {
                _logger.DSLogError($"Device Object Id is null or empty.", fullMethodName);
                return;
            }
            if (string.IsNullOrEmpty(auId))
            {
                _logger.DSLogError($"AU Id is null or empty.", fullMethodName);
                return;
            }
            if (Regex.IsMatch(deviceId, _guidRegex) == false)
            {
                _logger.DSLogError($"Device Object Id is not a valid GUID. DeviceId: {deviceObjectId}", fullMethodName);
                return;
            }
            if (Regex.IsMatch(auId, _guidRegex) == false)
            {
                _logger.DSLogError($"AU Id is not a valid GUID. AU Id: {auId}", fullMethodName);
                return;
            }

            try
            {
                var requestBody = new ReferenceCreate
                {
                    OdataId = $"{graphEndpoint}v1.0/devices/{deviceObjectId}"
                };
                await _graphClient.Directory.AdministrativeUnits[$"{auId}"].Members.Ref.PostAsync(requestBody);
                _logger.DSLogAudit($"Added Device {deviceId} (as Object ID {deviceObjectId}) to Administrative Unit {adminUnit.Name} ({auId}).", fullMethodName);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("conflicting object"))
                {
                   _logger.DSLogInformation($"Device {deviceId} (as Object ID {deviceObjectId}) already exists in Administrative Unit {adminUnit.Name} ({auId}).", fullMethodName);
                }
                else
                {
                   _logger.DSLogException($"Unable to add Device {deviceId} (as Object ID {deviceObjectId}) to Administrative Unit: {adminUnit.Name} ({auId}).", ex, fullMethodName);
                }
            }
        }

        private async Task<List<Microsoft.Graph.Models.ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Getting devices enrolled since {dateTime:MM/dd/yyyy HH:mm:ss} UTC", fullMethodName);


            List<Microsoft.Graph.Models.ManagedDevice> devices = new List<Microsoft.Graph.Models.ManagedDevice>();

            try
            {
                var managedDevices = await _graphClient.DeviceManagement.ManagedDevices
                    .GetAsync((requestConfiguration) =>
                    {
                      // requestConfiguration.QueryParameters.Select = ["id","manufacturer","model","serialNumber","azureADDeviceId"];
                      requestConfiguration.QueryParameters.Select = new string[] { "id", "manufacturer", "model", "serialNumber", "azureADDeviceId", "userPrincipalName" };
                      requestConfiguration.QueryParameters.Filter = $"enrolledDateTime ge {dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
                    });

                var pageIterator = Microsoft.Graph.PageIterator<ManagedDevice, ManagedDeviceCollectionResponse>
                    .CreatePageIterator(_graphClient, managedDevices, (device) =>
                    {
                        devices.Add(device);
                        return true;
                    });

                await pageIterator.IterateAsync();

                _logger.DSLogInformation($"Found {devices.Count} new enrolled devices since {dateTime:MM/dd/yyyy HH:mm:ss} UTC", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Unable to retrieve managed devices from Graph", ex, fullMethodName);
            }

            return devices;
        }

        
        private async Task<FunctionSettings> GetFunctionSettings()
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;
            FunctionSettings settings = new FunctionSettings();
            try
            {
                settings = await _container.ReadItemAsync<FunctionSettings>(settings.Id.ToString(), new PartitionKey(settings.PartitionKey));
                _logger.DSLogInformation($"Successfully retrieved function settings.",fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Unable to retrieve function settings.",ex, fullMethodName);
      }
            finally
            {
                if (settings == null)
                {
                    settings = new FunctionSettings();
                }
            }

            return settings;
        }

        private async Task UpdateFunctionSettings()
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            FunctionSettings settings = new FunctionSettings();
            settings.LastRun = DateTime.UtcNow;
            try
            {
                var response = await _container.UpsertItemAsync<FunctionSettings>(settings, new PartitionKey(settings.PartitionKey));
                _logger.DSLogInformation($"Successfully updated function settings.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Unable to update function settings.",ex,fullMethodName);
            }
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
                _logger.DSLogWarning("COSMOS_CONTAINER_NAME is null or empty, using default value of DeviceData",fullMethodName);
                containerName = "DeviceData";
            }
            if(string.IsNullOrEmpty(databaseName))
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
                _logger.DSLogException($"Failed to connect to CosmosDB",ex, fullMethodName);
            }

            _logger.DSLogInformation($"Connected to Cosmos DB database {databaseName} container {containerName}.", fullMethodName);
        }

        private void ConnectToGraph()
        {

            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Graph...", fullMethodName);
      

            var subject = Environment.GetEnvironmentVariable("CertificateDistinguishedName", EnvironmentVariableTarget.Process);
            var TenantId = Environment.GetEnvironmentVariable("AzureAd:TenantId", EnvironmentVariableTarget.Process);
            var ClientId = Environment.GetEnvironmentVariable("AzureAd:ClientId", EnvironmentVariableTarget.Process);
            var ClientSecret = Environment.GetEnvironmentVariable("AzureApp:ClientSecret", EnvironmentVariableTarget.Process);
            var azureCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);
            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint", EnvironmentVariableTarget.Process);

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "v1.0";

            var options = new TokenCredentialOptions
            {
                AuthorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };



            if (string.IsNullOrEmpty(TenantId) || string.IsNullOrEmpty(ClientId) || (string.IsNullOrEmpty(ClientSecret) && string.IsNullOrEmpty(subject)))
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("Missing required environment variables. Please check the following environment variables are set:");
                sb.Append(string.IsNullOrEmpty(TenantId) ? "AzureAd:TenantId, " : "");
                sb.Append(string.IsNullOrEmpty(ClientId) ? "AzureAd:ClientId, " : "");
                sb.Append(string.IsNullOrEmpty(ClientSecret) && string.IsNullOrEmpty(subject) ? "AzureApp:ClientSecret or CertificateDistinguishedName" : "");
                _logger.DSLogError(sb.ToString(),fullMethodName);
                return;
            }


            if (!string.IsNullOrEmpty(subject))
            {
                _logger.DSLogInformation("Using certificate authentication: ", fullMethodName);
                _logger.DSLogDebug("TenantId: " + TenantId, fullMethodName);
                _logger.DSLogDebug("ClientId: " + ClientId, fullMethodName);
                _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);
                X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert => cert.Subject == subject);

                var clientCertCredential = new ClientCertificateCredential(
                    TenantId,
                    ClientId,
                    certificate,
                    options
                );
                store.Close();
                _graphClient = new GraphServiceClient(clientCertCredential,scopes,baseUrl);
            }
            else
            {
                _logger.DSLogInformation("Using client secret authentication: ", fullMethodName);
                _logger.DSLogDebug("TenantId: " + TenantId, fullMethodName);
                _logger.DSLogDebug("ClientId: " + ClientId, fullMethodName);
                _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);
                var clientSecretCredential = new ClientSecretCredential(
                    TenantId,
                    ClientId,
                    ClientSecret,
                    options
                );

                _graphClient = new GraphServiceClient(clientSecretCredential,scopes,baseUrl);

            }

            _logger.DSLogInformation($"Connected to Graph endpoint: {graphEndpoint}.", fullMethodName);
        }

        public class TimerInfo
        {
            public TimerScheduleStatus ScheduleStatus { get; set; }

            public bool IsPastDue { get; set; }
        }

        public class TimerScheduleStatus
        {
            public DateTime Last { get; set; }

            public DateTime Next { get; set; }

            public DateTime LastUpdated { get; set; }
        }
    }

}

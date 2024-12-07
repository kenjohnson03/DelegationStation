using Azure.Identity;
using DelegationSharedLibrary;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.DeviceManagement.ImportedDeviceIdentities.ImportDeviceIdentityList;
using Microsoft.Graph.Beta.Models;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DelegationStation.IdentityImporter
{

     public class IdentityImporter
    {
        private readonly ILogger _logger;
        private static Container? _container = null;
        private static GraphServiceClient? _graphClient = null;

        public IdentityImporter(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<IdentityImporter>();
        }

        internal async Task RunAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Corporate Identity Importer starting....", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }

           
            ConnectToGraph();
            if (_graphClient == null)
            {
                _logger.DSLogError("Failed to connect to Graph Beta Endpoint, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            int result = await UpdateDevicesAsync();

            _logger.DSLogInformation($"Corporate Identity Importer done:  Updated {result} devices.", fullMethodName);

        }

        private void ConnectToCosmosDb()
        {

            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Cosmos DB...", fullMethodName);

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
            if (string.IsNullOrEmpty(connectionString))
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
                _logger.DSLogException("Failed to connect to CosmosDB: ", ex, fullMethodName);
                return;
            }

            _logger.DSLogInformation($"Connected to Cosmos DB database {databaseName} container {containerName}.", fullMethodName);
        }

        private void ConnectToGraph()
        {

            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Graph Beta...", fullMethodName);

            var subject = Environment.GetEnvironmentVariable("CertificateDistinguishedName", EnvironmentVariableTarget.Process);
            var TenantId = Environment.GetEnvironmentVariable("AzureAd__TenantId", EnvironmentVariableTarget.Process);
            var ClientId = Environment.GetEnvironmentVariable("AzureAd__ClientId", EnvironmentVariableTarget.Process);
            var ClientSecret = Environment.GetEnvironmentVariable("AzureApp__ClientSecret", EnvironmentVariableTarget.Process);
            var azureCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);
            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint", EnvironmentVariableTarget.Process);

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "beta";

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
                _logger.DSLogError(sb.ToString(), fullMethodName);
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
                _graphClient = new GraphServiceClient(clientCertCredential, scopes, baseUrl);
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

                _graphClient = new GraphServiceClient(clientSecretCredential, scopes, baseUrl);

            }

            _logger.DSLogInformation($"Connected to Graph endpoint: {graphEndpoint}.", fullMethodName);
        }



        /// <summary>
        /// For each deviuce in DS database, submit a corporate identifier to Graph Beta and update the device in the DB with the returned information.
        /// </summary>
        /// <returns>The count of devices updated.</returns>
        private async Task<int> UpdateDevicesAsync()
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Processing devices...", fullMethodName);

            int deviceUpdated = 0;

            // Search CosmosDB for all devices
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\"");

            FeedIterator<DelegationStationShared.Models.Device> queryIterator = null;
            try
            {
                _logger.DSLogInformation("Querying CosmosDB for devices...", fullMethodName);
                queryIterator = _container.GetItemQueryIterator<DelegationStationShared.Models.Device>(query);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Unable to query CosmosDB: ", ex, fullMethodName);
                return 0;
            }


            while (queryIterator.HasMoreResults)
            {

                try
                {
                    var response = queryIterator.ReadNextAsync().Result;
                    _logger.DSLogInformation($"Retrieved devices: {response.Count}", fullMethodName);


                    foreach (var device in response)
                    {
                        _logger.DSLogInformation($"Updating Device {device.Id} - Make: '{device.Make}' Model: '{device.Model}' SerialNumber: '{device.SerialNumber}'", fullMethodName);

                        string corpID = device.Make.Trim() + "," + device.Model.Trim() + "," + device.SerialNumber.ToString();
                        _logger.DSLogInformation($"Submitting identifier for device {device.Id}: {corpID}", fullMethodName);

                        // Insert data into Graph Beta
                        ImportedDeviceIdentity newEntry = await AddImportedDevice(corpID);

                        // Update database device entry with corporate identifier info
                        if ((newEntry != null) && (newEntry.Id != null) && (newEntry.ImportedDeviceIdentifier != null))
                        {
                            _logger.DSLogInformation($"Corporate Identifier added for device {device.Id} - ID: '{newEntry.Id}' Identifier: '{newEntry.ImportedDeviceIdentifier}'", fullMethodName);
                            // FIXME
                            //device.updateCorporateIdentityInfo(newEntry.ImportedDeviceIdentifier, newEntry.Id, DateTime.UtcNow);
                            device.CorporateIdentityType = "manufacturerModelSerial";
                            device.CorporateIdentityID = newEntry.Id;
                            device.CorporateIdentity = newEntry.ImportedDeviceIdentifier;
                            device.LastCorpIdentitySync = DateTime.UtcNow;
                            try
                            {
                                await _container.ReplaceItemAsync(device, device.Id.ToString());
                                _logger.DSLogInformation($"Device {device.Id} database entry updated.", fullMethodName);
                                deviceUpdated++;
                            }
                            catch (Exception ex)
                            {
                                _logger.DSLogException("Unable to update device {device.Id} in CosmosDB: ", ex, fullMethodName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Unable to read next page of devices from CosmosDB: ", ex, fullMethodName);
                    break;
                }
            }
            _logger.DSLogInformation($"Devices processed: {deviceUpdated}", fullMethodName);

            return deviceUpdated;

        }



        /// <summary>
        /// Adds a Corporate Identifer to InTune via Graph Beta
        /// </summary>
        /// <param name="identifier">A CSV string in the format "Make,Model,SerialNumber"</param>
        /// <returns>ImportedDeviceIdentity object</returns>
        private async Task<ImportedDeviceIdentity> AddImportedDevice(string identifier)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Adding identifier: {identifier}", fullMethodName);

            ImportedDeviceIdentity importedDevice = new ImportedDeviceIdentity();
            importedDevice.ImportedDeviceIdentityType = ImportedDeviceIdentityType.ManufacturerModelSerial;
            importedDevice.ImportedDeviceIdentifier = identifier;

            List<ImportedDeviceIdentity> addList = new List<ImportedDeviceIdentity>();
            addList.Add(importedDevice);

            ImportDeviceIdentityListPostRequestBody requestBody = new ImportDeviceIdentityListPostRequestBody();
            requestBody.OverwriteImportedDeviceIdentities = false;
            requestBody.ImportedDeviceIdentities = addList;


            ImportedDeviceIdentity deviceIdentity; 
            try
            {
                // Note:  If entry already exists, it will just return object
                var result = await _graphClient.DeviceManagement.ImportedDeviceIdentities.ImportDeviceIdentityList.PostAsImportDeviceIdentityListPostResponseAsync(requestBody);
                deviceIdentity = result.Value[0];
                _logger.DSLogInformation($"Identifier Added: {deviceIdentity.ImportedDeviceIdentifier}", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Unable to add device identifierto Graph: {identifier}", ex, fullMethodName);
                return null;
            }

            return deviceIdentity;

        }
    }
}

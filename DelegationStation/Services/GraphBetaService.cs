using Azure.Identity;
using DelegationSharedLibrary;
using DelegationStation.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.DeviceManagement.ImportedDeviceIdentities.ImportDeviceIdentityList;
using Microsoft.Graph.Beta.Models;
using System.Security.Cryptography.X509Certificates;

namespace DelegationStation.Services
{
    public class GraphBetaService : IGraphBetaService
    {
        private readonly ILogger<GraphBetaService> _logger;
        private GraphServiceClient _graphClient;

        public GraphBetaService(IConfiguration configuration, ILogger<GraphBetaService> logger)
        {
            this._logger = logger;

            var azureCloud = configuration.GetSection("AzureEnvironment").Value;
            var graphEndpoint = configuration.GetSection("GraphEndpoint").Value;

            var options = new TokenCredentialOptions
            {
                AuthorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "beta";

            var certDN = configuration.GetSection("AzureAd:ClientCertificates:CertificateDistinguishedName").Value;

            if (!String.IsNullOrEmpty(certDN))
            {
                _logger.LogInformation("Using certificate authentication: ");
                _logger.LogDebug("AzureCloud: " + azureCloud);
                _logger.LogDebug("GraphEndpoint: " + graphEndpoint);

                X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                _logger.LogInformation("Using certificate with Subject Name {0} for Graph service", certDN);
                var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert => cert.Subject.ToString() == certDN);

                var clientCertCredential = new ClientCertificateCredential(
                    configuration.GetSection("AzureAd:TenantId").Value,
                    configuration.GetSection("AzureAd:ClientId").Value,
                    certificate,
                    options
                );
                store.Close();
                this._graphClient = new GraphServiceClient(clientCertCredential, scopes, baseUrl);
            }
            else
            {
                _logger.LogInformation("Using Client Secret for Graph service");
                _logger.LogDebug("AzureCloud: " + azureCloud);
                _logger.LogDebug("GraphEndpoint: " + graphEndpoint);


                var clientSecretCredential = new ClientSecretCredential(
                    configuration.GetSection("AzureAd:TenantId").Value,
                    configuration.GetSection("AzureAd:ClientId").Value,
                    configuration.GetSection("AzureApp:ClientSecret").Value,
                    options
                );

                this._graphClient = new GraphServiceClient(clientSecretCredential, scopes, baseUrl);
            }
        }

        public async Task<ImportedDeviceIdentity> AddCorporateIdentifer(string identifier)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.LogInformation($"Adding identifier: {identifier}");

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
                _logger.LogInformation($"Identifier Added: {deviceIdentity.ImportedDeviceIdentifier}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unable to add device identifier {identifier} to Graph: " + ex);
                return null;
            }

            return deviceIdentity;
        }
    }
}

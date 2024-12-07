using Azure.Identity;
using CorporateIdentiferSync.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Beta.Models.ODataErrors;
using Microsoft.Graph.Models;
using System.Security.Cryptography.X509Certificates;


namespace CorporateIdentiferSync.Services
{
    public class GraphService : IGraphService
    {
        private readonly ILogger<GraphService> _logger;
        private GraphServiceClient _graphClient;

        public GraphService(IConfiguration configuration, ILogger<GraphService> logger)
        {
            this._logger = logger;

            var azureCloud = configuration.GetSection("AzureEnvironment").Value;
            var graphEndpoint = configuration.GetSection("GraphEndpoint").Value;

            var options = new TokenCredentialOptions
            {
                AuthorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "v1.0";

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



        public async Task<bool> DeleteManagedDevice(string managedDeviceID)
        {
            try
            {
                await _graphClient.DeviceManagement.ManagedDevices[managedDeviceID].DeleteAsync();
                _logger.LogInformation($"Managed Device Deleted: {managedDeviceID}");
                return true;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError err) // when (err.Error.Code.Equals("ResourceNotFound"))
            {
                _logger.LogInformation($"Unable to remove managed device found for: {managedDeviceID}");
                //return true;
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unable to delete device {managedDeviceID} from Intune: " + ex);
                return false;
            }
        }


        public async Task<ManagedDevice> GetManagedDevice(string make, string model, string serialNum)
        {
            ManagedDevice? result = null;
            try
            {
                var devices = await _graphClient.DeviceManagement.ManagedDevices.GetAsync((requestConfiguration) =>
                {
                    requestConfiguration.QueryParameters.Filter = $"(manufacturer eq '{make}' and model eq '{model}' and serialNumber eq '{serialNum}')";
                });
                if (devices == null)
                {
                    return null;
                }
                else
                {
                    result = devices.Value.FirstOrDefault();
                }
            }
            catch(Microsoft.Graph.Models.ODataErrors.ODataError err) when (err.Error.Code.Equals("ResourceNotFound"))
            {
                _logger.LogInformation($"No managed device found for: {make} {model} {serialNum}");
                return null;
            }

            return result;

        }
    }
}

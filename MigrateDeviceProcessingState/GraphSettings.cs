using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace MigrateDeviceProcessingState;

public sealed record GraphSettings(Uri Endpoint, Uri AuthorityHost)
{
    public static GraphSettings FromConfiguration(IConfiguration configuration)
    {
        string cloud = configuration["AzureEnvironment"]?.Trim()
            ?? throw new InvalidOperationException("AzureEnvironment must match the webapp configuration.");
        if (cloud is not ("AzurePublicCloud" or "AzureUSGovernment" or "AzureUSDoD"))
            throw new InvalidOperationException("AzureEnvironment must be AzurePublicCloud, AzureUSGovernment or AzureUSDoD.");

        string endpoint = configuration["GraphEndpoint"]?.Trim()
            ?? throw new InvalidOperationException("GraphEndpoint must match the webapp configuration.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? graphEndpoint) ||
            graphEndpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("GraphEndpoint must be an absolute HTTPS URL.");

        // Match GraphService: use the configured endpoint, and use Government authority for non-public clouds.
        Uri normalizedEndpoint = new(graphEndpoint.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        Uri authorityHost = cloud == "AzurePublicCloud"
            ? AzureAuthorityHosts.AzurePublicCloud
            : AzureAuthorityHosts.AzureGovernment;
        return new GraphSettings(normalizedEndpoint, authorityHost);
    }
}

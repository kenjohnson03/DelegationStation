using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace MigrateDeviceProcessingState.Tests;

public class GraphSettingsTests
{
    [Theory]
    [InlineData("AzurePublicCloud", "https://graph.microsoft.com", "https://graph.microsoft.com/", true)]
    [InlineData("AzureUSGovernment", "https://graph.microsoft.us/", "https://graph.microsoft.us/", false)]
    [InlineData("AzureUSDoD", "https://dod-graph.microsoft.us/", "https://dod-graph.microsoft.us/", false)]
    public void UsesWebAppEnvironmentSettingNamesAndGraphEndpoints(
        string cloud, string endpoint, string normalizedEndpoint, bool publicCloud)
    {
        var configuration = new ConfigurationManager
        {
            ["AzureEnvironment"] = cloud,
            ["GraphEndpoint"] = endpoint
        };

        GraphSettings settings = GraphSettings.FromConfiguration(configuration);

        Assert.Equal(new Uri(normalizedEndpoint), settings.Endpoint);
        Assert.Equal(publicCloud ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment,
            settings.AuthorityHost);
    }

    [Theory]
    [InlineData(null, "https://graph.microsoft.com/")]
    [InlineData("AzurePublicCloud", null)]
    [InlineData("UnknownCloud", "https://graph.microsoft.com/")]
    [InlineData("AzurePublicCloud", "http://graph.microsoft.com/")]
    public void RejectsMissingOrInvalidWebAppGraphSettings(string? cloud, string? endpoint)
    {
        var configuration = new ConfigurationManager
        {
            ["AzureEnvironment"] = cloud,
            ["GraphEndpoint"] = endpoint
        };

        Assert.Throws<InvalidOperationException>(() => GraphSettings.FromConfiguration(configuration));
    }
}

using System.Net;
using System.Text;
using DelegationStationShared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using UpdateDevices.Services;

namespace UpdateDevices.Tests;

public class GraphServiceMembershipTests
{
    private const string ManagedDeviceId = "11111111-1111-1111-1111-111111111111";
    private const string EntraObjectId = "22222222-2222-2222-2222-222222222222";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string? _errorMessage;

        public int RequestCount { get; private set; }

        public StubHttpMessageHandler(HttpStatusCode statusCode, string? errorMessage = null)
        {
            _statusCode = statusCode;
            _errorMessage = errorMessage;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(_statusCode) { RequestMessage = request };
            if (_errorMessage != null)
            {
                string body = "{\"error\":{\"code\":\"Request_BadRequest\",\"message\":\"" + _errorMessage + "\"}}";
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }
    }

    private static GraphService CreateService(StubHttpMessageHandler handler)
    {
        var graphClient = new GraphServiceClient(
            new HttpClient(handler),
            new AnonymousAuthenticationProvider(),
            "https://graph.microsoft.com/v1.0");
        return new GraphService(graphClient, NullLogger<GraphService>.Instance);
    }

    private static DeviceUpdateAction Group() =>
        new(Guid.NewGuid(), DeviceUpdateActionType.Group, "Group A", Guid.NewGuid().ToString());

    private static DeviceUpdateAction AdminUnit() =>
        new(Guid.NewGuid(), DeviceUpdateActionType.AdministrativeUnit, "AU A", Guid.NewGuid().ToString());

    [Fact]
    public async Task AddDeviceToAzureADGroup_DeviceAlreadyMember_ReturnsTrue()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest,
            "One or more added object references already exist for the following modified properties: 'members'.");
        GraphService service = CreateService(handler);

        bool result = await service.AddDeviceToAzureADGroup(ManagedDeviceId, EntraObjectId, Group());

        Assert.True(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AddDeviceToAzureADGroup_OtherGraphError_ReturnsFalse()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, "Insufficient privileges to complete the operation.");
        GraphService service = CreateService(handler);

        bool result = await service.AddDeviceToAzureADGroup(ManagedDeviceId, EntraObjectId, Group());

        Assert.False(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AddDeviceToAzureAdministrativeUnit_DeviceAlreadyMember_ReturnsTrue()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest,
            "A conflicting object with one or more of the specified property values is present in the directory.");
        GraphService service = CreateService(handler);

        bool result = await service.AddDeviceToAzureAdministrativeUnit(ManagedDeviceId, EntraObjectId, AdminUnit());

        Assert.True(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AddDeviceToAzureAdministrativeUnit_OtherGraphError_ReturnsFalse()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, "Insufficient privileges to complete the operation.");
        GraphService service = CreateService(handler);

        bool result = await service.AddDeviceToAzureAdministrativeUnit(ManagedDeviceId, EntraObjectId, AdminUnit());

        Assert.False(result);
        Assert.Equal(1, handler.RequestCount);
    }
}

using System.Net;
using System.Net.Http.Headers;
using Azure.Core;
using DelegationStationShared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigrateDeviceProcessingState.Tests;

public class GraphIntuneReaderTests
{
    private static readonly Uri Endpoint = new("https://graph.microsoft.com/");
    private static readonly Device Device = new() { Make = "Dell", Model = "Model", SerialNumber = "Serial" };
    private static readonly HashSet<DeviceKey> Keys = [DeviceKey.FromCosmos(Device)];

    [Fact]
    public async Task PagesWithSelectAndChoosesNewestMatchingRecord()
    {
        var handler = new Handler(
            Json("""{"value":[{"manufacturer":" Dell ","model":" model ","serialNumber":" SERIAL ","enrolledDateTime":"2024-01-01T00:00:00Z","lastSyncDateTime":"2026-01-01T00:00:00Z"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/deviceManagement/managedDevices?$skiptoken=abc"}"""),
            Json("""{"value":[{"manufacturer":"dell","model":"Model","serialNumber":"serial","enrolledDateTime":"2025-01-01T00:00:00+03:00","lastSyncDateTime":"2026-09-01T00:00:00Z"},{"manufacturer":"dell","model":"Model","serialNumber":"other","lastSyncDateTime":"2026-09-01T00:00:00Z"}]}"""));
        using var http = new HttpClient(handler);
        var result = await Reader(http).GetMatchesAsync(Keys, CancellationToken.None);
        var match = Assert.Single(result).Value;
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T00:00:00Z"), match.LastSyncDateTime);
        Assert.Equal(DateTimeOffset.Parse("2024-12-31T21:00:00Z"), match.EnrolledDateTime);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("$select=manufacturer,model,serialNumber,enrolledDateTime,lastSyncDateTime", handler.Requests[0].Query);
        Assert.Contains("$skiptoken=abc", handler.Requests[1].Query);
    }

    [Fact]
    public async Task DatedRecordWinsOverNullSyncRegardlessOfPageOrder()
    {
        var handler = new Handler(Json("""
            {"value":[
            {"manufacturer":"Dell","model":"Model","serialNumber":"Serial"},
            {"manufacturer":"Dell","model":"Model","serialNumber":"Serial","lastSyncDateTime":"2026-09-01T00:00:00Z"},
            {"manufacturer":"Dell","model":"Model","serialNumber":"Serial"}
            ]}
            """));
        using var http = new HttpClient(handler);
        Assert.NotNull((await Reader(http).GetMatchesAsync(Keys, CancellationToken.None)).Single().Value.LastSyncDateTime);
    }

    [Fact]
    public async Task Retries429UsingRetryAfter()
    {
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var handler = new Handler(throttled, Json("""{"value":[]}"""));
        using var http = new HttpClient(handler);
        var waits = new List<TimeSpan>();
        await Reader(http, (wait, _) => { waits.Add(wait); return Task.CompletedTask; })
            .GetMatchesAsync(Keys, CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(waits));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetriesHttpDateAndDoesNotIgnoreCancellation()
    {
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(1));
        using var http = new HttpClient(new Handler(throttled));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Reader(http, (wait, token) =>
            {
                Assert.InRange(wait.TotalSeconds, 1, 61);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }).GetMatchesAsync(Keys, cancellation.Token));
    }

    [Fact]
    public async Task ExhaustedRetriesFailInsteadOfReturningPartialMatches()
    {
        var handler = new Handler(new(HttpStatusCode.TooManyRequests), new(HttpStatusCode.TooManyRequests),
            new(HttpStatusCode.TooManyRequests));
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Reader(http, (_, _) => Task.CompletedTask).GetMatchesAsync(Keys, CancellationToken.None));
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"value":[],"@odata.nextLink":"https://untrusted.example/page"}""")]
    public async Task InvalidPageFailsClosed(string json)
    {
        var handler = new Handler(Json(json));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reader(http).GetMatchesAsync(Keys, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MatchingDoesNotTrimStoredCosmosValues()
    {
        var stored = new Device { Make = "Dell ", Model = "Model", SerialNumber = "Serial" };
        using var http = new HttpClient(new Handler(Json(
            """{"value":[{"manufacturer":"Dell","model":"Model","serialNumber":"Serial"}]}""")));
        Assert.Empty(await Reader(http).GetMatchesAsync(
            new HashSet<DeviceKey> { DeviceKey.FromCosmos(stored) }, CancellationToken.None));
    }

    private static GraphIntuneReader Reader(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(http, new Credential(), Endpoint, 2, NullLogger<GraphIntuneReader>.Instance, delay);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class Handler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> pages = new(responses);
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Requests.Add(request.RequestUri!);
            return Task.FromResult(pages.Dequeue());
        }
    }

    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}

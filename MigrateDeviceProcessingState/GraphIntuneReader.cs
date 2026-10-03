using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace MigrateDeviceProcessingState;

public sealed class GraphIntuneReader(HttpClient http, TokenCredential credential, Uri graphEndpoint,
    int maxRetries, ILogger<GraphIntuneReader> logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IIntuneReader
{
    private sealed class Page
    {
        [JsonPropertyName("value")]
        public List<ManagedDeviceFields>? Value { get; set; }
        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private sealed class ManagedDeviceFields
    {
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public DateTimeOffset? EnrolledDateTime { get; set; }
        public DateTimeOffset? LastSyncDateTime { get; set; }
    }

    public async Task<IReadOnlyDictionary<DeviceKey, IntuneDevice>> GetMatchesAsync(
        IReadOnlySet<DeviceKey> keys, CancellationToken cancellationToken)
    {
        var matches = new Dictionary<DeviceKey, IntuneDevice>();
        Uri? next = new(graphEndpoint,
            "v1.0/deviceManagement/managedDevices?$select=manufacturer,model,serialNumber,enrolledDateTime,lastSyncDateTime");
        int pages = 0, records = 0;
        while (next != null)
        {
            if (next.Scheme != Uri.UriSchemeHttps || next.Authority != graphEndpoint.Authority)
                throw new InvalidOperationException("Graph returned a nextLink outside the configured HTTPS Graph endpoint.");

            using HttpResponseMessage response = await GetPageAsync(next, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            Page page = await JsonSerializer.DeserializeAsync<Page>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
                ?? throw new InvalidOperationException("Graph returned an empty page.");
            if (page.Value == null)
                throw new InvalidOperationException("Graph managedDevices response is missing value; refusing to mark devices not found.");

            pages++;
            records += page.Value.Count;
            foreach (var item in page.Value)
            {
                if (string.IsNullOrWhiteSpace(item.Manufacturer) || string.IsNullOrWhiteSpace(item.Model) ||
                    string.IsNullOrWhiteSpace(item.SerialNumber))
                    continue;
                var device = new IntuneDevice(item.Manufacturer, item.Model, item.SerialNumber,
                    item.EnrolledDateTime, item.LastSyncDateTime);
                DeviceKey key = DeviceKey.FromGraph(device);
                if (keys.Contains(key) &&
                    (!matches.TryGetValue(key, out var previous) ||
                     (device.LastSyncDateTime ?? DateTimeOffset.MinValue) >
                     (previous.LastSyncDateTime ?? DateTimeOffset.MinValue)))
                    matches[key] = device;
            }
            next = string.IsNullOrEmpty(page.NextLink) ? null : new Uri(page.NextLink, UriKind.Absolute);
        }
        logger.LogInformation("Read {Pages} Graph pages, {Records} managedDevices, {Matches} matching hardware keys.",
            pages, records, matches.Count);
        return matches;
    }

    private async Task<HttpResponseMessage> GetPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            AccessToken token = await credential.GetTokenAsync(
                new TokenRequestContext([$"{graphEndpoint.AbsoluteUri.TrimEnd('/')}/.default"]), cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
            {
                try
                {
                    response.EnsureSuccessStatusCode();
                    return response;
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }
            if (attempt >= maxRetries)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"Graph retry limit ({maxRetries}) exhausted with HTTP {(int)status}.", null, status);
            }

            TimeSpan wait = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date is DateTimeOffset date
                    ? date - DateTimeOffset.UtcNow
                    : TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt + 1))));
            if (wait < TimeSpan.Zero)
                wait = TimeSpan.Zero;
            logger.LogWarning("Graph HTTP {Status}; retry {Attempt}/{MaxRetries} after {DelaySeconds}s.",
                (int)response.StatusCode, attempt + 1, maxRetries, wait.TotalSeconds);
            response.Dispose();
            await (delay ?? Task.Delay)(wait, cancellationToken);
        }
    }
}

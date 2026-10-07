using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using DelegationStationShared.Models;
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
        // Keep the deserialized Graph shape limited to fields needed for hardware matching and qualification.
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public DateTimeOffset? EnrolledDateTime { get; set; }
        public DateTimeOffset? LastSyncDateTime { get; set; }
    }

    public async Task<IntuneDevice?> GetMatchAsync(Device cosmosDevice, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cosmosDevice.Make) || string.IsNullOrWhiteSpace(cosmosDevice.Model) ||
            string.IsNullOrWhiteSpace(cosmosDevice.SerialNumber))
        {
            logger.LogWarning("Device {DeviceID} lacks make, model or serial number; no Intune lookup can be made.", cosmosDevice.Id);
            return null;
        }

        IntuneDevice? match = null;
        DeviceKey expectedKey = DeviceKey.FromCosmos(cosmosDevice);
        // Escape OData string literals before URL encoding; never interpolate unescaped hardware values.
        string filter = $"manufacturer eq {Literal(cosmosDevice.Make)} and model eq {Literal(cosmosDevice.Model)} " +
            $"and serialNumber eq {Literal(cosmosDevice.SerialNumber)}";
        Uri? next = new(graphEndpoint,
            "v1.0/deviceManagement/managedDevices?$select=manufacturer,model,serialNumber,enrolledDateTime,lastSyncDateTime" +
            "&$filter=" + Uri.EscapeDataString(filter));
        int pages = 0, records = 0;
        logger.LogDebug("Looking up Intune managedDevices by make/model/serial for device {DeviceID}.", cosmosDevice.Id);
        while (next != null)
        {
            if (next.Scheme != Uri.UriSchemeHttps || next.Authority != graphEndpoint.Authority)
                throw new InvalidOperationException("Graph returned a nextLink outside the configured HTTPS Graph endpoint.");

            Page page;
            try
            {
                using HttpResponseMessage response = await GetPageAsync(next, cancellationToken);
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                page = await JsonSerializer.DeserializeAsync<Page>(stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
                    ?? throw new InvalidOperationException("Graph returned an empty page.");
                if (page.Value == null)
                    throw new InvalidOperationException("Graph managedDevices response is missing value; refusing to mark devices not found.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed reading Intune lookup page {PageNumber} for device {DeviceID}; its migration marker will not be written.",
                    pages + 1, cosmosDevice.Id);
                throw;
            }

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
                // A hardware key can have multiple Intune records; retain the one most recently synced.
                if (key == expectedKey &&
                    (match == null ||
                     (device.LastSyncDateTime ?? DateTimeOffset.MinValue) >
                     (match.LastSyncDateTime ?? DateTimeOffset.MinValue)))
                {
                    match = device;
                }
            }
            logger.LogDebug("Read Intune lookup page {PageNumber} for device {DeviceID}: {RecordCount} records.",
                pages, cosmosDevice.Id, page.Value.Count);
            next = string.IsNullOrEmpty(page.NextLink) ? null : new Uri(page.NextLink, UriKind.Absolute);
        }
        logger.LogInformation("Intune lookup for device {DeviceID} completed: pages={Pages}, records={Records}, matched={Matched}.",
            cosmosDevice.Id, pages, records, match != null);
        return match;
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

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
                if (response.IsSuccessStatusCode)
                    return response;

                var status = response.StatusCode;
                response.Dispose();
                var exception = new HttpRequestException($"Graph returned HTTP {(int)status} for a managedDevices page.", null, status);
                logger.LogError(exception, "Graph request for managedDevices failed with HTTP {Status}.", (int)status);
                throw exception;
            }
            if (attempt >= maxRetries)
            {
                var status = response.StatusCode;
                response.Dispose();
                var exception = new HttpRequestException($"Graph retry limit ({maxRetries}) exhausted with HTTP {(int)status}.", null, status);
                logger.LogError(exception, "Graph retry limit exhausted after {AttemptCount} attempts with HTTP {Status}.",
                    attempt + 1, (int)status);
                throw exception;
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

using System.Net;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MigrateDeviceProcessingState;

public sealed class CosmosDeviceStore(Container container, ILogger<CosmosDeviceStore> logger) : IDeviceStore
{
    // The migration marker is the sole eligibility state: each event evaluates every device once,
    // including documents with already-populated processing fields.
    public const string EligibilityPredicate =
        "c.Type = 'Device' " +
        "AND (NOT IS_DEFINED(c.MigrationID) OR IS_NULL(c.MigrationID) OR c.MigrationID != @migrationID)";

    public static QueryDefinition CreateQuery(MigrationOptions options) => new QueryDefinition(
        "SELECT TOP @batchSize c.id, c.PartitionKey, c.Make, c.Model, c.SerialNumber, c._etag " +
        "FROM c WHERE " + EligibilityPredicate + " ORDER BY c.ModifiedUTC ASC")
        .WithParameter("@batchSize", options.BatchSize)
        .WithParameter("@migrationID", options.MigrationID);

    public async Task<IReadOnlyList<Device>> GetBatchAsync(MigrationOptions options, CancellationToken cancellationToken)
    {
        logger.LogInformation("Querying up to {BatchSize} devices not yet marked for MigrationID {MigrationID}.",
            options.BatchSize, options.MigrationID);
        using var iterator = container.GetItemQueryIterator<Device>(CreateQuery(options),
            requestOptions: new QueryRequestOptions { MaxItemCount = options.BatchSize });
        var devices = new List<Device>();
        double requestCharge = 0;
        try
        {
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                devices.AddRange(page);
                requestCharge += page.RequestCharge;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Cosmos query for migration {MigrationID} failed after loading {DeviceCount} devices.",
                options.MigrationID, devices.Count);
            throw;
        }
        logger.LogInformation("Cosmos query loaded {DeviceCount} devices for migration {MigrationID} at {RequestCharge} RU.",
            devices.Count, options.MigrationID, requestCharge);
        return devices;
    }

    public static IReadOnlyList<PatchOperation> CreatePatch(string migrationID, DateTime? enrollmentUtc)
    {
        // Apply the event marker atomically with any processing state so failed evaluations remain retryable.
        var operations = new List<PatchOperation> { PatchOperation.Set("/MigrationID", migrationID) };
        if (enrollmentUtc.HasValue)
        {
            operations.Add(PatchOperation.Set("/SuccessfullyProcessedUTC", enrollmentUtc.Value));
            operations.Add(PatchOperation.Set("/LastProcessingAttemptUTC", enrollmentUtc.Value));
            operations.Add(PatchOperation.Set("/ProcessingStatus", ProcessingStatus.Processed));
        }
        return operations;
    }

    public async Task<bool> PatchAsync(Device device, string migrationID, DateTime? enrollmentUtc)
    {
        if (string.IsNullOrWhiteSpace(device.ETag))
            throw new InvalidOperationException($"Device {device.Id} has no ETag; refusing an unguarded patch.");

        try
        {
            await container.PatchItemAsync<Device>(device.Id.ToString(), new PartitionKey(device.PartitionKey),
                CreatePatch(migrationID, enrollmentUtc),
                new PatchItemRequestOptions
                {
                    FilterPredicate = CreateETagPredicate(device.ETag),
                    EnableContentResponseOnWrite = false
                },
                CancellationToken.None);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed ||
                                        ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(ex,
                "Skipping Cosmos patch for device {DeviceID}: the item changed or was deleted after the query; it remains eligible for a later query.",
                device.Id);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cosmos patch failed for device {DeviceID}; MigrationID {MigrationID} was not confirmed.",
                device.Id, migrationID);
            throw;
        }
    }

    // PatchItemAsync has no IfMatchEtag option, so enforce the queried version in its SQL filter predicate.
    public static string CreateETagPredicate(string etag) =>
        $"FROM c WHERE c._etag = {JsonConvert.ToString(etag)}";
}

using System.Net;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace MigrateDeviceProcessingState;

public sealed class CosmosDeviceStore(Container container) : IDeviceStore
{
    public const string EligibilityPredicate =
        "c.Type = 'Device' " +
        "AND (NOT IS_DEFINED(c.MigrationID) OR IS_NULL(c.MigrationID) OR c.MigrationID != @migrationID) " +
        "AND (NOT IS_DEFINED(c.SuccessfullyProcessedUTC) OR IS_NULL(c.SuccessfullyProcessedUTC) " +
        "OR NOT IS_DEFINED(c.LastProcessingAttemptUTC) OR IS_NULL(c.LastProcessingAttemptUTC) " +
        "OR NOT IS_DEFINED(c.ProcessingStatus) OR IS_NULL(c.ProcessingStatus))";

    public static QueryDefinition CreateQuery(MigrationOptions options) => new QueryDefinition(
        "SELECT TOP @batchSize c.id, c.PartitionKey, c.Make, c.Model, c.SerialNumber, c._etag " +
        "FROM c WHERE " + EligibilityPredicate + " ORDER BY c.ModifiedUTC ASC")
        .WithParameter("@batchSize", options.BatchSize)
        .WithParameter("@migrationID", options.MigrationID);

    public async Task<IReadOnlyList<Device>> GetBatchAsync(MigrationOptions options, CancellationToken cancellationToken)
    {
        using var iterator = container.GetItemQueryIterator<Device>(CreateQuery(options),
            requestOptions: new QueryRequestOptions { MaxItemCount = options.BatchSize });
        var devices = new List<Device>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            devices.AddRange(page);
        }
        return devices;
    }

    public static IReadOnlyList<PatchOperation> CreatePatch(string migrationID, DateTime? enrollmentUtc)
    {
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
            return false;
        }
    }

    // PatchItemAsync does not support IfMatchEtag; enforce the read version in the patch predicate.
    public static string CreateETagPredicate(string etag) =>
        $"FROM c WHERE c._etag = {JsonConvert.ToString(etag)}";
}

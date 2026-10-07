using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace MigrateDeviceProcessingState.Tests;

public class CosmosDeviceStoreTests
{
    [Fact]
    public void QueryUsesOldestFirstBoundedEligibilityAndNoOffset()
    {
        var query = CosmosDeviceStore.CreateQuery(new("event", 37, 180, 2));
        Assert.Contains("TOP @batchSize", query.QueryText);
        Assert.Contains("ORDER BY c.ModifiedUTC ASC", query.QueryText);
        Assert.Contains("NOT IS_DEFINED(c.MigrationID)", query.QueryText);
        Assert.Contains("c.MigrationID != @migrationID", query.QueryText);
        Assert.DoesNotContain("SuccessfullyProcessedUTC", query.QueryText);
        Assert.DoesNotContain("LastProcessingAttemptUTC", query.QueryText);
        Assert.DoesNotContain("ProcessingStatus", query.QueryText);
        Assert.DoesNotContain("OFFSET", query.QueryText);
        Assert.Equal(37, query.GetQueryParameters().Single(p => p.Name == "@batchSize").Value);
        Assert.Equal("event", query.GetQueryParameters().Single(p => p.Name == "@migrationID").Value);
    }

    [Fact]
    public void QualifyingPatchOnlyWritesThreeFieldsAndMarker()
    {
        var enrollment = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var operations = CosmosDeviceStore.CreatePatch("event", enrollment);
        Assert.Equal(new[] { "/MigrationID", "/SuccessfullyProcessedUTC", "/LastProcessingAttemptUTC", "/ProcessingStatus" },
            operations.Select(o => o.Path));
        Assert.All(operations, o => Assert.Equal(PatchOperationType.Set, o.OperationType));
        Assert.Equal(enrollment, ((PatchOperation<DateTime>)operations[1]).Value);
        Assert.Equal(enrollment, ((PatchOperation<DateTime>)operations[2]).Value);
        Assert.Equal(ProcessingStatus.Processed, ((PatchOperation<ProcessingStatus>)operations[3]).Value);
    }

    [Fact]
    public void NonqualifyingPatchOnlyWritesMarker()
    {
        var operation = Assert.Single(CosmosDeviceStore.CreatePatch("event", null));
        Assert.Equal("/MigrationID", operation.Path);
        Assert.Equal("event", ((PatchOperation<string>)operation).Value);
    }

    [Fact]
    public void PatchPredicateEscapesETag()
    {
        const string etag = "\"etag-value\"";
        Assert.Equal("FROM c WHERE c._etag = " + JsonConvert.ToString(etag),
            CosmosDeviceStore.CreateETagPredicate(etag));
    }

    [Fact]
    public void SharedDeviceModelRoundTripsMigrationAndProcessingState()
    {
        var device = new Device
        {
            MigrationID = "event",
            SuccessfullyProcessedUTC = DateTime.UtcNow,
            LastProcessingAttemptUTC = DateTime.UtcNow,
            ProcessingStatus = ProcessingStatus.Processed
        };
        var roundTrip = JsonConvert.DeserializeObject<Device>(JsonConvert.SerializeObject(device))!;
        Assert.Equal(device.MigrationID, roundTrip.MigrationID);
        Assert.Equal(device.SuccessfullyProcessedUTC, roundTrip.SuccessfullyProcessedUTC);
        Assert.Equal(device.LastProcessingAttemptUTC, roundTrip.LastProcessingAttemptUTC);
        Assert.Equal(device.ProcessingStatus, roundTrip.ProcessingStatus);
    }
}

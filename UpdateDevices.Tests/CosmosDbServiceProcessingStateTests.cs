using UpdateDevices.Services;

namespace UpdateDevices.Tests;

public class CosmosDbServiceProcessingStateTests
{
    [Fact]
    public void ProcessingStateFilterPredicate_AllowsMissingOrOlderEnrollment()
    {
        DateTime enrolledUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        string predicate = CosmosDbService.ProcessingStateFilterPredicate(enrolledUtc);

        Assert.Equal(
            "FROM c WHERE NOT IS_DEFINED(c.LastSeenEnrollmentUTC) OR IS_NULL(c.LastSeenEnrollmentUTC) " +
            "OR DATETIMETOTICKS(c.LastSeenEnrollmentUTC) <= DATETIMETOTICKS(\"2026-01-15T12:00:00.0000000Z\")",
            predicate);
    }

    [Fact]
    public void ProcessingStateFilterPredicate_PreservesFractionalSeconds()
    {
        DateTime enrolledUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        string predicate = CosmosDbService.ProcessingStateFilterPredicate(enrolledUtc.AddTicks(1));

        Assert.Contains("DATETIMETOTICKS(\"2026-01-15T12:00:00.0000001Z\")", predicate);
    }
}

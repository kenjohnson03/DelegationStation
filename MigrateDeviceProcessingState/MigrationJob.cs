using DelegationStationShared.Models;
using Microsoft.Extensions.Logging;

namespace MigrateDeviceProcessingState;

public readonly record struct DeviceKey(string? Make, string? Model, string? SerialNumber)
{
    // UpdateDevices trims Graph values, but compares stored Cosmos values without trimming.
    public static DeviceKey FromCosmos(Device device) => new(
        device.Make?.ToUpperInvariant(), device.Model?.ToUpperInvariant(), device.SerialNumber?.ToUpperInvariant());

    public static DeviceKey FromGraph(IntuneDevice device) => new(
        device.Manufacturer!.Trim().ToUpperInvariant(), device.Model!.Trim().ToUpperInvariant(),
        device.SerialNumber!.Trim().ToUpperInvariant());
}

public sealed record IntuneDevice(string? Manufacturer, string? Model, string? SerialNumber,
    DateTimeOffset? EnrolledDateTime, DateTimeOffset? LastSyncDateTime);

public interface IDeviceStore
{
    Task<IReadOnlyList<Device>> GetBatchAsync(MigrationOptions options, CancellationToken cancellationToken);
    Task<bool> PatchAsync(Device device, string migrationID, DateTime? enrollmentUtc);
}

public interface IIntuneReader
{
    Task<IReadOnlyDictionary<DeviceKey, IntuneDevice>> GetMatchesAsync(
        IReadOnlySet<DeviceKey> keys, CancellationToken cancellationToken);
}

public sealed class RunCounts
{
    public int Checked { get; set; }
    public int Matched { get; set; }
    public int Qualified { get; set; }
    public int Stale { get; set; }
    public int NotFound { get; set; }
    public int MissingDates { get; set; }
    public int FutureSync { get; set; }
    public int Updated { get; set; }
    public int Marked { get; set; }
    public int Conflicts { get; set; }
    public int Errors { get; set; }
}

public sealed class MigrationJob(IDeviceStore store, IIntuneReader intune, MigrationOptions options,
    ILogger<MigrationJob> logger, TimeProvider? timeProvider = null)
{
    public async Task<RunCounts> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset runUtc = (timeProvider ?? TimeProvider.System).GetUtcNow();
        DateTimeOffset cutoff = runUtc.AddDays(-options.MaxIntuneSyncAgeDays);
        var counts = new RunCounts();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["MigrationID"] = options.MigrationID,
            ["MigrationRunUTC"] = runUtc.ToString("O")
        });
        logger.LogInformation("Starting migration {MigrationID} at {RunUTC}, batch limit {BatchSize}, cutoff {CutoffUTC}.",
            options.MigrationID, runUtc, options.BatchSize, cutoff);
        try
        {
            var devices = await store.GetBatchAsync(options, cancellationToken);
            if (devices.Count == 0)
            {
                logger.LogInformation("No pending devices for migration {MigrationID}.", options.MigrationID);
                return counts;
            }

            var keys = devices.Select(DeviceKey.FromCosmos).ToHashSet();
            // Do not mark any devices until every Graph page has been read successfully.
            var matches = await intune.GetMatchesAsync(keys, cancellationToken);
            foreach (Device device in devices)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                counts.Checked++;
                DateTime? enrollmentUtc = null;
                if (!matches.TryGetValue(DeviceKey.FromCosmos(device), out var match))
                    counts.NotFound++;
                else
                {
                    counts.Matched++;
                    if (match.LastSyncDateTime < cutoff)
                        counts.Stale++;
                    else if (match.EnrolledDateTime == null || match.LastSyncDateTime == null)
                        counts.MissingDates++;
                    else if (match.LastSyncDateTime > runUtc)
                        counts.FutureSync++;
                    else
                    {
                        counts.Qualified++;
                        enrollmentUtc = match.EnrolledDateTime.Value.UtcDateTime;
                    }
                }

                try
                {
                    // Once an evaluation starts, complete its atomic patch even during graceful shutdown.
                    if (await store.PatchAsync(device, options.MigrationID, enrollmentUtc))
                    {
                        counts.Marked++;
                        if (enrollmentUtc.HasValue)
                            counts.Updated++;
                    }
                    else
                    {
                        counts.Conflicts++;
                        logger.LogWarning("Device {DeviceID} changed or was deleted before patching; re-query next run.", device.Id);
                    }
                }
                catch (Exception ex)
                {
                    counts.Errors++;
                    logger.LogError(ex, "Device {DeviceID} patch failed or its outcome is unknown; re-query eligibility next run.", device.Id);
                }
            }
            return counts;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Migration {MigrationID} cancelled before starting another device.", options.MigrationID);
            return counts;
        }
        catch (Exception ex)
        {
            counts.Errors++;
            logger.LogError(ex, "Migration {MigrationID} failed; pending devices remain eligible for retry.", options.MigrationID);
            throw;
        }
        finally
        {
            logger.LogInformation(
                "Migration {MigrationID} summary: checked={Checked}, matched={Matched}, qualified={Qualified}, stale={Stale}, " +
                "notFound={NotFound}, missingDates={MissingDates}, futureSync={FutureSync}, updated={Updated}, marked={Marked}, " +
                "conflicts={Conflicts}, errors={Errors}, cancelled={Cancelled}, durationSeconds={Duration}.",
                options.MigrationID, counts.Checked, counts.Matched, counts.Qualified, counts.Stale, counts.NotFound,
                counts.MissingDates, counts.FutureSync, counts.Updated, counts.Marked, counts.Conflicts, counts.Errors,
                cancellationToken.IsCancellationRequested, ((timeProvider ?? TimeProvider.System).GetUtcNow() - runUtc).TotalSeconds);
        }
    }
}

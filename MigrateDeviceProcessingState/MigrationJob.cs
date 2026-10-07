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
    Task<IntuneDevice?> GetMatchAsync(Device device, CancellationToken cancellationToken);
}

public sealed class RunCounts
{
    public int Checked { get; set; }
    public int Matched { get; set; }
    public int Qualified { get; set; }
    public int Stale { get; set; }
    public int NotFound { get; set; }
    public int MissingDates { get; set; }
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
        logger.LogInformation(
            "Starting migration {MigrationID} at {RunUTC}, batch limit {BatchSize}, maximum Intune sync age {MaxIntuneSyncAgeDays} days, cutoff {CutoffUTC}.",
            options.MigrationID, runUtc, options.BatchSize, options.MaxIntuneSyncAgeDays, cutoff);
        try
        {
            var devices = await store.GetBatchAsync(options, cancellationToken);
            if (devices.Count == 0)
            {
                logger.LogInformation("No pending devices for migration {MigrationID}.", options.MigrationID);
                return counts;
            }

            logger.LogInformation("Loaded {DeviceCount} pending devices, ordered oldest ModifiedUTC first.",
                devices.Count);
            foreach (Device device in devices)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                counts.Checked++;
                try
                {
                    // Finish the current device's lookup and patch before honoring graceful shutdown.
                    // A failed lookup throws before any marker is written, leaving this device retryable.
                    var match = await intune.GetMatchAsync(device, CancellationToken.None);
                    DateTime? enrollmentUtc = null;
                    // A missing Intune match or a nonqualifying match still completes this migration's
                    // evaluation; only a failed/conflicted Cosmos patch leaves the marker unset.
                    if (match == null)
                    {
                        counts.NotFound++;
                        logger.LogDebug("Device {DeviceID} was not found in Intune; marking evaluated for migration without changing processing fields.",
                            device.Id);
                    }
                    else if (match.EnrolledDateTime == null || match.LastSyncDateTime == null)
                    {
                        counts.Matched++;
                        counts.MissingDates++;
                        logger.LogDebug(
                            "Device {DeviceID} matched Intune but is not qualified because enrolledDateTime or lastSyncDateTime is missing.",
                            device.Id);
                    }
                    else if (match.LastSyncDateTime < cutoff)
                    {
                        counts.Matched++;
                        counts.Stale++;
                        logger.LogDebug(
                            "Device {DeviceID} matched Intune but is stale: lastSyncDateTime={LastSyncDateTime}, cutoff={CutoffUTC}.",
                            device.Id, match.LastSyncDateTime, cutoff);
                    }
                    else
                    {
                        // A timestamp later than run start is still within the configured maximum age.
                        // Future-dated Intune values can result from clock skew and qualify on recency.
                        counts.Matched++;
                        counts.Qualified++;
                        enrollmentUtc = match.EnrolledDateTime.Value.UtcDateTime;
                        logger.LogInformation(
                            "Device {DeviceID} qualified for Processed state using Intune enrollment time {EnrolledUTC}.",
                            device.Id, enrollmentUtc);
                    }

                    if (await store.PatchAsync(device, options.MigrationID, enrollmentUtc))
                    {
                        counts.Marked++;
                        if (enrollmentUtc.HasValue)
                        {
                            counts.Updated++;
                            logger.LogInformation(
                                "Device {DeviceID} processing fields updated and marked with MigrationID {MigrationID}.",
                                device.Id, options.MigrationID);
                        }
                        else
                        {
                            logger.LogDebug("Device {DeviceID} evaluated without processing-field changes and marked with MigrationID {MigrationID}.",
                                device.Id, options.MigrationID);
                        }
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
                    logger.LogError(ex, "Device {DeviceID} lookup or patch failed, or patch outcome is unknown; re-query eligibility next run.", device.Id);
                }
            }
            return counts;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Migration {MigrationID} cancelled before starting another device.", options.MigrationID);
            return counts;
        }
        catch (OperationCanceledException ex)
        {
            counts.Errors++;
            logger.LogError(ex, "Migration {MigrationID} was cancelled by an unexpected operation; pending devices remain eligible for retry.",
                options.MigrationID);
            throw;
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
                "notFound={NotFound}, missingDates={MissingDates}, updated={Updated}, marked={Marked}, " +
                "conflicts={Conflicts}, errors={Errors}, cancelled={Cancelled}, durationSeconds={Duration}.",
                options.MigrationID, counts.Checked, counts.Matched, counts.Qualified, counts.Stale, counts.NotFound,
                counts.MissingDates, counts.Updated, counts.Marked, counts.Conflicts, counts.Errors,
                cancellationToken.IsCancellationRequested, ((timeProvider ?? TimeProvider.System).GetUtcNow() - runUtc).TotalSeconds);
        }
    }
}

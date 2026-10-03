namespace MigrateDeviceProcessingState;

public sealed record MigrationOptions(string MigrationID, int BatchSize, int MaxIntuneSyncAgeDays, int GraphMaxRetries)
{
    public static MigrationOptions FromEnvironment() => new(
        Required("MigrationID"),
        PositiveInteger("ProcessingMigrationBatchSize", 1000),
        PositiveInteger("ProcessingMigration_ProcessedIfSeenDays", 180),
        PositiveInteger("ProcessingMigrationGraphMaxRetries", 8));

    public static string Required(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? throw new InvalidOperationException($"{name} must be configured.")
            : Environment.GetEnvironmentVariable(name)!;

    private static int PositiveInteger(string name, int defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (value == null)
            return defaultValue;
        if (!int.TryParse(value, out int result) || result <= 0)
            throw new InvalidOperationException($"{name} must be a positive integer.");
        return result;
    }
}

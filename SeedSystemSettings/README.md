# SeedSystemSettings WebJob

Creates the initial `SystemSettings` document in the existing Cosmos DB container.
If any document already exists in the `SystemSettings` partition, the job exits
successfully without modifying it. The job uses a create-only write with the fixed
ID `system-settings`; a conflict from a concurrent seed run is also safely skipped.
Other failures are logged and return a nonzero exit code.

## Initial settings

| Setting | Source | Default |
|---------|--------|---------|
| `MaxCorpIdsAllowed` | `MAX_CORPIDS_ALLOWED` | `10000` |
| `CorpIdWarningThresholdPercent` | `CORPID_WARNING_THRESHOLD_PERCENT` | `90` |
| `ExpireProcessedAfterDays` | Fixed initial default | `180` |
| `ExpireUnprocessedAfterDays` | Fixed initial default | `180` |

Missing or invalid environment values use the defaults with a warning.
The maximum must be a positive integer. The warning percentage is clamped to
1-100, matching CorporateIdentifierSync. Creation and modification timestamps
are initialized in UTC.

## Running the job

1. Download the `seed-system-settings-webjob` artifact from the build workflow,
   or publish `SeedSystemSettings\SeedSystemSettings.csproj` and zip its output.
2. Use the same `COSMOS_CONNECTION_STRING` or `COSMOS_ENDPOINT` configuration as
   the existing deployment. A connection string takes precedence; otherwise the
   job uses managed identity, which must have Cosmos DB data read/create access.
   `COSMOS_DATABASE_NAME` defaults to `DelegationStationData` and
   `COSMOS_CONTAINER_NAME` defaults to `DeviceData`. The database and container
   must already exist.
3. Ensure `MAX_CORPIDS_ALLOWED` and `CORPID_WARNING_THRESHOLD_PERCENT` on the
   WebJob host match the values used by the existing Function App. WebJobs inherit
   their host App Service's environment, not a separate Function App's settings.
4. In App Service, add a **Triggered**, **Manual** WebJob named
   `SeedSystemSettings`, upload the zip, and run it once.
5. Check the logs for creation or an existing-settings skip, then remove the
   WebJob if it is no longer needed.

This job only initializes the settings document; it does not change how existing
applications read their environment variables or implement device expiration.

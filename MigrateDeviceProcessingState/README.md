# MigrateDeviceProcessingState WebJob

## Purpose and behavior

Run this **before** deploying the webapp or functions. Its package includes the updated shared
Device model and all .NET dependencies; it does not require any application deployment first.
Only this WebJob is deployed at this stage.

Each scheduled invocation queries up to `ProcessingMigrationBatchSize` Device documents in
**oldest ModifiedUTC first** order. It excludes documents marked with the configured `MigrationID`
and documents where all three processing fields are already non-null. Missing and null fields
are both eligible; partially populated devices are eligible.

The job scans all Intune `managedDevices` pages with
`$select=manufacturer,model,serialNumber,enrolledDateTime,lastSyncDateTime`. It retains only
hardware keys for the current Cosmos batch, limiting memory usage to that batch. There are
no per-device Graph calls. Every nonempty batch scans Graph again; select the schedule and
batch size with tenant size, Graph throttling and Cosmos RU capacity in mind.

Matching follows UpdateDevices: case-insensitive make/manufacturer, model and serial number,
with Graph values trimmed and stored Cosmos values not trimmed. Missing hardware fields
cannot match. For duplicate Intune records, the record with the most recent lastSyncDateTime
wins; a dated record wins over a null date. Equal sync dates retain the first returned record.

A record qualifies when it has enrolledDateTime and lastSyncDateTime, and its sync is between
the run's UTC start minus `ProcessingMigration_ProcessedIfSeenDays` and the run's UTC start, inclusive.
Enrollment itself need not be recent. Future sync dates do not qualify.

Qualifying documents receive one atomic `PatchItemAsync` containing:

| Field | Value |
|-------|-------|
| `SuccessfullyProcessedUTC` | Intune enrolledDateTime converted to UTC |
| `LastProcessingAttemptUTC` | Same UTC enrollment time |
| `ProcessingStatus` | `Processed` (the existing enum's numeric value, 1) |
| `MigrationID` | Configured migration event ID |

Unmatched, stale or otherwise nonqualifying documents receive **only MigrationID**. Their
processing fields remain unchanged, including any partial values. No other fields, including
ModifiedUTC and LastSeenEnrollmentUTC, are written. The job never replaces Device documents.

## Configuration

Application settings are inherited from the hosting App Service. Newly added settings must
also be applied to that host, without replacing the deployed webapp.

| Setting | Required/default | Purpose |
|---------|------------------|---------|
| `MigrationID` | Required, nonblank | Stable event identifier, e.g. `device-processing-migration-2026-10`. Keep unchanged when resuming. |
| `ProcessingMigrationBatchSize` | 1000 | Maximum devices checked per invocation; positive integer. |
| `ProcessingMigration_ProcessedIfSeenDays` | 180 | Maximum age in days of Intune lastSyncDateTime for marking a device Processed, relative to the run's UTC start. All Intune devices are read regardless of this setting. |
| `ProcessingMigrationGraphMaxRetries` | 8 | Positive retry count per Graph page for 429/503. Honors Retry-After seconds or HTTP date, otherwise exponential backoff. Exhaustion fails the run. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Required unless alias below is set | Use the **same connection string as the webapp's Application Insights resource**. |
| `APPINSIGHTS_CONNECTION_STRING` | Fallback alias | Used only when the standard connection-string setting is absent/blank. |
| `COSMOS_ENDPOINT` | Required unless connection string is set | Cosmos endpoint, authenticated using the host's system-assigned managed identity. |
| `COSMOS_CONNECTION_STRING` | Optional | Takes precedence over COSMOS_ENDPOINT for this WebJob. |
| `COSMOS_DATABASE_NAME` | `DelegationStationData` | Existing database. |
| `COSMOS_CONTAINER_NAME` | `DeviceData` | Existing container; partition key must be `/PartitionKey`. |
| `AzureEnvironment` | `AzurePublicCloud` | `AzurePublicCloud`, `AzureUSGovernment` or `AzureUSDoD`. |
| `GraphEndpoint` | Based on cloud | Public: `https://graph.microsoft.com/`; Government: `https://graph.microsoft.us/`; DoD: `https://dod-graph.microsoft.us/`. |

The host's **system-assigned managed identity** needs admin-consented Graph application permission
`DeviceManagementManagedDevices.Read.All`. The webapp's existing permissions alone may not include
this. If using managed identity for Cosmos, grant query/read and patch permissions (for example
Cosmos DB Built-in Data Contributor) on the existing container. No Graph write permission is needed.

The timer is an App Service **scheduled triggered WebJob**, not an Azure Function timer.
`settings.job` ships with NCRONTAB schedule `0 */10 * * * *` (every ten minutes), singleton
configuration and a 300-second shutdown grace period. Change its `schedule` before upload or
in the WebJob's settings.job to configure the cadence; App Service does not substitute an
environment variable into this file. Use UTC scheduling and enable **Always On** on a supported
App Service plan so it continues unattended.

## Pre-deployment decision and deployment

**Confirm with the deployment owner that ConfirmSync can be disabled for the migration period.**
This approval is still outstanding. Its old full-document saves can strip the new processing
fields and marker. If it cannot be disabled, agree on an alternate solution, potentially a
patch-only ConfirmSync hotfix, **before** starting the migration; that will delay delivery.
Do not treat this note as authorization to disable it.

Tag sync enablement changes are expected to remain on hold until the update, limiting
ReconcileSyncState overlap. Oldest-first processing reduces AddNewDevices overlap with newer
devices but does not eliminate it.

1. Resolve the ConfirmSync decision. Apply the approved temporary disablement if acceptable.
2. Configure the settings and permissions above on the existing WebJob host.
3. Download **migrate-device-processing-state-webjob** from the build artifacts. Upload only this
   artifact as a **Triggered / Scheduled** WebJob named `MigrateDeviceProcessingState` on the
   existing Windows App Service. Do not deploy the main application artifacts yet.
4. Confirm `run.cmd`, settings.job and the published DLLs are at the WebJob package root.
   The package requires the .NET 10 runtime; it is framework-dependent. For manual publishing:
   `dotnet publish .\MigrateDeviceProcessingState\MigrateDeviceProcessingState.csproj -c Release -o .\MigrateDeviceProcessingState\published`.
5. Run a small initial batch, inspect the Cosmos fields, and verify logs in the webapp's
   Application Insights resource using the query below. Then allow the schedule to continue.
6. A log saying `No pending devices` indicates completion at that moment. Review error/conflict
   counts and any documents modified by old applications. Stop/remove the scheduled job after
   completion, deploy the updated applications, then restore ConfirmSync if it was disabled.

Run just **one named job** against a migration event, not simultaneous manual instances or
additional job copies. Atomic patches guard the queried ETag using a Cosmos filter predicate
(PatchItemAsync does not support IfMatchEtag). If another writer changes or deletes a document,
the patch is skipped and logged; a changed document is re-queried on the next invocation if
still eligible. An unsuccessful atomic patch leaves the marker unchanged. A network error can
leave the outcome unknown; the next eligibility query distinguishes a committed patch from
pending work. Cosmos SDK handles transient throttling; exhausted retries are logged.

## Resuming, limitations and shutdown

There is no saved offset: each invocation queries devices still eligible for the current
MigrationID. Stop/restart with the **same ID** to resume. Changing the ID allows reevaluation
of non-fully-populated documents from earlier events. This is a single reusable marker, not a
migration-history ledger; concurrent migrations using different IDs are not supported.

Successful evaluation, including "not found", is recorded once per event **as long as the
marker survives**. Old applications can remove new fields when replacing a document, causing
reevaluation. Device deletion is acceptable and new additions normally create distinct IDs.
After deployment, UpdateDevices/StragglerHandler can legitimately supersede processing values
when processing an enrollment; they do not patch MigrationID. This migration does not address
later preservation behavior.

Graph must finish all pages before any Device is marked, so partial retrieval never incorrectly
classifies missing devices. Errors return a nonzero exit code and log a summary.

Ctrl+C, SIGTERM on non-Windows hosts, and App Service's `WEBJOBS_SHUTDOWN_FILE` request graceful
shutdown. No new device starts after cancellation is observed. An already-started device patch
finishes without the cancellation token, then the job logs a summary and flushes telemetry.
Cancellation during Graph retrieval or the Cosmos query cancels that retrieval without marking
devices. A hard process kill or exhausted App Service shutdown grace period cannot guarantee
completion/log delivery; an atomic patch is either committed with its marker or remains retryable.

## Application Insights

The WebJob explicitly registers the Application Insights ILogger provider; it does not rely on
webapp instrumentation automatically capturing a separate process. Information, warning and error
logs are sent to the configured resource and also to the WebJob console. There is no job-configured
sampling. A missing connection string fails startup **before any Cosmos changes**.

Telemetry is tagged with `cloud_RoleName = MigrateDeviceProcessingState`. Migration run logs include
MigrationID and run UTC properties. Each run logs checked, matched, qualified, stale, not-found,
missing-date, future-sync, updated, marked, conflict and error counts, cancellation status and duration.
Telemetry is explicitly flushed before exit; ingestion/network failures or resource-side sampling
can still affect delivery, so verify on the deployed host.

In the webapp's Application Insights **Logs** view:

```kusto
traces
| where timestamp > ago(24h)
| where cloud_RoleName == "MigrateDeviceProcessingState"
| where message contains "summary" or message contains "No pending devices"
| project timestamp, message, customDimensions
| order by timestamp desc
```

For failures inspect `exceptions` with the same role filter, as well as warning/error traces and
WebJob console output. If using workspace table names, use `AppTraces` / `AppExceptions` and
`AppRoleName` instead. Confirm the first run summary appears in **the webapp's resource** before
leaving the migration unattended.

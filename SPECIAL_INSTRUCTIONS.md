# Special Instructions

These steps upgrade an existing environment to support tracking device processing state.

## Pre-deployment

### Configure the WebJob host

On the existing webapp's App Service, configure:

| Setting | Required/default | Purpose |
|---------|------------------|---------|
| `MigrationID` | Required | Stable migration event ID. |
| `ProcessingMigrationBatchSize` | `1000` | Maximum devices evaluated per invocation. |
| `ProcessingMigration_ProcessedIfSeenDays` | `180` | Maximum age in days of Intune last sync for marking a device Processed. |
| `ProcessingMigrationGraphMaxRetries` | `8` | Graph retries per page for throttling or service unavailability. |

Verify the following settings are already present:

| Setting | Required/default | Purpose |
|---------|------------------|---------|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` or `APPINSIGHTS_CONNECTION_STRING` | One required | Webapp's Application Insights resource. |
| `COSMOS_ENDPOINT` or `COSMOS_CONNECTION_STRING` | One required | Existing Cosmos account. |
| `COSMOS_DATABASE_NAME` | `DelegationStationData` | Existing database. |
| `COSMOS_CONTAINER_NAME` | `DeviceData` | Existing device container. |
| `GraphEndpoint` | Same value as webapp | Tenant's Microsoft Graph endpoint. |
| `AzureEnvironment` | Same value as webapp | Azure cloud environment. |

See [`MigrateDeviceProcessingState/README.md`](MigrateDeviceProcessingState/README.md)
for the complete configuration and permission requirements.

### Verify Graph permission

Confirm that the managed identity or app registration already has the Microsoft Graph
application permission `DeviceManagementManagedDevices.Read.All`, with admin consent.

### Disable ConfirmSync

- In Azure Portal, open the **CorporateIdentifierSync** function app.
- Go to **Functions** and select **ConfirmSync**.
- Click **Disable**.
- Verify ConfirmSync is disabled and any in-progress execution has finished before
  uploading the migration WebJob.

### Upload only the migration WebJob

1. Download the **migrate-device-processing-state-webjob** artifact from the build.
2. Review `settings.job`: the default timer runs every ten minutes. Adjust its schedule
   if needed, and enable App Service **Always On** for unattended execution.
3. In Azure Portal, open the existing webapp's **WebJobs → Add**. Upload the artifact ZIP
   as a **Triggered / Scheduled** WebJob named **MigrateDeviceProcessingState**.

### Verify an initial batch and allow scheduled processing

Start with a small batch and verify that qualifying devices receive:

- `SuccessfullyProcessedUTC` and `LastProcessingAttemptUTC` set to Intune enrolledDateTime in UTC.
- `ProcessingStatus` set to `Processed`.
- `MigrationID` set to the configured event ID.

Nonqualifying devices receive only the migration marker; their processing values remain
unchanged. Devices are skipped only when their `MigrationID` already matches the configured
event ID; existing processing-field values do not exclude a device from evaluation.

Confirm the run summary appears in **the webapp's Application Insights resource** under
`cloud_RoleName = MigrateDeviceProcessingState` before leaving the job unattended. The
WebJob README includes a Logs query. Review checked, matched, qualified, stale, not-found,
updated, conflict and error counts.

Use the small run to estimate batch timing:

1. Divide the summary's `durationSeconds` by `checked` to estimate seconds per device
   evaluated. Use `checked`, not `updated`, because nonqualifying devices also take time.
2. Multiply that estimate by the proposed batch size to estimate the batch duration.
   For example, 120 seconds for 100 checked devices is roughly 1.2 seconds per device;
   a batch of 500 would take roughly 600 seconds (10 minutes).
3. Update `ProcessingMigrationBatchSize` in the webapp's App Service application settings
   and adjust the trigger schedule in `settings.job`. Allow at least 50% extra time above
   the estimated batch duration before the next trigger (15 minutes in the example).
4. Observe a run at the new batch size and adjust the batch size or trigger interval
   again if needed before leaving the job unattended.

This is a rough estimate: individual Intune lookup latency, extra pages of matching records,
and Graph/Cosmos throttling can extend a run. Verify the make/model/serial filter returns expected
matches in the initial run. Choose an interval comfortably longer than
observed run times rather than relying on the estimate alone.

### Complete the migration

1. Confirm the job reports `No pending devices`; investigate errors or repeated conflicts.
   This indicates completion at that moment, not protection against subsequent old application saves.

## Deployment

Stop/remove the scheduled migration WebJob before deploying application code.

Deploy the updated applications:

1. Webapp (`DelegationStation`).
2. UpdateDevices function app.
3. CorporateIdentifierSync function app.

## Post-deployment

**TBD:** Finalize post-deployment steps.

1. Set up the maintenance banner: use App Service **Advanced Tools (Kudu)** to create
   `maintenance-banner.txt` in the deployed webapp's `wwwroot` directory (the web root
   containing static assets). Add the banner message and verify it appears in the webapp.
   **TBD:** Finalize the maintenance banner text.
2. Restore ConfirmSync if it was temporarily disabled, and verify normal operation.

## Rollback

1. Redeploy the previous version of the code.
2. **TBD:** Define how to migrate devices that moved to new states back to a previously
   valid state before rollback, likely using a WebJob. If no devices are in the new states,
   no rollback WebJob is needed. It is safe to leave the processing fields and `MigrationID`
   on the device documents.

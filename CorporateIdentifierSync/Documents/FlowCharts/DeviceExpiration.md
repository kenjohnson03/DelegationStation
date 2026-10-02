```mermaid

flowchart TD
    A["Timer Trigger Fires"] --> L{"Acquire singleton<br/>blob lease?"}
    L -- "No" --> END
    L -- "Yes" --> S["X = ProcessedDevicesExpiredAfterDays<br/>Y = UnprocessedDevicesExpiredAfterDays<br/>(temporarily static values of 180)<br/>B = ExpireDevicesBatchSize (default 1000)<br/>R = MAX_EXPIRATION_RETRIES (default 10)"]
    S --> DR["Get up to 20% of B retry devices where<br/>Status == Synced AND (<br/>(ProcessingStatus == Processed AND SuccessfullyProcessedUTC < UtcNow - X days)<br/>OR (ProcessingStatus != Processed AND ModifiedUTC < UtcNow - Y days))<br/>AND ExpirationFailureCount > 0<br/>ORDER BY ModifiedUTC"]
    DR --> D["Get up to (B - retry count) new devices<br/>(same criteria, ExpirationFailureCount zero or missing)<br/>ORDER BY ModifiedUTC"]
    D --> LOOP

    subgraph LOOP ["For Each Device"]
        direction TB

        MK["MarkedForExpirationUTC ??= UtcNow<br/>(preserved on retry)"] --> H{"Does device have CorporateIdentityID?"}

        H -- "No" --> N["corpIDRemoved = true"]
        H -- "Yes" --> I["Delete CorpID"]
        I --> J{"Delete<br/>succeeded?"}
        J -- "Yes" --> K["corpIDsDeletedCount++<br/>corpIDRemoved = true"]
        J -- "No (Not Found)" --> M["corpIDRemoved = true"]
        J -- "No (Error)" --> MM["corpIDRemoved = false"]

        K --> O{"corpIDRemoved?"}
        M --> O
        MM --> O
        N --> O

        O -- "Yes" --> P["ExpiredUTC = UtcNow<br/>Status = Expired<br/>ExpiredReason = Processed: 'Device was expired since it was<br/>processed over X days ago.'<br/>Unprocessed: 'Device was expired since it was not<br/>processed within Y days of being added.'<br/>Clear CorporateIdentityID / CorporateIdentity<br/>Update device"]
        O -- "No" --> FC["ExpirationFailureCount++"]
        FC --> FR{"ExpirationFailureCount > R?"}
        FR -- "No" --> Q["Leave device Synced<br/>Update device<br/>Will retry on next run."]
        FR -- "Yes" --> QF["Status = ExpirationFailed<br/>Update device<br/>(CorpID requires manual cleanup)"]

        P --> Z["End of For Loop"]
        Q --> Z
        QF --> Z
    end

    Z --> AA{"corpIDsDeletedCount > 0?"}
    AA -- "No" --> END
    AA -- "Yes" --> AC["ReleaseCorpIDs(corpIDsDeletedCount)"]
    AC --> END

```

Expiration is calculated at run time from the current settings, so changing either setting applies to all existing devices.
`MarkedForExpirationUTC` records when expiration processing started for a device and is persisted with the outcome update.

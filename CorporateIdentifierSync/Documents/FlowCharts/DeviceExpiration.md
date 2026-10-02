```mermaid

flowchart TD
    A["Timer Trigger Fires"] --> L{"Acquire singleton<br/>blob lease?"}
    L -- "No" --> END
    L -- "Yes" --> S["X = ProcessedDevicesExpiredAfterDays<br/>Y = UnprocessedDevicesExpiredAfterDays<br/>(temporarily static values of 180)<br/>B = ExpireDevicesBatchSize (default 1000)<br/>R = MAX_EXPIRATION_RETRIES (default 10)"]
    S --> DR["Get up to 20% of B retry devices where<br/>Status == Synced AND (<br/>(ProcessingStatus == Processed AND SuccessfullyProcessedUTC < UtcNow - X days)<br/>OR (ProcessingStatus != Processed AND ModifiedUTC < UtcNow - Y days<br/>AND (LastSeenEnrollmentUTC missing OR < UtcNow - Y days)))<br/>AND ExpirationFailureCount > 0<br/>ORDER BY ModifiedUTC"]
    DR --> D["Get up to (B - retry count) new devices<br/>(same criteria, ExpirationFailureCount zero or missing)<br/>ORDER BY ModifiedUTC"]
    D --> LOOP

    subgraph LOOP ["For Each Device"]
        direction TB

        RR["Re-read device from DB"] --> RE{"Read OK and still eligible?<br/>(Synced and same criteria as query)"}
        RE -- "Read failed" --> Z
        RE -- "No (deleted, status changed,<br/>re-enrolled, or recently processed)" --> SK["Skip; leave device as is"]
        RE -- "Yes" --> MK["MarkedForExpirationUTC ??= UtcNow<br/>(preserved on retry)"]
        MK --> H{"Does device have CorporateIdentityID?"}

        H -- "No" --> N["corpIDRemoved = true"]
        H -- "Yes" --> I["Delete CorpID"]
        I --> J{"Delete<br/>succeeded?"}
        J -- "Yes" --> K["deletedThisRun = true<br/>corpIDRemoved = true"]
        J -- "No (Not Found)" --> M["corpIDRemoved = true"]
        J -- "No (Error)" --> MM["corpIDRemoved = false"]

        K --> O{"corpIDRemoved?"}
        M --> O
        MM --> O
        N --> O

        O -- "Yes" --> P["ExpiredUTC = UtcNow<br/>Status = Expired<br/>ExpiredReason = Processed: 'Device was expired since it was<br/>processed over X days ago.'<br/>Unprocessed: 'Device was expired since it was not<br/>processed within Y days of being added.'<br/>Clear CorporateIdentityID / CorporateIdentity<br/>Update device (ETag)"]
        P --> PU{"Update result?"}
        PU -- "Success or NotFound" --> REL["If deletedThisRun:<br/>corpIDsDeletedCount++"]
        PU -- "Other error" --> NOREL["Do not release<br/>Will retry on next run"]
        PU -- "412 PreconditionFailed" --> RR2["Re-read device"]
        RR2 --> RS{"Fresh device?"}
        RS -- "Read failed" --> NOREL
        RS -- "Deleted or not Synced" --> REL
        RS -- "Synced, not eligible<br/>(re-enrolled / processed)" --> KEEP["Leave Synced, do not release<br/>If deletedThisRun: re-add CorpID and save<br/>(re-add fails: ConfirmSync re-adds;<br/>save fails: delete re-added CorpID)"]
        RS -- "Synced, still eligible" --> RP["Apply Expired fields to fresh copy<br/>Update device"]
        RP -- "Success" --> REL
        RP -- "Error" --> NOREL

        O -- "No" --> FC["ExpirationFailureCount++"]
        FC --> FR{"ExpirationFailureCount > R?"}
        FR -- "No" --> Q["Leave device Synced<br/>Update device<br/>Will retry on next run."]
        FR -- "Yes" --> QF["Status = ExpirationFailed<br/>Update device<br/>(CorpID requires manual cleanup)"]

        SK --> Z["End of For Loop"]
        REL --> Z
        NOREL --> Z
        KEEP --> Z
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
Each device is re-read right before its CorpID is deleted so that a device UpdateDevices/StragglerHandler just recorded as enrolled or processed stays Synced. CorpID slots are released only when the outcome is persisted (over-counting is the safe failure).

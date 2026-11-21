IF COL_LENGTH('dbo.CalendarSyncState', 'PendingBackfillFrom') IS NOT NULL
BEGIN
    DECLARE @dfName sysname;
    SELECT @dfName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.CalendarSyncState')
      AND c.name = N'PendingBackfillFrom';

    IF @dfName IS NOT NULL
    BEGIN
        EXEC(N'ALTER TABLE dbo.CalendarSyncState DROP CONSTRAINT ' + QUOTENAME(@dfName) + N';');
    END

    ALTER TABLE dbo.CalendarSyncState DROP COLUMN PendingBackfillFrom;
END

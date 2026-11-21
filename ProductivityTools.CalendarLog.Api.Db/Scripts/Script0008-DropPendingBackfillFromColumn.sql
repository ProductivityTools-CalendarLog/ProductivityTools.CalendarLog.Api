IF COL_LENGTH('dbo.CalendarSyncState', 'PendingBackfillFrom') IS NOT NULL
BEGIN
    DECLARE @dfName sysname;
    DECLARE @dropConstraintSql NVARCHAR(512);

    SELECT @dfName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.CalendarSyncState')
      AND c.name = N'PendingBackfillFrom';

    IF @dfName IS NOT NULL
    BEGIN
        SET @dropConstraintSql = N'ALTER TABLE dbo.CalendarSyncState DROP CONSTRAINT ' + QUOTENAME(@dfName) + N';';
        EXEC sp_executesql @dropConstraintSql;
    END

    ALTER TABLE dbo.CalendarSyncState DROP COLUMN PendingBackfillFrom;
END

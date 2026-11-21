IF COL_LENGTH('dbo.CalendarSyncState', 'Type') IS NULL
BEGIN
    ALTER TABLE dbo.CalendarSyncState
    ADD [Type] NVARCHAR(32) NOT NULL
        CONSTRAINT DF_CalendarSyncState_Type DEFAULT N'DataLog',
        CONSTRAINT CK_CalendarSyncState_Type CHECK ([Type] IN (N'DataPoint', N'DataLog'));

    EXEC(N'
        UPDATE dbo.CalendarSyncState
        SET [Type] = N''DataPoint'', UpdatedAtUtc = SYSUTCDATETIME()
        WHERE CalendarName LIKE N''%DataPoint%'';
    ');
END

CREATE TABLE dbo.CalendarCategoryRule (
    CalendarCategoryRuleId INT IDENTITY(1,1) PRIMARY KEY,
    Account NVARCHAR(255) NOT NULL,
    CalendarId NVARCHAR(255) NOT NULL CONSTRAINT DF_CalendarCategoryRule_CalendarId DEFAULT '*',
    Priority INT NOT NULL CONSTRAINT DF_CalendarCategoryRule_Priority DEFAULT 100,
    MatchField NVARCHAR(50) NOT NULL,
    MatchValue NVARCHAR(500) NOT NULL,
    Category NVARCHAR(255) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_CalendarCategoryRule_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarCategoryRule_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarCategoryRule_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_CalendarCategoryRule_Account_Priority
    ON dbo.CalendarCategoryRule (Account, Priority, CalendarCategoryRuleId);

CREATE TABLE dbo.CalendarLog (
    CalendarLogId BIGINT IDENTITY(1,1) PRIMARY KEY,
    RawCalendarEventId BIGINT NULL,
    Source NVARCHAR(32) NOT NULL CONSTRAINT DF_CalendarLog_Source DEFAULT 'RAW_SYNC',
    Account NVARCHAR(255) NOT NULL,
    CalendarId NVARCHAR(255) NULL,
    CalendarName NVARCHAR(255) NOT NULL,
    StartTime DATETIMEOFFSET NOT NULL,
    EndTime DATETIMEOFFSET NOT NULL,
    [Day] DATE NOT NULL,
    WeekNumber INT NOT NULL,
    [Month] NVARCHAR(7) NOT NULL,
    DurationHours DECIMAL(10, 4) NOT NULL,
    Title NVARCHAR(1000) NULL,
    MyStatus NVARCHAR(50) NULL,
    EventType NVARCHAR(50) NULL,
    Color NVARCHAR(50) NULL,
    Category NVARCHAR(255) NULL,
    [Value] NVARCHAR(500) NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarLog_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarLog_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CalendarLog_RawCalendarEvent
        FOREIGN KEY (RawCalendarEventId) REFERENCES dbo.RawCalendarEvent(RawCalendarEventId)
        ON DELETE CASCADE
);

CREATE UNIQUE INDEX UQ_CalendarLog_RawCalendarEventId
    ON dbo.CalendarLog (RawCalendarEventId)
    WHERE RawCalendarEventId IS NOT NULL;

CREATE INDEX IX_CalendarLog_Account_Day
    ON dbo.CalendarLog (Account, [Day]);

CREATE INDEX IX_CalendarLog_Account_WeekNumber
    ON dbo.CalendarLog (Account, WeekNumber);

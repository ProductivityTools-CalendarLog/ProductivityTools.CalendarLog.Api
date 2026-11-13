CREATE TABLE dbo.RawCalendarEvent (
    RawCalendarEventId BIGINT IDENTITY(1,1) PRIMARY KEY,
    Account NVARCHAR(255) NOT NULL,
    CalendarId NVARCHAR(255) NOT NULL,
    CalendarName NVARCHAR(255) NOT NULL,
    ExternalEventId NVARCHAR(512) NOT NULL,
    ICalUid NVARCHAR(512) NULL,
    StartTime DATETIMEOFFSET NOT NULL,
    EndTime DATETIMEOFFSET NOT NULL,
    IsAllDay BIT NOT NULL CONSTRAINT DF_RawCalendarEvent_IsAllDay DEFAULT 0,
    IsRecurring BIT NOT NULL CONSTRAINT DF_RawCalendarEvent_IsRecurring DEFAULT 0,
    Title NVARCHAR(1000) NULL,
    Description NVARCHAR(MAX) NULL,
    Location NVARCHAR(1000) NULL,
    MyStatus NVARCHAR(50) NULL,
    EventStatus NVARCHAR(50) NULL,
    EventType NVARCHAR(50) NULL,
    Color NVARCHAR(50) NULL,
    IsOwnedByMe BIT NULL,
    GuestCount INT NOT NULL CONSTRAINT DF_RawCalendarEvent_GuestCount DEFAULT 0,
    GuestsJson NVARCHAR(MAX) NULL,
    EventCreatedAt DATETIMEOFFSET NULL,
    EventUpdatedAt DATETIMEOFFSET NULL,
    RawJson NVARCHAR(MAX) NULL,
    IsDeleted BIT NOT NULL CONSTRAINT DF_RawCalendarEvent_IsDeleted DEFAULT 0,
    ProcessingStatus NVARCHAR(32) NOT NULL CONSTRAINT DF_RawCalendarEvent_ProcessingStatus DEFAULT 'NEW',
    ErrorMessage NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_RawCalendarEvent_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_RawCalendarEvent_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    ProcessedAtUtc DATETIME2 NULL,
    CONSTRAINT UQ_RawCalendarEvent_Account_Calendar_ExternalEvent
        UNIQUE (Account, CalendarId, ExternalEventId)
);

CREATE INDEX IX_RawCalendarEvent_Account_Calendar_StartTime
    ON dbo.RawCalendarEvent (Account, CalendarId, StartTime);

CREATE INDEX IX_RawCalendarEvent_ProcessingStatus
    ON dbo.RawCalendarEvent (ProcessingStatus);

CREATE TABLE dbo.CalendarSyncState (
    CalendarSyncStateId INT IDENTITY(1,1) PRIMARY KEY,
    Account NVARCHAR(255) NOT NULL,
    CalendarId NVARCHAR(255) NOT NULL,
    CalendarName NVARCHAR(255) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_CalendarSyncState_IsActive DEFAULT 1,
    DefaultLookbackDays INT NOT NULL CONSTRAINT DF_CalendarSyncState_DefaultLookbackDays DEFAULT 14,
    LastSyncedDate DATE NULL,
    LastSyncedAtUtc DATETIME2 NULL,
    PendingBackfillFrom DATE NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarSyncState_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarSyncState_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CalendarSyncState_Account_Calendar
        UNIQUE (Account, CalendarId)
);

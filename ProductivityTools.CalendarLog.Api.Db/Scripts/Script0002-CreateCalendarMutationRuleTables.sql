CREATE TABLE dbo.CalendarConvertRule (
    CalendarConvertRuleId INT IDENTITY(1,1) PRIMARY KEY,
    Account NVARCHAR(255) NOT NULL,
    KeyPrefix NVARCHAR(100) NOT NULL,
    TargetCalendarId NVARCHAR(255) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_CalendarConvertRule_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarConvertRule_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarConvertRule_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CalendarConvertRule_Account_KeyPrefix
        UNIQUE (Account, KeyPrefix)
);

CREATE TABLE dbo.CalendarTitleRenameRule (
    CalendarTitleRenameRuleId INT IDENTITY(1,1) PRIMARY KEY,
    Account NVARCHAR(255) NOT NULL,
    CalendarId NVARCHAR(255) NOT NULL,
    KeyPrefix NVARCHAR(200) NOT NULL,
    TargetPrefix NVARCHAR(200) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_CalendarTitleRenameRule_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarTitleRenameRule_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarTitleRenameRule_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CalendarTitleRenameRule_Account_Calendar_KeyPrefix
        UNIQUE (Account, CalendarId, KeyPrefix)
);

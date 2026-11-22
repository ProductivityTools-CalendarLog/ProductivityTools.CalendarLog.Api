IF OBJECT_ID(N'dbo.CalendarValueRenameRule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalendarValueRenameRule (
        CalendarValueRenameRuleId INT IDENTITY(1,1) PRIMARY KEY,
        Account NVARCHAR(255) NOT NULL,
        CalendarId NVARCHAR(255) NOT NULL,
        CategoryPrefix NVARCHAR(200) NOT NULL CONSTRAINT DF_CalendarValueRenameRule_CategoryPrefix DEFAULT N'*',
        KeySuffix NVARCHAR(200) NOT NULL,
        TargetSuffix NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_CalendarValueRenameRule_IsActive DEFAULT 1,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarValueRenameRule_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarValueRenameRule_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_CalendarValueRenameRule_Account_Calendar_Category_KeySuffix
            UNIQUE (Account, CalendarId, CategoryPrefix, KeySuffix)
    );
END

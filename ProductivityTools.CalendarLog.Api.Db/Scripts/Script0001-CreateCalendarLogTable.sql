CREATE TABLE dbo.CalendarLog (
    CalendarLogId INT IDENTITY(1,1) PRIMARY KEY,
    [Start] DATETIME NOT NULL,
    [End] DATETIME NOT NULL,
    [Day] DATE NOT NULL,
    [WeekNumber] INT,
    [Month] INT,
    [Duration] INT,
    [Title] NVARCHAR(255),
    [CalendarName] NVARCHAR(255),
    [Status] NVARCHAR(50),
    [Type] NVARCHAR(50),
    [Color] NVARCHAR(50)
); 
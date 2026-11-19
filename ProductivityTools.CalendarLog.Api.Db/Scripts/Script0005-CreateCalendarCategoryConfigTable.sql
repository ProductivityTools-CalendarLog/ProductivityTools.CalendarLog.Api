CREATE TABLE dbo.CalendarCategory (
    CalendarCategoryId INT IDENTITY(1,1) PRIMARY KEY,
    Category NVARCHAR(255) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_CalendarCategory_SortOrder DEFAULT 100,
    Color NVARCHAR(32) NOT NULL CONSTRAINT DF_CalendarCategory_Color DEFAULT '#64748b',
    IsActive BIT NOT NULL CONSTRAINT DF_CalendarCategory_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarCategory_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CalendarCategory_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CalendarCategory_Category UNIQUE (Category)
);

CREATE INDEX IX_CalendarCategory_SortOrder
    ON dbo.CalendarCategory (SortOrder, CalendarCategoryId);

INSERT INTO dbo.CalendarCategory (Category, SortOrder, Color, IsActive)
VALUES
    (N'OOO',                   10,  N'#4285f4', 1),
    (N'Meetings',              20,  N'#a4c2f4', 1),
    (N'One2One',               30,  N'#c9daf8', 1),
    (N'Hanging-out',           40,  N'#ea9999', 1),
    (N'TopDown',               50,  N'#ff6d01', 1),
    (N'Emails',                60,  N'#f9cb9c', 1),
    (N'Operations',            70,  N'#ffe599', 1),
    (N'IndividualProjectWork', 80,  N'#d9ead3', 1),
    (N'FocusedWork',           90,  N'#b6d7a8', 1),
    (N'Journal',               100, N'#93c47d', 1),
    (N'Coding',                110, N'#6aa84f', 1),
    (N'SelfDevelopment',       120, N'#38761d', 1);

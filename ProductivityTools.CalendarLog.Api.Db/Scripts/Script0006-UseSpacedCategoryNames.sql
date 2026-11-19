-- Update configured chart category names to spaced versions ("Individual Project Work", "Focused Work", "Self Development")
-- and normalize existing CalendarCategoryRule / CalendarLog rows to match.

IF NOT EXISTS (SELECT 1 FROM dbo.CalendarCategory WHERE Category = N'Individual Project Work')
BEGIN
    UPDATE dbo.CalendarCategory
    SET Category = N'Individual Project Work', UpdatedAtUtc = SYSUTCDATETIME()
    WHERE Category = N'IndividualProjectWork';
END

IF NOT EXISTS (SELECT 1 FROM dbo.CalendarCategory WHERE Category = N'Focused Work')
BEGIN
    UPDATE dbo.CalendarCategory
    SET Category = N'Focused Work', UpdatedAtUtc = SYSUTCDATETIME()
    WHERE Category = N'FocusedWork';
END

IF NOT EXISTS (SELECT 1 FROM dbo.CalendarCategory WHERE Category = N'Self Development')
BEGIN
    UPDATE dbo.CalendarCategory
    SET Category = N'Self Development', UpdatedAtUtc = SYSUTCDATETIME()
    WHERE Category = N'SelfDevelopment';
END

UPDATE dbo.CalendarCategoryRule
SET Category = N'Individual Project Work', UpdatedAtUtc = SYSUTCDATETIME()
WHERE REPLACE(Category, N' ', N'') = N'IndividualProjectWork' AND Category <> N'Individual Project Work';

UPDATE dbo.CalendarCategoryRule
SET Category = N'Focused Work', UpdatedAtUtc = SYSUTCDATETIME()
WHERE REPLACE(Category, N' ', N'') = N'FocusedWork' AND Category <> N'Focused Work';

UPDATE dbo.CalendarCategoryRule
SET Category = N'Self Development', UpdatedAtUtc = SYSUTCDATETIME()
WHERE REPLACE(Category, N' ', N'') = N'SelfDevelopment' AND Category <> N'Self Development';

UPDATE dbo.CalendarLog
SET Category = N'Individual Project Work', UpdatedAtUtc = SYSUTCDATETIME()
WHERE Category IS NOT NULL AND REPLACE(Category, N' ', N'') = N'IndividualProjectWork' AND Category <> N'Individual Project Work';

UPDATE dbo.CalendarLog
SET Category = N'Focused Work', UpdatedAtUtc = SYSUTCDATETIME()
WHERE Category IS NOT NULL AND REPLACE(Category, N' ', N'') = N'FocusedWork' AND Category <> N'Focused Work';

UPDATE dbo.CalendarLog
SET Category = N'Self Development', UpdatedAtUtc = SYSUTCDATETIME()
WHERE Category IS NOT NULL AND REPLACE(Category, N' ', N'') = N'SelfDevelopment' AND Category <> N'Self Development';

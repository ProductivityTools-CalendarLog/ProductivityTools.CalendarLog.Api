namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public static class CalendarLogSources
{
    public const string RawSync = "RAW_SYNC";
    public const string SheetImport = "SHEET_IMPORT";
}

public class CalendarLogEntry
{
    public long CalendarLogId { get; set; }
    public long? RawCalendarEventId { get; set; }
    public string Source { get; set; } = CalendarLogSources.RawSync;
    public string Account { get; set; } = string.Empty;
    public string? CalendarId { get; set; }
    public string CalendarName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public DateOnly Day { get; set; }
    public int WeekNumber { get; set; }
    public string Month { get; set; } = string.Empty;
    public decimal DurationHours { get; set; }
    public string? Title { get; set; }
    public string? MyStatus { get; set; }
    public string? EventType { get; set; }
    public string? Color { get; set; }
    public string? Category { get; set; }
    public string? Value { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public RawCalendarEvent? RawCalendarEvent { get; set; }
}

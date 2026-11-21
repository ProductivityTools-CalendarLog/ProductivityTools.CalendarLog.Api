namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public static class CalendarTypes
{
    public const string DataPoint = "DataPoint";
    public const string DataLog = "DataLog";
}

public class CalendarSyncState
{
    public int CalendarSyncStateId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string? CalendarName { get; set; }
    public string Type { get; set; } = CalendarTypes.DataLog;
    public bool IsActive { get; set; } = true;
    public int DefaultLookbackDays { get; set; } = 14;
    public DateOnly? LastSyncedDate { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

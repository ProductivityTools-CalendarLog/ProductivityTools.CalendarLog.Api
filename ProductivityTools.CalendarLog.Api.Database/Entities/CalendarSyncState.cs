namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public class CalendarSyncState
{
    public int CalendarSyncStateId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string? CalendarName { get; set; }
    public bool IsActive { get; set; } = true;
    public int DefaultLookbackDays { get; set; } = 14;
    public DateOnly? LastSyncedDate { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public DateOnly? PendingBackfillFrom { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

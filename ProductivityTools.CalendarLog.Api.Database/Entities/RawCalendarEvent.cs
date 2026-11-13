namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public class RawCalendarEvent
{
    public long RawCalendarEventId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string CalendarName { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public string? ICalUid { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsRecurring { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? MyStatus { get; set; }
    public string? EventStatus { get; set; }
    public string? EventType { get; set; }
    public string? Color { get; set; }
    public bool? IsOwnedByMe { get; set; }
    public int GuestCount { get; set; }
    public string? GuestsJson { get; set; }
    public DateTimeOffset? EventCreatedAt { get; set; }
    public DateTimeOffset? EventUpdatedAt { get; set; }
    public string? RawJson { get; set; }
    public bool IsDeleted { get; set; }
    public string ProcessingStatus { get; set; } = RawEventProcessingStatus.New;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}

public static class RawEventProcessingStatus
{
    public const string New = "NEW";
    public const string Processed = "PROCESSED";
    public const string Error = "ERROR";
}

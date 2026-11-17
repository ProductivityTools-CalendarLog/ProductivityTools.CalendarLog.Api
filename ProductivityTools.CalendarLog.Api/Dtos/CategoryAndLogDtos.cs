using System.ComponentModel.DataAnnotations;

namespace ProductivityTools.CalendarLog.Api.Dtos;

public sealed class UpsertCategoryRuleRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    /// <summary>
    /// Specific CalendarId (or CalendarName), or "*" to apply across all calendars of the account.
    /// </summary>
    public string CalendarId { get; set; } = "*";

    public int Priority { get; set; } = 100;

    /// <summary>
    /// One of: TitlePrefix, Title, Color, CalendarName.
    /// </summary>
    [Required]
    public string MatchField { get; set; } = string.Empty;

    [Required]
    public string MatchValue { get; set; } = string.Empty;

    [Required]
    public string Category { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class CalendarLogDto
{
    public long CalendarLogId { get; set; }
    public long? RawCalendarEventId { get; set; }
    public string Source { get; set; } = string.Empty;
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
}

public sealed class ReprocessCalendarLogsResult
{
    public int RawEventsEvaluated { get; set; }
    public int LogsUpserted { get; set; }
    public int LogsRemovedOrSkipped { get; set; }
}

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

public sealed class DailyCategorySummaryResponse
{
    public List<string> Categories { get; set; } = new();
    public List<CalendarCategoryDto> CategoryConfigs { get; set; } = new();
    public List<DailyCategoryBucketDto> Days { get; set; } = new();
}

public sealed class DailyCategoryBucketDto
{
    public DateOnly Day { get; set; }
    public decimal TotalHours { get; set; }
    public Dictionary<string, decimal> ByCategory { get; set; } = new();
}

public sealed class WeeklyCategorySummaryResponse
{
    public List<string> Categories { get; set; } = new();
    public List<CalendarCategoryDto> CategoryConfigs { get; set; } = new();
    public List<WeeklyCategoryBucketDto> Weeks { get; set; } = new();
}

public sealed class WeeklyCategoryBucketDto
{
    public int WeekNumber { get; set; }
    public DateOnly WeekStartDay { get; set; }
    public DateOnly WeekEndDay { get; set; }
    public decimal TotalHours { get; set; }
    public Dictionary<string, decimal> ByCategory { get; set; } = new();
}

public sealed class CalendarCategoryDto
{
    public int CalendarCategoryId { get; set; }
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string Color { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class UpsertCalendarCategoryRequest
{
    [Required]
    public string Category { get; set; } = string.Empty;

    public int SortOrder { get; set; } = 100;

    [Required]
    public string Color { get; set; } = "#64748b";

    public bool IsActive { get; set; } = true;
}

public sealed class ReorderCalendarCategoriesRequest
{
    [Required]
    public List<int> OrderedCategoryIds { get; set; } = new();
}

public sealed class DataPointMonthlySummaryResponse
{
    public List<string> DataPointCalendars { get; set; } = new();
    public int TotalEvents { get; set; }
    public List<DataPointCategoryChartDto> Categories { get; set; } = new();
}

public sealed class DataPointCategoryChartDto
{
    public string Category { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public List<string> Values { get; set; } = new();
    public List<DataPointMonthBucketDto> Months { get; set; } = new();
}

public sealed class DataPointMonthBucketDto
{
    public string Month { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public Dictionary<string, int> ByValue { get; set; } = new();
}

public sealed class DataPointEventItemDto
{
    public long RawCalendarEventId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string CalendarName { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public string Month { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? Title { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

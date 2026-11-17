using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ProductivityTools.CalendarLog.Api.Dtos;

public sealed class SyncPlanResponse
{
    public string Account { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int DefaultLookbackDays { get; set; }
    public List<CalendarSyncPlanItem> Calendars { get; set; } = new();
}

public sealed class CalendarSyncPlanItem
{
    public string CalendarId { get; set; } = string.Empty;
    public string? CalendarName { get; set; }
    public int LookbackDays { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly? LastSyncedDate { get; set; }
    public DateOnly? PendingBackfillFrom { get; set; }
}

public sealed class RawEventsBatchRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    [Required]
    public string CalendarId { get; set; } = string.Empty;

    [Required]
    public string CalendarName { get; set; } = string.Empty;

    public DateTimeOffset RangeStart { get; set; }
    public DateTimeOffset RangeEnd { get; set; }

    /// <summary>
    /// Raw Google Calendar API v3 event objects (as returned by Calendar.Events.list).
    /// </summary>
    public List<JsonElement> Events { get; set; } = new();
}

public sealed class RawEventsSyncResult
{
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public DateTimeOffset RangeStart { get; set; }
    public DateTimeOffset RangeEnd { get; set; }
    public int TotalReceived { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int SoftDeleted { get; set; }
    public List<CalendarMutationCommand> Mutations { get; set; } = new();
}

public static class CalendarMutationActions
{
    public const string Move = "MOVE";
    public const string Rename = "RENAME";
}

public sealed class CalendarMutationCommand
{
    public string Action { get; set; } = string.Empty;
    public string SourceCalendarId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string? TargetCalendarId { get; set; }
    public string NewTitle { get; set; } = string.Empty;
}

public sealed class UpsertConvertRuleRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    [Required]
    public string KeyPrefix { get; set; } = string.Empty;

    [Required]
    public string TargetCalendarId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class UpsertTitleRenameRuleRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    /// <summary>
    /// Target calendar ID, or "*" to match all calendars for this account.
    /// </summary>
    [Required]
    public string CalendarId { get; set; } = string.Empty;

    [Required]
    public string KeyPrefix { get; set; } = string.Empty;

    [Required]
    public string TargetPrefix { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class BackfillRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    [Required]
    public DateOnly FromDate { get; set; }

    public string? CalendarId { get; set; }
}

public sealed class UpsertSyncCalendarRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    [Required]
    public string CalendarId { get; set; } = string.Empty;

    public string? CalendarName { get; set; }
    public bool IsActive { get; set; } = true;
    public int? DefaultLookbackDays { get; set; }
    public DateOnly? PendingBackfillFrom { get; set; }
}

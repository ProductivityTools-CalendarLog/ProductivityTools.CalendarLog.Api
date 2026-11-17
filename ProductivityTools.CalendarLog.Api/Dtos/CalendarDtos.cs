using System.ComponentModel.DataAnnotations;

namespace ProductivityTools.CalendarLog.Api.Dtos;

public sealed class CalendarItemDto
{
    public int CalendarSyncStateId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string? CalendarName { get; set; }
    public bool IsActive { get; set; }
    public int DefaultLookbackDays { get; set; }
    public DateOnly? LastSyncedDate { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public DateOnly? PendingBackfillFrom { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CreateCalendarRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    [Required]
    public string CalendarId { get; set; } = string.Empty;

    public string? CalendarName { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, 3650)]
    public int? DefaultLookbackDays { get; set; }

    public DateOnly? PendingBackfillFrom { get; set; }
}

public sealed class UpdateCalendarRequest
{
    [Required]
    public string Account { get; set; } = string.Empty;

    [Required]
    public string CalendarId { get; set; } = string.Empty;

    public string? CalendarName { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, 3650)]
    public int DefaultLookbackDays { get; set; } = 14;

    public DateOnly? PendingBackfillFrom { get; set; }
}

public sealed class SetCalendarActiveRequest
{
    public bool IsActive { get; set; }
}

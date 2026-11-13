namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public class CalendarTitleRenameRule
{
    public int CalendarTitleRenameRuleId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string TargetPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

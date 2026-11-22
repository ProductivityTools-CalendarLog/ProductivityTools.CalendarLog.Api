namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public class CalendarValueRenameRule
{
    public int CalendarValueRenameRuleId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string CategoryPrefix { get; set; } = "*";
    public string KeySuffix { get; set; } = string.Empty;
    public string TargetSuffix { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

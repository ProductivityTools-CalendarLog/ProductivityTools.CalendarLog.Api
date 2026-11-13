namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public class CalendarConvertRule
{
    public int CalendarConvertRuleId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string TargetCalendarId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

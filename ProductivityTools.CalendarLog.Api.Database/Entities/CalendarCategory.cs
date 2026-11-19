namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public class CalendarCategory
{
    public int CalendarCategoryId { get; set; }
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 100;
    public string Color { get; set; } = "#64748b";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

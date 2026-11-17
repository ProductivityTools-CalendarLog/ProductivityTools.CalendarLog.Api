namespace ProductivityTools.CalendarLog.Api.Database.Entities;

public static class CategoryMatchFields
{
    public const string TitlePrefix = "TitlePrefix";
    public const string Title = "Title";
    public const string Color = "Color";
    public const string CalendarName = "CalendarName";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TitlePrefix,
        Title,
        Color,
        CalendarName
    };
}

public class CalendarCategoryRule
{
    public int CalendarCategoryRuleId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string CalendarId { get; set; } = "*";
    public int Priority { get; set; } = 100;
    public string MatchField { get; set; } = string.Empty;
    public string MatchValue { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

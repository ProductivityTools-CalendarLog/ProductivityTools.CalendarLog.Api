using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Database.Entities;
using ProductivityTools.CalendarLog.Api.Dtos;

namespace ProductivityTools.CalendarLog.Api.Services;

public sealed class CalendarLogProcessor
{
    private const int LookupChunkSize = 500;

    private static readonly HashSet<string> ExcludedEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "workingLocation",
        "WORKING_LOCATION",
        "outOfOffice",
        "OUT_OF_OFFICE"
    };

    private static readonly HashSet<string> ExcludedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "declined",
        "NO",
        "needsAction",
        "INVITED"
    };

    private static readonly HashSet<string> AcceptedOwnerStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "accepted",
        "organizer",
        "YES",
        "OWNER"
    };

    private readonly CalendarLogContext _db;
    private readonly TimeZoneInfo _timeZone;

    public CalendarLogProcessor(CalendarLogContext db, IConfiguration configuration)
    {
        _db = db;
        var tzId = configuration["Sync:TimeZone"] ?? "Europe/Warsaw";
        _timeZone = ResolveTimeZone(tzId);
    }

    public async Task<List<CalendarCategoryRule>> ListRulesAsync(
        string? account = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CalendarCategoryRules.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(r => r.Account == normalized);
        }

        return await query
            .OrderBy(r => r.Account)
            .ThenBy(r => r.Priority)
            .ThenBy(r => r.CalendarCategoryRuleId)
            .ToListAsync(cancellationToken);
    }

    public async Task<CalendarCategoryRule> CreateRuleAsync(
        UpsertCategoryRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = ValidateAndNormalize(request);
        var nowUtc = DateTime.UtcNow;

        var rule = new CalendarCategoryRule
        {
            Account = normalized.Account,
            CalendarId = normalized.CalendarId,
            Priority = normalized.Priority,
            MatchField = normalized.MatchField,
            MatchValue = normalized.MatchValue,
            Category = normalized.Category,
            IsActive = normalized.IsActive,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        _db.CalendarCategoryRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<CalendarCategoryRule?> UpdateRuleAsync(
        int id,
        UpsertCategoryRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = ValidateAndNormalize(request);
        var rule = await _db.CalendarCategoryRules
            .SingleOrDefaultAsync(r => r.CalendarCategoryRuleId == id, cancellationToken);

        if (rule == null)
        {
            return null;
        }

        rule.Account = normalized.Account;
        rule.CalendarId = normalized.CalendarId;
        rule.Priority = normalized.Priority;
        rule.MatchField = normalized.MatchField;
        rule.MatchValue = normalized.MatchValue;
        rule.Category = normalized.Category;
        rule.IsActive = normalized.IsActive;
        rule.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await _db.CalendarCategoryRules
            .SingleOrDefaultAsync(r => r.CalendarCategoryRuleId == id, cancellationToken);

        if (rule == null)
        {
            return false;
        }

        _db.CalendarCategoryRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReprocessCalendarLogsResult> ProcessPendingAsync(
        string account,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = account.Trim();
        var pendingRawEvents = await _db.RawCalendarEvents
            .Where(e => e.Account == normalizedAccount && e.ProcessingStatus == RawEventProcessingStatus.New)
            .ToListAsync(cancellationToken);

        return await EvaluateAndPersistAsync(pendingRawEvents, cancellationToken);
    }

    public async Task<ReprocessCalendarLogsResult> ReprocessAllAsync(
        string? account = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.RawCalendarEvents.AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(e => e.Account == normalized);
        }

        var rawEvents = await query.ToListAsync(cancellationToken);
        return await EvaluateAndPersistAsync(rawEvents, cancellationToken);
    }

    public async Task<List<CalendarLogDto>> ListLogsAsync(
        string? account = null,
        DateOnly? fromDay = null,
        DateOnly? toDay = null,
        string? category = null,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var cappedLimit = Math.Clamp(limit, 1, 10000);
        var query = _db.CalendarLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(l => l.Account == normalized);
        }

        if (fromDay.HasValue)
        {
            query = query.Where(l => l.Day >= fromDay.Value);
        }

        if (toDay.HasValue)
        {
            query = query.Where(l => l.Day <= toDay.Value);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim();
            query = query.Where(l => l.Category == normalizedCategory);
        }

        var items = await query
            .OrderByDescending(l => l.StartTime)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);

        return items.Select(MapToDto).ToList();
    }

    private async Task<ReprocessCalendarLogsResult> EvaluateAndPersistAsync(
        List<RawCalendarEvent> rawEvents,
        CancellationToken cancellationToken)
    {
        if (rawEvents.Count == 0)
        {
            return new ReprocessCalendarLogsResult();
        }

        var accounts = rawEvents.Select(e => e.Account).Distinct(StringComparer.Ordinal).ToList();
        var activeRules = await _db.CalendarCategoryRules
            .AsNoTracking()
            .Where(r => accounts.Contains(r.Account) && r.IsActive)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CalendarCategoryRuleId)
            .ToListAsync(cancellationToken);

        var rulesByAccount = activeRules
            .GroupBy(r => r.Account, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarCategoryRule>)g.ToList(), StringComparer.Ordinal);

        var rawIds = rawEvents.Select(e => e.RawCalendarEventId).ToList();
        var existingLogsByRawId = new Dictionary<long, CalendarLogEntry>();

        foreach (var chunk in rawIds.Chunk(LookupChunkSize))
        {
            var existingChunk = await _db.CalendarLogs
                .Where(l => l.RawCalendarEventId.HasValue && chunk.Contains(l.RawCalendarEventId.Value))
                .ToListAsync(cancellationToken);

            foreach (var log in existingChunk)
            {
                if (log.RawCalendarEventId.HasValue)
                {
                    existingLogsByRawId[log.RawCalendarEventId.Value] = log;
                }
            }
        }

        var nowUtc = DateTime.UtcNow;
        var upserted = 0;
        var removedOrSkipped = 0;

        foreach (var raw in rawEvents)
        {
            existingLogsByRawId.TryGetValue(raw.RawCalendarEventId, out var existingLog);

            if (!ShouldIncludeEvent(raw))
            {
                if (existingLog != null)
                {
                    _db.CalendarLogs.Remove(existingLog);
                }
                raw.ProcessingStatus = RawEventProcessingStatus.Processed;
                raw.ProcessedAtUtc = nowUtc;
                raw.ErrorMessage = null;
                removedOrSkipped++;
                continue;
            }

            rulesByAccount.TryGetValue(raw.Account, out var accountRules);
            var (category, extractedValue) = ResolveCategoryAndValue(
                raw,
                accountRules ?? Array.Empty<CalendarCategoryRule>());

            var localStart = TimeZoneInfo.ConvertTime(raw.StartTime, _timeZone);
            var day = DateOnly.FromDateTime(localStart.DateTime);
            var weekNumber = ISOWeek.GetYear(localStart.DateTime) * 100 + ISOWeek.GetWeekOfYear(localStart.DateTime);
            var month = localStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var durationHours = Math.Max(0m, Math.Round((decimal)(raw.EndTime - raw.StartTime).TotalHours, 4));

            if (existingLog == null)
            {
                _db.CalendarLogs.Add(new CalendarLogEntry
                {
                    RawCalendarEventId = raw.RawCalendarEventId,
                    Source = CalendarLogSources.RawSync,
                    Account = raw.Account,
                    CalendarId = raw.CalendarId,
                    CalendarName = raw.CalendarName,
                    StartTime = raw.StartTime,
                    EndTime = raw.EndTime,
                    Day = day,
                    WeekNumber = weekNumber,
                    Month = month,
                    DurationHours = durationHours,
                    Title = raw.Title,
                    MyStatus = raw.MyStatus,
                    EventType = raw.EventType,
                    Color = raw.Color,
                    Category = category,
                    Value = extractedValue,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
            }
            else
            {
                existingLog.Account = raw.Account;
                existingLog.CalendarId = raw.CalendarId;
                existingLog.CalendarName = raw.CalendarName;
                existingLog.StartTime = raw.StartTime;
                existingLog.EndTime = raw.EndTime;
                existingLog.Day = day;
                existingLog.WeekNumber = weekNumber;
                existingLog.Month = month;
                existingLog.DurationHours = durationHours;
                existingLog.Title = raw.Title;
                existingLog.MyStatus = raw.MyStatus;
                existingLog.EventType = raw.EventType;
                existingLog.Color = raw.Color;
                existingLog.Category = category;
                existingLog.Value = extractedValue;
                existingLog.UpdatedAtUtc = nowUtc;
            }

            raw.ProcessingStatus = RawEventProcessingStatus.Processed;
            raw.ProcessedAtUtc = nowUtc;
            raw.ErrorMessage = null;
            upserted++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ReprocessCalendarLogsResult
        {
            RawEventsEvaluated = rawEvents.Count,
            LogsUpserted = upserted,
            LogsRemovedOrSkipped = removedOrSkipped
        };
    }

    private static bool ShouldIncludeEvent(RawCalendarEvent raw)
    {
        if (raw.IsDeleted)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(raw.EventType) && ExcludedEventTypes.Contains(raw.EventType.Trim()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(raw.MyStatus) && ExcludedStatuses.Contains(raw.MyStatus.Trim()))
        {
            return false;
        }

        // Color "1" (Lavender) = "do not count" in Analysis.js.
        if (string.Equals(raw.Color?.Trim(), "1", StringComparison.Ordinal))
        {
            return false;
        }

        // Replicates ownerAccepted() from Analysis.js:
        // On the primary calendar (CalendarId == Account or CalendarName == Account),
        // multi-guest events require the owner to have accepted.
        var isPrimaryCalendar =
            string.Equals(raw.CalendarId, raw.Account, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw.CalendarName, raw.Account, StringComparison.OrdinalIgnoreCase);

        if (isPrimaryCalendar && raw.GuestCount > 0)
        {
            if (string.IsNullOrWhiteSpace(raw.MyStatus) || !AcceptedOwnerStatuses.Contains(raw.MyStatus.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private static (string? Category, string? Value) ResolveCategoryAndValue(
        RawCalendarEvent raw,
        IReadOnlyList<CalendarCategoryRule> rules)
    {
        var title = raw.Title?.Trim() ?? string.Empty;
        string? colonPrefix = null;
        string? afterColonValue = null;

        var colonIndex = title.IndexOf(':');
        if (colonIndex >= 0)
        {
            colonPrefix = title[..colonIndex].Trim();
            var suffix = title[(colonIndex + 1)..].Trim();
            afterColonValue = string.IsNullOrEmpty(suffix) ? null : suffix;
        }
        else if (!string.IsNullOrEmpty(title))
        {
            colonPrefix = title;
        }

        foreach (var rule in rules)
        {
            if (!MatchesCalendarScope(raw, rule.CalendarId))
            {
                continue;
            }

            if (string.Equals(rule.MatchField, CategoryMatchFields.TitlePrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(colonPrefix))
                {
                    if (rule.MatchValue == "*")
                    {
                        var cat = rule.Category == "*" ? colonPrefix : rule.Category;
                        return (cat, afterColonValue);
                    }

                    if (string.Equals(colonPrefix, rule.MatchValue, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(colonPrefix, rule.Category, StringComparison.OrdinalIgnoreCase))
                    {
                        return (rule.Category, afterColonValue);
                    }
                }
            }
            else if (string.Equals(rule.MatchField, CategoryMatchFields.Title, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(title)
                    && string.Equals(title, rule.MatchValue, StringComparison.OrdinalIgnoreCase))
                {
                    return (rule.Category, null);
                }
            }
            else if (string.Equals(rule.MatchField, CategoryMatchFields.Color, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(raw.Color)
                    && string.Equals(raw.Color.Trim(), rule.MatchValue, StringComparison.OrdinalIgnoreCase))
                {
                    return (rule.Category, null);
                }
            }
            else if (string.Equals(rule.MatchField, CategoryMatchFields.CalendarName, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(raw.CalendarName, rule.MatchValue, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw.CalendarId, rule.MatchValue, StringComparison.OrdinalIgnoreCase))
                {
                    return (rule.Category, null);
                }
            }
        }

        return (null, null);
    }

    private static bool MatchesCalendarScope(RawCalendarEvent raw, string ruleCalendarId)
    {
        if (string.IsNullOrWhiteSpace(ruleCalendarId) || ruleCalendarId == "*")
        {
            return true;
        }

        return string.Equals(raw.CalendarId, ruleCalendarId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw.CalendarName, ruleCalendarId, StringComparison.OrdinalIgnoreCase);
    }

    private static UpsertCategoryRuleRequest ValidateAndNormalize(UpsertCategoryRuleRequest request)
    {
        var account = request.Account.Trim();
        var calendarId = string.IsNullOrWhiteSpace(request.CalendarId) ? "*" : request.CalendarId.Trim();
        var matchField = NormalizeCanonicalMatchField(request.MatchField);
        var matchValue = request.MatchValue.Trim();
        var category = request.Category.Trim();

        if (string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("Account is required.");
        }
        if (matchField == null)
        {
            throw new ArgumentException("MatchField must be one of: TitlePrefix, Title, Color, CalendarName.");
        }
        if (string.IsNullOrEmpty(matchValue))
        {
            throw new ArgumentException("MatchValue is required.");
        }
        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentException("Category is required.");
        }

        return new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = calendarId,
            Priority = request.Priority,
            MatchField = matchField,
            MatchValue = matchValue,
            Category = category,
            IsActive = request.IsActive
        };
    }

    private static string? NormalizeCanonicalMatchField(string? field)
    {
        var trimmed = field?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (string.Equals(trimmed, CategoryMatchFields.TitlePrefix, StringComparison.OrdinalIgnoreCase))
            return CategoryMatchFields.TitlePrefix;
        if (string.Equals(trimmed, CategoryMatchFields.Title, StringComparison.OrdinalIgnoreCase))
            return CategoryMatchFields.Title;
        if (string.Equals(trimmed, CategoryMatchFields.Color, StringComparison.OrdinalIgnoreCase))
            return CategoryMatchFields.Color;
        if (string.Equals(trimmed, CategoryMatchFields.CalendarName, StringComparison.OrdinalIgnoreCase))
            return CategoryMatchFields.CalendarName;
        return null;
    }

    private static CalendarLogDto MapToDto(CalendarLogEntry l) => new()
    {
        CalendarLogId = l.CalendarLogId,
        RawCalendarEventId = l.RawCalendarEventId,
        Source = l.Source,
        Account = l.Account,
        CalendarId = l.CalendarId,
        CalendarName = l.CalendarName,
        StartTime = l.StartTime,
        EndTime = l.EndTime,
        Day = l.Day,
        WeekNumber = l.WeekNumber,
        Month = l.Month,
        DurationHours = l.DurationHours,
        Title = l.Title,
        MyStatus = l.MyStatus,
        EventType = l.EventType,
        Color = l.Color,
        Category = l.Category,
        Value = l.Value
    };

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (string.Equals(timeZoneId, "Europe/Warsaw", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
                }
                catch
                {
                    // Fall through to UTC
                }
            }
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

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

    // Exact bottom-to-top stacked chart series order and colors from the PT.Calendar spreadsheet:
    public static readonly IReadOnlyList<(string Category, int SortOrder, string Color)> DefaultSpreadsheetCategories =
        new List<(string, int, string)>
        {
            ("OOO",                   10,  "#4285f4"),
            ("Meetings",              20,  "#a4c2f4"),
            ("One2One",               30,  "#c9daf8"),
            ("Hanging-out",           40,  "#ea9999"),
            ("TopDown",               50,  "#ff6d01"),
            ("Emails",                60,  "#f9cb9c"),
            ("Operations",            70,  "#ffe599"),
            ("IndividualProjectWork", 80,  "#d9ead3"),
            ("FocusedWork",           90,  "#b6d7a8"),
            ("Journal",               100, "#93c47d"),
            ("Coding",                110, "#6aa84f"),
            ("SelfDevelopment",       120, "#38761d")
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

    public async Task<List<CalendarCategoryDto>> ListCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultCategoriesSeededAsync(cancellationToken);

        var items = await _db.CalendarCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CalendarCategoryId)
            .ToListAsync(cancellationToken);

        return items.Select(MapCategoryToDto).ToList();
    }

    public async Task<CalendarCategoryDto> CreateCategoryAsync(
        UpsertCalendarCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultCategoriesSeededAsync(cancellationToken);

        var name = request.Category.Trim();
        var color = NormalizeHexColor(request.Color);
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Category name is required.");
        }

        var exists = await _db.CalendarCategories
            .AnyAsync(c => c.Category.ToLower() == name.ToLower(), cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"Category '{name}' already exists.");
        }

        var nowUtc = DateTime.UtcNow;
        var entity = new CalendarCategory
        {
            Category = name,
            SortOrder = request.SortOrder,
            Color = color,
            IsActive = request.IsActive,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        _db.CalendarCategories.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return MapCategoryToDto(entity);
    }

    public async Task<CalendarCategoryDto?> UpdateCategoryAsync(
        int id,
        UpsertCalendarCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Category.Trim();
        var color = NormalizeHexColor(request.Color);
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Category name is required.");
        }

        var entity = await _db.CalendarCategories
            .SingleOrDefaultAsync(c => c.CalendarCategoryId == id, cancellationToken);
        if (entity == null)
        {
            return null;
        }

        var duplicate = await _db.CalendarCategories
            .AnyAsync(c => c.CalendarCategoryId != id && c.Category.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException($"Category '{name}' already exists.");
        }

        entity.Category = name;
        entity.SortOrder = request.SortOrder;
        entity.Color = color;
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapCategoryToDto(entity);
    }

    public async Task<List<CalendarCategoryDto>> ReorderCategoriesAsync(
        IReadOnlyList<int> orderedIds,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultCategoriesSeededAsync(cancellationToken);

        var all = await _db.CalendarCategories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CalendarCategoryId)
            .ToListAsync(cancellationToken);

        var byId = all.ToDictionary(c => c.CalendarCategoryId);
        var nowUtc = DateTime.UtcNow;
        var currentOrder = 10;
        var touched = new HashSet<int>();

        foreach (var id in orderedIds)
        {
            if (byId.TryGetValue(id, out var item) && touched.Add(id))
            {
                item.SortOrder = currentOrder;
                item.UpdatedAtUtc = nowUtc;
                currentOrder += 10;
            }
        }

        foreach (var item in all)
        {
            if (touched.Add(item.CalendarCategoryId))
            {
                item.SortOrder = currentOrder;
                item.UpdatedAtUtc = nowUtc;
                currentOrder += 10;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return all
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CalendarCategoryId)
            .Select(MapCategoryToDto)
            .ToList();
    }

    public async Task<List<CalendarCategoryDto>> ResetDefaultCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.CalendarCategories.ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(c => c.Category, StringComparer.OrdinalIgnoreCase);
        var nowUtc = DateTime.UtcNow;

        foreach (var (name, sortOrder, color) in DefaultSpreadsheetCategories)
        {
            if (byName.TryGetValue(name, out var entity))
            {
                entity.Category = name;
                entity.SortOrder = sortOrder;
                entity.Color = color;
                entity.IsActive = true;
                entity.UpdatedAtUtc = nowUtc;
            }
            else
            {
                _db.CalendarCategories.Add(new CalendarCategory
                {
                    Category = name,
                    SortOrder = sortOrder,
                    Color = color,
                    IsActive = true,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await ListCategoriesAsync(cancellationToken);
    }

    public async Task<bool> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.CalendarCategories
            .SingleOrDefaultAsync(c => c.CalendarCategoryId == id, cancellationToken);
        if (entity == null)
        {
            return false;
        }

        _db.CalendarCategories.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task EnsureDefaultCategoriesSeededAsync(CancellationToken cancellationToken)
    {
        if (await _db.CalendarCategories.AnyAsync(cancellationToken))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var (name, sortOrder, color) in DefaultSpreadsheetCategories)
        {
            _db.CalendarCategories.Add(new CalendarCategory
            {
                Category = name,
                SortOrder = sortOrder,
                Color = color,
                IsActive = true,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
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
        CancellationToken cancellationToken = default,
        int? weekNumber = null)
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

        if (weekNumber.HasValue)
        {
            query = query.Where(l => l.WeekNumber == weekNumber.Value);
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

    public async Task<DailyCategorySummaryResponse> GetDailySummaryAsync(
        string? account = null,
        DateOnly? fromDay = null,
        DateOnly? toDay = null,
        bool includeUncategorized = true,
        CancellationToken cancellationToken = default)
    {
        const string uncategorizedLabel = "Uncategorized";

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

        if (!includeUncategorized)
        {
            query = query.Where(l => l.Category != null && l.Category != "");
        }

        var categoryConfigs = await ListCategoriesAsync(cancellationToken);
        var canonicalByLower = categoryConfigs
            .ToDictionary(c => c.Category.ToLowerInvariant(), c => c.Category, StringComparer.Ordinal);
        var sortOrderByCat = categoryConfigs
            .ToDictionary(c => c.Category, c => c.SortOrder, StringComparer.OrdinalIgnoreCase);

        var rawGrouped = await query
            .GroupBy(l => new { l.Day, l.Category })
            .Select(g => new
            {
                g.Key.Day,
                g.Key.Category,
                Hours = g.Sum(x => x.DurationHours)
            })
            .ToListAsync(cancellationToken);

        var normalizedGrouped = rawGrouped
            .Select(g => new
            {
                g.Day,
                Category = CanonicalizeCategoryLabel(g.Category, canonicalByLower, uncategorizedLabel),
                g.Hours
            })
            .ToList();

        var categories = OrderSummaryCategories(
            normalizedGrouped.Select(g => g.Category),
            sortOrderByCat,
            uncategorizedLabel);

        var days = normalizedGrouped
            .GroupBy(g => g.Day)
            .OrderByDescending(g => g.Key)
            .Select(dayGroup =>
            {
                var rawByCat = new Dictionary<string, decimal>(StringComparer.Ordinal);
                decimal total = 0m;
                foreach (var item in dayGroup)
                {
                    rawByCat[item.Category] = rawByCat.GetValueOrDefault(item.Category) + item.Hours;
                    total += item.Hours;
                }

                var byCat = rawByCat.ToDictionary(
                    kvp => kvp.Key,
                    kvp => Math.Round(kvp.Value, 2),
                    StringComparer.Ordinal);

                return new DailyCategoryBucketDto
                {
                    Day = dayGroup.Key,
                    TotalHours = Math.Round(total, 2),
                    ByCategory = byCat
                };
            })
            .ToList();

        return new DailyCategorySummaryResponse
        {
            Categories = categories,
            CategoryConfigs = categoryConfigs,
            Days = days
        };
    }

    public async Task<WeeklyCategorySummaryResponse> GetWeeklySummaryAsync(
        string? account = null,
        DateOnly? fromDay = null,
        DateOnly? toDay = null,
        int? fromWeek = null,
        int? toWeek = null,
        bool includeUncategorized = true,
        CancellationToken cancellationToken = default)
    {
        const string uncategorizedLabel = "Uncategorized";

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

        if (fromWeek.HasValue)
        {
            query = query.Where(l => l.WeekNumber >= fromWeek.Value);
        }

        if (toWeek.HasValue)
        {
            query = query.Where(l => l.WeekNumber <= toWeek.Value);
        }

        if (!includeUncategorized)
        {
            query = query.Where(l => l.Category != null && l.Category != "");
        }

        var categoryConfigs = await ListCategoriesAsync(cancellationToken);
        var canonicalByLower = categoryConfigs
            .ToDictionary(c => c.Category.ToLowerInvariant(), c => c.Category, StringComparer.Ordinal);
        var sortOrderByCat = categoryConfigs
            .ToDictionary(c => c.Category, c => c.SortOrder, StringComparer.OrdinalIgnoreCase);

        var rawGrouped = await query
            .GroupBy(l => new { l.WeekNumber, l.Category })
            .Select(g => new
            {
                g.Key.WeekNumber,
                g.Key.Category,
                Hours = g.Sum(x => x.DurationHours)
            })
            .ToListAsync(cancellationToken);

        var normalizedGrouped = rawGrouped
            .Select(g => new
            {
                g.WeekNumber,
                Category = CanonicalizeCategoryLabel(g.Category, canonicalByLower, uncategorizedLabel),
                g.Hours
            })
            .ToList();

        var categories = OrderSummaryCategories(
            normalizedGrouped.Select(g => g.Category),
            sortOrderByCat,
            uncategorizedLabel);

        var weeks = normalizedGrouped
            .GroupBy(g => g.WeekNumber)
            .OrderByDescending(g => g.Key)
            .Select(weekGroup =>
            {
                var rawByCat = new Dictionary<string, decimal>(StringComparer.Ordinal);
                decimal total = 0m;
                foreach (var item in weekGroup)
                {
                    rawByCat[item.Category] = rawByCat.GetValueOrDefault(item.Category) + item.Hours;
                    total += item.Hours;
                }

                var byCat = rawByCat.ToDictionary(
                    kvp => kvp.Key,
                    kvp => Math.Round(kvp.Value, 2),
                    StringComparer.Ordinal);

                var (weekStart, weekEnd) = ResolveIsoWeekBounds(weekGroup.Key);

                return new WeeklyCategoryBucketDto
                {
                    WeekNumber = weekGroup.Key,
                    WeekStartDay = weekStart,
                    WeekEndDay = weekEnd,
                    TotalHours = Math.Round(total, 2),
                    ByCategory = byCat
                };
            })
            .ToList();

        return new WeeklyCategorySummaryResponse
        {
            Categories = categories,
            CategoryConfigs = categoryConfigs,
            Weeks = weeks
        };
    }

    private static (DateOnly Start, DateOnly End) ResolveIsoWeekBounds(int weekNumber)
    {
        var year = weekNumber / 100;
        var week = weekNumber % 100;
        if (year >= 1970 && year <= 9998 && week >= 1 && week <= ISOWeek.GetWeeksInYear(year))
        {
            var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
            var sunday = monday.AddDays(6);
            return (monday, sunday);
        }

        return (DateOnly.MinValue, DateOnly.MinValue);
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
        var categoryConfigs = await ListCategoriesAsync(cancellationToken);
        var canonicalByLower = categoryConfigs
            .ToDictionary(c => c.Category.ToLowerInvariant(), c => c.Category, StringComparer.Ordinal);
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
            var (rawCategory, extractedValue) = ResolveCategoryAndValue(
                raw,
                accountRules ?? Array.Empty<CalendarCategoryRule>());
            var category = rawCategory == null
                ? null
                : canonicalByLower.GetValueOrDefault(rawCategory.Trim().ToLowerInvariant(), rawCategory.Trim());

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

        // Matches PT.Calendar (Analysis.js): 24h / multi-day all-day events are excluded;
        // OOO blocks are logged as explicit timed events (e.g. 08:00-16:00 / 09:00-17:00 = 8.00 h).
        if (raw.IsAllDay)
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

    private static CalendarCategoryDto MapCategoryToDto(CalendarCategory c) => new()
    {
        CalendarCategoryId = c.CalendarCategoryId,
        Category = c.Category,
        SortOrder = c.SortOrder,
        Color = c.Color,
        IsActive = c.IsActive
    };

    private static string NormalizeHexColor(string? color)
    {
        var trimmed = color?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "#64748b";
        }
        return trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }

    private static string CanonicalizeCategoryLabel(
        string? rawCategory,
        IReadOnlyDictionary<string, string> canonicalByLower,
        string uncategorizedLabel)
    {
        if (string.IsNullOrWhiteSpace(rawCategory))
        {
            return uncategorizedLabel;
        }
        var trimmed = rawCategory.Trim();
        return canonicalByLower.GetValueOrDefault(trimmed.ToLowerInvariant(), trimmed);
    }

    private static List<string> OrderSummaryCategories(
        IEnumerable<string> rawCategories,
        IReadOnlyDictionary<string, int> sortOrderByCategory,
        string uncategorizedLabel)
    {
        return rawCategories
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c =>
            {
                if (string.Equals(c, uncategorizedLabel, StringComparison.Ordinal))
                {
                    return int.MaxValue;
                }
                return sortOrderByCategory.TryGetValue(c, out var order) ? order : int.MaxValue - 1;
            })
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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

using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Database.Entities;
using ProductivityTools.CalendarLog.Api.Dtos;

namespace ProductivityTools.CalendarLog.Api.Services;

public sealed record EventMutationCandidate(string ExternalEventId, string? Title);

public sealed class CalendarMutationPlanner
{
    public const string WildcardCalendarId = "*";

    private readonly CalendarLogContext _db;

    public CalendarMutationPlanner(CalendarLogContext db)
    {
        _db = db;
    }

    public async Task<List<CalendarMutationCommand>> PlanMutationsAsync(
        string account,
        string sourceCalendarId,
        IEnumerable<EventMutationCandidate> candidates,
        CancellationToken cancellationToken = default,
        string? sourceCalendarName = null)
    {
        var normalizedAccount = account.Trim();
        var normalizedSourceCalendarId = sourceCalendarId.Trim();
        var normalizedSourceCalendarName = sourceCalendarName?.Trim();

        var convertRules = await _db.CalendarConvertRules
            .AsNoTracking()
            .Where(r => r.Account == normalizedAccount && r.IsActive)
            .ToListAsync(cancellationToken);

        var renameRules = await _db.CalendarTitleRenameRules
            .AsNoTracking()
            .Where(r => r.Account == normalizedAccount && r.IsActive)
            .ToListAsync(cancellationToken);

        var valueRenameRules = await _db.CalendarValueRenameRules
            .AsNoTracking()
            .Where(r => r.Account == normalizedAccount && r.IsActive)
            .ToListAsync(cancellationToken);

        if (convertRules.Count == 0 && renameRules.Count == 0 && valueRenameRules.Count == 0)
        {
            return new List<CalendarMutationCommand>();
        }

        var convertByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in convertRules)
        {
            var key = NormalizeConvertKey(rule.KeyPrefix);
            var target = rule.TargetCalendarId.Trim();
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(target))
            {
                convertByKey[key] = target;
            }
        }

        var renameByCalendar = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in renameRules)
        {
            var calId = rule.CalendarId.Trim();
            var key = NormalizeRenameKey(rule.KeyPrefix);
            var targetPrefix = rule.TargetPrefix.Trim();
            if (string.IsNullOrEmpty(calId) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(targetPrefix))
            {
                continue;
            }

            if (!renameByCalendar.TryGetValue(calId, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                renameByCalendar[calId] = map;
            }
            map[key] = targetPrefix;
        }

        var valueRenameByCalAndCat =
            new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in valueRenameRules)
        {
            var calId = rule.CalendarId.Trim();
            var catPrefix = NormalizeCategoryPrefixScope(rule.CategoryPrefix);
            var keySuffix = NormalizeRenameKey(rule.KeySuffix);
            var targetSuffix = rule.TargetSuffix.Trim();
            if (string.IsNullOrEmpty(calId) || string.IsNullOrEmpty(keySuffix) || string.IsNullOrEmpty(targetSuffix))
            {
                continue;
            }

            if (!valueRenameByCalAndCat.TryGetValue(calId, out var byCat))
            {
                byCat = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                valueRenameByCalAndCat[calId] = byCat;
            }
            if (!byCat.TryGetValue(catPrefix, out var bySuffix))
            {
                bySuffix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                byCat[catPrefix] = bySuffix;
            }
            bySuffix[keySuffix] = targetSuffix;
        }

        var mutations = new List<CalendarMutationCommand>();
        foreach (var candidate in candidates)
        {
            var command = PlanSingle(
                normalizedSourceCalendarId,
                normalizedSourceCalendarName,
                candidate.ExternalEventId,
                candidate.Title,
                convertByKey,
                renameByCalendar,
                valueRenameByCalAndCat);

            if (command != null)
            {
                mutations.Add(command);
            }
        }

        return mutations;
    }

    public async Task<CalendarConvertRule> UpsertConvertRuleAsync(
        UpsertConvertRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var keyPrefix = NormalizeConvertKey(request.KeyPrefix);
        var targetCalendarId = request.TargetCalendarId.Trim();

        if (string.IsNullOrEmpty(keyPrefix))
        {
            throw new ArgumentException("KeyPrefix must contain at least one non-whitespace character after optional '#'.");
        }

        var nowUtc = DateTime.UtcNow;
        var rule = await _db.CalendarConvertRules
            .SingleOrDefaultAsync(r => r.Account == account && r.KeyPrefix == keyPrefix, cancellationToken);

        if (rule == null)
        {
            rule = new CalendarConvertRule
            {
                Account = account,
                KeyPrefix = keyPrefix,
                TargetCalendarId = targetCalendarId,
                IsActive = request.IsActive,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.CalendarConvertRules.Add(rule);
        }
        else
        {
            rule.TargetCalendarId = targetCalendarId;
            rule.IsActive = request.IsActive;
            rule.UpdatedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<CalendarConvertRule?> UpdateConvertRuleAsync(
        int id,
        UpsertConvertRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var keyPrefix = NormalizeConvertKey(request.KeyPrefix);
        var targetCalendarId = request.TargetCalendarId.Trim();

        if (string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("Account must not be empty.");
        }
        if (string.IsNullOrEmpty(keyPrefix))
        {
            throw new ArgumentException("KeyPrefix must contain at least one non-whitespace character after optional '#'.");
        }
        if (string.IsNullOrEmpty(targetCalendarId))
        {
            throw new ArgumentException("TargetCalendarId must not be empty.");
        }

        var rule = await _db.CalendarConvertRules
            .SingleOrDefaultAsync(r => r.CalendarConvertRuleId == id, cancellationToken);

        if (rule == null)
        {
            return null;
        }

        var duplicate = await _db.CalendarConvertRules.AnyAsync(
            r => r.CalendarConvertRuleId != id && r.Account == account && r.KeyPrefix == keyPrefix,
            cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException($"Convert rule '#{keyPrefix}' already exists for account '{account}'.");
        }

        rule.Account = account;
        rule.KeyPrefix = keyPrefix;
        rule.TargetCalendarId = targetCalendarId;
        rule.IsActive = request.IsActive;
        rule.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<CalendarTitleRenameRule> UpsertTitleRenameRuleAsync(
        UpsertTitleRenameRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var keyPrefix = NormalizeRenameKey(request.KeyPrefix);
        var targetPrefix = request.TargetPrefix.Trim();

        if (string.IsNullOrEmpty(keyPrefix))
        {
            throw new ArgumentException("KeyPrefix must not be empty.");
        }

        if (string.IsNullOrEmpty(targetPrefix))
        {
            throw new ArgumentException("TargetPrefix must not be empty.");
        }

        var nowUtc = DateTime.UtcNow;
        var rule = await _db.CalendarTitleRenameRules
            .SingleOrDefaultAsync(
                r => r.Account == account && r.CalendarId == calendarId && r.KeyPrefix == keyPrefix,
                cancellationToken);

        if (rule == null)
        {
            rule = new CalendarTitleRenameRule
            {
                Account = account,
                CalendarId = calendarId,
                KeyPrefix = keyPrefix,
                TargetPrefix = targetPrefix,
                IsActive = request.IsActive,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.CalendarTitleRenameRules.Add(rule);
        }
        else
        {
            rule.TargetPrefix = targetPrefix;
            rule.IsActive = request.IsActive;
            rule.UpdatedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<CalendarTitleRenameRule?> UpdateTitleRenameRuleAsync(
        int id,
        UpsertTitleRenameRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var keyPrefix = NormalizeRenameKey(request.KeyPrefix);
        var targetPrefix = request.TargetPrefix.Trim();

        if (string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("Account must not be empty.");
        }
        if (string.IsNullOrEmpty(calendarId))
        {
            throw new ArgumentException("CalendarId must not be empty.");
        }
        if (string.IsNullOrEmpty(keyPrefix))
        {
            throw new ArgumentException("KeyPrefix must not be empty.");
        }
        if (string.IsNullOrEmpty(targetPrefix))
        {
            throw new ArgumentException("TargetPrefix must not be empty.");
        }

        var rule = await _db.CalendarTitleRenameRules
            .SingleOrDefaultAsync(r => r.CalendarTitleRenameRuleId == id, cancellationToken);

        if (rule == null)
        {
            return null;
        }

        var duplicate = await _db.CalendarTitleRenameRules.AnyAsync(
            r => r.CalendarTitleRenameRuleId != id
                && r.Account == account
                && r.CalendarId == calendarId
                && r.KeyPrefix == keyPrefix,
            cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException($"Title rename rule '{keyPrefix}' already exists for calendar '{calendarId}'.");
        }

        rule.Account = account;
        rule.CalendarId = calendarId;
        rule.KeyPrefix = keyPrefix;
        rule.TargetPrefix = targetPrefix;
        rule.IsActive = request.IsActive;
        rule.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<CalendarValueRenameRule> UpsertValueRenameRuleAsync(
        UpsertValueRenameRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var categoryPrefix = NormalizeCategoryPrefixScope(request.CategoryPrefix);
        var keySuffix = NormalizeRenameKey(request.KeySuffix);
        var targetSuffix = request.TargetSuffix.Trim();

        if (string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("Account must not be empty.");
        }
        if (string.IsNullOrEmpty(calendarId))
        {
            throw new ArgumentException("CalendarId must not be empty.");
        }
        if (string.IsNullOrEmpty(keySuffix))
        {
            throw new ArgumentException("KeySuffix must not be empty.");
        }
        if (string.IsNullOrEmpty(targetSuffix))
        {
            throw new ArgumentException("TargetSuffix must not be empty.");
        }

        var nowUtc = DateTime.UtcNow;
        var rule = await _db.CalendarValueRenameRules
            .SingleOrDefaultAsync(
                r => r.Account == account
                  && r.CalendarId == calendarId
                  && r.CategoryPrefix == categoryPrefix
                  && r.KeySuffix == keySuffix,
                cancellationToken);

        if (rule == null)
        {
            rule = new CalendarValueRenameRule
            {
                Account = account,
                CalendarId = calendarId,
                CategoryPrefix = categoryPrefix,
                KeySuffix = keySuffix,
                TargetSuffix = targetSuffix,
                IsActive = request.IsActive,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.CalendarValueRenameRules.Add(rule);
        }
        else
        {
            rule.TargetSuffix = targetSuffix;
            rule.IsActive = request.IsActive;
            rule.UpdatedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<CalendarValueRenameRule?> UpdateValueRenameRuleAsync(
        int id,
        UpsertValueRenameRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var categoryPrefix = NormalizeCategoryPrefixScope(request.CategoryPrefix);
        var keySuffix = NormalizeRenameKey(request.KeySuffix);
        var targetSuffix = request.TargetSuffix.Trim();

        if (string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("Account must not be empty.");
        }
        if (string.IsNullOrEmpty(calendarId))
        {
            throw new ArgumentException("CalendarId must not be empty.");
        }
        if (string.IsNullOrEmpty(keySuffix))
        {
            throw new ArgumentException("KeySuffix must not be empty.");
        }
        if (string.IsNullOrEmpty(targetSuffix))
        {
            throw new ArgumentException("TargetSuffix must not be empty.");
        }

        var rule = await _db.CalendarValueRenameRules
            .SingleOrDefaultAsync(r => r.CalendarValueRenameRuleId == id, cancellationToken);

        if (rule == null)
        {
            return null;
        }

        var duplicate = await _db.CalendarValueRenameRules.AnyAsync(
            r => r.CalendarValueRenameRuleId != id
                && r.Account == account
                && r.CalendarId == calendarId
                && r.CategoryPrefix == categoryPrefix
                && r.KeySuffix == keySuffix,
            cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Value rename rule '{keySuffix}' (prefix '{categoryPrefix}') already exists for calendar '{calendarId}'.");
        }

        rule.Account = account;
        rule.CalendarId = calendarId;
        rule.CategoryPrefix = categoryPrefix;
        rule.KeySuffix = keySuffix;
        rule.TargetSuffix = targetSuffix;
        rule.IsActive = request.IsActive;
        rule.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    private static CalendarMutationCommand? PlanSingle(
        string sourceCalendarId,
        string? sourceCalendarName,
        string externalEventId,
        string? title,
        IReadOnlyDictionary<string, string> convertByKey,
        IReadOnlyDictionary<string, Dictionary<string, string>> renameByCalendar,
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, string>>> valueRenameByCalAndCat)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // 1. Check "#<key> ..." calendar move/convert rules (replaces Conversion.js).
        if (title.StartsWith('#') && convertByKey.Count > 0)
        {
            var afterHash = title[1..];
            var whitespaceIndex = FindFirstWhitespace(afterHash);
            string rawKey;
            string strippedTitle;

            if (whitespaceIndex == -1)
            {
                rawKey = afterHash;
                strippedTitle = rawKey;
            }
            else
            {
                rawKey = afterHash[..whitespaceIndex];
                var rest = afterHash[(whitespaceIndex + 1)..].Trim();
                strippedTitle = string.IsNullOrEmpty(rest) ? rawKey : rest;
            }

            if (!string.IsNullOrEmpty(rawKey) && convertByKey.TryGetValue(rawKey, out var targetCalendarId))
            {
                // Chain target calendar's before-colon and after-colon rename rules.
                var finalTitle =
                    TryApplyTitleAndValueRename(
                        strippedTitle,
                        targetCalendarId,
                        null,
                        renameByCalendar,
                        valueRenameByCalAndCat)
                    ?? strippedTitle;

                if (!string.Equals(targetCalendarId, sourceCalendarId, StringComparison.Ordinal))
                {
                    return new CalendarMutationCommand
                    {
                        Action = CalendarMutationActions.Move,
                        SourceCalendarId = sourceCalendarId,
                        EventId = externalEventId,
                        TargetCalendarId = targetCalendarId,
                        NewTitle = finalTitle
                    };
                }

                if (!string.Equals(finalTitle, title, StringComparison.Ordinal))
                {
                    return new CalendarMutationCommand
                    {
                        Action = CalendarMutationActions.Rename,
                        SourceCalendarId = sourceCalendarId,
                        EventId = externalEventId,
                        NewTitle = finalTitle
                    };
                }

                return null;
            }
        }

        // 2. Check before-colon and after-colon title normalization rules.
        var renamedTitle = TryApplyTitleAndValueRename(
            title,
            sourceCalendarId,
            sourceCalendarName,
            renameByCalendar,
            valueRenameByCalAndCat);

        if (renamedTitle != null)
        {
            return new CalendarMutationCommand
            {
                Action = CalendarMutationActions.Rename,
                SourceCalendarId = sourceCalendarId,
                EventId = externalEventId,
                NewTitle = renamedTitle
            };
        }

        return null;
    }

    private static string? TryApplyTitleAndValueRename(
        string title,
        string calendarId,
        string? calendarName,
        IReadOnlyDictionary<string, Dictionary<string, string>> renameByCalendar,
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, string>>> valueRenameByCalAndCat)
    {
        var current = TryApplyTitleRename(title, calendarId, calendarName, renameByCalendar) ?? title;
        var afterValue = TryApplyValueRename(current, calendarId, calendarName, valueRenameByCalAndCat) ?? current;
        return string.Equals(afterValue, title, StringComparison.Ordinal) ? null : afterValue;
    }

    private static string? TryApplyValueRename(
        string title,
        string calendarId,
        string? calendarName,
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, string>>> valueRenameByCalAndCat)
    {
        if (valueRenameByCalAndCat.Count == 0)
        {
            return null;
        }

        var colonIndex = title.IndexOf(':');
        if (colonIndex == -1)
        {
            return null;
        }

        var rawPrefix = title[..colonIndex].Trim();
        var rawSuffix = title[(colonIndex + 1)..].Trim();
        if (string.IsNullOrEmpty(rawSuffix))
        {
            return null;
        }

        var targetSuffix =
            LookupValueTarget(valueRenameByCalAndCat, calendarId, rawPrefix, rawSuffix)
            ?? (!string.IsNullOrEmpty(calendarName)
                ? LookupValueTarget(valueRenameByCalAndCat, calendarName, rawPrefix, rawSuffix)
                : null)
            ?? LookupValueTarget(valueRenameByCalAndCat, WildcardCalendarId, rawPrefix, rawSuffix);

        if (targetSuffix == null)
        {
            return null;
        }

        var newTitle = string.IsNullOrEmpty(rawPrefix)
            ? $": {targetSuffix}"
            : $"{rawPrefix}: {targetSuffix}";

        return string.Equals(newTitle, title, StringComparison.Ordinal) ? null : newTitle;
    }

    private static string? LookupValueTarget(
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, string>>> valueRenameByCalAndCat,
        string calKey,
        string rawPrefix,
        string rawSuffix)
    {
        if (!valueRenameByCalAndCat.TryGetValue(calKey, out var byCat))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(rawPrefix)
            && byCat.TryGetValue(rawPrefix, out var specificMap)
            && specificMap.TryGetValue(rawSuffix, out var specificTarget))
        {
            return specificTarget;
        }

        if (byCat.TryGetValue(WildcardCalendarId, out var wildcardMap)
            && wildcardMap.TryGetValue(rawSuffix, out var wildcardTarget))
        {
            return wildcardTarget;
        }

        return null;
    }

    private static string? TryApplyTitleRename(
        string title,
        string calendarId,
        string? calendarName,
        IReadOnlyDictionary<string, Dictionary<string, string>> renameByCalendar)
    {
        if (renameByCalendar.Count == 0)
        {
            return null;
        }

        var colonIndex = title.IndexOf(':');
        var rawKey = colonIndex == -1
            ? title.Trim()
            : title[..colonIndex].Trim();

        if (string.IsNullOrEmpty(rawKey))
        {
            return null;
        }

        string? targetPrefix = null;
        if (renameByCalendar.TryGetValue(calendarId, out var specificRules)
            && specificRules.TryGetValue(rawKey, out var specificTarget))
        {
            targetPrefix = specificTarget;
        }
        else if (!string.IsNullOrEmpty(calendarName)
            && renameByCalendar.TryGetValue(calendarName, out var namedRules)
            && namedRules.TryGetValue(rawKey, out var namedTarget))
        {
            targetPrefix = namedTarget;
        }
        else if (renameByCalendar.TryGetValue(WildcardCalendarId, out var wildcardRules)
            && wildcardRules.TryGetValue(rawKey, out var wildcardTarget))
        {
            targetPrefix = wildcardTarget;
        }

        if (targetPrefix == null)
        {
            return null;
        }

        var newTitle = colonIndex == -1
            ? targetPrefix
            : targetPrefix + title[colonIndex..];

        return string.Equals(newTitle, title, StringComparison.Ordinal)
            ? null
            : newTitle;
    }

    private static int FindFirstWhitespace(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                return i;
            }
        }
        return -1;
    }

    public static string NormalizeConvertKey(string keyPrefix) =>
        keyPrefix.Trim().TrimStart('#').Trim().ToLowerInvariant();

    public static string NormalizeRenameKey(string keyPrefix) =>
        keyPrefix.Trim().ToLowerInvariant();

    public static string NormalizeCategoryPrefixScope(string? categoryPrefix)
    {
        var trimmed = categoryPrefix?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed == WildcardCalendarId
            ? WildcardCalendarId
            : trimmed.ToLowerInvariant();
    }
}

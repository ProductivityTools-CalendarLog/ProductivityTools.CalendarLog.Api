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
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = account.Trim();
        var normalizedSourceCalendarId = sourceCalendarId.Trim();

        var convertRules = await _db.CalendarConvertRules
            .AsNoTracking()
            .Where(r => r.Account == normalizedAccount && r.IsActive)
            .ToListAsync(cancellationToken);

        var renameRules = await _db.CalendarTitleRenameRules
            .AsNoTracking()
            .Where(r => r.Account == normalizedAccount && r.IsActive)
            .ToListAsync(cancellationToken);

        if (convertRules.Count == 0 && renameRules.Count == 0)
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

        var renameByCalendar = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
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

        var mutations = new List<CalendarMutationCommand>();
        foreach (var candidate in candidates)
        {
            var command = PlanSingle(
                normalizedSourceCalendarId,
                candidate.ExternalEventId,
                candidate.Title,
                convertByKey,
                renameByCalendar);

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

    private static CalendarMutationCommand? PlanSingle(
        string sourceCalendarId,
        string externalEventId,
        string? title,
        IReadOnlyDictionary<string, string> convertByKey,
        IReadOnlyDictionary<string, Dictionary<string, string>> renameByCalendar)
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
                // Chain target calendar's title rename rules so "#DL sd: reading" moves directly as "SelfDevelopment: reading".
                var finalTitle = TryApplyTitleRename(strippedTitle, targetCalendarId, renameByCalendar) ?? strippedTitle;

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

        // 2. Check prefix-before-colon title normalization rules (replaces Analysis.js ReplaceTitleWithCategory).
        var renamedTitle = TryApplyTitleRename(title, sourceCalendarId, renameByCalendar);
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

    private static string? TryApplyTitleRename(
        string title,
        string calendarId,
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
}

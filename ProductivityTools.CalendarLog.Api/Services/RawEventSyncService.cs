using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Database.Entities;
using ProductivityTools.CalendarLog.Api.Dtos;

namespace ProductivityTools.CalendarLog.Api.Services;

public sealed class RawEventSyncService
{
    private const int LookupChunkSize = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CalendarLogContext _db;
    private readonly CalendarMutationPlanner _mutationPlanner;
    private readonly int _defaultLookbackDays;
    private readonly TimeZoneInfo _timeZone;

    public RawEventSyncService(
        CalendarLogContext db,
        IConfiguration configuration,
        CalendarMutationPlanner mutationPlanner)
    {
        _db = db;
        _mutationPlanner = mutationPlanner;
        _defaultLookbackDays = Math.Max(1, configuration.GetValue<int?>("Sync:DefaultLookbackDays") ?? 14);
        var tzId = configuration["Sync:TimeZone"] ?? "Europe/Warsaw";
        _timeZone = ResolveTimeZone(tzId);
    }

    public async Task<SyncPlanResponse> GetSyncPlanAsync(
        string account,
        DateOnly? todayOverride = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = account.Trim();
        var today = todayOverride ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        var states = await _db.CalendarSyncStates
            .AsNoTracking()
            .Where(s => s.Account == normalizedAccount && s.IsActive)
            .OrderBy(s => s.CalendarId)
            .ToListAsync(cancellationToken);

        var calendarItems = new List<CalendarSyncPlanItem>(states.Count);
        DateOnly? overallStart = null;

        foreach (var state in states)
        {
            var lookbackDays = state.DefaultLookbackDays > 0 ? state.DefaultLookbackDays : _defaultLookbackDays;
            var start = today.AddDays(-lookbackDays);

            if (state.LastSyncedDate.HasValue)
            {
                var catchupStart = state.LastSyncedDate.Value.AddDays(-1);
                if (catchupStart < start)
                {
                    start = catchupStart;
                }
            }

            if (state.PendingBackfillFrom.HasValue && state.PendingBackfillFrom.Value < start)
            {
                start = state.PendingBackfillFrom.Value;
            }

            if (overallStart == null || start < overallStart.Value)
            {
                overallStart = start;
            }

            calendarItems.Add(new CalendarSyncPlanItem
            {
                CalendarId = state.CalendarId,
                CalendarName = state.CalendarName,
                StartDate = start,
                EndDate = today,
                LastSyncedDate = state.LastSyncedDate,
                PendingBackfillFrom = state.PendingBackfillFrom
            });
        }

        return new SyncPlanResponse
        {
            Account = normalizedAccount,
            StartDate = overallStart ?? today.AddDays(-_defaultLookbackDays),
            EndDate = today,
            DefaultLookbackDays = _defaultLookbackDays,
            Calendars = calendarItems
        };
    }

    public async Task<RawEventsSyncResult> IngestRawEventsAsync(
        RawEventsBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var calendarName = request.CalendarName.Trim();

        if (request.RangeEnd < request.RangeStart)
        {
            throw new ArgumentException("RangeEnd must be greater than or equal to RangeStart.");
        }

        var nowUtc = DateTime.UtcNow;

        // Parse raw Google Calendar v3 event objects on the server, deduplicate by ExternalEventId,
        // skip cancelled events and multi-day events starting outside [RangeStart, RangeEnd].
        var incomingById = new Dictionary<string, ParsedGoogleEvent>(StringComparer.Ordinal);
        foreach (var rawElement in request.Events)
        {
            var parsed = TryParseGoogleEvent(rawElement);
            if (parsed == null)
            {
                continue;
            }

            if (string.Equals(parsed.EventStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (parsed.StartTime < request.RangeStart || parsed.StartTime > request.RangeEnd)
            {
                continue;
            }

            incomingById[parsed.ExternalEventId] = parsed;
        }

        var useTransaction = _db.Database.IsRelational();
        await using var transaction = useTransaction
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // 1. Load existing events inside the synced window for this (Account, CalendarId).
        var existingInRange = await _db.RawCalendarEvents
            .Where(e => e.Account == account
                && e.CalendarId == calendarId
                && e.StartTime >= request.RangeStart
                && e.StartTime <= request.RangeEnd)
            .ToListAsync(cancellationToken);

        var existingById = existingInRange.ToDictionary(e => e.ExternalEventId, StringComparer.Ordinal);

        // 2. Also load any incoming ExternalEventIds that already exist outside this window
        //    (e.g. an event moved from an earlier date into the current sync window).
        var missingIds = incomingById.Keys
            .Where(id => !existingById.ContainsKey(id))
            .ToList();

        foreach (var chunk in missingIds.Chunk(LookupChunkSize))
        {
            var outsideMatches = await _db.RawCalendarEvents
                .Where(e => e.Account == account
                    && e.CalendarId == calendarId
                    && chunk.Contains(e.ExternalEventId))
                .ToListAsync(cancellationToken);

            foreach (var match in outsideMatches)
            {
                existingById[match.ExternalEventId] = match;
            }
        }

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;
        var softDeleted = 0;

        // 3. Upsert incoming events.
        foreach (var (externalEventId, item) in incomingById)
        {
            if (!existingById.TryGetValue(externalEventId, out var existing))
            {
                _db.RawCalendarEvents.Add(new RawCalendarEvent
                {
                    Account = account,
                    CalendarId = calendarId,
                    CalendarName = calendarName,
                    ExternalEventId = externalEventId,
                    ICalUid = item.ICalUid,
                    StartTime = item.StartTime,
                    EndTime = item.EndTime,
                    IsAllDay = item.IsAllDay,
                    IsRecurring = item.IsRecurring,
                    Title = item.Title,
                    Description = item.Description,
                    Location = item.Location,
                    MyStatus = item.MyStatus,
                    EventStatus = item.EventStatus,
                    EventType = item.EventType,
                    Color = item.Color,
                    IsOwnedByMe = item.IsOwnedByMe,
                    GuestCount = item.GuestCount,
                    GuestsJson = item.GuestsJson,
                    EventCreatedAt = item.EventCreatedAt,
                    EventUpdatedAt = item.EventUpdatedAt,
                    RawJson = item.RawJson,
                    IsDeleted = false,
                    ProcessingStatus = RawEventProcessingStatus.New,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
                inserted++;
                continue;
            }

            if (HasChanged(existing, calendarName, item))
            {
                existing.CalendarName = calendarName;
                existing.ICalUid = item.ICalUid;
                existing.StartTime = item.StartTime;
                existing.EndTime = item.EndTime;
                existing.IsAllDay = item.IsAllDay;
                existing.IsRecurring = item.IsRecurring;
                existing.Title = item.Title;
                existing.Description = item.Description;
                existing.Location = item.Location;
                existing.MyStatus = item.MyStatus;
                existing.EventStatus = item.EventStatus;
                existing.EventType = item.EventType;
                existing.Color = item.Color;
                existing.IsOwnedByMe = item.IsOwnedByMe;
                existing.GuestCount = item.GuestCount;
                existing.GuestsJson = item.GuestsJson;
                existing.EventCreatedAt = item.EventCreatedAt;
                existing.EventUpdatedAt = item.EventUpdatedAt;
                existing.RawJson = item.RawJson;
                existing.IsDeleted = false;
                existing.ProcessingStatus = RawEventProcessingStatus.New;
                existing.ErrorMessage = null;
                existing.ProcessedAtUtc = null;
                existing.UpdatedAtUtc = nowUtc;
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        // 4. Soft-delete events in this window that are no longer present in Google Calendar.
        foreach (var existing in existingInRange)
        {
            if (!existing.IsDeleted && !incomingById.ContainsKey(existing.ExternalEventId))
            {
                existing.IsDeleted = true;
                existing.ProcessingStatus = RawEventProcessingStatus.New;
                existing.ErrorMessage = null;
                existing.ProcessedAtUtc = null;
                existing.UpdatedAtUtc = nowUtc;
                softDeleted++;
            }
        }

        // 5. Update per-calendar sync state and clear PendingBackfillFrom if covered by this range.
        var rangeStartDate = ToLocalDate(request.RangeStart);
        var rangeEndDate = ToLocalDate(request.RangeEnd);

        var syncState = await _db.CalendarSyncStates
            .SingleOrDefaultAsync(s => s.Account == account && s.CalendarId == calendarId, cancellationToken);

        if (syncState == null)
        {
            syncState = new CalendarSyncState
            {
                Account = account,
                CalendarId = calendarId,
                CalendarName = calendarName,
                DefaultLookbackDays = _defaultLookbackDays,
                LastSyncedDate = rangeEndDate,
                LastSyncedAtUtc = nowUtc,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.CalendarSyncStates.Add(syncState);
        }
        else
        {
            syncState.CalendarName = calendarName;
            syncState.LastSyncedAtUtc = nowUtc;
            syncState.UpdatedAtUtc = nowUtc;

            if (!syncState.LastSyncedDate.HasValue || rangeEndDate > syncState.LastSyncedDate.Value)
            {
                syncState.LastSyncedDate = rangeEndDate;
            }

            if (syncState.PendingBackfillFrom.HasValue && rangeStartDate <= syncState.PendingBackfillFrom.Value)
            {
                syncState.PendingBackfillFrom = null;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        // 6. Compute server-driven calendar mutation commands (MOVE / RENAME) for Apps Script to execute.
        var mutationCandidates = incomingById.Values
            .OrderBy(e => e.StartTime)
            .ThenBy(e => e.ExternalEventId, StringComparer.Ordinal)
            .Select(e => new EventMutationCandidate(e.ExternalEventId, e.Title));

        var mutations = await _mutationPlanner.PlanMutationsAsync(
            account,
            calendarId,
            mutationCandidates,
            cancellationToken);

        return new RawEventsSyncResult
        {
            Account = account,
            CalendarId = calendarId,
            RangeStart = request.RangeStart,
            RangeEnd = request.RangeEnd,
            TotalReceived = incomingById.Count,
            Inserted = inserted,
            Updated = updated,
            Unchanged = unchanged,
            SoftDeleted = softDeleted,
            Mutations = mutations
        };
    }

    public async Task<int> RequestBackfillAsync(
        BackfillRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var nowUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.CalendarId))
        {
            var calendarId = request.CalendarId.Trim();
            var state = await _db.CalendarSyncStates
                .SingleOrDefaultAsync(s => s.Account == account && s.CalendarId == calendarId, cancellationToken);

            if (state == null)
            {
                _db.CalendarSyncStates.Add(new CalendarSyncState
                {
                    Account = account,
                    CalendarId = calendarId,
                    DefaultLookbackDays = _defaultLookbackDays,
                    PendingBackfillFrom = request.FromDate,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
            }
            else
            {
                state.PendingBackfillFrom = request.FromDate;
                state.UpdatedAtUtc = nowUtc;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return 1;
        }

        var states = await _db.CalendarSyncStates
            .Where(s => s.Account == account)
            .ToListAsync(cancellationToken);

        foreach (var state in states)
        {
            state.PendingBackfillFrom = request.FromDate;
            state.UpdatedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return states.Count;
    }

    public async Task<CalendarSyncState> UpsertCalendarAsync(
        UpsertSyncCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var nowUtc = DateTime.UtcNow;

        var state = await _db.CalendarSyncStates
            .SingleOrDefaultAsync(s => s.Account == account && s.CalendarId == calendarId, cancellationToken);

        if (state == null)
        {
            state = new CalendarSyncState
            {
                Account = account,
                CalendarId = calendarId,
                CalendarName = request.CalendarName?.Trim(),
                IsActive = request.IsActive,
                DefaultLookbackDays = request.DefaultLookbackDays is > 0 ? request.DefaultLookbackDays.Value : _defaultLookbackDays,
                PendingBackfillFrom = request.PendingBackfillFrom,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.CalendarSyncStates.Add(state);
        }
        else
        {
            if (request.CalendarName != null)
            {
                state.CalendarName = request.CalendarName.Trim();
            }
            state.IsActive = request.IsActive;
            if (request.DefaultLookbackDays is > 0)
            {
                state.DefaultLookbackDays = request.DefaultLookbackDays.Value;
            }
            if (request.PendingBackfillFrom.HasValue)
            {
                state.PendingBackfillFrom = request.PendingBackfillFrom;
            }
            state.UpdatedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return state;
    }

    private ParsedGoogleEvent? TryParseGoogleEvent(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetString(raw, "id")?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (!raw.TryGetProperty("start", out var startProp) || !raw.TryGetProperty("end", out var endProp))
        {
            return null;
        }

        var startParsed = TryParseGoogleDateTime(startProp);
        var endParsed = TryParseGoogleDateTime(endProp);
        if (startParsed == null || endParsed == null)
        {
            return null;
        }

        var isOwnedByMe = raw.TryGetProperty("organizer", out var organizer)
            && organizer.ValueKind == JsonValueKind.Object
            && organizer.TryGetProperty("self", out var orgSelf)
            && orgSelf.ValueKind == JsonValueKind.True;

        var guests = new List<ParsedGuest>();
        string? selfResponseStatus = null;

        if (raw.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array)
        {
            foreach (var attendee in attendees.EnumerateArray())
            {
                if (attendee.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var email = GetString(attendee, "email");
                var responseStatus = GetString(attendee, "responseStatus");
                var isSelf = attendee.TryGetProperty("self", out var selfProp) && selfProp.ValueKind == JsonValueKind.True;

                if (isSelf)
                {
                    selfResponseStatus = responseStatus;
                }

                guests.Add(new ParsedGuest(email, responseStatus, isSelf));
            }
        }

        string? myStatus = selfResponseStatus ?? (isOwnedByMe ? "organizer" : null);
        string? guestsJson = guests.Count > 0
            ? JsonSerializer.Serialize(
                guests.OrderBy(g => g.Email ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(g => g.ResponseStatus ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                JsonOptions)
            : null;

        return new ParsedGoogleEvent(
            ExternalEventId: id,
            ICalUid: GetString(raw, "iCalUID"),
            StartTime: startParsed.Value.Value,
            EndTime: endParsed.Value.Value,
            IsAllDay: startParsed.Value.IsAllDay,
            IsRecurring: !string.IsNullOrWhiteSpace(GetString(raw, "recurringEventId")),
            Title: GetString(raw, "summary"),
            Description: GetString(raw, "description"),
            Location: GetString(raw, "location"),
            MyStatus: myStatus,
            EventStatus: GetString(raw, "status"),
            EventType: GetString(raw, "eventType"),
            Color: GetString(raw, "colorId"),
            IsOwnedByMe: isOwnedByMe,
            GuestCount: guests.Count,
            GuestsJson: guestsJson,
            EventCreatedAt: ParseOptionalDateTimeOffset(GetString(raw, "created")),
            EventUpdatedAt: ParseOptionalDateTimeOffset(GetString(raw, "updated")),
            RawJson: raw.GetRawText());
    }

    private (DateTimeOffset Value, bool IsAllDay)? TryParseGoogleDateTime(JsonElement timeElement)
    {
        if (timeElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dateTimeStr = GetString(timeElement, "dateTime");
        if (!string.IsNullOrWhiteSpace(dateTimeStr)
            && DateTimeOffset.TryParse(dateTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return (dto, false);
        }

        var dateStr = GetString(timeElement, "date");
        if (!string.IsNullOrWhiteSpace(dateStr)
            && DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            var localMidnight = dateOnly.ToDateTime(TimeOnly.MinValue);
            var offset = _timeZone.GetUtcOffset(localMidnight);
            return (new DateTimeOffset(localMidnight, offset), true);
        }

        return null;
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static DateTimeOffset? ParseOptionalDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
    }

    private DateOnly ToLocalDate(DateTimeOffset dto)
    {
        var local = TimeZoneInfo.ConvertTime(dto, _timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static bool HasChanged(RawCalendarEvent existing, string calendarName, ParsedGoogleEvent incoming)
    {
        return existing.IsDeleted
            || existing.CalendarName != calendarName
            || existing.ICalUid != incoming.ICalUid
            || existing.StartTime != incoming.StartTime
            || existing.EndTime != incoming.EndTime
            || existing.IsAllDay != incoming.IsAllDay
            || existing.IsRecurring != incoming.IsRecurring
            || existing.Title != incoming.Title
            || existing.Description != incoming.Description
            || existing.Location != incoming.Location
            || existing.MyStatus != incoming.MyStatus
            || existing.EventStatus != incoming.EventStatus
            || existing.EventType != incoming.EventType
            || existing.Color != incoming.Color
            || existing.IsOwnedByMe != incoming.IsOwnedByMe
            || existing.GuestCount != incoming.GuestCount
            || existing.GuestsJson != incoming.GuestsJson
            || existing.EventCreatedAt != incoming.EventCreatedAt
            || existing.EventUpdatedAt != incoming.EventUpdatedAt
            || existing.RawJson != incoming.RawJson;
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private sealed record ParsedGuest(string? Email, string? ResponseStatus, bool IsSelf);

    private sealed record ParsedGoogleEvent(
        string ExternalEventId,
        string? ICalUid,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        bool IsAllDay,
        bool IsRecurring,
        string? Title,
        string? Description,
        string? Location,
        string? MyStatus,
        string? EventStatus,
        string? EventType,
        string? Color,
        bool IsOwnedByMe,
        int GuestCount,
        string? GuestsJson,
        DateTimeOffset? EventCreatedAt,
        DateTimeOffset? EventUpdatedAt,
        string RawJson);
}

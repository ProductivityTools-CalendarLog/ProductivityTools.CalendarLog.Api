using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProductivityTools.CalendarLog.Api.Controllers;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Database.Entities;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Security;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Tests;

[TestFixture]
public class RawEventSyncServiceTests
{
    private CalendarLogContext _db = null!;
    private CalendarMutationPlanner _mutationPlanner = null!;
    private RawEventSyncService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<CalendarLogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new CalendarLogContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sync:DefaultLookbackDays"] = "14",
                ["Sync:TimeZone"] = "Europe/Warsaw"
            })
            .Build();

        _mutationPlanner = new CalendarMutationPlanner(_db);
        _service = new RawEventSyncService(_db, config, _mutationPlanner);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    [Test]
    public async Task GetSyncPlan_NoStateYet_ReturnsDefaultLookbackStartDate()
    {
        var today = new DateOnly(2026, 11, 16);

        var plan = await _service.GetSyncPlanAsync("pwujczyk@google.com", today);

        Assert.That(plan.Account, Is.EqualTo("pwujczyk@google.com"));
        Assert.That(plan.StartDate, Is.EqualTo(new DateOnly(2026, 11, 2)));
        Assert.That(plan.EndDate, Is.EqualTo(today));
        Assert.That(plan.Calendars, Is.Empty);
    }

    [Test]
    public async Task GetSyncPlan_WithDowntimeGap_WidensStartDateToCatchUp()
    {
        _db.CalendarSyncStates.Add(new CalendarSyncState
        {
            Account = "pwujczyk@google.com",
            CalendarId = "cal-1",
            DefaultLookbackDays = 14,
            LastSyncedDate = new DateOnly(2026, 10, 10)
        });
        await _db.SaveChangesAsync();

        var plan = await _service.GetSyncPlanAsync("pwujczyk@google.com", new DateOnly(2026, 11, 16));

        Assert.That(plan.StartDate, Is.EqualTo(new DateOnly(2026, 10, 9)));
        Assert.That(plan.Calendars, Has.Count.EqualTo(1));
        Assert.That(plan.Calendars[0].StartDate, Is.EqualTo(new DateOnly(2026, 10, 9)));
    }

    [Test]
    public async Task RequestBackfill_UpdatesPlan_AndIsClearedAfterCoveringIngest()
    {
        var today = new DateOnly(2026, 11, 16);

        await _service.RequestBackfillAsync(new BackfillRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "cal-1",
            FromDate = new DateOnly(2026, 8, 1)
        });

        var planBefore = await _service.GetSyncPlanAsync("pwujczyk@google.com", today);
        Assert.That(planBefore.StartDate, Is.EqualTo(new DateOnly(2026, 8, 1)));

        await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "cal-1",
            CalendarName = "DailyLog",
            RangeStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            RangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1)),
            Events = new()
        });

        var planAfter = await _service.GetSyncPlanAsync("pwujczyk@google.com", today);
        Assert.That(planAfter.StartDate, Is.EqualTo(new DateOnly(2026, 11, 2)));
        Assert.That(planAfter.Calendars[0].PendingBackfillFrom, Is.Null);
        Assert.That(planAfter.Calendars[0].LastSyncedDate, Is.EqualTo(today));
    }

    [Test]
    public async Task IngestRawEvents_HandlesInsertUnchangedUpdateSoftDeleteAndMovedEvents()
    {
        var rangeStart = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(1));
        var rangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1));

        // 1. Initial insert
        var first = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "pwujczyk@google.com",
            CalendarName = "pwujczyk@google.com",
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            Events = new()
            {
                MakeGoogleEvent("evt-1", "Sync A", new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-2", "Sync B", new DateTimeOffset(2026, 11, 10, 11, 0, 0, TimeSpan.FromHours(1)))
            }
        });

        Assert.That(first.Inserted, Is.EqualTo(2));
        Assert.That(first.Updated, Is.EqualTo(0));
        Assert.That(first.SoftDeleted, Is.EqualTo(0));

        // Mark both as processed to verify unchanged events keep PROCESSED while changed/deleted reset to NEW.
        foreach (var e in await _db.RawCalendarEvents.ToListAsync())
        {
            e.ProcessingStatus = RawEventProcessingStatus.Processed;
        }
        await _db.SaveChangesAsync();

        // Add an older event outside the window that will later be moved into the window.
        _db.RawCalendarEvents.Add(new RawCalendarEvent
        {
            Account = "pwujczyk@google.com",
            CalendarId = "pwujczyk@google.com",
            CalendarName = "pwujczyk@google.com",
            ExternalEventId = "evt-moved",
            StartTime = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(2)),
            EndTime = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.FromHours(2)),
            Title = "Old date",
            ProcessingStatus = RawEventProcessingStatus.Processed,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // 2. Re-sync: evt-1 unchanged, evt-2 removed (soft-deleted), evt-3 added, evt-moved rescheduled into window.
        var second = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "pwujczyk@google.com",
            CalendarName = "pwujczyk@google.com",
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            Events = new()
            {
                MakeGoogleEvent("evt-1", "Sync A", new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-3", "Retroactive event from last week", new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-moved", "Moved into window", new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(1)))
            }
        });

        Assert.That(second.Unchanged, Is.EqualTo(1));
        Assert.That(second.Inserted, Is.EqualTo(1));
        Assert.That(second.Updated, Is.EqualTo(1));
        Assert.That(second.SoftDeleted, Is.EqualTo(1));

        var all = await _db.RawCalendarEvents.ToDictionaryAsync(e => e.ExternalEventId);
        Assert.That(all["evt-1"].ProcessingStatus, Is.EqualTo(RawEventProcessingStatus.Processed));
        Assert.That(all["evt-2"].IsDeleted, Is.True);
        Assert.That(all["evt-2"].ProcessingStatus, Is.EqualTo(RawEventProcessingStatus.New));
        Assert.That(all["evt-3"].ProcessingStatus, Is.EqualTo(RawEventProcessingStatus.New));
        Assert.That(all["evt-moved"].Title, Is.EqualTo("Moved into window"));
        Assert.That(all["evt-moved"].ProcessingStatus, Is.EqualTo(RawEventProcessingStatus.New));
    }

    [Test]
    public async Task IngestRawEvents_ParsesRawGoogleV3AllDayAndFiltersOverlapAndCancelled()
    {
        var rangeStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(1));
        var rangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1));

        var allDay = JsonSerializer.SerializeToElement(new
        {
            id = "evt-allday",
            iCalUID = "evt-allday@google.com",
            status = "confirmed",
            eventType = "outOfOffice",
            summary = "Vacation",
            start = new { date = "2026-11-12" },
            end = new { date = "2026-11-13" },
            organizer = new { self = true }
        });

        var startedBeforeRange = MakeGoogleEvent("evt-before", "Multi-day earlier",
            new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(1)));

        var cancelled = JsonSerializer.SerializeToElement(new
        {
            id = "evt-cancelled",
            status = "cancelled",
            start = new { dateTime = "2026-11-11T10:00:00+01:00" },
            end = new { dateTime = "2026-11-11T11:00:00+01:00" }
        });

        var result = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "pwujczyk@google.com",
            CalendarName = "pwujczyk@google.com",
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            Events = new() { allDay, startedBeforeRange, cancelled }
        });

        Assert.That(result.TotalReceived, Is.EqualTo(1));
        Assert.That(result.Inserted, Is.EqualTo(1));

        var stored = await _db.RawCalendarEvents.SingleAsync();
        Assert.That(stored.ExternalEventId, Is.EqualTo("evt-allday"));
        Assert.That(stored.IsAllDay, Is.True);
        Assert.That(stored.EventType, Is.EqualTo("outOfOffice"));
        Assert.That(stored.MyStatus, Is.EqualTo("organizer"));
        Assert.That(stored.StartTime, Is.EqualTo(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.FromHours(1))));
        Assert.That(stored.RawJson, Does.Contain("outOfOffice"));
    }

    [Test]
    public async Task IngestRawEvents_IsolatesAccounts()
    {
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));
        var end = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1));

        await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "shared-id",
            CalendarName = "Work",
            RangeStart = start,
            RangeEnd = end,
            Events = new() { MakeGoogleEvent("same-id", "Work event", start) }
        });

        await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@gmail.com",
            CalendarId = "shared-id",
            CalendarName = "Personal",
            RangeStart = start,
            RangeEnd = end,
            Events = new() { MakeGoogleEvent("same-id", "Personal event", start) }
        });

        var count = await _db.RawCalendarEvents.CountAsync();
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task AllowedEmailsHandler_EnforcesVerifiedAllowList()
    {
        var handler = new AllowedEmailsHandler();
        var requirement = new AllowedEmailsRequirement(new[] { "pwujczyk@gmail.com" });

        var allowedUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("email", "PWUJCZYK@gmail.com"),
            new Claim("email_verified", "true")
        }, "Bearer"));

        var unverifiedUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("email", "pwujczyk@gmail.com"),
            new Claim("email_verified", "false")
        }, "Bearer"));

        var otherUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("email", "someone@example.com"),
            new Claim("email_verified", "true")
        }, "Bearer"));

        var ctxAllowed = new AuthorizationHandlerContext(new[] { requirement }, allowedUser, null);
        await handler.HandleAsync(ctxAllowed);
        Assert.That(ctxAllowed.HasSucceeded, Is.True);

        var ctxUnverified = new AuthorizationHandlerContext(new[] { requirement }, unverifiedUser, null);
        await handler.HandleAsync(ctxUnverified);
        Assert.That(ctxUnverified.HasSucceeded, Is.False);

        var ctxOther = new AuthorizationHandlerContext(new[] { requirement }, otherUser, null);
        await handler.HandleAsync(ctxOther);
        Assert.That(ctxOther.HasSucceeded, Is.False);
    }

    [Test]
    public async Task UpsertCalendar_ControlsActiveCalendarsReturnedInPlan()
    {
        var today = new DateOnly(2026, 11, 16);

        await _service.UpsertCalendarAsync(new UpsertSyncCalendarRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "cal-active",
            CalendarName = "DailyLog",
            IsActive = true,
            DefaultLookbackDays = 21
        });
        await _service.UpsertCalendarAsync(new UpsertSyncCalendarRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "cal-inactive",
            CalendarName = "Archived",
            IsActive = false
        });

        var plan = await _service.GetSyncPlanAsync("pwujczyk@google.com", today);

        Assert.That(plan.Calendars, Has.Count.EqualTo(1));
        Assert.That(plan.Calendars[0].CalendarId, Is.EqualTo("cal-active"));
        Assert.That(plan.Calendars[0].StartDate, Is.EqualTo(new DateOnly(2026, 10, 26)));
    }

    [Test]
    public void DbUpAssembly_EmbedsRawEventsMigrationScript()
    {
        var dbUpAssembly = System.Reflection.Assembly.Load("ProductivityTools.CalendarLog.Api.DbUp");
        var resources = dbUpAssembly.GetManifestResourceNames();

        Assert.That(resources, Has.Some.EndsWith("Script0001-CreateRawCalendarEventsTables.sql"));
        Assert.That(resources, Has.Some.EndsWith("Script0002-CreateCalendarMutationRuleTables.sql"));
    }

    [Test]
    public async Task IngestRawEvents_EmitsMoveCommandsForHashPrefixRules_AndChainsTargetTitleRename()
    {
        var account = "pwujczyk@google.com";
        var primaryCal = "pwujczyk@google.com";
        var dailyLogCal = "dailylog-cal-id@group.calendar.google.com";

        await _mutationPlanner.UpsertConvertRuleAsync(new UpsertConvertRuleRequest
        {
            Account = account,
            KeyPrefix = "#DL",
            TargetCalendarId = dailyLogCal,
            IsActive = true
        });
        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            KeyPrefix = "sd",
            TargetPrefix = "SelfDevelopment",
            IsActive = true
        });

        var rangeStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(1));
        var rangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1));

        var result = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = primaryCal,
            CalendarName = primaryCal,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            Events = new()
            {
                MakeGoogleEvent("evt-1", "#DL Transfer", new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-2", "#DL", new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-3", "#dl sd: czytanie", new DateTimeOffset(2026, 11, 11, 11, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-4", "#UNKNOWN Keep me", new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-5", "Regular meeting", new DateTimeOffset(2026, 11, 11, 13, 0, 0, TimeSpan.FromHours(1)))
            }
        });

        Assert.That(result.Mutations, Has.Count.EqualTo(3));

        Assert.That(result.Mutations[0].Action, Is.EqualTo(CalendarMutationActions.Move));
        Assert.That(result.Mutations[0].EventId, Is.EqualTo("evt-1"));
        Assert.That(result.Mutations[0].SourceCalendarId, Is.EqualTo(primaryCal));
        Assert.That(result.Mutations[0].TargetCalendarId, Is.EqualTo(dailyLogCal));
        Assert.That(result.Mutations[0].NewTitle, Is.EqualTo("Transfer"));

        Assert.That(result.Mutations[1].Action, Is.EqualTo(CalendarMutationActions.Move));
        Assert.That(result.Mutations[1].EventId, Is.EqualTo("evt-2"));
        Assert.That(result.Mutations[1].TargetCalendarId, Is.EqualTo(dailyLogCal));
        Assert.That(result.Mutations[1].NewTitle, Is.EqualTo("DL"));

        Assert.That(result.Mutations[2].Action, Is.EqualTo(CalendarMutationActions.Move));
        Assert.That(result.Mutations[2].EventId, Is.EqualTo("evt-3"));
        Assert.That(result.Mutations[2].TargetCalendarId, Is.EqualTo(dailyLogCal));
        Assert.That(result.Mutations[2].NewTitle, Is.EqualTo("SelfDevelopment: czytanie"));
    }

    [Test]
    public async Task IngestRawEvents_HashPrefixAlreadyInTargetCalendar_EmitsRenameInsteadOfMove()
    {
        var account = "pwujczyk@google.com";
        var dailyLogCal = "dailylog-cal-id@group.calendar.google.com";

        await _mutationPlanner.UpsertConvertRuleAsync(new UpsertConvertRuleRequest
        {
            Account = account,
            KeyPrefix = "dl",
            TargetCalendarId = dailyLogCal,
            IsActive = true
        });

        var result = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            CalendarName = "DailyLog",
            RangeStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(1)),
            RangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1)),
            Events = new()
            {
                MakeGoogleEvent("evt-1", "#DL Spacer", new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(1)))
            }
        });

        Assert.That(result.Mutations, Has.Count.EqualTo(1));
        Assert.That(result.Mutations[0].Action, Is.EqualTo(CalendarMutationActions.Rename));
        Assert.That(result.Mutations[0].TargetCalendarId, Is.Null);
        Assert.That(result.Mutations[0].NewTitle, Is.EqualTo("Spacer"));
    }

    [Test]
    public async Task IngestRawEvents_TitleRenameRules_PreserveColonSuffix_AreIdempotent_AndRespectCalendarAndAccount()
    {
        var account = "pwujczyk@google.com";
        var dailyLogCal = "dailylog-cal-id@group.calendar.google.com";

        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            KeyPrefix = "sd",
            TargetPrefix = "SelfDevelopment"
        });
        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            KeyPrefix = "selfdevelopment",
            TargetPrefix = "SelfDevelopment"
        });
        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            KeyPrefix = "ipw",
            TargetPrefix = "IndividualProjectWork"
        });
        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            KeyPrefix = "ho",
            TargetPrefix = "Hanging-out"
        });
        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            KeyPrefix = "inactive",
            TargetPrefix = "ShouldNotApply",
            IsActive = false
        });
        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = "other@gmail.com",
            CalendarId = dailyLogCal,
            KeyPrefix = "other",
            TargetPrefix = "OtherAccount"
        });

        var result = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = dailyLogCal,
            CalendarName = "DailyLog",
            RangeStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(1)),
            RangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1)),
            Events = new()
            {
                MakeGoogleEvent("evt-1", "sd: czytanie", new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-2", "IPW:FM", new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-3", "ho", new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-4", "SelfDevelopment: juz znormalizowane", new DateTimeOffset(2026, 11, 11, 11, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-5", "inactive: skip", new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.FromHours(1))),
                MakeGoogleEvent("evt-6", "other: skip", new DateTimeOffset(2026, 11, 11, 13, 0, 0, TimeSpan.FromHours(1)))
            }
        });

        Assert.That(result.Mutations, Has.Count.EqualTo(3));

        Assert.That(result.Mutations[0].EventId, Is.EqualTo("evt-1"));
        Assert.That(result.Mutations[0].Action, Is.EqualTo(CalendarMutationActions.Rename));
        Assert.That(result.Mutations[0].NewTitle, Is.EqualTo("SelfDevelopment: czytanie"));

        Assert.That(result.Mutations[1].EventId, Is.EqualTo("evt-2"));
        Assert.That(result.Mutations[1].NewTitle, Is.EqualTo("IndividualProjectWork:FM"));

        Assert.That(result.Mutations[2].EventId, Is.EqualTo("evt-3"));
        Assert.That(result.Mutations[2].NewTitle, Is.EqualTo("Hanging-out"));
    }

    [Test]
    public async Task IngestRawEvents_TitleRenameWildcardRule_OverriddenByCalendarSpecificRule()
    {
        var account = "pwujczyk@google.com";
        var calA = "cal-a";
        var calB = "cal-b";

        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = "*",
            KeyPrefix = "o",
            TargetPrefix = "Operations"
        });
        await _mutationPlanner.UpsertTitleRenameRuleAsync(new UpsertTitleRenameRuleRequest
        {
            Account = account,
            CalendarId = calB,
            KeyPrefix = "o",
            TargetPrefix = "Other"
        });

        var resA = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = calA,
            CalendarName = "Cal A",
            RangeStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(1)),
            RangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1)),
            Events = new() { MakeGoogleEvent("evt-a", "o: mail", new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(1))) }
        });

        var resB = await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = calB,
            CalendarName = "Cal B",
            RangeStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(1)),
            RangeEnd = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1)),
            Events = new() { MakeGoogleEvent("evt-b", "o: mail", new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(1))) }
        });

        Assert.That(resA.Mutations.Single().NewTitle, Is.EqualTo("Operations: mail"));
        Assert.That(resB.Mutations.Single().NewTitle, Is.EqualTo("Other: mail"));
    }

    [Test]
    public async Task RawEventsController_DeleteAll_ClearsRawCalendarEventsTable()
    {
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));
        var end = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.FromHours(1));

        await _service.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "cal-1",
            CalendarName = "Work",
            RangeStart = start,
            RangeEnd = end,
            Events = new()
            {
                MakeGoogleEvent("evt-1", "One", start),
                MakeGoogleEvent("evt-2", "Two", start.AddHours(2))
            }
        });

        Assert.That(await _db.RawCalendarEvents.CountAsync(), Is.EqualTo(2));

        var controller = new RawEventsController(_db);
        var result = await controller.DeleteAll();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(await _db.RawCalendarEvents.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Sync_WithMaxEventsPerRunSetToOne_ExposesLimitInPlanAndIngestsOnlyOneEvent()
    {
        var limitedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sync:DefaultLookbackDays"] = "14",
                ["Sync:MaxEventsPerRun"] = "1",
                ["Sync:TimeZone"] = "Europe/Warsaw"
            })
            .Build();

        var limitedService = new RawEventSyncService(_db, limitedConfig, _mutationPlanner);

        var plan = await limitedService.GetSyncPlanAsync("pwujczyk@google.com", new DateOnly(2026, 11, 17));
        Assert.That(plan.MaxEvents, Is.EqualTo(1));

        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));
        var end = new DateTimeOffset(2026, 11, 17, 23, 59, 59, TimeSpan.FromHours(1));

        var syncResult = await limitedService.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "cal-1",
            CalendarName = "DailyLog",
            RangeStart = start,
            RangeEnd = end,
            Events = new()
            {
                MakeGoogleEvent("evt-1", "First event", start),
                MakeGoogleEvent("evt-2", "Second event", start.AddHours(2)),
                MakeGoogleEvent("evt-3", "Third event", start.AddHours(4))
            }
        });

        Assert.That(syncResult.TotalReceived, Is.EqualTo(1));
        Assert.That(syncResult.Inserted, Is.EqualTo(1));
        Assert.That(await _db.RawCalendarEvents.CountAsync(), Is.EqualTo(1));
        Assert.That((await _db.RawCalendarEvents.SingleAsync()).ExternalEventId, Is.EqualTo("evt-1"));
    }

    private static JsonElement MakeGoogleEvent(string id, string summary, DateTimeOffset start) =>
        JsonSerializer.SerializeToElement(new
        {
            id,
            iCalUID = id + "@google.com",
            status = "confirmed",
            eventType = "default",
            summary,
            colorId = "9",
            start = new { dateTime = start.ToString("O") },
            end = new { dateTime = start.AddHours(1).ToString("O") },
            organizer = new { email = "pwujczyk@google.com", self = true },
            attendees = new[]
            {
                new { email = "pwujczyk@google.com", responseStatus = "accepted", self = true }
            }
        });
}

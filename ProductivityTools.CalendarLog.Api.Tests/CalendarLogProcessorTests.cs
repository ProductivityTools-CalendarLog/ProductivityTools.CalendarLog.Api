using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Database.Entities;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Tests;

[TestFixture]
public class CalendarLogProcessorTests
{
    private CalendarLogContext _db = null!;
    private RawEventSyncService _syncService = null!;
    private CalendarLogProcessor _processor = null!;

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

        var mutationPlanner = new CalendarMutationPlanner(_db);
        _syncService = new RawEventSyncService(_db, config, mutationPlanner);
        _processor = new CalendarLogProcessor(_db, config);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    [Test]
    public async Task ProcessPendingAndReprocess_FiltersExcludedEvents_AndAppliesPriorityCategoryRules()
    {
        var account = "pwujczyk@google.com";
        var startDay = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1));
        var endDay = new DateTimeOffset(2026, 11, 17, 23, 59, 59, TimeSpan.FromHours(1));

        // Configure rules matching Mapping-Caldendars + Mapping-DailyLog:
        // 1. TitlePrefix on DailyLog: "sd" -> "SelfDevelopment" (also matches already renamed "SelfDevelopment: ...")
        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "DailyLog",
            Priority = 10,
            MatchField = CategoryMatchFields.TitlePrefix,
            MatchValue = "sd",
            Category = "SelfDevelopment"
        });
        // 2. Title match: "Lunch" -> "Lunch"
        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "*",
            Priority = 20,
            MatchField = CategoryMatchFields.Title,
            MatchValue = "Lunch",
            Category = "Lunch"
        });
        // 3. Color match: "9" -> "FocusedWork"
        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "*",
            Priority = 30,
            MatchField = CategoryMatchFields.Color,
            MatchValue = "9",
            Category = "FocusedWork"
        });
        // 4. Fallback CalendarName match: "pwujczyk@google.com" -> "Meetings"
        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "*",
            Priority = 100,
            MatchField = CategoryMatchFields.CalendarName,
            MatchValue = account,
            Category = "Meetings"
        });

        // Ingest primary calendar events (including excluded ones: workingLocation, outOfOffice, color=1, declined, unaccepted guest invite)
        await _syncService.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = account,
            CalendarName = account,
            RangeStart = startDay,
            RangeEnd = endDay,
            Events = new()
            {
                MakeEvent("e-lunch", "Lunch", startDay.AddHours(12), 0.5, colorId: "5", responseStatus: "accepted"),
                MakeEvent("e-focus", "Deep coding", startDay.AddHours(9), 1.5, colorId: "9", responseStatus: "accepted"),
                MakeEvent("e-meet", "1:1 Sync", startDay.AddHours(14), 0.75, colorId: "7", responseStatus: "accepted", guestCount: 2),
                MakeEvent("e-ooo", "Vacation", startDay.AddHours(8), 8.0, eventType: "outOfOffice"),
                MakeEvent("e-wloc", "Home", startDay.AddHours(8), 8.0, eventType: "workingLocation"),
                MakeEvent("e-color1", "Ignored block", startDay.AddHours(16), 1.0, colorId: "1"),
                MakeEvent("e-declined", "Skipped meeting", startDay.AddHours(17), 1.0, responseStatus: "declined", guestCount: 3),
                MakeEvent("e-tentative", "Maybe meeting", startDay.AddHours(18), 1.0, responseStatus: "tentative", guestCount: 3)
            }
        });

        // Ingest DailyLog events
        await _syncService.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = "cal-daily",
            CalendarName = "DailyLog",
            RangeStart = startDay,
            RangeEnd = endDay,
            Events = new()
            {
                MakeEvent("e-dl-1", "SelfDevelopment: Architecture book", startDay.AddHours(20), 1.25)
            }
        });

        var result = await _processor.ProcessPendingAsync(account);

        Assert.That(result.RawEventsEvaluated, Is.EqualTo(9));
        Assert.That(result.LogsUpserted, Is.EqualTo(4));
        Assert.That(result.LogsRemovedOrSkipped, Is.EqualTo(5));

        var logs = await _processor.ListLogsAsync(account);
        Assert.That(logs, Has.Count.EqualTo(4));

        var byTitle = logs.ToDictionary(l => l.Title!);
        Assert.That(byTitle["Lunch"].Category, Is.EqualTo("Lunch"));
        Assert.That(byTitle["Lunch"].DurationHours, Is.EqualTo(0.5m));

        Assert.That(byTitle["Deep coding"].Category, Is.EqualTo("FocusedWork"));
        Assert.That(byTitle["Deep coding"].DurationHours, Is.EqualTo(1.5m));

        Assert.That(byTitle["1:1 Sync"].Category, Is.EqualTo("Meetings"));
        Assert.That(byTitle["1:1 Sync"].DurationHours, Is.EqualTo(0.75m));
        Assert.That(byTitle["1:1 Sync"].Day, Is.EqualTo(new DateOnly(2026, 11, 17)));
        Assert.That(byTitle["1:1 Sync"].Month, Is.EqualTo("2026-11"));
        Assert.That(byTitle["1:1 Sync"].WeekNumber, Is.EqualTo(202647));

        Assert.That(byTitle["SelfDevelopment: Architecture book"].Category, Is.EqualTo("SelfDevelopment"));
        Assert.That(byTitle["SelfDevelopment: Architecture book"].Value, Is.EqualTo("Architecture book"));
        Assert.That(byTitle["SelfDevelopment: Architecture book"].DurationHours, Is.EqualTo(1.25m));

        // Now change Color "9" rule from "FocusedWork" -> "Coding" and reprocess:
        var colorRule = (await _processor.ListRulesAsync(account))
            .Single(r => r.MatchField == CategoryMatchFields.Color);
        await _processor.UpdateRuleAsync(colorRule.CalendarCategoryRuleId, new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "*",
            Priority = 30,
            MatchField = CategoryMatchFields.Color,
            MatchValue = "9",
            Category = "Coding",
            IsActive = true
        });

        await _processor.ReprocessAllAsync(account);

        var updatedLogs = await _processor.ListLogsAsync(account);
        Assert.That(updatedLogs.Single(l => l.Title == "Deep coding").Category, Is.EqualTo("Coding"));
    }

    private static JsonElement MakeEvent(
        string id,
        string summary,
        DateTimeOffset start,
        double hours,
        string eventType = "default",
        string colorId = "7",
        string responseStatus = "accepted",
        int guestCount = 0)
    {
        var attendees = new List<object>();
        if (guestCount > 0)
        {
            attendees.Add(new { email = "pwujczyk@google.com", responseStatus, self = true });
            for (var i = 1; i < guestCount; i++)
            {
                attendees.Add(new { email = $"guest{i}@google.com", responseStatus = "accepted", self = false });
            }
        }
        else if (responseStatus != "accepted")
        {
            attendees.Add(new { email = "pwujczyk@google.com", responseStatus, self = true });
        }

        return JsonSerializer.SerializeToElement(new
        {
            id,
            iCalUID = id + "@google.com",
            status = "confirmed",
            eventType,
            summary,
            colorId,
            start = new { dateTime = start.ToString("O") },
            end = new { dateTime = start.AddHours(hours).ToString("O") },
            organizer = new { email = "pwujczyk@google.com", self = true },
            attendees = attendees.Count > 0 ? attendees : null
        });
    }
}

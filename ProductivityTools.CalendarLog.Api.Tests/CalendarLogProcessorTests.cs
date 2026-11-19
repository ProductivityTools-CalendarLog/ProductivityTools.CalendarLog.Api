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

        var dailySummary = await _processor.GetDailySummaryAsync(account);
        Assert.That(dailySummary.Days, Has.Count.EqualTo(1));
        Assert.That(dailySummary.Days[0].Day, Is.EqualTo(new DateOnly(2026, 11, 17)));
        Assert.That(dailySummary.Days[0].TotalHours, Is.EqualTo(4.00m));
        Assert.That(dailySummary.Days[0].ByCategory["Coding"], Is.EqualTo(1.50m));
        Assert.That(dailySummary.Days[0].ByCategory["SelfDevelopment"], Is.EqualTo(1.25m));
        Assert.That(dailySummary.Days[0].ByCategory["Meetings"], Is.EqualTo(0.75m));
        Assert.That(dailySummary.Days[0].ByCategory["Lunch"], Is.EqualTo(0.50m));
    }

    [Test]
    public async Task GetDailySummary_GroupsMultipleDaysAndHandlesUncategorizedFilter()
    {
        var account = "pwujczyk@google.com";
        var day1 = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(1));
        var day2 = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1));

        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "DailyLog",
            Priority = 10,
            MatchField = CategoryMatchFields.TitlePrefix,
            MatchValue = "Coding",
            Category = "Coding"
        });

        await _syncService.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = "cal-daily",
            CalendarName = "DailyLog",
            RangeStart = day1,
            RangeEnd = day2.AddHours(23),
            Events = new()
            {
                MakeEvent("d1-code", "Coding: Feature A", day1.AddHours(10), 2.5),
                MakeEvent("d1-uncat", "Mystery task", day1.AddHours(14), 1.0),
                MakeEvent("d2-code", "Coding: Feature B", day2.AddHours(9), 3.25)
            }
        });

        await _processor.ProcessPendingAsync(account);

        var withUncat = await _processor.GetDailySummaryAsync(account, includeUncategorized: true);
        Assert.That(withUncat.Days, Has.Count.EqualTo(2));
        // Ordered newest day first:
        Assert.That(withUncat.Days[0].Day, Is.EqualTo(new DateOnly(2026, 11, 17)));
        Assert.That(withUncat.Days[0].TotalHours, Is.EqualTo(3.25m));
        Assert.That(withUncat.Days[1].Day, Is.EqualTo(new DateOnly(2026, 11, 16)));
        Assert.That(withUncat.Days[1].TotalHours, Is.EqualTo(3.50m));
        Assert.That(withUncat.Days[1].ByCategory["Coding"], Is.EqualTo(2.50m));
        Assert.That(withUncat.Days[1].ByCategory["Uncategorized"], Is.EqualTo(1.00m));

        var withoutUncat = await _processor.GetDailySummaryAsync(account, includeUncategorized: false);
        Assert.That(withoutUncat.Categories, Does.Not.Contain("Uncategorized"));
        Assert.That(withoutUncat.Days[1].TotalHours, Is.EqualTo(2.50m));
        Assert.That(withoutUncat.Days[1].ByCategory.ContainsKey("Uncategorized"), Is.False);
    }

    [Test]
    public async Task GetWeeklySummary_GroupsAcrossWeeksAndComputesIsoWeekBounds()
    {
        var account = "pwujczyk@google.com";
        // 2026-11-11 is ISO week 202646; 2026-11-16 & 2026-11-17 are ISO week 202647
        var w46Day = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.FromHours(1));
        var w47Day1 = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(1));
        var w47Day2 = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1));

        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "DailyLog",
            Priority = 10,
            MatchField = CategoryMatchFields.TitlePrefix,
            MatchValue = "Coding",
            Category = "Coding"
        });
        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "DailyLog",
            Priority = 20,
            MatchField = CategoryMatchFields.TitlePrefix,
            MatchValue = "Meetings",
            Category = "Meetings"
        });

        await _syncService.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = "cal-daily",
            CalendarName = "DailyLog",
            RangeStart = w46Day,
            RangeEnd = w47Day2.AddHours(23),
            Events = new()
            {
                MakeEvent("w46-1", "Coding: API", w46Day.AddHours(9), 4.0),
                MakeEvent("w47-1", "Coding: UI", w47Day1.AddHours(9), 3.5),
                MakeEvent("w47-2", "Meetings: Planning", w47Day2.AddHours(13), 2.25)
            }
        });

        await _processor.ProcessPendingAsync(account);

        var weekly = await _processor.GetWeeklySummaryAsync(account);
        Assert.That(weekly.Weeks, Has.Count.EqualTo(2));
        Assert.That(weekly.Weeks[0].WeekNumber, Is.EqualTo(202647));
        Assert.That(weekly.Weeks[0].WeekStartDay, Is.EqualTo(new DateOnly(2026, 11, 16)));
        Assert.That(weekly.Weeks[0].WeekEndDay, Is.EqualTo(new DateOnly(2026, 11, 22)));
        Assert.That(weekly.Weeks[0].TotalHours, Is.EqualTo(5.75m));
        Assert.That(weekly.Weeks[0].ByCategory["Coding"], Is.EqualTo(3.50m));
        Assert.That(weekly.Weeks[0].ByCategory["Meetings"], Is.EqualTo(2.25m));

        Assert.That(weekly.Weeks[1].WeekNumber, Is.EqualTo(202646));
        Assert.That(weekly.Weeks[1].TotalHours, Is.EqualTo(4.00m));

        var logsW47 = await _processor.ListLogsAsync(account, weekNumber: 202647);
        Assert.That(logsW47, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ExcludesAllDayEvents_NormalizesCategoryCasing_AndAppliesSpreadsheetCategorySortOrder()
    {
        var account = "pwujczyk@google.com";
        var startDay = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1));

        // Verify default PT.Calendar category sort order (bottom-to-top: OOO .. SelfDevelopment):
        var defaults = await _processor.ListCategoriesAsync();
        Assert.That(defaults.Select(c => c.Category).ToArray(), Is.EqualTo(new[]
        {
            "OOO", "Meetings", "One2One", "Hanging-out", "TopDown", "Emails",
            "Operations", "IndividualProjectWork", "FocusedWork", "Journal", "Coding", "SelfDevelopment"
        }));

        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "DailyLog",
            Priority = 10,
            MatchField = CategoryMatchFields.Title,
            MatchValue = "emails",
            Category = "emails" // lowercase in Mapping-DailyLog -> should canonicalize to "Emails"
        });
        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "DailyLog",
            Priority = 20,
            MatchField = CategoryMatchFields.Title,
            MatchValue = "OOO",
            Category = "OOO"
        });
        await _processor.CreateRuleAsync(new UpsertCategoryRuleRequest
        {
            Account = account,
            CalendarId = "*",
            Priority = 100,
            MatchField = CategoryMatchFields.CalendarName,
            MatchValue = account,
            Category = "Meetings"
        });

        var allDayBanner = JsonSerializer.SerializeToElement(new
        {
            id = "ev-allday-1",
            iCalUID = "ev-allday-1@google.com",
            status = "confirmed",
            eventType = "default",
            summary = "Company Holiday / SVL Trip Banner",
            start = new { date = "2026-11-17" },
            end = new { date = "2026-11-20" },
            organizer = new { email = account, self = true }
        });

        await _syncService.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = account,
            CalendarName = account,
            RangeStart = startDay.AddDays(-1),
            RangeEnd = startDay.AddDays(5),
            Events = new()
            {
                allDayBanner,
                MakeEvent("ev-meet-1", "Team Sync", startDay.AddHours(10), 1.0)
            }
        });

        await _syncService.IngestRawEventsAsync(new RawEventsBatchRequest
        {
            Account = account,
            CalendarId = "cal-daily",
            CalendarName = "DailyLog",
            RangeStart = startDay.AddDays(-1),
            RangeEnd = startDay.AddDays(5),
            Events = new()
            {
                MakeEvent("ev-ooo-8h", "OOO", startDay.AddHours(9), 8.0),
                MakeEvent("ev-emails-lc", "emails", startDay.AddHours(17), 0.5)
            }
        });

        var procRes = await _processor.ProcessPendingAsync(account);
        Assert.That(procRes.RawEventsEvaluated, Is.EqualTo(4));
        Assert.That(procRes.LogsUpserted, Is.EqualTo(3));
        Assert.That(procRes.LogsRemovedOrSkipped, Is.EqualTo(1)); // all-day banner excluded!

        var daily = await _processor.GetDailySummaryAsync(account);
        Assert.That(daily.Days, Has.Count.EqualTo(1));
        Assert.That(daily.Days[0].TotalHours, Is.EqualTo(9.50m));
        Assert.That(daily.Days[0].ByCategory["OOO"], Is.EqualTo(8.00m));
        Assert.That(daily.Days[0].ByCategory["Meetings"], Is.EqualTo(1.00m));
        Assert.That(daily.Days[0].ByCategory["Emails"], Is.EqualTo(0.50m));
        Assert.That(daily.Categories, Is.EqualTo(new[] { "OOO", "Meetings", "Emails" }));

        // Test reordering (move SelfDevelopment first):
        var sdId = defaults.Single(c => c.Category == "SelfDevelopment").CalendarCategoryId;
        var reordered = await _processor.ReorderCategoriesAsync(
            new[] { sdId }.Concat(defaults.Where(c => c.CalendarCategoryId != sdId).Select(c => c.CalendarCategoryId)).ToList());
        Assert.That(reordered[0].Category, Is.EqualTo("SelfDevelopment"));
        Assert.That(reordered[0].SortOrder, Is.EqualTo(10));
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

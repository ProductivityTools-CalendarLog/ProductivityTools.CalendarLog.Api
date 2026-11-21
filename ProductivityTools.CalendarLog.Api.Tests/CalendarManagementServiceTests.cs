using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Tests;

[TestFixture]
public class CalendarManagementServiceTests
{
    private CalendarLogContext _db = null!;
    private CalendarManagementService _service = null!;

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
                ["Sync:DefaultLookbackDays"] = "14"
            })
            .Build();

        _service = new CalendarManagementService(_db, config);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    [Test]
    public async Task CreateListUpdateToggleAndDelete_WorksEndToEnd_AndRejectsDuplicates()
    {
        var created1 = await _service.CreateAsync(new CreateCalendarRequest
        {
            Account = " pwujczyk@google.com ",
            CalendarId = " dailylog@group.calendar.google.com ",
            CalendarName = " DailyLog ",
            IsActive = true,
            DefaultLookbackDays = 21
        });

        Assert.That(created1.Account, Is.EqualTo("pwujczyk@google.com"));
        Assert.That(created1.CalendarId, Is.EqualTo("dailylog@group.calendar.google.com"));
        Assert.That(created1.CalendarName, Is.EqualTo("DailyLog"));
        Assert.That(created1.Type, Is.EqualTo("DataLog"));
        Assert.That(created1.DefaultLookbackDays, Is.EqualTo(21));

        // Duplicate for same account throws DuplicateCalendarException
        Assert.ThrowsAsync<DuplicateCalendarException>(() => _service.CreateAsync(new CreateCalendarRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "dailylog@group.calendar.google.com"
        }));

        // Invalid calendar type is rejected
        Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new CreateCalendarRequest
        {
            Account = "pwujczyk@gmail.com",
            CalendarId = "invalid-type@group.calendar.google.com",
            Type = "OtherType"
        }));

        // Same CalendarId for different account is allowed, with explicit DataPoint type
        var created2 = await _service.CreateAsync(new CreateCalendarRequest
        {
            Account = "pwujczyk@gmail.com",
            CalendarId = "pwujczyk@gmail.com",
            Type = "datapoint"
        });
        Assert.That(created2.DefaultLookbackDays, Is.EqualTo(14));
        Assert.That(created2.Type, Is.EqualTo("DataPoint"));

        var switchedType = await _service.SetTypeAsync(created1.CalendarSyncStateId, "DataPoint");
        Assert.That(switchedType, Is.Not.Null);
        Assert.That(switchedType!.Type, Is.EqualTo("DataPoint"));

        Assert.ThrowsAsync<ArgumentException>(() => _service.SetTypeAsync(created1.CalendarSyncStateId, "Invalid"));

        var all = await _service.ListAsync();
        Assert.That(all, Has.Count.EqualTo(2));

        var filtered = await _service.ListAsync("pwujczyk@google.com");
        Assert.That(filtered, Has.Count.EqualTo(1));

        var toggled = await _service.SetActiveAsync(created1.CalendarSyncStateId, false);
        Assert.That(toggled, Is.Not.Null);
        Assert.That(toggled!.IsActive, Is.False);

        var updated = await _service.UpdateAsync(created1.CalendarSyncStateId, new UpdateCalendarRequest
        {
            Account = "pwujczyk@google.com",
            CalendarId = "dailylog@group.calendar.google.com",
            CalendarName = "DailyLog Updated",
            Type = "DataLog",
            IsActive = true,
            DefaultLookbackDays = 30
        });
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.CalendarName, Is.EqualTo("DailyLog Updated"));
        Assert.That(updated.Type, Is.EqualTo("DataLog"));
        Assert.That(updated.DefaultLookbackDays, Is.EqualTo(30));

        var deleted = await _service.DeleteAsync(created1.CalendarSyncStateId);
        Assert.That(deleted, Is.True);
        Assert.That(await _service.GetByIdAsync(created1.CalendarSyncStateId), Is.Null);
    }
}

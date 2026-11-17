using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/raw-events")]
public class RawEventsController : ControllerBase
{
    private readonly CalendarLogContext _db;

    public RawEventsController(CalendarLogContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? account,
        [FromQuery] string? calendarId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var cappedLimit = Math.Clamp(limit, 1, 5000);
        var query = _db.RawCalendarEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(e => e.Account == normalized);
        }

        if (!string.IsNullOrWhiteSpace(calendarId))
        {
            var normalized = calendarId.Trim();
            query = query.Where(e => e.CalendarId == normalized);
        }

        if (from.HasValue)
        {
            query = query.Where(e => e.StartTime >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(e => e.StartTime <= to.Value);
        }

        if (!includeDeleted)
        {
            query = query.Where(e => !e.IsDeleted);
        }

        var events = await query
            .OrderByDescending(e => e.StartTime)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);

        return Ok(events);
    }

    /// <summary>
    /// Deletes all rows from dbo.RawCalendarEvent (optionally filtered by account) for testing.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteAll(
        [FromQuery] string? account = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.RawCalendarEvents.AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(e => e.Account == normalized);
        }

        int deletedCount;
        if (_db.Database.IsRelational())
        {
            deletedCount = await query.ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var items = await query.ToListAsync(cancellationToken);
            deletedCount = items.Count;
            if (deletedCount > 0)
            {
                var ids = items.Select(i => i.RawCalendarEventId).ToHashSet();
                var linkedLogs = await _db.CalendarLogs
                    .Where(l => l.RawCalendarEventId.HasValue && ids.Contains(l.RawCalendarEventId.Value))
                    .ToListAsync(cancellationToken);
                _db.CalendarLogs.RemoveRange(linkedLogs);
                _db.RawCalendarEvents.RemoveRange(items);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return Ok(new { deletedCount });
    }
}

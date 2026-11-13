using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly RawEventSyncService _syncService;
    private readonly CalendarLogContext _db;

    public SyncController(RawEventSyncService syncService, CalendarLogContext db)
    {
        _syncService = syncService;
        _db = db;
    }

    /// <summary>
    /// Returns the server-decided start date (and per-calendar start dates) for the given account.
    /// Intentionally unauthenticated for Google Apps Script (matching Spendings.WebApi ingestion pattern).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("plan")]
    public async Task<ActionResult<SyncPlanResponse>> GetPlan(
        [FromQuery] string account,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return BadRequest("Query parameter 'account' is required.");
        }

        var plan = await _syncService.GetSyncPlanAsync(account, cancellationToken: cancellationToken);
        return Ok(plan);
    }

    /// <summary>
    /// Ingests raw Google Calendar events for a single calendar over [RangeStart, RangeEnd].
    /// Intentionally unauthenticated for Google Apps Script (matching Spendings.WebApi ingestion pattern).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("raw-events")]
    public async Task<ActionResult<RawEventsSyncResult>> IngestRawEvents(
        [FromBody] RawEventsBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RangeEnd < request.RangeStart)
        {
            return BadRequest("RangeEnd must be greater than or equal to RangeStart.");
        }

        var result = await _syncService.IngestRawEventsAsync(request, cancellationToken);
        return Accepted(result);
    }

    /// <summary>
    /// Lists current sync state rows (requires Firebase ID token).
    /// </summary>
    [Authorize]
    [HttpGet("state")]
    public async Task<IActionResult> GetSyncStates(
        [FromQuery] string? account,
        CancellationToken cancellationToken)
    {
        var query = _db.CalendarSyncStates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(s => s.Account == normalized);
        }

        var items = await query
            .OrderBy(s => s.Account)
            .ThenBy(s => s.CalendarId)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>
    /// Schedules a one-time deeper backfill starting from FromDate for an account (or single calendar).
    /// Requires Firebase ID token.
    /// </summary>
    [Authorize]
    [HttpPost("backfill")]
    public async Task<IActionResult> RequestBackfill(
        [FromBody] BackfillRequest request,
        CancellationToken cancellationToken)
    {
        var updatedCount = await _syncService.RequestBackfillAsync(request, cancellationToken);
        if (updatedCount == 0)
        {
            return NotFound($"No sync state found for account '{request.Account}'. Specify calendarId or run an initial sync first.");
        }

        return Ok(new { account = request.Account.Trim(), fromDate = request.FromDate, calendarsUpdated = updatedCount });
    }

    /// <summary>
    /// Registers or updates a calendar tracked for synchronization by the server.
    /// Requires Firebase ID token.
    /// </summary>
    [Authorize]
    [HttpPost("calendars")]
    public async Task<IActionResult> UpsertCalendar(
        [FromBody] UpsertSyncCalendarRequest request,
        CancellationToken cancellationToken)
    {
        var state = await _syncService.UpsertCalendarAsync(request, cancellationToken);
        return Ok(state);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/mutation-rules")]
public class MutationRulesController : ControllerBase
{
    private readonly CalendarLogContext _db;
    private readonly CalendarMutationPlanner _planner;

    public MutationRulesController(CalendarLogContext db, CalendarMutationPlanner planner)
    {
        _db = db;
        _planner = planner;
    }

    [HttpGet("convert")]
    public async Task<IActionResult> ListConvertRules(
        [FromQuery] string? account,
        CancellationToken cancellationToken)
    {
        var query = _db.CalendarConvertRules.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(r => r.Account == normalized);
        }

        var rules = await query
            .OrderBy(r => r.Account)
            .ThenBy(r => r.KeyPrefix)
            .ToListAsync(cancellationToken);

        return Ok(rules);
    }

    [HttpPost("convert")]
    public async Task<IActionResult> UpsertConvertRule(
        [FromBody] UpsertConvertRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rule = await _planner.UpsertConvertRuleAsync(request, cancellationToken);
            return Ok(rule);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("convert/{id:int}")]
    public async Task<IActionResult> UpdateConvertRule(
        int id,
        [FromBody] UpsertConvertRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _planner.UpdateConvertRuleAsync(id, request, cancellationToken);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("convert/{id:int}")]
    public async Task<IActionResult> DeleteConvertRule(int id, CancellationToken cancellationToken)
    {
        var rule = await _db.CalendarConvertRules
            .SingleOrDefaultAsync(r => r.CalendarConvertRuleId == id, cancellationToken);

        if (rule == null)
        {
            return NotFound();
        }

        _db.CalendarConvertRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("title-rename")]
    public async Task<IActionResult> ListTitleRenameRules(
        [FromQuery] string? account,
        [FromQuery] string? calendarId,
        CancellationToken cancellationToken)
    {
        var query = _db.CalendarTitleRenameRules.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(r => r.Account == normalized);
        }

        if (!string.IsNullOrWhiteSpace(calendarId))
        {
            var normalized = calendarId.Trim();
            query = query.Where(r => r.CalendarId == normalized);
        }

        var rules = await query
            .OrderBy(r => r.Account)
            .ThenBy(r => r.CalendarId)
            .ThenBy(r => r.KeyPrefix)
            .ToListAsync(cancellationToken);

        return Ok(rules);
    }

    [HttpPost("title-rename")]
    public async Task<IActionResult> UpsertTitleRenameRule(
        [FromBody] UpsertTitleRenameRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rule = await _planner.UpsertTitleRenameRuleAsync(request, cancellationToken);
            return Ok(rule);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("title-rename/{id:int}")]
    public async Task<IActionResult> UpdateTitleRenameRule(
        int id,
        [FromBody] UpsertTitleRenameRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _planner.UpdateTitleRenameRuleAsync(id, request, cancellationToken);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("title-rename/{id:int}")]
    public async Task<IActionResult> DeleteTitleRenameRule(int id, CancellationToken cancellationToken)
    {
        var rule = await _db.CalendarTitleRenameRules
            .SingleOrDefaultAsync(r => r.CalendarTitleRenameRuleId == id, cancellationToken);

        if (rule == null)
        {
            return NotFound();
        }

        _db.CalendarTitleRenameRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("value-rename")]
    public async Task<IActionResult> ListValueRenameRules(
        [FromQuery] string? account,
        [FromQuery] string? calendarId,
        CancellationToken cancellationToken)
    {
        var query = _db.CalendarValueRenameRules.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalized = account.Trim();
            query = query.Where(r => r.Account == normalized);
        }

        if (!string.IsNullOrWhiteSpace(calendarId))
        {
            var normalized = calendarId.Trim();
            query = query.Where(r => r.CalendarId == normalized);
        }

        var rules = await query
            .OrderBy(r => r.Account)
            .ThenBy(r => r.CalendarId)
            .ThenBy(r => r.CategoryPrefix)
            .ThenBy(r => r.KeySuffix)
            .ToListAsync(cancellationToken);

        return Ok(rules);
    }

    [HttpPost("value-rename")]
    public async Task<IActionResult> UpsertValueRenameRule(
        [FromBody] UpsertValueRenameRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rule = await _planner.UpsertValueRenameRuleAsync(request, cancellationToken);
            return Ok(rule);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("value-rename/{id:int}")]
    public async Task<IActionResult> UpdateValueRenameRule(
        int id,
        [FromBody] UpsertValueRenameRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _planner.UpdateValueRenameRuleAsync(id, request, cancellationToken);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("value-rename/{id:int}")]
    public async Task<IActionResult> DeleteValueRenameRule(int id, CancellationToken cancellationToken)
    {
        var rule = await _db.CalendarValueRenameRules
            .SingleOrDefaultAsync(r => r.CalendarValueRenameRuleId == id, cancellationToken);

        if (rule == null)
        {
            return NotFound();
        }

        _db.CalendarValueRenameRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

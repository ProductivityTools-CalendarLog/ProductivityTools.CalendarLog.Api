using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/calendars")]
public class CalendarsController : ControllerBase
{
    private readonly CalendarManagementService _calendarService;

    public CalendarsController(CalendarManagementService calendarService)
    {
        _calendarService = calendarService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CalendarItemDto>>> List(
        [FromQuery] string? account,
        CancellationToken cancellationToken)
    {
        var items = await _calendarService.ListAsync(account, cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CalendarItemDto>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var item = await _calendarService.GetByIdAsync(id, cancellationToken);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<CalendarItemDto>> Create(
        [FromBody] CreateCalendarRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _calendarService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.CalendarSyncStateId }, created);
        }
        catch (DuplicateCalendarException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CalendarItemDto>> Update(
        int id,
        [FromBody] UpdateCalendarRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _calendarService.UpdateAsync(id, request, cancellationToken);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (DuplicateCalendarException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:int}/active")]
    public async Task<ActionResult<CalendarItemDto>> SetActive(
        int id,
        [FromBody] SetCalendarActiveRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _calendarService.SetActiveAsync(id, request.IsActive, cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var deleted = await _calendarService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

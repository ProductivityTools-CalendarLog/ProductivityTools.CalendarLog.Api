using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/calendar-logs")]
public class CalendarLogsController : ControllerBase
{
    private readonly CalendarLogProcessor _processor;

    public CalendarLogsController(CalendarLogProcessor processor)
    {
        _processor = processor;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? account = null,
        [FromQuery] DateOnly? fromDay = null,
        [FromQuery] DateOnly? toDay = null,
        [FromQuery] string? category = null,
        [FromQuery] int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var items = await _processor.ListLogsAsync(account, fromDay, toDay, category, limit, cancellationToken);
        return Ok(items);
    }

    [HttpPost("reprocess")]
    public async Task<IActionResult> Reprocess(
        [FromQuery] string? account = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _processor.ReprocessAllAsync(account, cancellationToken);
        return Ok(result);
    }
}

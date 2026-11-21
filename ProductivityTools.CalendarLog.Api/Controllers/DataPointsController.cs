using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/data-points")]
public class DataPointsController : ControllerBase
{
    private readonly CalendarLogProcessor _processor;

    public DataPointsController(CalendarLogProcessor processor)
    {
        _processor = processor;
    }

    [HttpGet("monthly-summary")]
    public async Task<IActionResult> GetMonthlySummary(
        [FromQuery] string? account = null,
        [FromQuery] DateOnly? fromDay = null,
        [FromQuery] DateOnly? toDay = null,
        CancellationToken cancellationToken = default)
    {
        var summary = await _processor.GetDataPointMonthlySummaryAsync(
            account,
            fromDay,
            toDay,
            cancellationToken);

        return Ok(summary);
    }

    [HttpGet("events")]
    public async Task<IActionResult> ListEvents(
        [FromQuery] string? account = null,
        [FromQuery] string? category = null,
        [FromQuery] string? month = null,
        [FromQuery] DateOnly? fromDay = null,
        [FromQuery] DateOnly? toDay = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _processor.ListDataPointEventsAsync(
            account,
            category,
            month,
            fromDay,
            toDay,
            cancellationToken);

        return Ok(items);
    }
}

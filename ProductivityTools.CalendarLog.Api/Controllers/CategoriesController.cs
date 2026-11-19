using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly CalendarLogProcessor _processor;

    public CategoriesController(CalendarLogProcessor processor)
    {
        _processor = processor;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default)
    {
        var items = await _processor.ListCategoriesAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] UpsertCalendarCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _processor.CreateCategoryAsync(request, cancellationToken);
            return Ok(created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpsertCalendarCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _processor.UpdateCategoryAsync(id, request, cancellationToken);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(
        [FromBody] ReorderCalendarCategoriesRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = await _processor.ReorderCategoriesAsync(request.OrderedCategoryIds, cancellationToken);
        return Ok(items);
    }

    [HttpPost("reset-defaults")]
    public async Task<IActionResult> ResetDefaults(CancellationToken cancellationToken = default)
    {
        var items = await _processor.ResetDefaultCategoriesAsync(cancellationToken);
        return Ok(items);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await _processor.DeleteCategoryAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

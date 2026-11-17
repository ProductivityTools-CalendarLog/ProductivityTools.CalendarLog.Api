using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductivityTools.CalendarLog.Api.Dtos;
using ProductivityTools.CalendarLog.Api.Services;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/category-rules")]
public class CategoryRulesController : ControllerBase
{
    private readonly CalendarLogProcessor _processor;

    public CategoryRulesController(CalendarLogProcessor processor)
    {
        _processor = processor;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? account,
        CancellationToken cancellationToken)
    {
        var rules = await _processor.ListRulesAsync(account, cancellationToken);
        return Ok(rules);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] UpsertCategoryRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _processor.CreateRuleAsync(request, cancellationToken);
            return Ok(created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpsertCategoryRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _processor.UpdateRuleAsync(id, request, cancellationToken);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _processor.DeleteRuleAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

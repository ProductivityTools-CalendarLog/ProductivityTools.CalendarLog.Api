using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("hello")]
    public IActionResult Hello()
    {
        return Ok(new
        {
            status = "ok",
            service = "ProductivityTools.CalendarLog.Api",
            utcNow = DateTime.UtcNow
        });
    }
}

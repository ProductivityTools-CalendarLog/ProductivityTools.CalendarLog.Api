using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;

namespace ProductivityTools.CalendarLog.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class DebugController : ControllerBase
{
    private readonly CalendarLogContext _db;

    public DebugController(CalendarLogContext db)
    {
        _db = db;
    }

    [HttpGet]
    [Route("Date")]
    public string Date()
    {
        return DateTime.Now.ToString();
    }

    [HttpGet]
    [Route("AppName")]
    public string AppName()
    {
        return "PTCalendarLog";
    }

    [HttpGet]
    [Route("Hello")]
    public string Hello(string? name = null)
    {
        return $"Hello {name} Current date:{DateTime.Now}";
    }

    [HttpGet]
    [Route("ServerName")]
    public string ServerName()
    {
        return _db.Database.SqlQuery<string>($"select @@SERVERNAME as value").Single();
    }

    [HttpGet]
    [Route("DatabaseName")]
    public string DatabaseName()
    {
        return _db.Database.SqlQuery<string>($"select DB_NAME() as value").Single();
    }
}

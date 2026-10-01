using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;

namespace PingDashboard.Controllers;

/// <summary>MVC controller that renders the status-log page.</summary>
[Authorize]
public class LogsController : Controller
{
    private readonly AppDbContext _db;

    public LogsController(AppDbContext db) => _db = db;

    // GET /Logs — show the most recent 200 log entries
    public async Task<IActionResult> Index()
    {
        var logs = await _db.StatusLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(200)
            .ToListAsync();
        return View(logs);
    }
}

/// <summary>JSON API for status logs.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StatusLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StatusLogsController(AppDbContext db) => _db = db;

    // GET api/statuslogs?take=200
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);
        var logs = await _db.StatusLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(take)
            .ToListAsync();
        return Ok(logs);
    }

    // DELETE api/statuslogs — clear all logs (Admin only)
    [HttpDelete]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Clear()
    {
        await _db.StatusLogs.ExecuteDeleteAsync();
        return Ok(new { message = "All logs cleared." });
    }
}

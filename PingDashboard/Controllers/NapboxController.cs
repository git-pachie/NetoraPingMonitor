using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Models;

namespace PingDashboard.Controllers;

/// <summary>MVC controller that renders the Napbox management page.</summary>
public class NapboxController : Controller
{
    private readonly AppDbContext _db;

    public NapboxController(AppDbContext db) => _db = db;

    // GET /Napbox
    public async Task<IActionResult> Index()
    {
        var boxes = await _db.Napboxes.OrderBy(n => n.NapboxName).ToListAsync();
        return View(boxes);
    }
}

/// <summary>JSON API for Napbox CRUD.</summary>
[ApiController]
[Route("api/[controller]")]
public class NapboxesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Services.IFileLogger _fileLog;

    public NapboxesController(AppDbContext db, Services.IFileLogger fileLog)
    {
        _db = db;
        _fileLog = fileLog;
    }

    // GET api/napboxes
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var boxes = await _db.Napboxes.OrderBy(n => n.NapboxName).ToListAsync();
        return Ok(boxes);
    }

    // GET api/napboxes/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var box = await _db.Napboxes.FindAsync(id);
        return box is null ? NotFound() : Ok(box);
    }

    // POST api/napboxes
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NapboxDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var name = dto.NapboxName.Trim();
        var duplicate = await _db.Napboxes.AnyAsync(n => n.NapboxName == name);
        if (duplicate)
            return Conflict(new { message = $"Napbox '{name}' already exists." });

        var box = new Napbox { NapboxName = name };
        _db.Napboxes.Add(box);
        await _db.SaveChangesAsync();
        _fileLog.Log("NAPBOX", $"Added: '{box.NapboxName}' (id {box.Id})");
        return Ok(box);
    }

    // PUT api/napboxes/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] NapboxDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var oldName = await _db.Napboxes
            .Where(n => n.Id == id)
            .Select(n => n.NapboxName)
            .FirstOrDefaultAsync();
        if (oldName is null) return NotFound();

        var name = dto.NapboxName.Trim();
        var duplicate = await _db.Napboxes.AnyAsync(n => n.NapboxName == name && n.Id != id);
        if (duplicate)
            return Conflict(new { message = $"Napbox '{name}' already exists." });

        await _db.Napboxes
            .Where(n => n.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.NapboxName, name));

        _fileLog.Log("NAPBOX", $"Updated: '{oldName}' → '{name}' (id {id})");
        return Ok(new { id, napboxName = name });
    }

    // DELETE api/napboxes/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var name = await _db.Napboxes
            .Where(n => n.Id == id)
            .Select(n => n.NapboxName)
            .FirstOrDefaultAsync();
        if (name is null) return NotFound();

        await _db.Napboxes.Where(n => n.Id == id).ExecuteDeleteAsync();
        _fileLog.Log("NAPBOX", $"Deleted: '{name}' (id {id})");
        return Ok(new { message = "Deleted." });
    }
}

/// <summary>Input DTO for Napbox Create / Update.</summary>
public class NapboxDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string NapboxName { get; set; } = string.Empty;
}

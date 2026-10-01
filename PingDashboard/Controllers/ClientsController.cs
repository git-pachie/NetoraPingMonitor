using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Models;

namespace PingDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // any logged-in user can read; writes require AdminOnly (per method)
public class ClientsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Services.IFileLogger _fileLog;

    public ClientsController(AppDbContext db, Services.IFileLogger fileLog)
    {
        _db = db;
        _fileLog = fileLog;
    }

    // GET api/clients — scoped to the caller's napboxes (Admin sees all)
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var allowed = await Services.NapboxScope.GetAllowedNapboxesAsync(_db, User);
        var query = _db.Clients.AsQueryable();
        if (allowed is not null)
        {
            var list = allowed.ToList();
            query = query.Where(c => c.NapboxName != null && list.Contains(c.NapboxName));
        }
        var clients = await query.OrderBy(c => c.Name).ToListAsync();
        return Ok(clients);
    }

    // GET api/clients/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var client = await _db.Clients.FindAsync(id);
        return client is null ? NotFound() : Ok(client);
    }

    // POST api/clients
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] ClientDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var duplicate = await _db.Clients
            .AnyAsync(c => c.IpAddress == dto.IpAddress.Trim());
        if (duplicate)
            return Conflict(new { message = $"IP address '{dto.IpAddress}' already exists." });

        var client = new Client
        {
            Name                  = dto.Name.Trim(),
            IpAddress             = dto.IpAddress.Trim(),
            NotifyEmail           = string.IsNullOrWhiteSpace(dto.NotifyEmail) ? null : dto.NotifyEmail.Trim(),
            IsNotificationEnabled = dto.IsNotificationEnabled,
            NapboxName            = string.IsNullOrWhiteSpace(dto.NapboxName) ? null : dto.NapboxName.Trim()
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _fileLog.Log("CLIENT", $"Added: '{client.Name}' ({client.IpAddress}) napbox='{client.NapboxName ?? "-"}' notify={client.IsNotificationEnabled}");
        return Ok(client);
    }

    // PUT api/clients/5
    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(int id, [FromBody] ClientDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // No-tracking read just to confirm existence
        var exists = await _db.Clients.AnyAsync(c => c.Id == id);
        if (!exists) return NotFound();

        var duplicate = await _db.Clients
            .AnyAsync(c => c.IpAddress == dto.IpAddress.Trim() && c.Id != id);
        if (duplicate)
            return Conflict(new { message = $"IP address '{dto.IpAddress}' already exists." });

        var name       = dto.Name.Trim();
        var ip         = dto.IpAddress.Trim();
        var email      = string.IsNullOrWhiteSpace(dto.NotifyEmail) ? null : dto.NotifyEmail.Trim();
        var notify     = dto.IsNotificationEnabled;
        var napbox     = string.IsNullOrWhiteSpace(dto.NapboxName) ? null : dto.NapboxName.Trim();

        // Targeted UPDATE — no entity tracking needed
        await _db.Clients
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Name, name)
                .SetProperty(c => c.IpAddress, ip)
                .SetProperty(c => c.NotifyEmail, email)
                .SetProperty(c => c.IsNotificationEnabled, notify)
                .SetProperty(c => c.NapboxName, napbox));

        _fileLog.Log("CLIENT", $"Updated: '{name}' ({ip}) napbox='{napbox ?? "-"}' notify={notify}");
        return Ok(new { id, name, ipAddress = ip, notifyEmail = email, isNotificationEnabled = notify, napboxName = napbox });
    }

    // DELETE api/clients/5
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        // Read the name/ip for logging (no-tracking) then delete via ExecuteDelete
        var client = await _db.Clients
            .Where(c => c.Id == id)
            .Select(c => new { c.Name, c.IpAddress })
            .FirstOrDefaultAsync();
        if (client is null) return NotFound();

        await _db.Clients.Where(c => c.Id == id).ExecuteDeleteAsync();
        _fileLog.Log("CLIENT", $"Deleted: '{client.Name}' ({client.IpAddress}) (id {id})");
        return Ok(new { message = "Deleted." });
    }
}

/// <summary>Input DTO for Create / Update.</summary>
public class ClientDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string IpAddress { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string? NotifyEmail { get; set; }

    public bool IsNotificationEnabled { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? NapboxName { get; set; }
}

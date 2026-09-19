using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Models;

namespace PingDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ClientsController(AppDbContext db) => _db = db;

    // GET api/clients
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var clients = await _db.Clients.OrderBy(c => c.Name).ToListAsync();
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
        return Ok(client);
    }

    // PUT api/clients/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ClientDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var client = await _db.Clients.FindAsync(id);
        if (client is null) return NotFound();

        var duplicate = await _db.Clients
            .AnyAsync(c => c.IpAddress == dto.IpAddress.Trim() && c.Id != id);
        if (duplicate)
            return Conflict(new { message = $"IP address '{dto.IpAddress}' already exists." });

        client.Name                  = dto.Name.Trim();
        client.IpAddress             = dto.IpAddress.Trim();
        client.NotifyEmail           = string.IsNullOrWhiteSpace(dto.NotifyEmail) ? null : dto.NotifyEmail.Trim();
        client.IsNotificationEnabled = dto.IsNotificationEnabled;
        client.NapboxName            = string.IsNullOrWhiteSpace(dto.NapboxName) ? null : dto.NapboxName.Trim();
        await _db.SaveChangesAsync();
        return Ok(client);
    }

    // DELETE api/clients/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var client = await _db.Clients.FindAsync(id);
        if (client is null) return NotFound();

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync();
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

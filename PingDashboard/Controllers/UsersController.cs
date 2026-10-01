using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Models;
using PingDashboard.Services;

namespace PingDashboard.Controllers;

/// <summary>MVC controller that renders the User management page (Admin only).</summary>
[Authorize(Policy = "AdminOnly")]
public class UsersController : Controller
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db) => _db = db;

    // GET /Users
    public async Task<IActionResult> Index()
    {
        var users = await _db.Users
            .Include(u => u.Napboxes)
            .OrderBy(u => u.Username)
            .ToListAsync();

        ViewBag.AllNapboxes = await _db.Napboxes
            .OrderBy(n => n.NapboxName)
            .Select(n => n.NapboxName)
            .ToListAsync();

        return View(users);
    }
}

/// <summary>JSON API for user CRUD (Admin only).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class UsersApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileLogger _fileLog;

    public UsersApiController(AppDbContext db, IFileLogger fileLog)
    {
        _db = db;
        _fileLog = fileLog;
    }

    // GET api/usersapi — never returns hashes
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Users
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                u.Id, u.Username, u.Role, u.Email, u.Mobile, u.ProfileImagePath,
                Napboxes = u.Napboxes.Select(n => n.NapboxName).ToList()
            })
            .ToListAsync();
        return Ok(users);
    }

    // POST api/usersapi
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserDto dto)
    {
        var username = (dto.Username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest(new { message = "Username is required." });
        if (string.IsNullOrEmpty(dto.Password))
            return BadRequest(new { message = "Password is required." });
        if (!Roles.IsValid(dto.Role))
            return BadRequest(new { message = "Role must be Admin or Technician." });

        var duplicate = await _db.Users.AnyAsync(u => u.Username == username);
        if (duplicate)
            return Conflict(new { message = $"User '{username}' already exists." });

        var (hash, salt) = PasswordHasher.Hash(dto.Password);
        var user = new AppUser
        {
            Username     = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role         = dto.Role!,
            Email        = Clean(dto.Email),
            Mobile       = Clean(dto.Mobile),
            Napboxes     = BuildNapboxes(dto.Napboxes)
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _fileLog.Log("USER", $"Added: '{user.Username}' ({user.Role}) by {User.Identity?.Name}");
        return Ok(await ProjectAsync(user.Id));
    }

    // PUT api/usersapi/5 — update profile, role, napboxes, and/or password
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UserDto dto)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        if (!Roles.IsValid(dto.Role))
            return BadRequest(new { message = "Role must be Admin or Technician." });

        // Prevent removing the last admin
        if (user.Role == Roles.Admin && dto.Role != Roles.Admin)
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == Roles.Admin);
            if (adminCount <= 1)
                return BadRequest(new { message = "Cannot change the role of the last remaining Admin." });
        }

        user.Role   = dto.Role!;
        user.Email  = Clean(dto.Email);
        user.Mobile = Clean(dto.Mobile);
        if (!string.IsNullOrEmpty(dto.Password))
        {
            var (hash, salt) = PasswordHasher.Hash(dto.Password);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
        }
        _db.Users.Update(user);

        // Replace napbox assignments
        await _db.UserNapboxes.Where(n => n.UserId == id).ExecuteDeleteAsync();
        foreach (var box in NormaliseNapboxes(dto.Napboxes))
            _db.UserNapboxes.Add(new UserNapbox { UserId = id, NapboxName = box });

        await _db.SaveChangesAsync();

        _fileLog.Log("USER", $"Updated: '{user.Username}' role={user.Role}" +
            (string.IsNullOrEmpty(dto.Password) ? "" : " (password reset)") +
            $" by {User.Identity?.Name}");
        return Ok(await ProjectAsync(id));
    }

    // POST api/usersapi/5/image — upload a profile image (multipart form)
    [HttpPost("{id:int}/image")]
    public async Task<IActionResult> UploadImage(int id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });
        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(new { message = "Image must be 2 MB or smaller." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
        if (!allowed.Contains(ext))
            return BadRequest(new { message = "Allowed types: png, jpg, jpeg, gif, webp." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        var dir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads", "profiles");
        Directory.CreateDirectory(dir);
        var fileName = $"user{id}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        // Remove previous image if any
        if (!string.IsNullOrEmpty(user.ProfileImagePath))
        {
            var old = Path.Combine(AppContext.BaseDirectory, "wwwroot",
                user.ProfileImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            try { if (System.IO.File.Exists(old)) System.IO.File.Delete(old); } catch { }
        }

        var relative = $"/uploads/profiles/{fileName}";
        user.ProfileImagePath = relative;
        _db.Users.Update(user);
        await _db.SaveChangesAsync();

        _fileLog.Log("USER", $"Profile image updated for '{user.Username}' by {User.Identity?.Name}");
        return Ok(new { profileImagePath = relative });
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static string? Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<string> NormaliseNapboxes(List<string>? napboxes) =>
        (napboxes ?? new())
            .Select(n => (n ?? "").Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<UserNapbox> BuildNapboxes(List<string>? napboxes) =>
        NormaliseNapboxes(napboxes).Select(n => new UserNapbox { NapboxName = n }).ToList();

    private async Task<object> ProjectAsync(int id) =>
        await _db.Users
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id, u.Username, u.Role, u.Email, u.Mobile, u.ProfileImagePath,
                Napboxes = u.Napboxes.Select(n => n.NapboxName).ToList()
            })
            .FirstAsync();

    // DELETE api/usersapi/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        // Prevent deleting yourself
        if (string.Equals(user.Username, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "You cannot delete your own account while signed in." });

        // Prevent deleting the last admin
        if (user.Role == Roles.Admin)
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == Roles.Admin);
            if (adminCount <= 1)
                return BadRequest(new { message = "Cannot delete the last remaining Admin." });
        }

        await _db.Users.Where(u => u.Id == id).ExecuteDeleteAsync();
        _fileLog.Log("USER", $"Deleted: '{user.Username}' by {User.Identity?.Name}");
        return Ok(new { message = "Deleted." });
    }
}

/// <summary>Input DTO for user create/update.</summary>
public class UserDto
{
    public string? Username { get; set; }
    public string? Password { get; set; }   // required on create, optional on update
    public string? Role     { get; set; }
    public string? Email    { get; set; }
    public string? Mobile   { get; set; }
    public List<string>? Napboxes { get; set; }
}

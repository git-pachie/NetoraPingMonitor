using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Services;

namespace PingDashboard.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IFileLogger _fileLog;

    public AccountController(AppDbContext db, IFileLogger fileLog)
    {
        _db = db;
        _fileLog = fileLog;
    }

    // GET /Account/Login
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // Already signed in? go home
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    // POST /Account/Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        username = (username ?? "").Trim();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !PasswordHasher.Verify(password ?? "", user.PasswordHash, user.PasswordSalt))
        {
            _fileLog.Log("AUTH", $"Failed login for '{username}'.");
            ViewData["Error"] = "Invalid username or password.";
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        _fileLog.Log("AUTH", $"Login: '{user.Username}' ({user.Role}).");

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    // POST /Account/Logout
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var name = User.Identity?.Name ?? "?";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _fileLog.Log("AUTH", $"Logout: '{name}'.");
        return RedirectToAction("Login");
    }

    // GET /Account/Denied
    [HttpGet]
    public IActionResult Denied() => View();
}

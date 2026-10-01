using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;

namespace PingDashboard.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var allowed = await Services.NapboxScope.GetAllowedNapboxesAsync(_db, User);

        var query = _db.Clients.AsQueryable();
        if (allowed is not null)
        {
            // Technician: only clients whose napbox is in their assigned set
            var list = allowed.ToList();
            query = query.Where(c => c.NapboxName != null && list.Contains(c.NapboxName));
        }

        // null (Admin) → "*" meaning no restriction; otherwise the allowed list
        ViewBag.AllowedNapboxes = allowed?.ToArray();

        var clients = await query.OrderBy(c => c.Name).ToListAsync();
        return View(clients);
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;

namespace PingDashboard.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var clients = await _db.Clients.OrderBy(c => c.Name).ToListAsync();
        return View(clients);
    }
}

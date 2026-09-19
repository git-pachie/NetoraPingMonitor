using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllersWithViews();

// JSON API controllers also need AddControllers — covered by AddControllersWithViews

// SignalR with camelCase JSON so browser JS gets ipAddress, isConnected, etc.
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase);

// SQLite via EF Core.
// Use AppContext.BaseDirectory so the .db file always lands next to the DLL,
// regardless of whether the app is launched with `dotnet run` or as an exe.
var dbPath = Path.Combine(AppContext.BaseDirectory, "pingdashboard.db");
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

// ── Pipeline ──────────────────────────────────────────────────────────────────

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<PingHub>("/pinghub");

// ── Migrate DB on startup ─────────────────────────────────────────────────────
DbInitialiser.Initialise(app.Services);

app.Run();

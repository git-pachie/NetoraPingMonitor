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

// SQLite via EF Core, using a context POOL so instances are reused instead of
// being allocated/collected per request — lower GC pressure and allocations.
// Default no-tracking: read queries won't build the change-tracker graph
// (writes use explicit ExecuteUpdate/Add, so tracking isn't needed for reads).
var dbPath = Path.Combine(AppContext.BaseDirectory, "pingdashboard.db");
builder.Services.AddDbContextPool<AppDbContext>(o =>
{
    o.UseSqlite($"Data Source={dbPath}");
    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
}, poolSize: 32);

// File logger + email sender (singletons — stateless, config-bound)
builder.Services.AddSingleton<PingDashboard.Services.IFileLogger, PingDashboard.Services.FileLogger>();
builder.Services.AddSingleton<PingDashboard.Services.IEmailSender, PingDashboard.Services.EmailSender>();

// ── Authentication & Authorization ─────────────────────────────────────────────
builder.Services
    .AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Denied";
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name      = "PingDashboard.Auth";
        options.Cookie.HttpOnly  = true;
    });

builder.Services.AddAuthorization(options =>
{
    // Admin-only policy for all create/edit/delete operations
    options.AddPolicy("AdminOnly", p => p.RequireRole(PingDashboard.Models.Roles.Admin));
});

// Background service: 5s after startup, ping all clients and log initial status
builder.Services.AddHostedService<PingDashboard.Services.StartupPingCheck>();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<PingHub>("/pinghub");

// ── Migrate DB on startup ─────────────────────────────────────────────────────
DbInitialiser.Initialise(app.Services);

// Record application start in the file log
using (var scope = app.Services.CreateScope())
{
    var fileLog = scope.ServiceProvider.GetRequiredService<PingDashboard.Services.IFileLogger>();
    fileLog.Log("APP", "PingDashboard started.");
}

app.Run();

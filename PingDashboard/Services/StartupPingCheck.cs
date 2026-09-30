using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Hubs;

namespace PingDashboard.Services;

/// <summary>
/// On startup, resets every client's status to "Unknown" (null) so the
/// dashboard starts from a clean slate. The dashboard does NOT ping clients
/// itself — a client's status only becomes Connected / Not Connected when
/// PingAgent reports a result for that IP via the SignalR hub. Any client the
/// agent never reports on simply stays Unknown.
/// </summary>
public class StartupPingCheck : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHubContext<PingHub> _hub;
    private readonly ILogger<StartupPingCheck> _logger;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);

    public StartupPingCheck(
        IServiceProvider services,
        IHubContext<PingHub> hub,
        ILogger<StartupPingCheck> logger)
    {
        _services = services;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay so the DB is migrated and the host is ready
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        using var scope = _services.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileLog = scope.ServiceProvider.GetRequiredService<IFileLogger>();

        int count;
        try
        {
            // Single bulk UPDATE resets every client to Unknown — far cheaper
            // than loading + tracking + saving each entity individually.
            count = await db.Clients.ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsConnected, (bool?)null)
                .SetProperty(c => c.LastReceivedTime, (DateTime?)null),
                stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartupReset: failed to reset client statuses.");
            return;
        }

        // Broadcast the reset to any connected browsers (lightweight projection,
        // no full entity materialisation).
        try
        {
            var rows = await db.Clients
                .Select(c => new
                {
                    c.Id, c.Name, c.IpAddress,
                    c.NotifyEmail, c.IsNotificationEnabled, c.NapboxName
                })
                .ToListAsync(stoppingToken);

            foreach (var c in rows)
            {
                await _hub.Clients.All.SendAsync("ReceiveStatus", new
                {
                    c.Id,
                    c.Name,
                    c.IpAddress,
                    IsConnected      = (bool?)null,
                    LastReceivedTime = (string?)null,
                    c.NotifyEmail,
                    c.IsNotificationEnabled,
                    c.NapboxName
                }, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartupReset: failed to broadcast reset statuses.");
        }

        fileLog.Log("APP", $"Startup reset: {count} client(s) set to Unknown. Awaiting PingAgent reports.");
        _logger.LogInformation("StartupReset: {Count} client(s) reset to Unknown.", count);
    }
}

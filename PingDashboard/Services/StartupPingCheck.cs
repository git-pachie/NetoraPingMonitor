using System.Net.NetworkInformation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Hubs;
using PingDashboard.Models;

namespace PingDashboard.Services;

/// <summary>
/// Background service that pings every client in the database and logs their
/// current connected/disconnected state:
///   • First sweep runs 5 seconds after startup and logs EVERY client's
///     current state (a full snapshot), regardless of the previously stored
///     status — so restarting the dashboard always produces a fresh log.
///   • Subsequent sweeps run every 5 minutes and log a full snapshot again.
/// Results are also written to the app logger and broadcast to browsers.
/// </summary>
public class StartupPingCheck : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHubContext<PingHub> _hub;
    private readonly ILogger<StartupPingCheck> _logger;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private const int PingTimeoutMs = 5000;

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
        // Initial delay so the DB is migrated and the host is ready
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        // First sweep logs everything (fresh snapshot on each restart)
        await RunSweepAsync(logAll: true, stoppingToken);

        // Periodic sweeps every 5 minutes
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }

            if (stoppingToken.IsCancellationRequested) break;
            await RunSweepAsync(logAll: true, stoppingToken);
        }
    }

    /// <summary>
    /// Pings all clients once and records their state.
    /// When <paramref name="logAll"/> is true, every client is logged;
    /// otherwise only clients whose status changed are logged.
    /// </summary>
    private async Task RunSweepAsync(bool logAll, CancellationToken ct)
    {
        _logger.LogInformation("PingSweep: starting status sweep (logAll={LogAll}).", logAll);

        using var scope = _services.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileLog = scope.ServiceProvider.GetRequiredService<IFileLogger>();
        var email   = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        List<Client> clients;
        try
        {
            clients = await db.Clients.ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PingSweep: failed to load clients.");
            return;
        }

        if (clients.Count == 0)
        {
            _logger.LogInformation("PingSweep: no clients to check.");
            return;
        }

        foreach (var client in clients)
        {
            if (ct.IsCancellationRequested) break;

            bool isConnected = await PingOnceAsync(client.IpAddress);
            var  timestamp   = DateTime.UtcNow;
            var  timeStr     = timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

            bool statusChanged = client.IsConnected != isConnected;
            bool shouldLog     = logAll || statusChanged;
            var  stateText     = isConnected ? "CONNECTED" : "DISCONNECTED";

            if (shouldLog)
            {
                db.StatusLogs.Add(new StatusLog
                {
                    ClientName  = client.Name,
                    IpAddress   = client.IpAddress,
                    IsConnected = isConnected,
                    Timestamp   = timestamp
                });

                if (isConnected)
                    _logger.LogInformation("CONNECTED: {Name} ({Ip})", client.Name, client.IpAddress);
                else
                    _logger.LogWarning("DISCONNECTED: {Name} ({Ip})", client.Name, client.IpAddress);

                fileLog.Log("STATUS", $"{stateText}: {client.Name} ({client.IpAddress})");
            }

            // Send email only on an actual status CHANGE (not on every logAll snapshot),
            // so periodic snapshots don't spam recipients.
            if (statusChanged && client.IsNotificationEnabled && !string.IsNullOrWhiteSpace(client.NotifyEmail))
            {
                var subject = $"[Ping Monitor] {client.Name} is {stateText}";
                var body =
                    $"Client : {client.Name}\n" +
                    $"IP     : {client.IpAddress}\n" +
                    $"Napbox : {client.NapboxName ?? "-"}\n" +
                    $"Status : {stateText}\n" +
                    $"Time   : {timeStr}\n";
                _ = email.SendAsync(client.NotifyEmail, subject, body);
            }

            client.IsConnected      = isConnected;
            client.LastReceivedTime = timestamp;

            await _hub.Clients.All.SendAsync("ReceiveStatus", new
            {
                client.Id,
                client.Name,
                client.IpAddress,
                IsConnected      = isConnected,
                LastReceivedTime = timeStr,
                client.NotifyEmail,
                client.IsNotificationEnabled,
                client.NapboxName
            }, ct);

            if (shouldLog)
            {
                await _hub.Clients.All.SendAsync("ReceiveLog", new
                {
                    ClientName  = client.Name,
                    IpAddress   = client.IpAddress,
                    IsConnected = isConnected,
                    Timestamp   = timeStr
                }, ct);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("PingSweep: complete ({Count} client(s)).", clients.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PingSweep: failed to persist status/logs.");
        }
    }

    private static async Task<bool> PingOnceAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, PingTimeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Models;

namespace PingDashboard.Hubs;

/// <summary>
/// Receives ping results from PingAgent and broadcasts to browser clients.
/// Known IPs (in the Clients table) get a "known" update; unknown IPs are
/// sent separately as an "unknown" event so the dashboard can group them.
///
/// Connectivity transitions are logged (both to the app logger and the
/// StatusLogs table) ONLY when the status differs from the last known status,
/// so a continuously-reporting agent does not flood the log.
/// </summary>
public class PingHub : Hub
{
    private readonly AppDbContext _db;
    private readonly ILogger<PingHub> _logger;

    public PingHub(AppDbContext db, ILogger<PingHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ReportStatus(string name, string ipAddress, bool isConnected)
    {
        var timestamp = DateTime.UtcNow;
        var timeStr   = timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.IpAddress == ipAddress);

        if (client is not null)
        {
            // Capture the previous status BEFORE overwriting it
            bool? previous = client.IsConnected;

            // Log only on a genuine transition:
            //   - previous is null  → first-ever report for this client
            //   - previous != new   → status changed
            bool statusChanged = previous != isConnected;

            if (statusChanged)
            {
                _db.StatusLogs.Add(new StatusLog
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
            }

            // Persist latest status + timestamp (and the log entry if any)
            client.IsConnected      = isConnected;
            client.LastReceivedTime = timestamp;
            await _db.SaveChangesAsync();

            await Clients.All.SendAsync("ReceiveStatus", new
            {
                client.Id,
                client.Name,
                client.IpAddress,
                IsConnected      = isConnected,
                LastReceivedTime = timeStr,
                client.NotifyEmail,
                client.IsNotificationEnabled,
                client.NapboxName
            });

            // Notify browsers of a new log entry so the Logs page can update live
            if (statusChanged)
            {
                await Clients.All.SendAsync("ReceiveLog", new
                {
                    ClientName  = client.Name,
                    IpAddress   = client.IpAddress,
                    IsConnected = isConnected,
                    Timestamp   = timeStr
                });
            }
        }
        else
        {
            // IP not in the database → unknown client (not logged)
            await Clients.All.SendAsync("ReceiveUnknown", new
            {
                Name             = string.IsNullOrWhiteSpace(name) ? ipAddress : name,
                IpAddress        = ipAddress,
                IsConnected      = isConnected,
                LastReceivedTime = timeStr
            });
        }
    }
}

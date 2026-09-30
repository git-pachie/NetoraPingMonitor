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
    private readonly Services.IFileLogger _fileLog;
    private readonly Services.IEmailSender _email;

    public PingHub(
        AppDbContext db,
        ILogger<PingHub> logger,
        Services.IFileLogger fileLog,
        Services.IEmailSender email)
    {
        _db = db;
        _logger = logger;
        _fileLog = fileLog;
        _email = email;
    }

    public async Task ReportStatus(string name, string ipAddress, bool isConnected)
    {
        var timestamp = DateTime.UtcNow;
        var timeStr   = timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        // Look up the client without EF change-tracking — we only need to read
        // its current values; the actual write is done via a targeted UPDATE.
        var client = await _db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IpAddress == ipAddress);

        if (client is not null)
        {
            bool? previous = client.IsConnected;
            bool statusChanged = previous != isConnected;

            if (statusChanged)
            {
                var stateText = isConnected ? "CONNECTED" : "DISCONNECTED";

                // Log the transition to the DB, app logger, and file log
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

                _fileLog.Log("STATUS", $"{stateText}: {client.Name} ({client.IpAddress})");

                // Persist the status log row
                await _db.SaveChangesAsync();

                // Persist the client's new status + timestamp via a single
                // targeted UPDATE (no entity tracking / full-row materialisation).
                await _db.Clients
                    .Where(c => c.Id == client.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.IsConnected, isConnected)
                        .SetProperty(c => c.LastReceivedTime, timestamp));

                // Fire-and-forget notification email
                if (client.IsNotificationEnabled && !string.IsNullOrWhiteSpace(client.NotifyEmail))
                {
                    var subject = $"[Ping Monitor] {client.Name} is {stateText}";
                    var body =
                        $"Client : {client.Name}\n" +
                        $"IP     : {client.IpAddress}\n" +
                        $"Napbox : {client.NapboxName ?? "-"}\n" +
                        $"Status : {stateText}\n" +
                        $"Time   : {timeStr}\n";
                    _ = _email.SendAsync(client.NotifyEmail, subject, body);
                }
            }
            // NOTE: when the status is UNCHANGED we deliberately skip the DB
            // write. LastReceivedTime in the DB is only used for the first page
            // render; browsers already track "live" time client-side. This
            // avoids a disk write on every single ping (the main resource cost).

            // Always broadcast the live status to browsers
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
            // IP not in the database → unknown client (not logged, not persisted)
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

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;

namespace PingDashboard.Hubs;

/// <summary>
/// Receives ping results from PingAgent and broadcasts to browser clients.
/// Known IPs (in the Clients table) get a "known" update; unknown IPs are
/// sent separately as an "unknown" event so the dashboard can group them.
/// </summary>
public class PingHub : Hub
{
    private readonly AppDbContext _db;

    public PingHub(AppDbContext db) => _db = db;

    /// <summary>
    /// Called by PingAgent.
    /// Checks whether the IP is registered in the DB.
    ///   - Known   → broadcast "ReceiveStatus"   (updates the main table)
    ///   - Unknown → broadcast "ReceiveUnknown"  (populates the unknown section)
    /// Also persists the latest status back to the DB for known clients.
    /// </summary>
    public async Task ReportStatus(string name, string ipAddress, bool isConnected)
    {
        var timestamp = DateTime.UtcNow;
        var timeStr   = timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.IpAddress == ipAddress);

        if (client is not null)
        {
            // Update persisted status
            client.IsConnected      = isConnected;
            client.LastReceivedTime = timestamp;
            await _db.SaveChangesAsync();

            await Clients.All.SendAsync("ReceiveStatus", new
            {
                client.Id,
                client.Name,
                client.IpAddress,
                IsConnected      = isConnected,
                LastReceivedTime = timeStr
            });
        }
        else
        {
            // IP not in the database → unknown client
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

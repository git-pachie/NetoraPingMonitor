using System.Collections.Concurrent;
using PingDashboard.Models;

namespace PingDashboard.Services;

/// <summary>
/// Thread-safe in-memory store that holds the latest ping status for every client.
/// Registered as a singleton so both the Hub and the Controller share the same instance.
/// </summary>
public class ClientStatusStore
{
    private readonly ConcurrentDictionary<string, ClientStatus> _statuses = new();

    /// <summary>Update (or insert) the status for a given IP address.</summary>
    public ClientStatus Upsert(string name, string ipAddress, bool isConnected)
    {
        var entry = _statuses.AddOrUpdate(
            ipAddress,
            _ => new ClientStatus
            {
                Name = name,
                IpAddress = ipAddress,
                IsConnected = isConnected,
                LastReceivedTime = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.Name = name;
                existing.IsConnected = isConnected;
                existing.LastReceivedTime = DateTime.UtcNow;
                return existing;
            });

        return entry;
    }

    /// <summary>Return all known client statuses ordered by name.</summary>
    public IReadOnlyList<ClientStatus> GetAll() =>
        _statuses.Values.OrderBy(c => c.Name).ToList();
}

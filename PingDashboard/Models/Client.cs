using System.ComponentModel.DataAnnotations;

namespace PingDashboard.Models;

/// <summary>A monitored client stored in SQLite.</summary>
public class Client
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Latest ping status — updated in-memory by SignalR, persisted per upsert.</summary>
    public bool? IsConnected { get; set; }

    public DateTime? LastReceivedTime { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace PingDashboard.Models;

/// <summary>A logged connectivity transition (connected/disconnected) for a client.</summary>
public class StatusLog
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string ClientName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>true = became Connected, false = became Not Connected.</summary>
    public bool IsConnected { get; set; }

    /// <summary>UTC time the transition was recorded.</summary>
    public DateTime Timestamp { get; set; }
}

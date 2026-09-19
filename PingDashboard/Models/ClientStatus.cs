namespace PingDashboard.Models;

public class ClientStatus
{
    /// <summary>Friendly display name for the client.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>IP address being monitored.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>true = reachable, false = unreachable, null = never received.</summary>
    public bool? IsConnected { get; set; }

    /// <summary>UTC timestamp of the last status update received from the agent.</summary>
    public DateTime? LastReceivedTime { get; set; }
}

namespace PingAgent;

public class AgentSettings
{
    /// <summary>Full URL of the PingDashboard SignalR hub.</summary>
    public string DashboardHubUrl { get; set; } = "http://localhost:5000/pinghub";

    /// <summary>Milliseconds to wait between each full ping sweep.</summary>
    public int IntervalMs { get; set; } = 3000;

    /// <summary>Milliseconds before a single ping attempt is considered timed out.</summary>
    public int PingTimeoutMs { get; set; } = 5000;

    /// <summary>List of clients (name + IP) to monitor.</summary>
    public List<ClientEntry> Clients { get; set; } = new();
}

public class ClientEntry
{
    public string Name      { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

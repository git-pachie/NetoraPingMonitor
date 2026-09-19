namespace PingMonitor;

public class PingSettings
{
    /// <summary>Host or IP address to ping.</summary>
    public string Host { get; set; } = "8.8.8.8";

    /// <summary>Log a warning when round-trip time exceeds this value (ms).</summary>
    public long ThresholdMs { get; set; } = 20;

    /// <summary>How long to wait between each ping (ms).</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Path of the log file. Relative paths are resolved next to the executable.</summary>
    public string LogFilePath { get; set; } = "ping_log.txt";
}

using System.Net.NetworkInformation;
using Microsoft.Extensions.Configuration;
using PingMonitor;

// ── Configuration ────────────────────────────────────────────────────────────
IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

PingSettings settings = config.GetSection("PingMonitor").Get<PingSettings>()
    ?? throw new InvalidOperationException("'PingMonitor' section is missing from appsettings.json.");

// Resolve log file path relative to the executable directory
string logFilePath = Path.IsPathRooted(settings.LogFilePath)
    ? settings.LogFilePath
    : Path.Combine(AppContext.BaseDirectory, settings.LogFilePath);

// ── Banner ────────────────────────────────────────────────────────────────────
Console.WriteLine("=================================================");
Console.WriteLine("  Ping Monitor");
Console.WriteLine("=================================================");
Console.WriteLine($"  Target   : {settings.Host}");
Console.WriteLine($"  Threshold: {settings.ThresholdMs} ms");
Console.WriteLine($"  Interval : {settings.IntervalMs} ms");
Console.WriteLine($"  Log file : {logFilePath}");
Console.WriteLine("=================================================");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

// ── Cancellation ─────────────────────────────────────────────────────────────
using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // prevent immediate process kill
    Console.WriteLine("\nStopping...");
    cts.Cancel();
};

// ── Ping loop ─────────────────────────────────────────────────────────────────
using Ping pinger = new();

while (!cts.Token.IsCancellationRequested)
{
    try
    {
        PingReply reply = await pinger.SendPingAsync(settings.Host, timeout: 5_000);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (reply.Status == IPStatus.Success)
        {
            long rtt = reply.RoundtripTime;
            string statusText = rtt > settings.ThresholdMs ? "SLOW" : "OK";
            string consoleLine = $"[{timestamp}] {settings.Host}  {rtt,5} ms  [{statusText}]";

            Console.WriteLine(consoleLine);

            if (rtt > settings.ThresholdMs)
            {
                string logLine = $"[{timestamp}] THRESHOLD EXCEEDED  {rtt} ms  (threshold: {settings.ThresholdMs} ms)";
                await WriteLogAsync(logFilePath, logLine);
            }
        }
        else
        {
            string consoleLine = $"[{timestamp}] {settings.Host}  Status: {reply.Status}  [FAILED]";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(consoleLine);
            Console.ResetColor();

            string logLine = $"[{timestamp}] PING FAILED  Status: {reply.Status}";
            await WriteLogAsync(logFilePath, logLine);
        }
    }
    catch (PingException ex)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{timestamp}] Ping error: {ex.Message}");
        Console.ResetColor();

        await WriteLogAsync(logFilePath, $"[{timestamp}] PING ERROR  {ex.Message}");
    }

    try
    {
        await Task.Delay(settings.IntervalMs, cts.Token);
    }
    catch (TaskCanceledException)
    {
        // graceful shutdown — exit loop
        break;
    }
}

Console.WriteLine("Monitor stopped.");

// ── Helper ────────────────────────────────────────────────────────────────────
static async Task WriteLogAsync(string path, string line)
{
    try
    {
        await File.AppendAllTextAsync(path, line + Environment.NewLine);
    }
    catch (IOException ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARNING] Could not write to log file: {ex.Message}");
        Console.ResetColor();
    }
}

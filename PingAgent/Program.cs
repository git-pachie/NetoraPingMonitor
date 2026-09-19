using System.Net.NetworkInformation;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using PingAgent;

// ── Configuration ─────────────────────────────────────────────────────────────
IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

AgentSettings settings = config.GetSection("PingAgent").Get<AgentSettings>()
    ?? throw new InvalidOperationException("'PingAgent' section missing from appsettings.json.");

if (settings.Clients.Count == 0)
    throw new InvalidOperationException("No clients defined in appsettings.json → PingAgent → Clients.");

// ── Banner ────────────────────────────────────────────────────────────────────
Console.WriteLine("=================================================");
Console.WriteLine("  PingAgent");
Console.WriteLine("=================================================");
Console.WriteLine($"  Hub URL   : {settings.DashboardHubUrl}");
Console.WriteLine($"  Interval  : {settings.IntervalMs} ms");
Console.WriteLine($"  Timeout   : {settings.PingTimeoutMs} ms");
Console.WriteLine($"  Clients   : {settings.Clients.Count}");
foreach (var c in settings.Clients)
    Console.WriteLine($"             {c.Name,-28} {c.IpAddress}");
Console.WriteLine("=================================================");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

// ── Cancellation ─────────────────────────────────────────────────────────────
using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nStopping...");
    cts.Cancel();
};

// ── SignalR connection ────────────────────────────────────────────────────────
HubConnection hub = new HubConnectionBuilder()
    .WithUrl(settings.DashboardHubUrl)
    .Build();   // No WithAutomaticReconnect — we manage reconnection ourselves below

hub.Closed += async ex =>
{
    if (!cts.Token.IsCancellationRequested)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[HUB] Connection closed. {ex?.Message}");
        Console.ResetColor();
        // Reconnect loop will be handled by EnsureConnectedAsync inside the ping loop
    }
    await Task.CompletedTask;
};

// ── Ping loop ─────────────────────────────────────────────────────────────────
// EnsureConnectedAsync is called before every sweep — it re-establishes the
// connection if it dropped, with backoff, without ever giving up.
while (!cts.Token.IsCancellationRequested)
{
    await EnsureConnectedAsync(hub, settings.DashboardHubUrl, cts.Token);

    if (cts.Token.IsCancellationRequested) break;

    var tasks = settings.Clients
        .Select(client => PingAndReportAsync(client, hub, settings, cts.Token));
    await Task.WhenAll(tasks);

    try { await Task.Delay(settings.IntervalMs, cts.Token); }
    catch (TaskCanceledException) { break; }
}

await hub.StopAsync();
Console.WriteLine("Agent stopped.");

// ── Helpers ───────────────────────────────────────────────────────────────────

/// <summary>
/// Ensures the hub is Connected. If it is Disconnected, attempts StartAsync
/// with exponential backoff until it succeeds or cancellation is requested.
/// </summary>
static async Task EnsureConnectedAsync(HubConnection hub, string url, CancellationToken ct)
{
    if (hub.State == HubConnectionState.Connected) return;

    int attempt = 0;
    while (!ct.IsCancellationRequested)
    {
        // If a previous reconnect is still in progress, wait a moment
        if (hub.State == HubConnectionState.Connecting ||
            hub.State == HubConnectionState.Reconnecting)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (hub.State == HubConnectionState.Connected) return;
            continue;
        }

        attempt++;
        try
        {
            await hub.StartAsync(ct);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[HUB] Connected to {url}  (attempt {attempt})");
            Console.ResetColor();
            return;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            int delaySec = Math.Min(attempt * 2, 30);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[HUB] Connect attempt {attempt} failed: {ex.Message}. Retry in {delaySec}s…");
            Console.ResetColor();
            try { await Task.Delay(TimeSpan.FromSeconds(delaySec), ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}

static async Task PingAndReportAsync(
    ClientEntry client,
    HubConnection hub,
    AgentSettings settings,
    CancellationToken ct)
{
    if (ct.IsCancellationRequested) return;

    string timestamp  = DateTime.Now.ToString("HH:mm:ss");
    bool   isConnected = false;

    try
    {
        using var pinger = new Ping();
        PingReply reply = await pinger.SendPingAsync(client.IpAddress, settings.PingTimeoutMs);
        isConnected = reply.Status == IPStatus.Success;

        string label = isConnected ? "OK  " : "FAIL";
        string rtt   = isConnected ? $"{reply.RoundtripTime,5} ms" : "  ---  ";
        Console.ForegroundColor = isConnected ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"[{timestamp}] [{label}] {client.Name,-28} {client.IpAddress,-20} {rtt}");
        Console.ResetColor();
    }
    catch (PingException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{timestamp}] [ERR ] {client.Name,-28} {client.IpAddress,-20} {ex.Message}");
        Console.ResetColor();
    }

    // Send to hub only if connected (EnsureConnectedAsync already ran before this sweep)
    if (hub.State == HubConnectionState.Connected)
    {
        try
        {
            await hub.InvokeAsync("ReportStatus", client.Name, client.IpAddress, isConnected, ct);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[HUB] Send failed for {client.IpAddress}: {ex.Message}");
            Console.ResetColor();
        }
    }
}

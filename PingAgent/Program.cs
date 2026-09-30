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

// ── Cancellation ─────────────────────────────────────────────────────────────
using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// ── Draw the fixed dashboard once ──────────────────────────────────────────────
Dashboard.Init(settings);

// ── SignalR connection ────────────────────────────────────────────────────────
HubConnection hub = new HubConnectionBuilder()
    .WithUrl(settings.DashboardHubUrl)
    .Build();

hub.Closed += async ex =>
{
    if (!cts.Token.IsCancellationRequested)
        Dashboard.SetHubStatus("Disconnected — reconnecting…", ConsoleColor.Red);
    await Task.CompletedTask;
};

// ── Ping loop ─────────────────────────────────────────────────────────────────
while (!cts.Token.IsCancellationRequested)
{
    await EnsureConnectedAsync(hub, settings.DashboardHubUrl, cts.Token);
    if (cts.Token.IsCancellationRequested) break;

    var tasks = settings.Clients
        .Select((client, index) => PingAndReportAsync(client, index, hub, settings, cts.Token));
    await Task.WhenAll(tasks);

    try { await Task.Delay(settings.IntervalMs, cts.Token); }
    catch (TaskCanceledException) { break; }
}

await hub.StopAsync();
Dashboard.SetHubStatus("Agent stopped.", ConsoleColor.Yellow);
Dashboard.MoveCursorBelow();

// ── Helpers ───────────────────────────────────────────────────────────────────

static async Task EnsureConnectedAsync(HubConnection hub, string url, CancellationToken ct)
{
    if (hub.State == HubConnectionState.Connected) return;

    int attempt = 0;
    while (!ct.IsCancellationRequested)
    {
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
            Dashboard.SetHubStatus($"Connecting… (attempt {attempt})", ConsoleColor.Yellow);
            await hub.StartAsync(ct);
            Dashboard.SetHubStatus($"Connected to {url}", ConsoleColor.Green);
            return;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            int delaySec = Math.Min(attempt * 2, 30);
            Dashboard.SetHubStatus($"Connect failed: {ex.Message}. Retry in {delaySec}s", ConsoleColor.Red);
            try { await Task.Delay(TimeSpan.FromSeconds(delaySec), ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}

static async Task PingAndReportAsync(
    ClientEntry client,
    int rowIndex,
    HubConnection hub,
    AgentSettings settings,
    CancellationToken ct)
{
    if (ct.IsCancellationRequested) return;

    bool isConnected = false;
    long rtt = 0;
    bool errored = false;

    try
    {
        using var pinger = new Ping();
        PingReply reply = await pinger.SendPingAsync(client.IpAddress, settings.PingTimeoutMs);
        isConnected = reply.Status == IPStatus.Success;
        rtt = reply.RoundtripTime;
    }
    catch (PingException)
    {
        errored = true;
    }

    Dashboard.UpdateRow(rowIndex, isConnected, rtt, errored);

    if (hub.State == HubConnectionState.Connected)
    {
        try
        {
            await hub.InvokeAsync("ReportStatus", client.Name, client.IpAddress, isConnected, ct);
        }
        catch
        {
            // swallowed — the hub status line reflects connection health
        }
    }
}

/// <summary>
/// Renders a fixed, non-scrolling status table and updates individual rows in
/// place. Column widths adapt to the current console width, and every cursor
/// operation is bounds-checked so a narrow window can never crash the app.
/// </summary>
static class Dashboard
{
    private static readonly object _lock = new();

    // Column X positions — computed in Init() from the console width.
    private static int _colNum, _colName, _colIp, _colStatus, _colRtt, _colTime;
    private static int _nameWidth, _ipWidth;
    private static int _width;          // usable console width

    private static int _tableTopRow;
    private static int _hubRow;
    private static int _rowCount;
    private static bool _interactive;

    // Per-row cached display text so Name/IP can be recoloured on status change.
    private static string[] _names = Array.Empty<string>();
    private static string[] _ips   = Array.Empty<string>();

    public static void Init(AgentSettings settings)
    {
        lock (_lock)
        {
            _interactive = !Console.IsOutputRedirected;
            _rowCount = settings.Clients.Count;

            if (!_interactive)
            {
                Console.WriteLine("PingAgent started (non-interactive output — live table disabled).");
                return;
            }

            // ── Compute a layout that fits the actual window ───────────────────
            _width = Math.Max(40, SafeWindowWidth());

            _colNum    = 0;
            _colName   = 4;
            // Give Name up to 40% of the remaining space, IP a fixed chunk.
            int remaining = _width - _colName;
            _nameWidth = Math.Clamp(remaining * 40 / 100, 10, 28);
            _colIp     = _colName + _nameWidth + 1;
            _ipWidth   = Math.Clamp((_width - _colIp) * 40 / 100, 12, 24);
            _colStatus = _colIp + _ipWidth + 1;
            _colRtt    = Math.Min(_colStatus + 14, _width - 12);
            _colTime   = Math.Min(_colRtt + 9, _width - 9);

            Console.CursorVisible = false;
            try { Console.Clear(); } catch { /* ignore */ }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(Fit("  PingAgent — Live Network Monitor"));
            Console.ResetColor();
            Console.WriteLine(Fit($"  Hub: {settings.DashboardHubUrl}   Interval: {settings.IntervalMs}ms   Clients: {_rowCount}"));
            Console.WriteLine(new string('-', _width));

            // Header row
            int headerRow = SafeTop();
            Console.ForegroundColor = ConsoleColor.Gray;
            PutRaw(_colName,   headerRow, "NAME");
            PutRaw(_colIp,     headerRow, "IP");
            PutRaw(_colStatus, headerRow, "STATUS");
            if (_colRtt  < _width) PutRaw(_colRtt,  headerRow, "RTT");
            if (_colTime < _width) PutRaw(_colTime, headerRow, "UPDATED");
            Console.ResetColor();
            SafeSetCursor(0, headerRow + 1);
            Console.Write(new string('-', _width));

            _tableTopRow = headerRow + 2;

            // Cache display text for each row so Name/IP can be recoloured later
            _names = new string[_rowCount];
            _ips   = new string[_rowCount];

            // One line per client, initially "Pending"
            for (int i = 0; i < _rowCount; i++)
            {
                var c = settings.Clients[i];
                int row = _tableTopRow + i;
                _names[i] = Trunc(c.Name, _nameWidth);
                _ips[i]   = Trunc(c.IpAddress, _ipWidth);
                PutRaw(_colNum,  row, $"{i + 1,2}.");
                PutRaw(_colName, row, _names[i]);
                PutRaw(_colIp,   row, _ips[i]);
                PutColored(_colStatus, row, "PENDING", ConsoleColor.DarkGray);
            }

            _hubRow = _tableTopRow + _rowCount + 1;
            SafeSetCursor(0, _hubRow);
            Console.Write(new string('-', _width));
            SetHubStatus("Starting…", ConsoleColor.Yellow);
        }
    }

    public static void UpdateRow(int index, bool isConnected, long rtt, bool errored)
    {
        lock (_lock)
        {
            if (!_interactive)
            {
                var state = errored ? "ERROR" : isConnected ? "CONNECTED" : "NOT CONNECTED";
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} row {index + 1}: {state} {(isConnected ? rtt + "ms" : "")}");
                return;
            }

            int row = _tableTopRow + index;

            // Colour for the whole row: red when down/errored, default when up.
            bool down = errored || !isConnected;
            ConsoleColor nameColor = down ? ConsoleColor.Red : ConsoleColor.Gray;

            // Recolour NAME + IP to match the connection state
            if (index >= 0 && index < _names.Length)
            {
                ClearCell(_colName, row, _nameWidth);
                PutColored(_colName, row, _names[index], nameColor);
                ClearCell(_colIp, row, _ipWidth);
                PutColored(_colIp, row, _ips[index], nameColor);
            }

            // STATUS cell
            int statusWidth = Math.Max(6, _colRtt - _colStatus - 1);
            ClearCell(_colStatus, row, statusWidth);
            if (errored)
                PutColored(_colStatus, row, "ERROR", ConsoleColor.Red);
            else if (isConnected)
                PutColored(_colStatus, row, "* CONNECTED", ConsoleColor.Green);
            else
                PutColored(_colStatus, row, "* NOT CONN", ConsoleColor.Red);

            // RTT cell (only if it fits)
            if (_colRtt < _width)
            {
                int rttWidth = Math.Max(3, _colTime - _colRtt - 1);
                ClearCell(_colRtt, row, rttWidth);
                PutRaw(_colRtt, row, isConnected ? $"{rtt} ms" : "-");
            }

            // UPDATED cell (only if it fits)
            if (_colTime < _width)
            {
                ClearCell(_colTime, row, Math.Min(9, _width - _colTime));
                PutColored(_colTime, row, DateTime.Now.ToString("HH:mm:ss"), ConsoleColor.DarkGray);
            }
        }
    }

    public static void SetHubStatus(string text, ConsoleColor color)
    {
        lock (_lock)
        {
            if (!_interactive) { Console.WriteLine($"HUB: {text}"); return; }

            int row = _hubRow + 1;
            ClearCell(0, row, _width);
            PutColored(2, row, Fit($"HUB: {text}", _width - 2), color);
        }
    }

    public static void MoveCursorBelow()
    {
        lock (_lock)
        {
            if (!_interactive) return;
            SafeSetCursor(0, _hubRow + 3);
            Console.CursorVisible = true;
        }
    }

    // ── low-level, always bounds-checked ───────────────────────────────────────

    private static int SafeWindowWidth()
    {
        try { return Console.WindowWidth > 0 ? Console.WindowWidth : 80; }
        catch { return 80; }
    }

    private static int SafeTop()
    {
        try { return Console.CursorTop; } catch { return 0; }
    }

    /// <summary>SetCursorPosition that silently clamps/ignores out-of-range values.</summary>
    private static bool SafeSetCursor(int left, int top)
    {
        try
        {
            int maxLeft = Math.Max(0, Console.BufferWidth - 1);
            int maxTop  = Math.Max(0, Console.BufferHeight - 1);
            if (left < 0 || top < 0 || left > maxLeft || top > maxTop) return false;
            Console.SetCursorPosition(left, top);
            return true;
        }
        catch { return false; }
    }

    private static void PutRaw(int col, int row, string text)
    {
        if (!SafeSetCursor(col, row)) return;
        Console.Write(ClipToWidth(col, text));
    }

    private static void PutColored(int col, int row, string text, ConsoleColor color)
    {
        if (!SafeSetCursor(col, row)) return;
        Console.ForegroundColor = color;
        Console.Write(ClipToWidth(col, text));
        Console.ResetColor();
    }

    private static void ClearCell(int col, int row, int width)
    {
        if (!SafeSetCursor(col, row)) return;
        int w = Math.Max(0, Math.Min(width, _width - col));
        Console.Write(new string(' ', w));
    }

    // Never let a write run past the right edge of the window.
    private static string ClipToWidth(int col, string text)
    {
        int avail = Math.Max(0, _width - col);
        return text.Length <= avail ? text : text.Substring(0, avail);
    }

    private static string Fit(string s, int? width = null)
    {
        int w = width ?? _width;
        if (w <= 0) return s;
        return s.Length <= w ? s : s.Substring(0, w);
    }

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, Math.Max(1, max - 1)) + "…");
}

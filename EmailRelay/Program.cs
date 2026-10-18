using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text.Json.Serialization;

// ── EmailRelay ─────────────────────────────────────────────────────────────────
// A tiny HTTP → SMTP relay. Host this where outbound SMTP (port 587) WORKS.
// Clients (e.g. PingDashboard on a network that blocks SMTP) POST the email
// details over HTTP(S), and this service does the actual SMTP send.
//
//   POST /api/send
//   Header: X-Api-Key: <shared secret>
//   Body (JSON): { "to": "a@x.com,b@y.com", "subject": "...", "body": "..." }
//
// Config (appsettings.json or environment variables):
//   Relay:ApiKey            shared secret required in X-Api-Key header
//   Smtp:Host / Port / UseSsl / FromAddress / FromName / Username / Password
// ---------------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var cfg = app.Configuration;
var log = app.Logger;

// Bind SMTP + relay settings once at startup.
var smtp = new SmtpSettings();
cfg.GetSection("Smtp").Bind(smtp);
var apiKey = cfg["Relay:ApiKey"] ?? "";

// ── Startup self-test: confirm this host can actually reach SMTP port 587 ───────
_ = Task.Run(async () =>
{
    try
    {
        using var tcp = new TcpClient();
        var connect = tcp.ConnectAsync(smtp.Host, smtp.Port);
        var done = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(8)));
        if (done == connect && tcp.Connected)
            log.LogInformation("SMTP self-test OK: reached {Host}:{Port}", smtp.Host, smtp.Port);
        else
            log.LogError("SMTP self-test FAILED: cannot reach {Host}:{Port} (port blocked?)", smtp.Host, smtp.Port);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "SMTP self-test error for {Host}:{Port}", smtp.Host, smtp.Port);
    }
});

// ── Health check ────────────────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new { service = "EmailRelay", status = "up" }));

// ── Connectivity test endpoint (no email sent) ──────────────────────────────────
app.MapGet("/api/test", async () =>
{
    try
    {
        using var tcp = new TcpClient();
        var connect = tcp.ConnectAsync(smtp.Host, smtp.Port);
        var done = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(8)));
        bool ok = done == connect && tcp.Connected;
        return Results.Ok(new { smtpHost = smtp.Host, smtp.Port, reachable = ok });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { smtpHost = smtp.Host, smtp.Port, reachable = false, error = ex.Message });
    }
});

// ── Send endpoint ────────────────────────────────────────────────────────────────
app.MapPost("/api/send", async (HttpRequest http, SendRequest? req) =>
{
    // API key check (skip only if no key is configured — not recommended)
    if (!string.IsNullOrEmpty(apiKey))
    {
        var provided = http.Headers["X-Api-Key"].ToString();
        if (!FixedTimeEquals(provided, apiKey))
            return Results.Json(new { ok = false, error = "Invalid or missing X-Api-Key." }, statusCode: 401);
    }

    if (req is null || string.IsNullOrWhiteSpace(req.To))
        return Results.BadRequest(new { ok = false, error = "Field 'to' is required." });

    var recipients = req.To
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToArray();
    if (recipients.Length == 0)
        return Results.BadRequest(new { ok = false, error = "No valid recipients." });

    try
    {
        using var message = new MailMessage
        {
            From       = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject    = req.Subject ?? "",
            Body       = req.Body ?? "",
            IsBodyHtml = req.IsHtml
        };
        foreach (var r in recipients)
            message.To.Add(r);

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl      = smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(smtp.Username))
            client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);

        await client.SendMailAsync(message);

        log.LogInformation("Relayed email to [{To}] — {Subject}", string.Join(", ", recipients), req.Subject);
        return Results.Ok(new { ok = true, sentTo = recipients });
    }
    catch (SmtpException ex)
    {
        var detail = ex.InnerException?.Message ?? ex.Message;
        log.LogError(ex, "Relay SMTP failure ({Status})", ex.StatusCode);
        return Results.Json(new { ok = false, error = $"SMTP {ex.StatusCode}: {ex.Message} | {detail}" }, statusCode: 502);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Relay send failure");
        return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
    }
});

app.Run();

// Constant-time string compare to avoid leaking the key via timing.
static bool FixedTimeEquals(string a, string b)
{
    var ba = System.Text.Encoding.UTF8.GetBytes(a);
    var bb = System.Text.Encoding.UTF8.GetBytes(b);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
}

// ── DTOs / settings ──────────────────────────────────────────────────────────────
public class SendRequest
{
    [JsonPropertyName("to")]      public string? To { get; set; }
    [JsonPropertyName("subject")] public string? Subject { get; set; }
    [JsonPropertyName("body")]    public string? Body { get; set; }
    [JsonPropertyName("isHtml")]  public bool IsHtml { get; set; }
}

public class SmtpSettings
{
    public string Host        { get; set; } = "smtp.gmail.com";
    public int    Port        { get; set; } = 587;
    public bool   UseSsl      { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string FromName    { get; set; } = "Ping Dashboard";
    public string? Username   { get; set; }
    public string? Password   { get; set; }
}

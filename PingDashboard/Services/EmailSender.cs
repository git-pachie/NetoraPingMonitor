using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PingDashboard.Services;

public interface IEmailSender
{
    /// <summary>
    /// Send a plain-text email to a comma-separated recipient list.
    /// Returns true if the message was accepted for delivery.
    /// Logs the outcome to the file log. Sends ONCE — no retry.
    /// </summary>
    Task<bool> SendAsync(string recipientsCsv, string subject, string body);
}

/// <summary>SMTP settings bound from configuration (appsettings + user secrets / env).</summary>
public class SmtpSettings
{
    public bool   Enabled     { get; set; }
    public string Host        { get; set; } = "";
    public int    Port        { get; set; } = 587;
    public bool   UseSsl      { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string FromName    { get; set; } = "Ping Dashboard";
    public string? Username   { get; set; }
    public string? Password   { get; set; }
}

/// <summary>
/// HTTP relay settings. Use this when outbound SMTP ports (587/465) are blocked
/// by the network. PingDashboard POSTs the email over HTTP to an EmailRelay
/// service hosted elsewhere (on a network where SMTP works), and that service
/// performs the actual SMTP send.
/// </summary>
public class EmailApiSettings
{
    public bool   Enabled { get; set; }
    public string Url     { get; set; } = "";   // e.g. http://relay-host:5020/api/send
    public string? ApiKey { get; set; }          // sent as X-Api-Key header
}

public class EmailSender : IEmailSender
{
    private readonly SmtpSettings _smtp;
    private readonly EmailApiSettings _api;
    private readonly string _provider;        // "Api" or "Smtp"
    private readonly IFileLogger _fileLog;
    private readonly ILogger<EmailSender> _logger;

    // Shared HttpClient for the relay API (this service is a singleton → one client).
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public EmailSender(IConfiguration config, IFileLogger fileLog, ILogger<EmailSender> logger)
    {
        _smtp = new SmtpSettings();
        config.GetSection("Smtp").Bind(_smtp);

        _api = new EmailApiSettings();
        config.GetSection("EmailApi").Bind(_api);

        // Explicit provider wins; otherwise auto-pick the API if it's enabled.
        _provider = (config["Email:Provider"] ?? "").Trim();
        if (string.IsNullOrEmpty(_provider))
            _provider = _api.Enabled ? "Api" : "Smtp";

        _fileLog = fileLog;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string recipientsCsv, string subject, string body)
    {
        var recipients = (recipientsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (recipients.Length == 0)
        {
            _fileLog.Log("EMAIL", $"Skipped (no recipients): {subject}");
            return false;
        }

        // Route to the configured provider. One attempt — no retry.
        return _provider.Equals("Api", StringComparison.OrdinalIgnoreCase)
            ? await SendViaApiAsync(recipientsCsv!, recipients, subject, body)
            : await SendViaSmtpAsync(recipients, subject, body);
    }

    // ── HTTP relay (POST to EmailRelay, works over port 80/443) ──────────────────
    private async Task<bool> SendViaApiAsync(string recipientsCsv, string[] recipients, string subject, string body)
    {
        if (!_api.Enabled)
        {
            _fileLog.Log("EMAIL", $"Skipped (Email API disabled): {subject}");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_api.Url))
        {
            _fileLog.Log("EMAIL", $"Skipped (Email API URL missing): {subject}");
            return false;
        }

        try
        {
            var payload = new RelayRequest
            {
                To      = recipientsCsv,
                Subject = subject,
                Body    = body,
                IsHtml  = false
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _api.Url)
            {
                Content = JsonContent.Create(payload)
            };
            if (!string.IsNullOrWhiteSpace(_api.ApiKey))
                req.Headers.Add("X-Api-Key", _api.ApiKey);

            using var resp = await _http.SendAsync(req);

            if (resp.IsSuccessStatusCode)
            {
                _fileLog.Log("EMAIL", $"Sent (API) to [{string.Join(", ", recipients)}] — {subject}");
                _logger.LogInformation("Email relayed via API to {Recipients}: {Subject}",
                    string.Join(", ", recipients), subject);
                return true;
            }

            var errBody = await resp.Content.ReadAsStringAsync();
            _fileLog.Log("EMAIL",
                $"FAILED (API) to [{string.Join(", ", recipients)}] — {subject} — " +
                $"HTTP {(int)resp.StatusCode} {resp.StatusCode}: {errBody}");
            _logger.LogError("Email API send failed ({Status}): {Body}", resp.StatusCode, errBody);
            return false;
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            _fileLog.Log("EMAIL",
                $"FAILED (API) to [{string.Join(", ", recipients)}] — {subject} — {ex.Message} | {detail}");
            _logger.LogError(ex, "Email API send threw: {Subject}", subject);
            return false;
        }
    }

    // ── Classic SMTP (ports 587/465) ─────────────────────────────────────────────
    private async Task<bool> SendViaSmtpAsync(string[] recipients, string subject, string body)
    {
        if (!_smtp.Enabled)
        {
            _fileLog.Log("EMAIL", $"Skipped (SMTP disabled): {subject}");
            return false;
        }

        try
        {
            using var message = new MailMessage
            {
                From    = new MailAddress(_smtp.FromAddress, _smtp.FromName),
                Subject = subject,
                Body    = body,
                IsBodyHtml = false
            };
            foreach (var r in recipients)
                message.To.Add(r);

            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = _smtp.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(_smtp.Username))
                client.Credentials = new NetworkCredential(_smtp.Username, _smtp.Password);

            await client.SendMailAsync(message);

            _fileLog.Log("EMAIL", $"Sent (SMTP) to [{string.Join(", ", recipients)}] — {subject}");
            _logger.LogInformation("Email sent via SMTP to {Recipients}: {Subject}",
                string.Join(", ", recipients), subject);
            return true;
        }
        catch (SmtpException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            _fileLog.Log("EMAIL",
                $"FAILED (SMTP) to [{string.Join(", ", recipients)}] — {subject} — " +
                $"SMTP {ex.StatusCode}: {ex.Message} | {detail}");
            _logger.LogError(ex, "Failed to send email ({StatusCode}): {Subject}", ex.StatusCode, subject);
            return false;
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            _fileLog.Log("EMAIL",
                $"FAILED (SMTP) to [{string.Join(", ", recipients)}] — {subject} — {ex.Message} | {detail}");
            _logger.LogError(ex, "Failed to send email: {Subject}", subject);
            return false;
        }
    }

    // ── EmailRelay request DTO (matches EmailRelay SendRequest) ───────────────────
    private sealed class RelayRequest
    {
        [JsonPropertyName("to")]      public string To { get; set; } = "";
        [JsonPropertyName("subject")] public string Subject { get; set; } = "";
        [JsonPropertyName("body")]    public string Body { get; set; } = "";
        [JsonPropertyName("isHtml")]  public bool IsHtml { get; set; }
    }
}

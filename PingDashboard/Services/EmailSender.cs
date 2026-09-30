using System.Net;
using System.Net.Mail;

namespace PingDashboard.Services;

public interface IEmailSender
{
    /// <summary>
    /// Send a plain-text email to a comma-separated recipient list.
    /// Returns true if the message was accepted for delivery.
    /// Logs the outcome to the file log.
    /// </summary>
    Task<bool> SendAsync(string recipientsCsv, string subject, string body);
}

/// <summary>SMTP settings bound from configuration (appsettings + user secrets).</summary>
public class SmtpSettings
{
    public bool   Enabled     { get; set; }
    public string Host        { get; set; } = "";
    public int    Port        { get; set; } = 587;
    public bool   UseSsl      { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string FromName    { get; set; } = "Ping Dashboard";
    public string? Username   { get; set; }   // from user secrets
    public string? Password   { get; set; }   // from user secrets
}

public class EmailSender : IEmailSender
{
    private readonly SmtpSettings _smtp;
    private readonly IFileLogger _fileLog;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration config, IFileLogger fileLog, ILogger<EmailSender> logger)
    {
        _smtp = new SmtpSettings();
        config.GetSection("Smtp").Bind(_smtp);
        _fileLog = fileLog;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string recipientsCsv, string subject, string body)
    {
        if (!_smtp.Enabled)
        {
            _fileLog.Log("EMAIL", $"Skipped (SMTP disabled): {subject}");
            return false;
        }

        var recipients = (recipientsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (recipients.Length == 0)
        {
            _fileLog.Log("EMAIL", $"Skipped (no recipients): {subject}");
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

            _fileLog.Log("EMAIL", $"Sent to [{string.Join(", ", recipients)}] — {subject}");
            _logger.LogInformation("Email sent to {Recipients}: {Subject}",
                string.Join(", ", recipients), subject);
            return true;
        }
        catch (Exception ex)
        {
            _fileLog.Log("EMAIL", $"FAILED to [{string.Join(", ", recipients)}] — {subject} — {ex.Message}");
            _logger.LogError(ex, "Failed to send email: {Subject}", subject);
            return false;
        }
    }
}

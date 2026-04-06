using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using System.Net;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// Implementasi Email Service berbasis SMTP dengan link frontend dinamis.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailService> _logger;

    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly bool _useSsl;
    private readonly string? _smtpSenderEmail;
    private readonly string _senderName;
    private readonly string? _smtpSenderPassword;
    private readonly string _frontendBaseUrl;
    private readonly bool _checkCertificateRevocation;
    private readonly bool _overrideRecipientsEnabled;
    private readonly string? _overrideRecipientEmail;

    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
        _useSsl = bool.Parse(_configuration["Email:UseSsl"] ?? "true");
        _smtpSenderEmail = _configuration["Email:SenderEmail"];
        _senderName = _configuration["Email:SenderName"] ?? "Validasi Format Buku TA/Tesis";
        _smtpSenderPassword = _configuration["Email:SenderPassword"];
        _frontendBaseUrl = (_configuration["Email:DashboardUrl"] ?? "http://localhost:5173").TrimEnd('/');
        _checkCertificateRevocation = bool.Parse(_configuration["Email:CheckCertificateRevocation"] ?? "true");
        _overrideRecipientsEnabled = bool.Parse(_configuration["Email:OverrideRecipientsEnabled"] ?? "false");
        _overrideRecipientEmail = _configuration["Email:OverrideRecipientEmail"];

        var hasSmtpConfig = !string.IsNullOrEmpty(_smtpSenderEmail) &&
                            !string.IsNullOrEmpty(_smtpSenderPassword);

        if (hasSmtpConfig)
        {
            _logger.LogInformation("SmtpEmailService dikonfigurasi menggunakan SMTP");
            _logger.LogInformation(
                "SMTP certificate revocation check: {CertificateRevocationCheck}",
                _checkCertificateRevocation ? "enabled" : "disabled");
        }
        else
        {
            _logger.LogWarning("SmtpEmailService tidak dikonfigurasi dengan benar. Email tidak akan terkirim.");
        }

        if (_overrideRecipientsEnabled)
        {
            var effectiveOverrideRecipient = GetEffectiveOverrideRecipientEmail();
            if (string.IsNullOrWhiteSpace(effectiveOverrideRecipient))
            {
                _logger.LogWarning(
                    "Email recipient override aktif tetapi Email:OverrideRecipientEmail dan Email:SenderEmail kosong. Recipient override akan diabaikan.");
            }
            else
            {
                _logger.LogInformation(
                    "Email recipient override aktif. Semua email akan diarahkan ke {OverrideRecipient}",
                    effectiveOverrideRecipient);
            }
        }
    }

    public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string bodyHtml)
    {
        try
        {
            var message = CreateMimeMessage(
                new List<(string Email, string Name)> { (toEmail, toName) },
                subject,
                bodyHtml);

            await SendViaSmtpAsync(message);

            _logger.LogInformation("Email berhasil dikirim ke {ToEmail} dengan subject: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengirim email ke {ToEmail}", toEmail);
            return false;
        }
    }

    public async Task<bool> SendEmailAsync(List<(string Email, string Name)> recipients, string subject, string bodyHtml)
    {
        try
        {
            var message = CreateMimeMessage(recipients, subject, bodyHtml);

            await SendViaSmtpAsync(message);

            _logger.LogInformation(
                "Email berhasil dikirim ke {Count} penerima dengan subject: {Subject}",
                recipients.Count,
                subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengirim email ke multiple penerima");
            return false;
        }
    }

    public async Task<bool> SendEmailWithAttachmentAsync(string toEmail, string toName, string subject, string bodyHtml, string attachmentPath)
    {
        try
        {
            var effectiveRecipients = ResolveRecipients(
                new List<(string Email, string Name)> { (toEmail, toName) });
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_senderName, GetSenderEmail()));

            foreach (var (email, name) in effectiveRecipients)
            {
                message.To.Add(new MailboxAddress(name, email));
            }

            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = bodyHtml
            };

            if (File.Exists(attachmentPath))
            {
                builder.Attachments.Add(attachmentPath);
            }
            else
            {
                _logger.LogWarning("Attachment file tidak ditemukan: {Path}", attachmentPath);
            }

            message.Body = builder.ToMessageBody();

            await SendViaSmtpAsync(message);

            _logger.LogInformation("Email dengan attachment berhasil dikirim ke {ToEmail}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengirim email dengan attachment ke {ToEmail}", toEmail);
            return false;
        }
    }

    public async Task<bool> SendValidationCompleteNotificationAsync(
        string toEmail,
        string toName,
        string resourceType,
        int resourceId,
        string resourceTitle,
        bool isLolos,
        int errorCount,
        string academicWorkLabel)
    {
        var normalizedResourceType = NormalizeResourceType(resourceType);
        var resourceLabel = normalizedResourceType == "buku" ? "Buku" : "Dokumen";
        var normalizedAcademicWorkLabel = NormalizeAcademicWorkLabel(academicWorkLabel);
        var status = isLolos ? "LOLOS" : "TIDAK LOLOS";
        var statusColor = isLolos ? "#22c55e" : "#ef4444";
        var subject = $"Hasil Validasi Format {resourceLabel}: {resourceTitle}";
        var detailUrl = BuildValidationDetailUrl(normalizedResourceType, resourceId);
        var encodedRecipientName = WebUtility.HtmlEncode(toName);
        var encodedResourceTitle = WebUtility.HtmlEncode(resourceTitle);
        var encodedAcademicWorkLabel = WebUtility.HtmlEncode(normalizedAcademicWorkLabel);
        var encodedDetailUrl = WebUtility.HtmlEncode(detailUrl);

        var bodyHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #3b82f6, #1d4ed8); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f8fafc; padding: 30px; border: 1px solid #e2e8f0; }}
        .status-badge {{ display: inline-block; padding: 7px 16px; border-radius: 16px; font-weight: 700; font-size: 14px; line-height: 1.2; color: white; background-color: {statusColor}; }}
        .info-box {{ background: white; padding: 20px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #3b82f6; }}
        .footer {{ text-align: center; padding: 20px; color: #64748b; font-size: 12px; }}
        .button {{ display: inline-block; background: #3b82f6; color: #ffffff !important; padding: 12px 24px; text-decoration: none; border-radius: 6px; margin-top: 20px; font-weight: 600; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1 style='margin: 0;'>Validasi {resourceLabel}</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>Validasi Format Buku TA/Tesis</p>
        </div>
        <div class='content'>
            <p>Halo <strong>{encodedRecipientName}</strong>,</p>
            <p>Proses validasi format {resourceLabel.ToLowerInvariant()} {encodedAcademicWorkLabel} Anda telah selesai. Berikut adalah hasilnya:</p>

            <div class='info-box'>
                <p style='margin: 0 0 10px 0;'><strong>{resourceLabel}:</strong> {encodedResourceTitle}</p>
                <p style='margin: 0;'><strong>Status:</strong> <span class='status-badge'>{status}</span></p>
                {(errorCount > 0 ? $"<p style='margin: 10px 0 0 0;'><strong>Jumlah Kesalahan:</strong> {errorCount} kesalahan</p>" : "")}
            </div>

            {(isLolos
                ? $"<p>{resourceLabel} Anda telah memenuhi persyaratan format yang ditetapkan.</p>"
                : $"<p>Silakan buka detail {resourceLabel.ToLowerInvariant()} untuk melihat kesalahan dan langkah perbaikannya.</p>")}

            <center>
                <a href='{encodedDetailUrl}' class='button' style='display:inline-block;background:#3b82f6;color:#ffffff !important;-webkit-text-fill-color:#ffffff;padding:12px 24px;text-decoration:none;border-radius:6px;margin-top:20px;font-weight:600;'>Lihat Detail {resourceLabel}</a>
            </center>
        </div>
        <div class='footer'>
            <p>Email ini dikirim secara otomatis oleh Validasi Format Buku TA/Tesis.<br>
            Jangan membalas email ini.</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, toName, subject, bodyHtml);
    }

    private string GetSenderEmail()
    {
        return _smtpSenderEmail!;
    }

    private string BuildValidationDetailUrl(string resourceType, int resourceId)
    {
        var mahasiswaBaseUrl = _frontendBaseUrl.EndsWith("/mahasiswa", StringComparison.OrdinalIgnoreCase)
            ? _frontendBaseUrl
            : $"{_frontendBaseUrl}/mahasiswa";

        return $"{mahasiswaBaseUrl}/{resourceType}/{resourceId}";
    }

    private static string NormalizeResourceType(string resourceType)
    {
        return string.Equals(resourceType, "buku", StringComparison.OrdinalIgnoreCase)
            ? "buku"
            : "dokumen";
    }

    private static string NormalizeAcademicWorkLabel(string academicWorkLabel)
    {
        return string.Equals(academicWorkLabel, "Tesis", StringComparison.OrdinalIgnoreCase)
            ? "Tesis"
            : "Tugas Akhir";
    }

    private MimeMessage CreateMimeMessage(List<(string Email, string Name)> recipients, string subject, string bodyHtml)
    {
        var effectiveRecipients = ResolveRecipients(recipients);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_senderName, GetSenderEmail()));

        foreach (var (email, name) in effectiveRecipients)
        {
            message.To.Add(new MailboxAddress(name, email));
        }

        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Html) { Text = bodyHtml };

        return message;
    }

    private List<(string Email, string Name)> ResolveRecipients(List<(string Email, string Name)> recipients)
    {
        if (!_overrideRecipientsEnabled)
            return recipients;

        var overrideRecipientEmail = GetEffectiveOverrideRecipientEmail();
        if (string.IsNullOrWhiteSpace(overrideRecipientEmail))
            return recipients;

        var originalRecipients = recipients
            .Select(r => string.IsNullOrWhiteSpace(r.Name) ? r.Email : $"{r.Name} <{r.Email}>")
            .ToList();

        _logger.LogInformation(
            "Email recipient override aktif. Original recipients: {OriginalRecipients}. Redirected to: {OverrideRecipient}",
            string.Join(", ", originalRecipients),
            overrideRecipientEmail);

        return new List<(string Email, string Name)>
        {
            (overrideRecipientEmail, "Development Override")
        };
    }

    private string? GetEffectiveOverrideRecipientEmail()
    {
        if (!string.IsNullOrWhiteSpace(_overrideRecipientEmail))
            return _overrideRecipientEmail.Trim();

        return string.IsNullOrWhiteSpace(_smtpSenderEmail)
            ? null
            : _smtpSenderEmail.Trim();
    }

    private async Task SendViaSmtpAsync(MimeMessage message)
    {
        if (string.IsNullOrEmpty(_smtpSenderEmail) || string.IsNullOrEmpty(_smtpSenderPassword))
        {
            throw new InvalidOperationException("SMTP credentials tidak dikonfigurasi");
        }

        using var smtp = new SmtpClient
        {
            CheckCertificateRevocation = _checkCertificateRevocation
        };

        try
        {
            await smtp.ConnectAsync(
                _smtpHost,
                _smtpPort,
                _useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

            await smtp.AuthenticateAsync(_smtpSenderEmail, _smtpSenderPassword);
            await smtp.SendAsync(message);

            _logger.LogDebug("Email berhasil dikirim via SMTP {Host}:{Port}", _smtpHost, _smtpPort);
        }
        finally
        {
            await smtp.DisconnectAsync(true);
        }
    }
}

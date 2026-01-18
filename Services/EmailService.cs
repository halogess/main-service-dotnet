using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using System.Text;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// Interface untuk Email Service
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Kirim email ke satu penerima
    /// </summary>
    Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string bodyHtml);
    
    /// <summary>
    /// Kirim email ke multiple penerima
    /// </summary>
    Task<bool> SendEmailAsync(List<(string Email, string Name)> recipients, string subject, string bodyHtml);
    
    /// <summary>
    /// Kirim email dengan attachment
    /// </summary>
    Task<bool> SendEmailWithAttachmentAsync(string toEmail, string toName, string subject, string bodyHtml, string attachmentPath);
    
    /// <summary>
    /// Kirim notifikasi validasi selesai ke mahasiswa
    /// </summary>
    Task<bool> SendValidationCompleteNotificationAsync(string toEmail, string toName, string dokumenTitle, bool isLolos, int errorCount);
}

/// <summary>
/// Email Service dengan dukungan:
/// 1. Gmail API (Service Account) - Recommended untuk background service
/// 2. Gmail API (OAuth) - Untuk user consent flow
/// 3. SMTP Fallback - Jika Gmail API tidak dikonfigurasi
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    
    // Gmail API Settings
    private readonly string? _serviceAccountJsonPath;
    private readonly string? _delegatedUserEmail;
    private readonly bool _useGmailApi;
    
    // SMTP Settings (fallback)
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly bool _useSsl;
    private readonly string? _smtpSenderEmail;
    private readonly string _senderName;
    private readonly string? _smtpSenderPassword;

    private GmailService? _gmailService;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Gmail API Configuration
        _serviceAccountJsonPath = _configuration["Email:ServiceAccountJsonPath"];
        _delegatedUserEmail = _configuration["Email:DelegatedUserEmail"];
        _useGmailApi = !string.IsNullOrEmpty(_serviceAccountJsonPath) && 
                       !string.IsNullOrEmpty(_delegatedUserEmail);
        
        // SMTP Configuration (fallback)
        _smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
        _useSsl = bool.Parse(_configuration["Email:UseSsl"] ?? "true");
        _smtpSenderEmail = _configuration["Email:SenderEmail"];
        _senderName = _configuration["Email:SenderName"] ?? "Validasi Tugas Akhir";
        _smtpSenderPassword = _configuration["Email:SenderPassword"];
        
        if (_useGmailApi)
        {
            _logger.LogInformation("EmailService dikonfigurasi menggunakan Gmail API dengan Service Account");
            InitializeGmailServiceAsync().GetAwaiter().GetResult();
        }
        else if (!string.IsNullOrEmpty(_smtpSenderEmail) && !string.IsNullOrEmpty(_smtpSenderPassword))
        {
            _logger.LogInformation("EmailService dikonfigurasi menggunakan SMTP");
        }
        else
        {
            _logger.LogWarning("EmailService tidak dikonfigurasi dengan benar. Email tidak akan terkirim.");
        }
    }

    /// <summary>
    /// Initialize Gmail Service dengan Service Account
    /// </summary>
    private async Task InitializeGmailServiceAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_serviceAccountJsonPath) || !File.Exists(_serviceAccountJsonPath))
            {
                _logger.LogError("Service Account JSON file tidak ditemukan: {Path}", _serviceAccountJsonPath);
                return;
            }

            // Load Service Account credentials
            var credential = GoogleCredential
                .FromFile(_serviceAccountJsonPath)
                .CreateScoped(GmailService.Scope.GmailSend)
                .CreateWithUser(_delegatedUserEmail); // Domain-wide delegation

            // Create Gmail Service
            _gmailService = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Validasi Tugas Akhir"
            });

            _logger.LogInformation("Gmail Service berhasil diinisialisasi untuk user: {User}", _delegatedUserEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menginisialisasi Gmail Service");
            _gmailService = null;
        }
    }

    /// <summary>
    /// Kirim email ke satu penerima
    /// </summary>
    public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string bodyHtml)
    {
        try
        {
            // Buat MIME message
            var message = CreateMimeMessage(
                new List<(string Email, string Name)> { (toEmail, toName) },
                subject,
                bodyHtml
            );

            // Kirim via Gmail API atau SMTP
            if (_useGmailApi && _gmailService != null)
            {
                await SendViaGmailApiAsync(message);
            }
            else
            {
                await SendViaSmtpAsync(message);
            }
            
            _logger.LogInformation("Email berhasil dikirim ke {ToEmail} dengan subject: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengirim email ke {ToEmail}", toEmail);
            return false;
        }
    }

    /// <summary>
    /// Kirim email ke multiple penerima
    /// </summary>
    public async Task<bool> SendEmailAsync(List<(string Email, string Name)> recipients, string subject, string bodyHtml)
    {
        try
        {
            var message = CreateMimeMessage(recipients, subject, bodyHtml);

            if (_useGmailApi && _gmailService != null)
            {
                await SendViaGmailApiAsync(message);
            }
            else
            {
                await SendViaSmtpAsync(message);
            }
            
            _logger.LogInformation("Email berhasil dikirim ke {Count} penerima dengan subject: {Subject}", 
                recipients.Count, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengirim email ke multiple penerima");
            return false;
        }
    }

    /// <summary>
    /// Kirim email dengan attachment
    /// </summary>
    public async Task<bool> SendEmailWithAttachmentAsync(string toEmail, string toName, string subject, string bodyHtml, string attachmentPath)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_senderName, GetSenderEmail()));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            // Buat multipart body (HTML + attachment)
            var builder = new BodyBuilder
            {
                HtmlBody = bodyHtml
            };

            // Tambah attachment jika file exists
            if (File.Exists(attachmentPath))
            {
                builder.Attachments.Add(attachmentPath);
            }
            else
            {
                _logger.LogWarning("Attachment file tidak ditemukan: {Path}", attachmentPath);
            }

            message.Body = builder.ToMessageBody();

            if (_useGmailApi && _gmailService != null)
            {
                await SendViaGmailApiAsync(message);
            }
            else
            {
                await SendViaSmtpAsync(message);
            }
            
            _logger.LogInformation("Email dengan attachment berhasil dikirim ke {ToEmail}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengirim email dengan attachment ke {ToEmail}", toEmail);
            return false;
        }
    }

    /// <summary>
    /// Kirim notifikasi validasi selesai ke mahasiswa
    /// </summary>
    public async Task<bool> SendValidationCompleteNotificationAsync(
        string toEmail, 
        string toName, 
        string dokumenTitle, 
        bool isLolos, 
        int errorCount)
    {
        var status = isLolos ? "LOLOS" : "TIDAK LOLOS";
        var statusColor = isLolos ? "#22c55e" : "#ef4444";
        var statusEmoji = isLolos ? "✅" : "❌";
        
        var subject = $"{statusEmoji} Hasil Validasi Dokumen: {dokumenTitle}";
        
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
        .status-badge {{ display: inline-block; padding: 10px 20px; border-radius: 20px; font-weight: bold; font-size: 18px; color: white; background-color: {statusColor}; }}
        .info-box {{ background: white; padding: 20px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #3b82f6; }}
        .footer {{ text-align: center; padding: 20px; color: #64748b; font-size: 12px; }}
        .button {{ display: inline-block; background: #3b82f6; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; margin-top: 20px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1 style='margin: 0;'>📄 Validasi Dokumen</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>Sistem Validasi Tugas Akhir</p>
        </div>
        <div class='content'>
            <p>Halo <strong>{toName}</strong>,</p>
            <p>Proses validasi dokumen Anda telah selesai. Berikut adalah hasilnya:</p>
            
            <div class='info-box'>
                <p style='margin: 0 0 10px 0;'><strong>📁 Dokumen:</strong> {dokumenTitle}</p>
                <p style='margin: 0;'><strong>📊 Status:</strong> <span class='status-badge'>{status}</span></p>
                {(errorCount > 0 ? $"<p style='margin: 10px 0 0 0;'><strong>⚠️ Jumlah Kesalahan:</strong> {errorCount} kesalahan</p>" : "")}
            </div>
            
            {(isLolos 
                ? "<p>🎉 <strong>Selamat!</strong> Dokumen Anda telah memenuhi semua standar format yang ditetapkan.</p>" 
                : "<p>Silakan login ke sistem untuk melihat detail kesalahan dan cara memperbaikinya.</p>")}
            
            <center>
                <a href='http://localhost:5173/mahasiswa' class='button'>Lihat Detail di Dashboard</a>
            </center>
        </div>
        <div class='footer'>
            <p>Email ini dikirim secara otomatis oleh Sistem Validasi Tugas Akhir.<br>
            Jangan membalas email ini.</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, toName, subject, bodyHtml);
    }

    #region Private Helper Methods

    /// <summary>
    /// Get sender email (from Gmail API or SMTP config)
    /// </summary>
    private string GetSenderEmail()
    {
        return _useGmailApi ? _delegatedUserEmail! : _smtpSenderEmail!;
    }

    /// <summary>
    /// Create MIME message
    /// </summary>
    private MimeMessage CreateMimeMessage(List<(string Email, string Name)> recipients, string subject, string bodyHtml)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_senderName, GetSenderEmail()));
        
        foreach (var (email, name) in recipients)
        {
            message.To.Add(new MailboxAddress(name, email));
        }
        
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Html) { Text = bodyHtml };
        
        return message;
    }

    /// <summary>
    /// Kirim via Gmail API (menggunakan raw base64url format)
    /// </summary>
    private async Task SendViaGmailApiAsync(MimeMessage mimeMessage)
    {
        if (_gmailService == null)
        {
            throw new InvalidOperationException("Gmail Service belum diinisialisasi");
        }

        // Convert MIME message ke raw base64url format
        using var memoryStream = new MemoryStream();
        await mimeMessage.WriteToAsync(memoryStream);
        var rawMessage = Convert.ToBase64String(memoryStream.ToArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", ""); // Base64Url encoding

        // Buat Gmail Message
        var gmailMessage = new Message
        {
            Raw = rawMessage
        };

        // Kirim via Gmail API
        var request = _gmailService.Users.Messages.Send(gmailMessage, "me");
        var response = await request.ExecuteAsync();

        _logger.LogDebug("Email terkirim via Gmail API. Message ID: {MessageId}", response.Id);
    }

    /// <summary>
    /// Kirim via SMTP (fallback)
    /// </summary>
    private async Task SendViaSmtpAsync(MimeMessage message)
    {
        if (string.IsNullOrEmpty(_smtpSenderEmail) || string.IsNullOrEmpty(_smtpSenderPassword))
        {
            throw new InvalidOperationException("SMTP credentials tidak dikonfigurasi");
        }

        using var smtp = new SmtpClient();
        
        try
        {
            // Connect ke SMTP server
            await smtp.ConnectAsync(_smtpHost, _smtpPort, 
                _useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);
            
            // Authenticate
            await smtp.AuthenticateAsync(_smtpSenderEmail, _smtpSenderPassword);
            
            // Kirim email
            await smtp.SendAsync(message);
            
            _logger.LogDebug("Email berhasil dikirim via SMTP {Host}:{Port}", _smtpHost, _smtpPort);
        }
        finally
        {
            await smtp.DisconnectAsync(true);
        }
    }

    #endregion
}

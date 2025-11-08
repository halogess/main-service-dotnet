using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace _.Models;

[Table("adobe_api_logs")]
public class AdobeApiLog
{
    [Key]
    [Column("adobe_api_logs_id")]
    public int AdobeApiLogsId { get; set; }

    [Column("adobe_credentials_id")]
    public int? AdobeCredentialsId { get; set; }

    [Column("endpoint")]
    [MaxLength(255)]
    public string Endpoint { get; set; } = string.Empty;

    [Column("method")]
    [MaxLength(10)]
    public string Method { get; set; } = string.Empty;

    [Column("status_code")]
    public int? StatusCode { get; set; }

    [Column("response_time_ms")]
    public int? ResponseTimeMs { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace _.Models;

[Table("adobe_credentials")]
public class AdobeCredential
{
    [Key]
    [Column("adobe_credentials_id")]
    public int AdobeCredentialsId { get; set; }

    [Column("adobe_client_id")]
    [MaxLength(100)]
    public string AdobeClientId { get; set; } = string.Empty;

    [Column("adobe_client_secret")]
    [MaxLength(100)]
    public string AdobeClientSecret { get; set; } = string.Empty;

    [Column("adobe_credentials_status")]
    [MaxLength(8)]
    public string AdobeCredentialsStatus { get; set; } = "active";

    [Column("adobe_credentials_quota_used")]
    public int AdobeCredentialsQuotaUsed { get; set; } = 0;

    [Column("adobe_credentials_quota_limit")]
    public int AdobeCredentialsQuotaLimit { get; set; } = 500;

    [Column("adobe_credentials_reset_date")]
    public DateTime? AdobeCredentialsResetDate { get; set; }

    [Column("adobe_credentials_created_at")]
    public DateTime? AdobeCredentialsCreatedAt { get; set; }

    [Column("adobe_credentials_updated_at")]
    public DateTime? AdobeCredentialsUpdatedAt { get; set; }
}

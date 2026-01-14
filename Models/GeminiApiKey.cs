using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("credential_gemini")]
public class GeminiApiKey
{
    [Key]
    [Column("gemini_api_key_id")]
    public uint GeminiApiKeyId { get; set; }

    [Required]
    [Column("gemini_api_key_value")]
    [MaxLength(512)]
    public string GeminiApiKeyValue { get; set; } = string.Empty;

    [Required]
    [Column("gemini_api_key_tier")]
    [MaxLength(10)]
    public string GeminiApiKeyTier { get; set; } = "free";

    [Column("gemini_api_key_status")]
    public sbyte GeminiApiKeyStatus { get; set; } = 1;

    [Column("gemini_api_key_usage")]
    public uint? GeminiApiKeyUsage { get; set; }

    [Column("gemini_api_key_created_at")]
    public DateTime? GeminiApiKeyCreatedAt { get; set; }

    [Column("gemini_api_key_updated_at")]
    public DateTime? GeminiApiKeyUpdatedAt { get; set; }
}

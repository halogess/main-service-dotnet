using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("llm_api_logs")]
public class LlmApiLog
{
    [Key]
    [Column("log_id")]
    public uint LogId { get; set; }

    [Column("log_error_code")]
    public int? LogErrorCode { get; set; }

    [Column("log_message")]
    [MaxLength(50)]
    public string LogMessage { get; set; } = string.Empty;

    [Column("antrian_id")]
    public uint? AntrianId { get; set; }

    [Column("api_key_id")]
    public uint? ApiKeyId { get; set; }

    [Column("log_tokens_used")]
    public int? LogTokensUsed { get; set; }

    [Column("log_batch_number")]
    public int? LogBatchNumber { get; set; }

    [Column("log_total_batches")]
    public int? LogTotalBatches { get; set; }

    [Column("log_error_count")]
    public int? LogErrorCount { get; set; }

    [Column("log_key_tokens_used")]
    public int? LogKeyTokensUsed { get; set; }

    [Column("log_created_at")]
    public DateTime LogCreatedAt { get; set; } = DateTime.Now;
}

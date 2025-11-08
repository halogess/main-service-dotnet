using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace _.Models;

[Table("antrian_pdf")]
public class AntrianPdf
{
    [Key]
    [Column("antrian_pdf_id")]
    public int AntrianPdfId { get; set; }

    [Column("antrian_pdf_tipe")]
    [MaxLength(8)]
    public string AntrianPdfTipe { get; set; } = string.Empty;

    [Column("file_path")]
    [MaxLength(255)]
    public string FilePath { get; set; } = string.Empty;

    [Column("antrian_pdf_status")]
    [MaxLength(10)]
    public string AntrianPdfStatus { get; set; } = "in_queue";

    [Column("antrian_pdf_created_at")]
    public DateTime? AntrianPdfCreatedAt { get; set; }

    [Column("antrian_pdf_updated_at")]
    public DateTime? AntrianPdfUpdatedAt { get; set; }

    [Column("antrian_pdf_failed_reason")]
    [MaxLength(100)]
    public string? AntrianPdfFailedReason { get; set; }
}

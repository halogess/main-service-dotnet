using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("aturan")]
public class Aturan
{
    [Key]
    [Column("aturan_id")]
    public uint AturanId { get; set; }

    [Required]
    [Column("aturan_versi")]
    [MaxLength(255)]
    public string AturanVersi { get; set; } = string.Empty;

    [Column("aturan_status")]
    [MaxLength(32)]
    public string AturanStatus { get; set; } = AturanStatusValues.TidakAktif;

    [Column("aturan_skor_minimum")]
    public uint AturanSkorMinimum { get; set; } = 80;

    [Column("aturan_template_file_path")]
    [MaxLength(255)]
    public string? AturanTemplateFilePath { get; set; }

    [Column("aturan_template_pdf_path")]
    [MaxLength(255)]
    public string? AturanTemplatePdfPath { get; set; }

    [Column("aturan_created_at")]
    public DateTime? AturanCreatedAt { get; set; }

    [Column("aturan_updated_at")]
    public DateTime? AturanUpdatedAt { get; set; }
}

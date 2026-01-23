using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("template_generation")]
public class TemplateGeneration
{
    [Key]
    [Column("template_generation_id")]
    public uint TemplateGenerationId { get; set; }

    [Column("template_id")]
    public uint? TemplateId { get; set; }

    [Column("mhs_nrp")]
    public uint? MhsNrp { get; set; }

    [Column("template_generation_docx_filepath")]
    [MaxLength(255)]
    public string? TemplateGenerationDocxFilepath { get; set; }

    [Column("template_generation_pdf_filepath")]
    [MaxLength(255)]
    public string? TemplateGenerationPdfFilepath { get; set; }

    [Column("template_generation_created_at")]
    public DateTime TemplateGenerationCreatedAt { get; set; } = DateTime.Now;

    [Column("template_generation_updated_at")]
    public DateTime TemplateGenerationUpdatedAt { get; set; } = DateTime.Now;

    // Navigation property
    [ForeignKey("TemplateId")]
    public Template? Template { get; set; }
}

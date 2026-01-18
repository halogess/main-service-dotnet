using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("template")]
public class Template
{
    [Key]
    [Column("template_id")]
    public uint TemplateId { get; set; }

    [Required]
    [Column("template_name")]
    [MaxLength(255)]
    public string TemplateName { get; set; } = string.Empty;

    [Required]
    [Column("template_status")]
    [MaxLength(50)]
    public string TemplateStatus { get; set; } = string.Empty;

    [Column("template_docx_path")]
    [MaxLength(500)]
    public string? TemplateDocxPath { get; set; }

    [Column("template_pdf_path")]
    [MaxLength(500)]
    public string? TemplatePdfPath { get; set; }

    [Column("template_created_at")]
    public DateTime TemplateCreatedAt { get; set; } = DateTime.Now;
}

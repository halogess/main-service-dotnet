using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("template_detail")]
public class TemplateDetail
{
    [Key]
    [Column("template_detail_id")]
    public uint TemplateDetailId { get; set; }

    [Required]
    [Column("template_id")]
    public uint TemplateId { get; set; }

    [Required]
    [Column("template_detail_text")]
    [MaxLength(100)]
    public string TemplateDetailText { get; set; } = string.Empty;

    [Column("template_detail_field")]
    [MaxLength(100)]
    public string? TemplateDetailField { get; set; }

    [Column("template_detail_catatan")]
    [MaxLength(100)]
    public string? TemplateDetailCatatan { get; set; }

    // Navigation property
    [ForeignKey("TemplateId")]
    public Template? Template { get; set; }
}

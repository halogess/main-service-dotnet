using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("template_field")]
public class TemplateField
{
    [Key]
    [Column("template_field_id")]
    public uint TemplateFieldId { get; set; }

    [Required]
    [Column("template_id")]
    public uint TemplateId { get; set; }

    [Required]
    [Column("template_field_text")]
    [MaxLength(100)]
    public string TemplateFieldText { get; set; } = string.Empty;

    [Column("template_field_key")]
    [MaxLength(100)]
    public string? TemplateFieldKey { get; set; }

    // Navigation property
    [ForeignKey("TemplateId")]
    public Template? Template { get; set; }
}

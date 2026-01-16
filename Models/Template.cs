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

    [Required]
    [Column("template_filepath")]
    [MaxLength(255)]
    public string TemplateFilepath { get; set; } = string.Empty;
}

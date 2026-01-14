using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("kesalahan")]
public class Kesalahan
{
    [Key]
    [Column("kesalahan_id")]
    public uint KesalahanId { get; set; }

    [Required]
    [Column("kesalahan_kategori")]
    [MaxLength(100)]
    public string KesalahanKategori { get; set; } = string.Empty;

    [Required]
    [Column("kesalahan_judul")]
    [MaxLength(255)]
    public string KesalahanJudul { get; set; } = string.Empty;

    [Required]
    [Column("kesalahan_penjelasan", TypeName = "text")]
    public string KesalahanPenjelasan { get; set; } = string.Empty;

    [Column("kesalahan_location")]
    [MaxLength(255)]
    public string? KesalahanLocation { get; set; }

    [Column("kesalahan_bbox_visual", TypeName = "json")]
    public string? KesalahanBboxVisual { get; set; }

    [Column("kesalahan_steps", TypeName = "json")]
    public string KesalahanSteps { get; set; } = "[]";
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("kesalahan_detail")]
public class KesalahanDetail
{
    [Key]
    [Column("kesalahan_detail_id")]
    public uint KesalahanDetailId { get; set; }

    [Required]
    [Column("kesalahan_id")]
    public uint KesalahanId { get; set; }

    [Required]
    [Column("kesalahan_detail_judul")]
    [MaxLength(255)]
    public string KesalahanDetailJudul { get; set; } = string.Empty;

    [Required]
    [Column("kesalahan_detail_penjelasan")]
    [MaxLength(255)]
    public string KesalahanDetailPenjelasan { get; set; } = string.Empty;

    [Column("kesalahan_detail_steps", TypeName = "longtext")]
    public string? KesalahanDetailSteps { get; set; }

    [Required]
    [Column("kesalahan_is_required")]
    public bool KesalahanIsRequired { get; set; } = true;

    // Navigation property
    [ForeignKey("KesalahanId")]
    public Kesalahan? Kesalahan { get; set; }
}

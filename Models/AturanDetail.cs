using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("aturan_detail")]
public class AturanDetail
{
    [Key]
    [Column("aturan_detail_id")]
    public uint AturanDetailId { get; set; }

    [Column("aturan_id")]
    public uint AturanId { get; set; }

    [Column("aturan_detail_kategori")]
    [MaxLength(255)]
    public string? AturanDetailKategori { get; set; }

    [Column("aturan_detail_key")]
    [MaxLength(255)]
    public string? AturanDetailKey { get; set; }

    [Column("aturan_detail_json_value", TypeName = "longtext")]
    public string? AturanDetailJsonValue { get; set; }

    [Column("aturan_detail_status")]
    public sbyte AturanDetailStatus { get; set; } = 1;

    [Column("aturan_detail_catatan")]
    [MaxLength(255)]
    public string? AturanDetailCatatan { get; set; }

    // Navigation property
    [ForeignKey("AturanId")]
    public virtual Aturan? Aturan { get; set; }
}

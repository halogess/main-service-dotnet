using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

public enum KesalahanRefTipe
{
    buku,
    dokumen
}

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
    [Column("kesalahan_ref_tipe")]
    public KesalahanRefTipe KesalahanRefTipe { get; set; }

    [Required]
    [Column("kesalahan_ref_id")]
    public uint KesalahanRefId { get; set; }

    [Required]
    [Column("kesalahan_judul")]
    [MaxLength(255)]
    public string KesalahanJudul { get; set; } = string.Empty;

    [Required]
    [Column("kesalahan_penjelasan", TypeName = "text")]
    public string KesalahanPenjelasan { get; set; } = string.Empty;

    [Column("kesalahan_lokasi")]
    public string? KesalahanLokasi { get; set; }

    [Column("kesalahan_steps", TypeName = "json")]
    public string KesalahanSteps { get; set; } = "[]";

    [Required]
    [Column("kesalahan_is_required")]
    public bool KesalahanIsRequired { get; set; } = true;
}

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

    [Column("kesalahan_lokasi")]
    public string? KesalahanLokasi { get; set; }

    // Navigation property
    public ICollection<KesalahanDetail> Details { get; set; } = new List<KesalahanDetail>();
}

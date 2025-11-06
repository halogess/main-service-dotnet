using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace _.Models;

[Table("buku")]
public class Buku
{
    [Key]
    [Column("buku_id")]
    public int BukuId { get; set; }

    [Required]
    [Column("mhs_nrp")]
    [MaxLength(9)]
    public string MhsNrp { get; set; } = string.Empty;

    [Required]
    [Column("buku_judul")]
    [MaxLength(255)]
    public string BukuJudul { get; set; } = string.Empty;

    [Column("buku_status")]
    public byte BukuStatus { get; set; } = 1;

    [Column("buku_created_at")]
    public DateTime? BukuCreatedAt { get; set; }

    [Column("buku_updated_at")]
    public DateTime? BukuUpdatedAt { get; set; }

    [NotMapped]
    public List<Dokumen> Dokumens { get; set; } = new();
}

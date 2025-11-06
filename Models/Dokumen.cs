using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace _.Models;

[Table("dokumen")]
public class Dokumen
{
    [Key]
    [Column("dokumen_id")]
    public int DokumenId { get; set; }

    [Required]
    [Column("mhs_nrp")]
    [MaxLength(9)]
    public string MhsNrp { get; set; } = string.Empty;

    [Required]
    [Column("dokumen_filename")]
    [MaxLength(255)]
    public string DokumenFilename { get; set; } = string.Empty;

    [Column("dokumen_status")]
    public byte DokumenStatus { get; set; } = 1;

    [Column("dokumen_created_at")]
    public DateTime? DokumenCreatedAt { get; set; }

    [Column("dokumen_updated_at")]
    public DateTime? DokumenUpdatedAt { get; set; }
}

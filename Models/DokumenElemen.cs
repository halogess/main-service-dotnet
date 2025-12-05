using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("dokumen_elemen")]
public class DokumenElemen
{
    [Key]
    [Column("dokumen_elemen_id")]
    public long DokumenElemenId { get; set; }

    [Column("dokumen_id")]
    public int? DokumenId { get; set; }

    [Column("dokumen_elemen_sequence")]
    public int? DokumenElemenSequence { get; set; }

    [Column("dokumen_elemen_type")]
    [MaxLength(100)]
    public string? DokumenElemenType { get; set; }

    [Column("dokumen_elemen_json_tree", TypeName = "json")]
    public string? DokumenElemenJsonTree { get; set; }
}

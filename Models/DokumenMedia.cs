using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("dokumen_media")]
public class DokumenMedia
{
    [Key]
    [Column("dokumen_media_id")]
    public long DokumenMediaId { get; set; }

    [Column("dokumen_id")]
    public int? DokumenId { get; set; }

    [Column("dokumen_media_rid")]
    [MaxLength(50)]
    public string? DokumenMediaRid { get; set; }

    [Column("dokumen_media_filename")]
    [MaxLength(255)]
    public string? DokumenMediaFilename { get; set; }

    [Column("dokumen_media_filepath")]
    [MaxLength(255)]
    public string? DokumenMediaFilepath { get; set; }

    [Column("dokumen_media_content_type")]
    [MaxLength(100)]
    public string? DokumenMediaContentType { get; set; }
}

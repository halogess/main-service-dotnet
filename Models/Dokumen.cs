using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("dokumen")]
public class Dokumen
{
    [Key]
    [Column("dokumen_id")]
    public int DokumenId { get; set; }

    [Column("mhs_nrp")]
    public string MhsNrp { get; set; } = string.Empty;

    [Column("dokumen_filename")]
    public string DokumenFilename { get; set; } = string.Empty;

    [Column("dokumen_filesize_bytes")]
    public long? DokumenFilesizeBytes { get; set; }

    [Column("dokumen_status")]
    public string DokumenStatus { get; set; } = "dalam_antrian";

    [Column("dokumen_skor")]
    public int? DokumenSkor { get; set; }

    [Column("dokumen_jumlah_kesalahan")]
    public int? DokumenJumlahKesalahan { get; set; }

    [Column("dokumen_docx_path")]
    public string? DokumenDocxPath { get; set; }

    [Column("dokumen_pdf_path")]
    public string? DokumenPdfPath { get; set; }

    [Column("dokumen_created_at")]
    public DateTime? DokumenCreatedAt { get; set; }

    [Column("dokumen_updated_at")]
    public DateTime? DokumenUpdatedAt { get; set; }
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("buku")]
public class Buku
{
    [Key]
    [Column("buku_id")]
    public int BukuId { get; set; }

    [Column("mhs_nrp")]
    public string MhsNrp { get; set; } = string.Empty;

    [Column("buku_judul")]
    public string BukuJudul { get; set; } = string.Empty;

    [Column("buku_status")]
    public string BukuStatus { get; set; } = "dalam_antrian";

    [Column("buku_skor")]
    public int? BukuSkor { get; set; }

    [Column("buku_jumlah_kesalahan")]
    public int? BukuJumlahKesalahan { get; set; }

    [Column("buku_jumlah_bab")]
    public int BukuJumlahBab { get; set; } = 0;

    [Column("buku_report_path")]
    [MaxLength(255)]
    public string? BukuReportPath { get; set; }

    [Column("buku_docx_zip_path")]
    [MaxLength(100)]
    public string? BukuDocxZipPath { get; set; }

    [Column("buku_pdf_zip_path")]
    [MaxLength(100)]
    public string? BukuPdfZipPath { get; set; }

    [Column("buku_created_at")]
    public DateTime? BukuCreatedAt { get; set; }

    [Column("buku_updated_at")]
    public DateTime? BukuUpdatedAt { get; set; }
}

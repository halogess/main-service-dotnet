using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("bab")]
public class Bab
{
    [Key]
    [Column("bab_id")]
    public uint BabId { get; set; }

    [Column("bab_tipe")]
    [MaxLength(100)]
    public string? BabTipe { get; set; }

    [Column("buku_id")]
    public uint BukuId { get; set; }

    [Column("bab_order")]
    public byte? BabOrder { get; set; }

    [Column("bab_filename")]
    [MaxLength(255)]
    public string BabFilename { get; set; } = null!;

    [Column("bab_docx_path")]
    [MaxLength(255)]
    public string? BabDocxPath { get; set; }

    [Column("bab_pdf_path")]
    [MaxLength(255)]
    public string? BabPdfPath { get; set; }

    [Column("bab_images_path")]
    [MaxLength(255)]
    public string? BabImagesPath { get; set; }

    [Column("bab_skor")]
    public int? BabSkor { get; set; }

    [Column("bab_skor_minimal")]
    public int? BabSkorMinimal { get; set; }

    [Column("bab_jumlah_kesalahan")]
    public int? BabJumlahKesalahan { get; set; }
}

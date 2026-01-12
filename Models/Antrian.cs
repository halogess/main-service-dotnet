using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("antrian")]
public class Antrian
{
    [Key]
    [Column("antrian_id")]
    public uint AntrianId { get; set; }

    [Column("antrian_tipe")]
    public string AntrianTipe { get; set; } = string.Empty; // 'dokumen' or 'buku'

    [Column("buku_id")]
    public uint? BukuId { get; set; }

    [Column("bab_id")]
    public uint? BabId { get; set; }

    [Column("dokumen_id")]
    public uint? DokumenId { get; set; }

    [Column("antrian_extraction_status")]
    public string? AntrianExtractionStatus { get; set; } // 'in_queue', 'processing', 'completed', 'failed'

    [Column("antrian_labeling_status")]
    public string? AntrianLabelingStatus { get; set; } // 'in_queue', 'processing', 'completed', 'failed'

    [Column("antrian_validation_status")]
    public string? AntrianValidationStatus { get; set; } // 'in_queue', 'processing', 'completed', 'failed'

    [Column("antrian_error_message")]
    [MaxLength(255)]
    public string? AntrianErrorMessage { get; set; }

    [Column("antrian_created_at")]
    public DateTime? AntrianCreatedAt { get; set; }

    [Column("antrian_updated_at")]
    public DateTime? AntrianUpdatedAt { get; set; }
}

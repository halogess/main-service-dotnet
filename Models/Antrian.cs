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

    [Column("antrian_worker")]
    public string AntrianWorker { get; set; } = string.Empty; // 'convert_pdf', 'struktur', 'visual'

    [Column("antrian_convert_status")]
    public string? AntrianConvertStatus { get; set; } // 'in_queue', 'processing', 'completed', 'failed'

    [Column("antrian_visual_status")]
    public string? AntrianVisualStatus { get; set; } // 'in_queue', 'processing', 'completed', 'failed'

    [Column("antrian_struktur_status")]
    public string? AntrianStrukturStatus { get; set; } // 'in_queue', 'processing', 'completed', 'failed'

    [Column("antrian_error_message")]
    [MaxLength(255)]
    public string? AntrianErrorMessage { get; set; }

    [Column("antrian_created_at")]
    public DateTime? AntrianCreatedAt { get; set; }

    [Column("antrian_updated_at")]
    public DateTime? AntrianUpdatedAt { get; set; }
}

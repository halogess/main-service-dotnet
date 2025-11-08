using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace _.Models;

[Table("antrian")]
public class Antrian
{
    [Key]
    [Column("antrian_id")]
    public int AntrianId { get; set; }

    [Column("antrian_tipe")]
    [MaxLength(8)]
    public string AntrianTipe { get; set; } = string.Empty;

    [Column("buku_id")]
    public int? BukuId { get; set; }

    [Column("dokumen_id")]
    public int? DokumenId { get; set; }

    [Column("antrian_worker")]
    [MaxLength(8)]
    public string AntrianWorker { get; set; } = string.Empty;

    [Column("antrian_status")]
    [MaxLength(10)]
    public string AntrianStatus { get; set; } = "not_start";

    [Column("antrian_error_message")]
    [MaxLength(255)]
    public string? AntrianErrorMessage { get; set; }

    [Column("antrian_created_at")]
    public DateTime? AntrianCreatedAt { get; set; }

    [Column("antrian_updated_at")]
    public DateTime? AntrianUpdatedAt { get; set; }
}

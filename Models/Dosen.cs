using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("tk_dosen")]
public class Dosen
{
    [Key]
    [Column("dosen_kode")]
    [MaxLength(10)]
    public string DosenKode { get; set; } = null!;

    [Column("dosen_nama_sk")]
    [MaxLength(255)]
    public string? DosenNamaSk { get; set; }

    [Column("dosen_status")]
    public int DosenStatus { get; set; }

    [Column("karyawan_nip")]
    [MaxLength(15)]
    public string? KaryawanNip { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("tk_karyawan")]
public class Karyawan
{
    [Key]
    [Column("karyawan_nip")]
    [MaxLength(15)]
    public string KaryawanNip { get; set; } = null!;

    [Column("karyawan_status")]
    public short KaryawanStatus { get; set; } = 1;
}

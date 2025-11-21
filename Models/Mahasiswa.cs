namespace ValidasiTugasAkhir.MainService.Models;

public class Mahasiswa
{
    public string MhsNrp { get; set; } = null!;
    public string? MhsNama { get; set; }
    public string? MhsEmail { get; set; }
    public string? MhsHp { get; set; }
    public byte? MhsStatus { get; set; }   // tinyint(1)
    public string? JurKode { get; set; }
    public decimal? MhsIpk { get; set; }

}

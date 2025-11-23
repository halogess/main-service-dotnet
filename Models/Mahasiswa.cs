namespace ValidasiTugasAkhir.MainService.Models;

public class Mahasiswa
{
    public string MhsNrp { get; set; } = null!;
    public string? MhsNama { get; set; }
    public string? MhsEmail { get; set; }
    public string? MhsHp { get; set; }
    public int? MhsStatus { get; set; }
    public string? JurKode { get; set; }
    public decimal? MhsIpk { get; set; }
    public string? MhsLulusTahun { get; set; }
    public int? MhsAngkatan { get; set; }
}

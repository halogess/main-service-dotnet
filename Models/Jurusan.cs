namespace ValidasiTugasAkhir.MainService.Models;

public class Jurusan
{
    public string JurKode { get; set; } = null!;
    public string? JurNama { get; set; }
    public string? JurSingkat { get; set; }
    public string? JurGelar { get; set; }
    public string? JurFakultas { get; set; }
    public int JurStatus { get; set; }
}

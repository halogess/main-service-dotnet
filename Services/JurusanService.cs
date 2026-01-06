using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IJurusanService
{
    List<JurusanDto> GetActiveJurusans();
}

public class JurusanDto
{
    public string Kode { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public string Singkatan { get; set; } = string.Empty;
}

public class JurusanService : IJurusanService
{
    private readonly SttsDbContext _sttsDb;
    private readonly ILogger<JurusanService> _logger;

    public JurusanService(SttsDbContext sttsDb, ILogger<JurusanService> logger)
    {
        _sttsDb = sttsDb;
        _logger = logger;
    }

    public List<JurusanDto> GetActiveJurusans()
    {
        return _sttsDb.Jurusans
            .Where(j => j.JurStatus == 1)
            .Select(j => new JurusanDto
            {
                Kode = j.JurKode ?? string.Empty,
                Nama = j.JurNama ?? string.Empty,
                Singkatan = j.JurSingkat ?? string.Empty
            })
            .ToList();
    }
}

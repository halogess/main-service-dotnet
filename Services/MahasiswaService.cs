using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace _.Services;

public class MahasiswaService : IMahasiswaService
{
    private readonly SttsDbContext _sttsDbContext;

    public MahasiswaService(SttsDbContext sttsDbContext)
    {
        _sttsDbContext = sttsDbContext;
    }

    public async Task<IEnumerable<Mahasiswa>> GetMahasiswasAsync()
    {
        return await _sttsDbContext.Mahasiswas.ToListAsync();
    }
}

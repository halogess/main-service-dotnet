using ValidasiTugasAkhir.MainService.Models;

namespace _.Services;

public interface IMahasiswaService
{
    Task<IEnumerable<Mahasiswa>> GetMahasiswasAsync();
}

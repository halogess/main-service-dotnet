using ValidasiTugasAkhir.MainService.Models;
using _.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ValidasiTugasAkhir.MainService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MahasiswaController : ControllerBase
    {
        private readonly IMahasiswaService _mahasiswaService;

        // Suntikkan service, bukan DbContext
        public MahasiswaController(IMahasiswaService mahasiswaService)
        {
            _mahasiswaService = mahasiswaService;
        }

        // Endpoint: GET /api/mahasiswa
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Mahasiswa>>> GetAll()
        {
            try
            {
                var mahasiswa = await _mahasiswaService.GetMahasiswasAsync();

                return Ok(mahasiswa.Select(m => new 
                {
                    mhs_nrp = m.MhsNrp,
                    mhs_nama = m.MhsNama,
                    mhs_email = m.MhsEmail,
                    mhs_hp = m.MhsHp,
                    mhs_status = m.MhsStatus,
                    jur_kode = m.JurKode,
                    mhs_ipk = m.MhsIpk
                }));
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Terjadi kesalahan internal" });
            }
        }
    }
}

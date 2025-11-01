using _.Models;
using _.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Namespace
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
                // Panggil service untuk mendapatkan data
                var mahasiswa = await _mahasiswaService.GetMahasiswasAsync();

                // Kembalikan data dengan status 200 OK
                return Ok(mahasiswa);
            }
            catch (Exception ex)
            {
                // Penanganan kesalahan sederhana
                return StatusCode(500, "Terjadi kesalahan internal.");
            }
        }
    }
}

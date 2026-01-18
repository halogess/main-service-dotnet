using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class KesalahanController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;

    public KesalahanController(KorektorBukuDbContext db)
    {
        _db = db;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetKesalahanById(uint id)
    {
        var kesalahan = await _db.Kesalahans
            .Include(k => k.Details)
            .FirstOrDefaultAsync(k => k.KesalahanId == id);

        if (kesalahan == null)
            return NotFound(new { message = "Kesalahan tidak ditemukan" });

        return Ok(new
        {
            id = kesalahan.KesalahanId,
            kategori = kesalahan.KesalahanKategori,
            lokasi = kesalahan.KesalahanLokasi,
            details = kesalahan.Details.Select(d => new
            {
                id = d.KesalahanDetailId,
                judul = d.KesalahanDetailJudul,
                penjelasan = d.KesalahanDetailPenjelasan,
                steps = d.KesalahanDetailSteps,
                is_required = d.KesalahanIsRequired
            }).ToList()
        });
    }
}


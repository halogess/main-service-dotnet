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
    public async Task<IActionResult> GetKesalahanDetail(uint id)
    {
        var detail = await _db.KesalahanDetails
            .FirstOrDefaultAsync(d => d.KesalahanDetailId == id);

        if (detail == null)
            return NotFound(new { message = "Detail kesalahan tidak ditemukan" });

        return Ok(new
        {
            id = detail.KesalahanDetailId,
            kesalahan_id = detail.KesalahanId,
            judul = detail.KesalahanDetailJudul,
            penjelasan = detail.KesalahanDetailPenjelasan,
            steps = detail.KesalahanDetailSteps,
            is_required = detail.KesalahanIsRequired
        });
    }
}


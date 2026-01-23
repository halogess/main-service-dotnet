using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DosenController : ControllerBase
{
    private readonly SttsDbContext _db;

    public DosenController(SttsDbContext db)
    {
        _db = db;
    }

    // GET: api/dosen - Get all active dosen (join with karyawan, karyawan_status = 1)
    [HttpGet]
    public async Task<IActionResult> GetAllDosen()
    {
        var dosens = await (
            from d in _db.Dosens
            join k in _db.Karyawans on d.KaryawanNip equals k.KaryawanNip
            where k.KaryawanStatus == 1
            orderby d.DosenNamaSk
            select new
            {
                kode = d.DosenKode,
                nama = d.DosenNamaSk
            }
        ).ToListAsync();

        return Ok(new { data = dosens, total = dosens.Count });
    }
}

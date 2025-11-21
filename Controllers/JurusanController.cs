using Microsoft.AspNetCore.Mvc;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JurusanController : ControllerBase
{
    private readonly SttsDbContext _sttsDb;

    public JurusanController(SttsDbContext sttsDb)
    {
        _sttsDb = sttsDb;
    }

    [HttpGet]
    public IActionResult GetJurusan()
    {
        var role = HttpContext.Items["Role"]?.ToString();
        
        if (role != "admin")
        {
            return Forbid();
        }

        var jurusans = _sttsDb.Jurusans
            .Where(j => j.JurStatus == 1)
            .Select(j => new {
                kode = j.JurKode,
                nama = j.JurNama,
                singkatan = j.JurSingkat
            })
            .ToList();

        return Ok(jurusans);
    }
}

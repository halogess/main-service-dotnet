using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JurusanController : ControllerBase
{
    private readonly IJurusanService _jurusanService;

    public JurusanController(IJurusanService jurusanService)
    {
        _jurusanService = jurusanService;
    }

    [HttpGet]
    public IActionResult GetJurusan()
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var jurusans = _jurusanService.GetActiveJurusans();
        
        return Ok(jurusans.Select(j => new
        {
            kode = j.Kode,
            nama = j.Nama,
            singkatan = j.Singkatan
        }));
    }
}

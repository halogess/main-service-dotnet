using Microsoft.AspNetCore.Mvc;
using _.Services;

namespace _.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BukuController : ControllerBase
{
    private readonly IBukuService _bukuService;

    public BukuController(IBukuService bukuService)
    {
        _bukuService = bukuService;
    }

    [HttpPost]
    public async Task<IActionResult> UploadBuku([FromForm] string judul, [FromForm] List<IFormFile> files)
    {
        try
        {
            var nrp = HttpContext.Items["Nrp"]?.ToString();

            var buku = await _bukuService.UploadBuku(nrp!, judul, files);

            return Ok(new { message = "Buku berhasil diupload", data = buku });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Terjadi kesalahan", error = ex.Message });
        }
    }
}

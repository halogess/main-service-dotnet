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

            return Ok(new 
            { 
                message = "Buku berhasil diupload", 
                data = new 
                {
                    buku_id = buku.BukuId,
                    mhs_nrp = buku.MhsNrp,
                    buku_judul = buku.BukuJudul,
                    buku_status = buku.BukuStatus,
                    buku_created_at = buku.BukuCreatedAt,
                    buku_updated_at = buku.BukuUpdatedAt,
                    dokumens = buku.Dokumens.Select(d => new 
                    {
                        dokumen_id = d.DokumenId,
                        mhs_nrp = d.MhsNrp,
                        dokumen_filename = d.DokumenFilename,
                        dokumen_status = d.DokumenStatus,
                        dokumen_created_at = d.DokumenCreatedAt,
                        dokumen_updated_at = d.DokumenUpdatedAt
                    })
                }
            });
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

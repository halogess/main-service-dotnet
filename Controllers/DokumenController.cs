using Microsoft.AspNetCore.Mvc;
using _.Services;

namespace _.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DokumenController : ControllerBase
{
    private readonly IDokumenService _dokumenService;

    public DokumenController(IDokumenService dokumenService)
    {
        _dokumenService = dokumenService;
    }

    [HttpPost]
    public async Task<IActionResult> UploadDokumen(IFormFile file)
    {
        try
        {
            var nrp = HttpContext.Items["Nrp"]?.ToString();

            if (string.IsNullOrEmpty(nrp))
            {
                return Unauthorized(new { message = "NRP tidak ditemukan" });
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "File tidak boleh kosong" });
            }

            var dokumen = await _dokumenService.UploadDokumen(nrp, file);

            return Ok(new 
            { 
                message = "Dokumen berhasil diupload", 
                data = new 
                {
                    dokumen_id = dokumen.DokumenId,
                    mhs_nrp = dokumen.MhsNrp,
                    dokumen_filename = dokumen.DokumenFilename,
                    dokumen_status = dokumen.DokumenStatus,
                    dokumen_created_at = dokumen.DokumenCreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    dokumen_updated_at = dokumen.DokumenUpdatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
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

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            var dokumen = await _dokumenService.UpdateStatus(id, request.status);

            return Ok(new 
            { 
                message = request.status == 1 ? "Dokumen diterima" : "Dokumen ditolak", 
                data = new 
                {
                    dokumen_id = dokumen.DokumenId,
                    mhs_nrp = dokumen.MhsNrp,
                    dokumen_filename = dokumen.DokumenFilename,
                    dokumen_status = dokumen.DokumenStatus,
                    dokumen_created_at = dokumen.DokumenCreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    dokumen_updated_at = dokumen.DokumenUpdatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
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

public class UpdateStatusRequest
{
    public byte status { get; set; }
}

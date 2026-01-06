using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

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
    public async Task<IActionResult> UploadDokumen(IFormFile file, [FromForm] string? tipe = null)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });
        
        if (_dokumenService.HasDokumenInQueue(nrp!))
            return BadRequest(new { message = "Masih ada dokumen dalam antrian" });
        
        try
        {
            var dokumen = await _dokumenService.UploadDokumen(nrp!, file, tipe);
            return Ok(new { message = "Dokumen berhasil diupload", dokumen_id = dokumen.DokumenId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id}/batal")]
    public async Task<IActionResult> BatalDokumen(int id)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        try
        {
            await _dokumenService.BatalDokumen(nrp!, id);
            return Ok(new { message = "Dokumen berhasil dibatalkan" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("can-upload")]
    public IActionResult CanUpload()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        return Ok(new { can_upload = _dokumenService.CanUpload(nrp!) });
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var stats = _dokumenService.GetStats(nrp!);
        
        return Ok(new
        {
            total = stats.Total,
            dibatalkan = stats.Dibatalkan,
            dalam_antrian = stats.DalamAntrian,
            diproses = stats.Diproses,
            lolos = stats.Lolos,
            tidak_lolos = stats.TidakLolos
        });
    }

    [HttpGet]
    public IActionResult GetDokumen([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var result = _dokumenService.GetDokumenList(nrp!, status, sort, limit, offset);

        return Ok(new
        {
            data = result.Data.Select(d => new
            {
                id = d.Id,
                filename = d.Filename,
                tanggal_upload = d.TanggalUpload,
                ukuran_file = d.UkuranFile,
                status = d.Status,
                jumlah_kesalahan = d.JumlahKesalahan
            }),
            total = result.Total,
            limit = result.Limit,
            offset = result.Offset
        });
    }
}
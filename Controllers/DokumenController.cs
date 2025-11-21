using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DokumenController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly IDokumenService _dokumenService;
    private readonly IWebSocketService _wsService;

    public DokumenController(KorektorBukuDbContext db, IDokumenService dokumenService, IWebSocketService wsService)
    {
        _db = db;
        _dokumenService = dokumenService;
        _wsService = wsService;
    }

    [HttpPost]
    public async Task<IActionResult> UploadDokumen(IFormFile file)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });
        
        var hasQueue = _db.Dokumens.Any(d => d.MhsNrp == nrp && d.DokumenStatus == "dalam_antrian");
        if (hasQueue)
            return BadRequest(new { message = "Masih ada dokumen dalam antrian" });
        
        try
        {
            var dokumen = await _dokumenService.UploadDokumen(nrp!, file);
            return Ok(new { message = "Dokumen berhasil diupload", dokumen_id = dokumen.DokumenId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id}/cancel")]
    public async Task<IActionResult> CancelDokumen(int id)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var dokumen = _db.Dokumens.FirstOrDefault(d => d.DokumenId == id && d.MhsNrp == nrp);
        
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });
        
        if (dokumen.DokumenStatus != "dalam_antrian" && dokumen.DokumenStatus != "diproses")
            return BadRequest(new { message = "Dokumen tidak dapat dibatalkan" });
        
        dokumen.DokumenStatus = "dibatalkan";
        _db.SaveChanges();
        
        await _wsService.NotifyDokumenCancelled(nrp!, id);
        
        return Ok(new { message = "Dokumen berhasil dibatalkan" });
    }

    [HttpGet("can-upload")]
    public IActionResult CanUpload()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var hasQueue = _db.Dokumens.Any(d => d.MhsNrp == nrp && d.DokumenStatus == "dalam_antrian");
        
        return Ok(new {
            can_upload = !hasQueue
        });
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var dokumens = _db.Dokumens.Where(d => d.MhsNrp == nrp).ToList();
        var grouped = dokumens.GroupBy(d => d.DokumenStatus).ToDictionary(g => g.Key ?? "unknown", g => g.Count());
        
        return Ok(new {
            total = dokumens.Count,
            dibatalkan = grouped.GetValueOrDefault("dibatalkan", 0),
            dalam_antrian = grouped.GetValueOrDefault("dalam_antrian", 0),
            diproses = grouped.GetValueOrDefault("diproses", 0),
            lolos = grouped.GetValueOrDefault("lolos", 0),
            tidak_lolos = grouped.GetValueOrDefault("tidak_lolos", 0)
        });
    }

    [HttpGet]
    public IActionResult GetDokumen([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var query = _db.Dokumens.Where(d => d.MhsNrp == nrp);
        
        if (!string.IsNullOrEmpty(status))
        {
            var statuses = status.Split(',').Select(s => s.Trim()).ToList();
            query = query.Where(d => statuses.Contains(d.DokumenStatus));
        }
        
        query = sort.ToLower() == "asc" 
            ? query.OrderBy(d => d.DokumenCreatedAt)
            : query.OrderByDescending(d => d.DokumenCreatedAt);
        
        var totalCount = query.Count();
        
        var dokumenList = query
            .Skip(offset)
            .Take(limit)
            .Select(d => new {
                id = d.DokumenId,
                filename = d.DokumenFilename,
                tanggal_upload = d.DokumenCreatedAt,
                ukuran_file = d.DokumenFilesizeBytes ?? 0L,
                status = d.DokumenStatus,
                jumlah_kesalahan = d.DokumenJumlahKesalahan ?? 0
            })
            .ToList();

        return Ok(new {
            data = dokumenList,
            total = totalCount,
            limit = limit,
            offset = offset
        });
    }
}
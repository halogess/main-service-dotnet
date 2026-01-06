using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AturanController : ControllerBase
{
    private readonly IAturanService _aturanService;

    public AturanController(IAturanService aturanService)
    {
        _aturanService = aturanService;
    }

    // GET: api/aturan (Admin only)
    [HttpGet]
    public async Task<IActionResult> GetAllAturan()
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var aturanList = await _aturanService.GetAllAsync();

        var result = aturanList.Select(a => new
        {
            id = a.AturanId,
            versi = a.AturanVersi,
            status = a.AturanStatus,
            skor_minimum = a.AturanSkorMinimum,
            template_file_path = a.AturanTemplateFilePath,
            created_at = a.AturanCreatedAt,
            updated_at = a.AturanUpdatedAt
        }).ToList();

        return Ok(new { data = result, total = result.Count });
    }

    // GET: api/aturan/{id} (Admin only) - Returns aturan with details
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAturanById(uint id)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var result = await _aturanService.GetByIdWithDetailsAsync(id);

        if (result == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        return Ok(new
        {
            aturan_detail = result.Details.Select(d => new
            {
                id = d.AturanDetailId,
                kategori = d.AturanDetailKategori,
                key = d.AturanDetailKey,
                json_value = d.AturanDetailJsonValue,
                status = d.AturanDetailStatus,
                catatan = d.AturanDetailCatatan
            })
        });
    }

    // GET: api/aturan/aktif (Public - All authenticated users) - Returns aturan with details
    [HttpGet("aktif")]
    public async Task<IActionResult> GetAturanAktif()
    {
        var result = await _aturanService.GetAktifWithDetailsAsync();

        if (result == null)
            return NotFound(new { message = "Tidak ada aturan aktif" });

        return Ok(new
        {
            id = result.Aturan.AturanId,
            versi = result.Aturan.AturanVersi,
            status = result.Aturan.AturanStatus,
            skor_minimum = result.Aturan.AturanSkorMinimum,
            template_file_path = result.Aturan.AturanTemplateFilePath,
            created_at = result.Aturan.AturanCreatedAt,
            updated_at = result.Aturan.AturanUpdatedAt,
            details = result.Details.Select(d => new
            {
                id = d.AturanDetailId,
                kategori = d.AturanDetailKategori,
                key = d.AturanDetailKey,
                json_value = d.AturanDetailJsonValue,
                status = d.AturanDetailStatus,
                catatan = d.AturanDetailCatatan
            })
        });
    }

    // POST: api/aturan (Admin only)
    [HttpPost]
    public async Task<IActionResult> CreateAturan([FromBody] CreateAturanRequest request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        try
        {
            var aturan = await _aturanService.CreateAsync(
                request.Versi,
                request.Status ?? 1,
                request.SkorMinimum ?? 80,
                request.TemplateFilePath
            );

            return Ok(new
            {
                message = "Aturan berhasil dibuat",
                id = aturan.AturanId,
                versi = aturan.AturanVersi
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT: api/aturan/{id} (Admin only)
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAturan(uint id, [FromBody] UpdateAturanRequest request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        try
        {
            var aturan = await _aturanService.UpdateAsync(
                id,
                request.Versi,
                request.Status,
                request.SkorMinimum,
                request.TemplateFilePath
            );

            return Ok(new
            {
                message = "Aturan berhasil diupdate",
                id = aturan.AturanId,
                versi = aturan.AturanVersi
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}

// Request DTOs
public class CreateAturanRequest
{
    public string Versi { get; set; } = string.Empty;
    public sbyte? Status { get; set; }
    public uint? SkorMinimum { get; set; }
    public string? TemplateFilePath { get; set; }
}

public class UpdateAturanRequest
{
    public string? Versi { get; set; }
    public sbyte? Status { get; set; }
    public uint? SkorMinimum { get; set; }
    public string? TemplateFilePath { get; set; }
}

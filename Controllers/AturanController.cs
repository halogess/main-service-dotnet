using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AturanController : ControllerBase
{
    private readonly IAturanService _aturanService;
    private readonly KorektorBukuDbContext _db;

    public AturanController(IAturanService aturanService, KorektorBukuDbContext db)
    {
        _aturanService = aturanService;
        _db = db;
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

    // GET: api/aturan/{id}/export (Admin only)
    [HttpGet("{id}/export")]
    public async Task<IActionResult> ExportAturan(uint id)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var result = await _aturanService.GetByIdWithDetailsAsync(id);
        if (result == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        var fileBytes = AturanExcelExportBuilder.BuildWorkbook(result.Aturan.AturanVersi, result.Details);
        var safeVersion = string.IsNullOrWhiteSpace(result.Aturan.AturanVersi)
            ? $"aturan-{id}"
            : string.Concat(result.Aturan.AturanVersi.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var fileName = $"{safeVersion}.xlsx";

        return File(fileBytes, AturanExcelExportBuilder.ContentType, fileName);
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
                request.versi,
                request.status ?? 1,
                request.skor_minimum ?? 80,
                request.template_file_path
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

    // PATCH: api/aturan/{id} (Admin only)
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateAturan(uint id, [FromBody] UpdateAturanRequest request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        try
        {
            var aturan = await _aturanService.UpdateAsync(
                id,
                request.versi,
                request.status,
                request.skor_minimum,
                request.template_file_path
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

    // PATCH: api/aturan/{id}/detail (Admin only)
    [HttpPatch("{id}/detail")]
    public async Task<IActionResult> PatchAturanDetail(uint id, [FromBody] AturanDetailPatchRequest request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        if (request.details == null || request.details.Count == 0)
            return BadRequest(new { message = "details tidak boleh kosong" });

        var rawDetailIds = request.details
            .Select(d => d.aturan_detail_id)
            .ToList();

        if (rawDetailIds.Any(idValue => !idValue.HasValue || idValue.Value <= 0))
            return BadRequest(new { message = "aturan_detail_id wajib diisi untuk setiap detail" });

        var detailIds = rawDetailIds
            .Select(idValue => idValue!.Value)
            .Distinct()
            .ToList();

        if (detailIds.Count != rawDetailIds.Count)
            return BadRequest(new { message = "aturan_detail_id harus unik untuk setiap detail" });

        var normalizedJsonByDetailId = new Dictionary<uint, string>();
        foreach (var detail in request.details)
        {
            if (detail.aturan_detail_id is not { } detailId || detail.json_value == null)
                continue;

            if (!AturanDetailJsonNormalizer.TryNormalize(detail.json_value, out var normalizedJson, out var errorMessage))
            {
                return BadRequest(new
                {
                    message = $"json_value tidak valid untuk aturan_detail_id {detailId}: {errorMessage}"
                });
            }

            normalizedJsonByDetailId[detailId] = normalizedJson!;
        }

        var existingDetails = await _db.AturanDetails
            .Where(d => d.AturanId == id && detailIds.Contains(d.AturanDetailId))
            .ToListAsync();

        if (existingDetails.Count != detailIds.Count)
        {
            var missingIds = detailIds.Except(existingDetails.Select(d => d.AturanDetailId)).ToList();
            return NotFound(new { message = "Detail tidak ditemukan", missing_ids = missingIds });
        }

        var existingById = existingDetails.ToDictionary(d => d.AturanDetailId);
        foreach (var d in request.details)
        {
            var existing = existingById[d.aturan_detail_id!.Value];
            if (d.kategori != null)
                existing.AturanDetailKategori = d.kategori;
            if (d.key != null)
                existing.AturanDetailKey = d.key;
            if (normalizedJsonByDetailId.TryGetValue(d.aturan_detail_id!.Value, out var normalizedJson))
                existing.AturanDetailJsonValue = normalizedJson;
            if (d.status.HasValue)
                existing.AturanDetailStatus = d.status.Value;
            if (d.catatan != null)
                existing.AturanDetailCatatan = d.catatan;
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Detail berhasil diupdate", updated = existingDetails.Count });
    }
}

// Request DTOs
public class CreateAturanRequest
{
    public string versi { get; set; } = string.Empty;
    public sbyte? status { get; set; }
    public uint? skor_minimum { get; set; }
    public string? template_file_path { get; set; }
}

public class UpdateAturanRequest
{
    public string? versi { get; set; }
    public sbyte? status { get; set; }
    public uint? skor_minimum { get; set; }
    public string? template_file_path { get; set; }
}

public class AturanDetailPatchRequest
{
    public List<AturanDetailPatchItem> details { get; set; } = new();
}

public class AturanDetailPatchItem
{
    public uint? aturan_detail_id { get; set; }
    public string? kategori { get; set; }
    public string? key { get; set; }
    public string? json_value { get; set; }
    public sbyte? status { get; set; }
    public string? catatan { get; set; }
}

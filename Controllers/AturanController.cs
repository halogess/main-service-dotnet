using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AturanController : ControllerBase
{
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PdfContentType = "application/pdf";

    private readonly IAturanService _aturanService;
    private readonly KorektorBukuDbContext _db;
    private readonly string _storageRoot;

    public AturanController(IAturanService aturanService, KorektorBukuDbContext db)
    {
        _aturanService = aturanService;
        _db = db;
        _storageRoot = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAturan([FromQuery] int? limit = null, [FromQuery] int offset = 0)
    {
        if (!IsAdmin())
            return Forbid();

        if (limit.HasValue && limit.Value <= 0)
            return BadRequest(new { message = "limit harus lebih dari 0" });

        if (offset < 0)
            return BadRequest(new { message = "offset tidak boleh negatif" });

        var aturanList = await _aturanService.GetAllAsync(limit, offset);
        var result = aturanList.Data.Select(MapAturanSummary).ToList();
        return Ok(new { data = result, total = aturanList.Total, limit = aturanList.Limit, offset = aturanList.Offset });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAturanById(uint id)
    {
        if (!IsAdmin())
            return Forbid();

        var result = await _aturanService.GetByIdWithDetailsAsync(id);
        if (result == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        return Ok(MapAturanDetail(result));
    }

    [HttpGet("{id}/export")]
    public async Task<IActionResult> ExportAturan(uint id)
    {
        if (!IsAdmin())
            return Forbid();

        var result = await _aturanService.GetByIdWithDetailsAsync(id);
        if (result == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        var fileBytes = AturanExcelExportBuilder.BuildWorkbook(result.Aturan.AturanVersi, result.Details);
        var safeVersion = string.IsNullOrWhiteSpace(result.Aturan.AturanVersi)
            ? $"aturan-{id}"
            : string.Concat(result.Aturan.AturanVersi.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return File(fileBytes, AturanExcelExportBuilder.ContentType, $"{safeVersion}.xlsx");
    }

    [HttpPost("{id}/import-preview")]
    public async Task<IActionResult> ImportPreviewAturan(
        uint id,
        [FromForm] AturanImportPreviewRequest request,
        [FromServices] IAturanExcelImportPreviewService importPreviewService,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return Forbid();

        var aturan = await _aturanService.GetByIdAsync(id);
        if (aturan == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        try
        {
            if (request.file == null)
                return BadRequest(new { message = "file wajib diisi" });

            var result = await importPreviewService.PreviewAsync(id, request.file, cancellationToken);
            return Ok(new
            {
                message = "File import berhasil diparsing ke draft",
                total_rows = result.TotalRows,
                changed_rows = result.ChangedRows,
                changed_details = result.ChangedDetails,
                details = result.Details.Select(detail => new
                {
                    aturan_detail_id = detail.AturanDetailId,
                    kategori = detail.Kategori,
                    key = detail.Key,
                    json_value = detail.JsonValue
                }).ToList()
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

    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadTemplate(uint id)
    {
        var aturan = await _aturanService.GetByIdAsync(id);
        if (aturan == null || string.IsNullOrWhiteSpace(aturan.AturanTemplateFilePath))
            return NotFound(new { message = "Template tidak ditemukan" });

        if (!TryResolveStorageFilePath(aturan.AturanTemplateFilePath, out var fullPath))
            return NotFound(new { message = "File template tidak ditemukan" });

        var fileName = Path.GetFileName(fullPath);
        return PhysicalFile(fullPath, DocxContentType, fileName);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> PreviewTemplatePdf(uint id)
    {
        var aturan = await _aturanService.GetByIdAsync(id);
        if (aturan == null || string.IsNullOrWhiteSpace(aturan.AturanTemplatePdfPath))
            return NotFound(new { message = "File PDF template belum tersedia" });

        if (!TryResolveStorageFilePath(aturan.AturanTemplatePdfPath, out var fullPath))
            return NotFound(new { message = "File PDF template tidak ditemukan" });

        return PhysicalFile(fullPath, PdfContentType);
    }

    [HttpGet("aktif")]
    [HttpGet("active")]
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
            template_pdf_path = result.Aturan.AturanTemplatePdfPath,
            created_at = result.Aturan.AturanCreatedAt,
            updated_at = result.Aturan.AturanUpdatedAt,
            details = result.Details.Select(MapDetailRow).ToList(),
            aturan_detail = result.Details.Select(MapDetailRow).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateAturan([FromBody] CreateAturanRequest request)
    {
        if (!IsAdmin())
            return Forbid();

        try
        {
            var aturan = await _aturanService.CreateAsync(
                request.versi,
                request.status ?? AturanStatusValues.TidakAktif,
                request.skor_minimum ?? 80,
                request.template_file_path);

            return Ok(new
            {
                message = "Aturan berhasil dibuat",
                id = aturan.AturanId,
                versi = aturan.AturanVersi,
                status = aturan.AturanStatus
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

    [HttpPost("upload")]
    public async Task<IActionResult> UploadTemplate([FromForm] UploadAturanRequest request, CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return Forbid();

        try
        {
            if (request.file == null)
                return BadRequest(new { message = "file wajib diisi" });

            var aturan = await _aturanService.UploadAsync(
                request.versi,
                request.skor_minimum ?? 80,
                request.file,
                cancellationToken);

            return Ok(new
            {
                id = aturan.AturanId,
                versi = aturan.AturanVersi,
                status = aturan.AturanStatus
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

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateAturan(uint id, [FromBody] UpdateAturanRequest request)
    {
        if (!IsAdmin())
            return Forbid();

        try
        {
            var aturan = await _aturanService.UpdateAsync(
                id,
                request.versi,
                request.skor_minimum,
                null);

            return Ok(new
            {
                message = "Aturan berhasil diupdate",
                id = aturan.AturanId,
                versi = aturan.AturanVersi,
                status = aturan.AturanStatus
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPatch("{id}/detail")]
    public async Task<IActionResult> PatchAturanDetail(uint id, [FromBody] AturanDetailPatchRequest request)
    {
        if (!IsAdmin())
            return Forbid();

        var aturan = await _db.Aturans.FirstOrDefaultAsync(a => a.AturanId == id);
        if (aturan == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        var details = request.details ?? [];
        if (details.Count > 0)
        {
            var rawDetailIds = details
                .Select(item => item.aturan_detail_id)
                .ToList();

            if (rawDetailIds.Any(detailId => !detailId.HasValue || detailId.Value <= 0))
                return BadRequest(new { message = "aturan_detail_id wajib diisi untuk setiap detail" });

            var detailIds = rawDetailIds
                .Select(detailId => detailId!.Value)
                .Distinct()
                .ToList();

            if (detailIds.Count != rawDetailIds.Count)
                return BadRequest(new { message = "aturan_detail_id harus unik untuk setiap detail" });

            var existingDetails = await _db.AturanDetails
                .Where(detail => detail.AturanId == id && detailIds.Contains(detail.AturanDetailId))
                .ToListAsync();

            if (existingDetails.Count != detailIds.Count)
            {
                var missingIds = detailIds.Except(existingDetails.Select(detail => detail.AturanDetailId)).ToList();
                return NotFound(new { message = "Detail tidak ditemukan", missing_ids = missingIds });
            }

            var existingById = existingDetails.ToDictionary(detail => detail.AturanDetailId);
            var normalizedByDetailId = new Dictionary<uint, (string CanonicalKey, string CanonicalKategori, string NormalizedJson)>();
            var requestedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var detail in details)
            {
                if (detail.aturan_detail_id is not { } detailId)
                    continue;

                var existingDetail = existingById[detailId];
                var requestedKey = detail.key ?? existingDetail.AturanDetailKey;
                var requestedJson = detail.json_value ?? existingDetail.AturanDetailJsonValue;
                if (!TryNormalizePatchedDetail(
                        requestedKey,
                        requestedJson,
                        detailId,
                        out var canonicalKey,
                        out var canonicalKategori,
                        out var normalizedJson,
                        out var errorResponse))
                {
                    return errorResponse!;
                }

                if (!requestedKeys.Add(canonicalKey))
                    return BadRequest(new { message = $"key aturan `{canonicalKey}` duplikat dalam request." });

                normalizedByDetailId[detailId] = (canonicalKey, canonicalKategori, normalizedJson!);
            }

            foreach (var detail in details)
            {
                var existing = existingById[detail.aturan_detail_id!.Value];
                if (normalizedByDetailId.TryGetValue(detail.aturan_detail_id.Value, out var normalizedDetail))
                {
                    existing.AturanDetailKategori = normalizedDetail.CanonicalKategori;
                    existing.AturanDetailKey = normalizedDetail.CanonicalKey;
                    existing.AturanDetailJsonValue = normalizedDetail.NormalizedJson;
                }
                if (detail.catatan != null)
                    existing.AturanDetailCatatan = detail.catatan;
            }
        }

        if (string.Equals(aturan.AturanStatus, AturanStatusValues.MenungguReview, StringComparison.OrdinalIgnoreCase))
        {
            aturan.AturanStatus = AturanStatusValues.TidakAktif;
        }
        aturan.AturanUpdatedAt = AppClock.Now;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Detail berhasil diupdate",
            updated = details.Count,
            status = aturan.AturanStatus
        });
    }

    [HttpPut("{id}/activate")]
    public async Task<IActionResult> ActivateAturan(uint id, CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return Forbid();

        try
        {
            var aturan = await _aturanService.ActivateAsync(id, cancellationToken);
            return Ok(new
            {
                message = "Aturan berhasil diaktifkan",
                id = aturan.AturanId,
                status = aturan.AturanStatus
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAturan(uint id, CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return Forbid();

        try
        {
            await _aturanService.DeleteAsync(id, cancellationToken);
            return Ok(new { message = "Aturan berhasil dihapus" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private bool IsAdmin()
    {
        return string.Equals(HttpContext.Items["Role"]?.ToString(), "admin", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveStorageFilePath(string relativePath, out string fullPath)
    {
        var normalizedPath = relativePath.TrimStart('/', '\\');
        fullPath = Path.GetFullPath(Path.Combine(_storageRoot, normalizedPath));
        var storagePath = Path.GetFullPath(_storageRoot);

        return fullPath.StartsWith(storagePath, StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(fullPath);
    }

    private static object MapAturanSummary(Aturan aturan)
    {
        return new
        {
            id = aturan.AturanId,
            versi = aturan.AturanVersi,
            status = aturan.AturanStatus,
            skor_minimum = aturan.AturanSkorMinimum,
            template_file_path = aturan.AturanTemplateFilePath,
            template_pdf_path = aturan.AturanTemplatePdfPath,
            created_at = aturan.AturanCreatedAt,
            updated_at = aturan.AturanUpdatedAt
        };
    }

    private static object MapAturanDetail(AturanWithDetails result)
    {
        var detailRows = result.Details.Select(MapDetailRow).ToList();
        return new
        {
            id = result.Aturan.AturanId,
            versi = result.Aturan.AturanVersi,
            status = result.Aturan.AturanStatus,
            skor_minimum = result.Aturan.AturanSkorMinimum,
            template_file_path = result.Aturan.AturanTemplateFilePath,
            template_pdf_path = result.Aturan.AturanTemplatePdfPath,
            created_at = result.Aturan.AturanCreatedAt,
            updated_at = result.Aturan.AturanUpdatedAt,
            aturan_detail = detailRows,
            details = detailRows
        };
    }

    private static object MapDetailRow(AturanDetail detail)
    {
        return new
        {
            id = detail.AturanDetailId,
            aturan_detail_id = detail.AturanDetailId,
            kategori = detail.AturanDetailKategori,
            aturan_detail_kategori = detail.AturanDetailKategori,
            key = detail.AturanDetailKey,
            aturan_detail_key = detail.AturanDetailKey,
            json_value = detail.AturanDetailJsonValue,
            aturan_detail_json_value = detail.AturanDetailJsonValue,
            catatan = detail.AturanDetailCatatan,
            aturan_detail_catatan = detail.AturanDetailCatatan
        };
    }

    private static bool TryNormalizePatchedDetail(
        string? requestedKey,
        string? requestedJson,
        uint detailId,
        out string canonicalKey,
        out string canonicalKategori,
        out string? normalizedJson,
        out IActionResult? errorResponse)
    {
        canonicalKey = string.Empty;
        canonicalKategori = string.Empty;
        normalizedJson = null;
        errorResponse = null;

        if (!AturanDetailContract.IsCanonicalKey(requestedKey))
        {
            errorResponse = new BadRequestObjectResult(new
            {
                message = $"key aturan canonical wajib diisi untuk aturan_detail_id {detailId}"
            });
            return false;
        }

        canonicalKey = AturanDetailContract.NormalizeCanonicalKey(requestedKey);
        canonicalKategori = AturanDetailContract.GetKategori(canonicalKey);
        var sourceJson = string.IsNullOrWhiteSpace(requestedJson)
            ? AturanDetailContract.GetDefaultJsonValue(canonicalKey)
            : requestedJson;

        if (!AturanDetailCanonicalizer.TryCanonicalize(
                canonicalKey,
                sourceJson,
                out normalizedJson,
                out var canonicalChanged,
                out var errorMessage))
        {
            errorResponse = new BadRequestObjectResult(new
            {
                message = $"json_value tidak valid untuk aturan_detail_id {detailId}: {errorMessage}"
            });
            return false;
        }

        if (!AturanDetailShapeValidator.TryValidate(canonicalKey, normalizedJson, out var shapeErrorMessage))
        {
            errorResponse = new BadRequestObjectResult(new
            {
                message = $"json_value tidak sesuai schema untuk aturan_detail_id {detailId}: {shapeErrorMessage}"
            });
            return false;
        }

        return true;
    }
}

public class CreateAturanRequest
{
    public string versi { get; set; } = string.Empty;
    public string? status { get; set; }
    public uint? skor_minimum { get; set; }
    public string? template_file_path { get; set; }
}

public class UploadAturanRequest
{
    public string versi { get; set; } = string.Empty;
    public uint? skor_minimum { get; set; }
    public IFormFile? file { get; set; }
}

public class AturanImportPreviewRequest
{
    public IFormFile? file { get; set; }
}

public class UpdateAturanRequest
{
    public string? versi { get; set; }
    public uint? skor_minimum { get; set; }
}

public class AturanDetailPatchRequest
{
    public List<AturanDetailPatchItem>? details { get; set; } = [];
}

public class AturanDetailPatchItem
{
    public uint? aturan_detail_id { get; set; }
    public string? kategori { get; set; }
    public string? key { get; set; }
    public string? json_value { get; set; }
    public string? catatan { get; set; }
}

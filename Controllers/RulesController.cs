using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

/// <summary>
/// Development-only controller for managing Aturan and AturanDetail.
/// No authentication required.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RulesController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;

    public RulesController(KorektorBukuDbContext db)
    {
        _db = db;
    }

    // GET: api/rules - Get all aturan with details
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var aturans = await _db.Aturans
            .AsNoTracking()
            .OrderByDescending(a => a.AturanCreatedAt)
            .ToListAsync();

        var result = new List<object>();
        foreach (var a in aturans)
        {
            var details = CanonicalizeDetailsForResponse(await _db.AturanDetails
                .AsNoTracking()
                .Where(d => d.AturanId == a.AturanId)
                .ToListAsync());

            result.Add(new
            {
                aturan_id = a.AturanId,
                aturan_versi = a.AturanVersi,
                aturan_status = a.AturanStatus,
                aturan_skor_minimum = a.AturanSkorMinimum,
                aturan_template_file_path = a.AturanTemplateFilePath,
                aturan_created_at = a.AturanCreatedAt,
                aturan_updated_at = a.AturanUpdatedAt,
                details = details.Select(d => new
                {
                    aturan_detail_id = d.AturanDetailId,
                    aturan_id = d.AturanId,
                    aturan_detail_kategori = d.AturanDetailKategori,
                    aturan_detail_key = d.AturanDetailKey,
                    aturan_detail_json_value = d.AturanDetailJsonValue,
                    aturan_detail_catatan = d.AturanDetailCatatan
                })
            });
        }

        return Ok(new { data = result, total = result.Count });
    }

    // GET: api/rules/{id} - Get single aturan with details
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(uint id)
    {
        var aturan = await _db.Aturans
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.AturanId == id);
        if (aturan == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        var details = CanonicalizeDetailsForResponse(await _db.AturanDetails
            .AsNoTracking()
            .Where(d => d.AturanId == id)
            .ToListAsync());

        return Ok(new
        {
            aturan_id = aturan.AturanId,
            aturan_versi = aturan.AturanVersi,
            aturan_status = aturan.AturanStatus,
            aturan_skor_minimum = aturan.AturanSkorMinimum,
            aturan_template_file_path = aturan.AturanTemplateFilePath,
            aturan_created_at = aturan.AturanCreatedAt,
            aturan_updated_at = aturan.AturanUpdatedAt,
            details = details.Select(d => new
            {
                aturan_detail_id = d.AturanDetailId,
                aturan_id = d.AturanId,
                aturan_detail_kategori = d.AturanDetailKategori,
                aturan_detail_key = d.AturanDetailKey,
                aturan_detail_json_value = d.AturanDetailJsonValue,
                aturan_detail_catatan = d.AturanDetailCatatan
            })
        });
    }

    // POST: api/rules - Create aturan with details
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RulesCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.aturan_versi))
            return BadRequest(new { message = "aturan_versi tidak boleh kosong" });

        if (await _db.Aturans.AnyAsync(a => a.AturanVersi == request.aturan_versi))
            return BadRequest(new { message = "aturan_versi sudah ada" });

        var normalizedDetails = new List<(RulesDetailRequest Detail, string CanonicalKey, string CanonicalKategori, string NormalizedJson)>();
        if (request.details != null)
        {
            var requestedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < request.details.Count; index++)
            {
                var detail = request.details[index];
                var detailLabel = !string.IsNullOrWhiteSpace(detail.aturan_detail_key)
                    ? detail.aturan_detail_key
                    : $"index {index}";
                if (!TryNormalizeRequestDetail(
                        detail.aturan_detail_key,
                        detail.aturan_detail_json_value,
                        detailLabel,
                        out var canonicalKey,
                        out var canonicalKategori,
                        out var normalizedJson,
                        out var errorResponse))
                {
                    return errorResponse!;
                }

                if (!requestedKeys.Add(canonicalKey))
                    return BadRequest(new { message = $"key aturan `{canonicalKey}` duplikat dalam request." });

                normalizedDetails.Add((detail, canonicalKey, canonicalKategori, normalizedJson!));
            }
        }

        var now = AppClock.Now;
        var aturan = new Aturan
        {
            AturanVersi = request.aturan_versi,
            AturanStatus = string.IsNullOrWhiteSpace(request.aturan_status)
                ? AturanStatusValues.TidakAktif
                : request.aturan_status.Trim().ToLowerInvariant(),
            AturanSkorMinimum = request.aturan_skor_minimum ?? 80,
            AturanTemplateFilePath = request.aturan_template_file_path,
            AturanCreatedAt = now,
            AturanUpdatedAt = now
        };

        _db.Aturans.Add(aturan);
        await _db.SaveChangesAsync();

        // Create details if provided
        if (normalizedDetails.Count > 0)
        {
            foreach (var (detailRequest, canonicalKey, canonicalKategori, normalizedJson) in normalizedDetails)
            {
                var detail = new AturanDetail
                {
                    AturanId = aturan.AturanId,
                    AturanDetailKategori = canonicalKategori,
                    AturanDetailKey = canonicalKey,
                    AturanDetailJsonValue = normalizedJson,
                    AturanDetailCatatan = detailRequest.aturan_detail_catatan
                };
                _db.AturanDetails.Add(detail);
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "Aturan berhasil dibuat", aturan_id = aturan.AturanId });
    }

    // PATCH: api/rules/{id} - Update aturan with details
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(uint id, [FromBody] RulesUpdateRequest request)
    {
        var aturan = await _db.Aturans.FindAsync(id);
        if (aturan == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        var normalizedDetails = new List<(RulesDetailUpdateRequest Detail, string CanonicalKey, string CanonicalKategori, string NormalizedJson)>();
        var existingDetailIds = request.details?
            .Where(detail => detail.aturan_detail_id.HasValue && detail.aturan_detail_id.Value > 0)
            .Select(detail => detail.aturan_detail_id!.Value)
            .Distinct()
            .ToList()
            ?? new List<uint>();
        var existingDetailsById = existingDetailIds.Count > 0
            ? await _db.AturanDetails
                .Where(detail => detail.AturanId == id && existingDetailIds.Contains(detail.AturanDetailId))
                .ToDictionaryAsync(detail => detail.AturanDetailId)
            : new Dictionary<uint, AturanDetail>();
        if (request.details != null)
        {
            var requestedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < request.details.Count; index++)
            {
                var detail = request.details[index];
                existingDetailsById.TryGetValue(detail.aturan_detail_id ?? 0, out var existingDetail);
                var detailLabel = detail.aturan_detail_id.HasValue
                    ? $"aturan_detail_id {detail.aturan_detail_id.Value}"
                    : !string.IsNullOrWhiteSpace(detail.aturan_detail_key)
                        ? detail.aturan_detail_key
                        : $"index {index}";
                var requestedKey = detail.aturan_detail_key ?? existingDetail?.AturanDetailKey;
                var requestedJson = detail.aturan_detail_json_value ?? existingDetail?.AturanDetailJsonValue;

                if (!TryNormalizeRequestDetail(
                        requestedKey,
                        requestedJson,
                        detailLabel,
                        out var canonicalKey,
                        out var canonicalKategori,
                        out var normalizedJson,
                        out var errorResponse))
                {
                    return errorResponse!;
                }

                if (!requestedKeys.Add(canonicalKey))
                    return BadRequest(new { message = $"key aturan `{canonicalKey}` duplikat dalam request." });

                normalizedDetails.Add((detail, canonicalKey, canonicalKategori, normalizedJson!));
            }
        }

        // Update aturan fields
        if (request.aturan_versi != null)
        {
            if (request.aturan_versi != aturan.AturanVersi && await _db.Aturans.AnyAsync(a => a.AturanVersi == request.aturan_versi))
                return BadRequest(new { message = "aturan_versi sudah ada" });
            aturan.AturanVersi = request.aturan_versi;
        }
        if (!string.IsNullOrWhiteSpace(request.aturan_status))
            aturan.AturanStatus = request.aturan_status.Trim().ToLowerInvariant();
        if (request.aturan_skor_minimum.HasValue)
            aturan.AturanSkorMinimum = request.aturan_skor_minimum.Value;
        if (request.aturan_template_file_path != null)
            aturan.AturanTemplateFilePath = request.aturan_template_file_path;
        aturan.AturanUpdatedAt = AppClock.Now;

        // Update details if provided
        if (normalizedDetails.Count > 0)
        {
            foreach (var (detailRequest, canonicalKey, canonicalKategori, normalizedJson) in normalizedDetails)
            {
                if (detailRequest.aturan_detail_id.HasValue && detailRequest.aturan_detail_id.Value > 0)
                {
                    // Update existing detail
                    var existing = await _db.AturanDetails.FindAsync(detailRequest.aturan_detail_id.Value);
                    if (existing != null && existing.AturanId == id)
                    {
                        existing.AturanDetailKategori = canonicalKategori;
                        existing.AturanDetailKey = canonicalKey;
                        existing.AturanDetailJsonValue = normalizedJson;
                        if (detailRequest.aturan_detail_catatan != null)
                            existing.AturanDetailCatatan = detailRequest.aturan_detail_catatan;
                    }
                }
                else
                {
                    // Create new detail
                    var newDetail = new AturanDetail
                    {
                        AturanId = id,
                        AturanDetailKategori = canonicalKategori,
                        AturanDetailKey = canonicalKey,
                        AturanDetailJsonValue = normalizedJson,
                        AturanDetailCatatan = detailRequest.aturan_detail_catatan
                    };
                    _db.AturanDetails.Add(newDetail);
                }
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Aturan berhasil diupdate", aturan_id = aturan.AturanId });
    }

    // DELETE: api/rules/{id} - Delete aturan and its details
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(uint id)
    {
        var aturan = await _db.Aturans.FindAsync(id);
        if (aturan == null)
            return NotFound(new { message = "Aturan tidak ditemukan" });

        // Delete details first
        var details = await _db.AturanDetails.Where(d => d.AturanId == id).ToListAsync();
        _db.AturanDetails.RemoveRange(details);

        // Delete aturan
        _db.Aturans.Remove(aturan);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Aturan berhasil dihapus", deleted_details = details.Count });
    }

    // DELETE: api/rules/{id}/detail/{detailId} - Delete single detail
    [HttpDelete("{id}/detail/{detailId}")]
    public async Task<IActionResult> DeleteDetail(uint id, uint detailId)
    {
        var detail = await _db.AturanDetails.FindAsync(detailId);
        if (detail == null || detail.AturanId != id)
            return NotFound(new { message = "Detail tidak ditemukan" });

        _db.AturanDetails.Remove(detail);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Detail berhasil dihapus" });
    }

    private static List<AturanDetail> CanonicalizeDetailsForResponse(IReadOnlyList<AturanDetail> details)
        => AturanDetailContract.NormalizeDetailsForContract(details);

    private static bool TryNormalizeRequestDetail(
        string? requestedKey,
        string? requestedJson,
        string detailLabel,
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
                message = $"key aturan canonical wajib diisi untuk detail {detailLabel}."
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
                message = $"json_value tidak valid untuk detail {detailLabel}: {errorMessage}"
            });
            return false;
        }

        if (!AturanDetailShapeValidator.TryValidate(canonicalKey, normalizedJson, out var shapeErrorMessage))
        {
            errorResponse = new BadRequestObjectResult(new
            {
                message = $"json_value tidak sesuai schema untuk detail {detailLabel}: {shapeErrorMessage}"
            });
            return false;
        }

        return true;
    }
}

// Request DTOs
public class RulesCreateRequest
{
    public string aturan_versi { get; set; } = string.Empty;
    public string? aturan_status { get; set; }
    public uint? aturan_skor_minimum { get; set; }
    public string? aturan_template_file_path { get; set; }
    public List<RulesDetailRequest>? details { get; set; }
}

public class RulesUpdateRequest
{
    public string? aturan_versi { get; set; }
    public string? aturan_status { get; set; }
    public uint? aturan_skor_minimum { get; set; }
    public string? aturan_template_file_path { get; set; }
    public List<RulesDetailUpdateRequest>? details { get; set; }
}

public class RulesDetailRequest
{
    public string? aturan_detail_kategori { get; set; }
    public string? aturan_detail_key { get; set; }
    public string? aturan_detail_json_value { get; set; }
    public string? aturan_detail_catatan { get; set; }
}

public class RulesDetailUpdateRequest
{
    public uint? aturan_detail_id { get; set; }
    public string? aturan_detail_kategori { get; set; }
    public string? aturan_detail_key { get; set; }
    public string? aturan_detail_json_value { get; set; }
    public string? aturan_detail_catatan { get; set; }
}

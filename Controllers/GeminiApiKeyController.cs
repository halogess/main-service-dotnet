using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/gemini")]
public class GeminiApiKeyController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;

    public GeminiApiKeyController(KorektorBukuDbContext db)
    {
        _db = db;
    }

    private bool IsAdmin() => HttpContext.Items["Role"]?.ToString() == "admin";

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] sbyte? status = null, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        if (!IsAdmin())
            return Forbid();

        var query = _db.GeminiApiKeys.AsQueryable();
        if (status.HasValue)
            query = query.Where(k => k.GeminiApiKeyStatus == status.Value);

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(k => k.GeminiApiKeyCreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var result = data.Select(k => new
        {
            id = k.GeminiApiKeyId,
            gemini_api_key_value = k.GeminiApiKeyValue,
            gemini_api_key_status = k.GeminiApiKeyStatus,
            gemini_api_key_usage = k.GeminiApiKeyUsage,
            created_at = k.GeminiApiKeyCreatedAt,
            updated_at = k.GeminiApiKeyUpdatedAt
        }).ToList();

        return Ok(new { data = result, total, limit, offset });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(uint id)
    {
        if (!IsAdmin())
            return Forbid();

        var apiKey = await _db.GeminiApiKeys.FindAsync(id);
        if (apiKey == null)
            return NotFound(new { message = "API key tidak ditemukan" });

        return Ok(new
        {
            id = apiKey.GeminiApiKeyId,
            gemini_api_key_value = apiKey.GeminiApiKeyValue,
            gemini_api_key_status = apiKey.GeminiApiKeyStatus,
            gemini_api_key_usage = apiKey.GeminiApiKeyUsage,
            created_at = apiKey.GeminiApiKeyCreatedAt,
            updated_at = apiKey.GeminiApiKeyUpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GeminiApiKeyCreateRequest request)
    {
        if (!IsAdmin())
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.gemini_api_key_value))
            return BadRequest(new { message = "gemini_api_key_value tidak boleh kosong" });

        var apiKey = new GeminiApiKey
        {
            GeminiApiKeyValue = request.gemini_api_key_value.Trim(),
            GeminiApiKeyStatus = request.gemini_api_key_status ?? 1,
            GeminiApiKeyUsage = request.gemini_api_key_usage
        };

        _db.GeminiApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        return Ok(new { message = "API key berhasil dibuat", id = apiKey.GeminiApiKeyId });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(uint id, [FromBody] GeminiApiKeyUpdateRequest request)
    {
        if (!IsAdmin())
            return Forbid();

        var apiKey = await _db.GeminiApiKeys.FindAsync(id);
        if (apiKey == null)
            return NotFound(new { message = "API key tidak ditemukan" });

        if (request.gemini_api_key_value != null)
        {
            if (string.IsNullOrWhiteSpace(request.gemini_api_key_value))
                return BadRequest(new { message = "gemini_api_key_value tidak boleh kosong" });
            apiKey.GeminiApiKeyValue = request.gemini_api_key_value.Trim();
        }

        if (request.gemini_api_key_status.HasValue)
            apiKey.GeminiApiKeyStatus = request.gemini_api_key_status.Value;

        if (request.clear_usage == true)
        {
            apiKey.GeminiApiKeyUsage = null;
        }
        else if (request.gemini_api_key_usage.HasValue)
        {
            apiKey.GeminiApiKeyUsage = request.gemini_api_key_usage.Value;
        }

        apiKey.GeminiApiKeyUpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();

        return Ok(new { message = "API key berhasil diupdate", id = apiKey.GeminiApiKeyId });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(uint id)
    {
        if (!IsAdmin())
            return Forbid();

        var apiKey = await _db.GeminiApiKeys.FindAsync(id);
        if (apiKey == null)
            return NotFound(new { message = "API key tidak ditemukan" });

        _db.GeminiApiKeys.Remove(apiKey);
        await _db.SaveChangesAsync();

        return Ok(new { message = "API key berhasil dihapus" });
    }
}

public class GeminiApiKeyCreateRequest
{
    public string gemini_api_key_value { get; set; } = string.Empty;
    public sbyte? gemini_api_key_status { get; set; }
    public uint? gemini_api_key_usage { get; set; }
}

public class GeminiApiKeyUpdateRequest
{
    public string? gemini_api_key_value { get; set; }
    public sbyte? gemini_api_key_status { get; set; }
    public uint? gemini_api_key_usage { get; set; }
    public bool? clear_usage { get; set; }
}

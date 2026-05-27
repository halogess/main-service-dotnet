using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/adobe")]
public class AdobeCredentialController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;

    public AdobeCredentialController(KorektorBukuDbContext db)
    {
        _db = db;
    }

    private bool IsAdmin() => HttpContext.Items["Role"]?.ToString() == "admin";

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status = null, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        if (!IsAdmin())
            return Forbid();

        var query = _db.AdobeCredentials.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.AdobeCredentialsStatus == status);

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(c => c.AdobeCredentialsCreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var result = data.Select(c => new
        {
            id = c.AdobeCredentialsId,
            adobe_client_id = c.AdobeClientId,
            adobe_client_secret = c.AdobeClientSecret,
            adobe_credentials_status = c.AdobeCredentialsStatus,
            adobe_credentials_quota_used = c.AdobeCredentialsQuotaUsed,
            adobe_credentials_quota_limit = c.AdobeCredentialsQuotaLimit,
            adobe_credentials_reset_date = c.AdobeCredentialsResetDate,
            created_at = c.AdobeCredentialsCreatedAt,
            updated_at = c.AdobeCredentialsUpdatedAt
        }).ToList();

        return Ok(new { data = result, total, limit, offset });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!IsAdmin())
            return Forbid();

        var credential = await _db.AdobeCredentials.FindAsync(id);
        if (credential == null)
            return NotFound(new { message = "Credential tidak ditemukan" });

        return Ok(new
        {
            id = credential.AdobeCredentialsId,
            adobe_client_id = credential.AdobeClientId,
            adobe_client_secret = credential.AdobeClientSecret,
            adobe_credentials_status = credential.AdobeCredentialsStatus,
            adobe_credentials_quota_used = credential.AdobeCredentialsQuotaUsed,
            adobe_credentials_quota_limit = credential.AdobeCredentialsQuotaLimit,
            adobe_credentials_reset_date = credential.AdobeCredentialsResetDate,
            created_at = credential.AdobeCredentialsCreatedAt,
            updated_at = credential.AdobeCredentialsUpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdobeCredentialCreateRequest request)
    {
        if (!IsAdmin())
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.adobe_client_id))
            return BadRequest(new { message = "adobe_client_id tidak boleh kosong" });

        if (string.IsNullOrWhiteSpace(request.adobe_client_secret))
            return BadRequest(new { message = "adobe_client_secret tidak boleh kosong" });

        if (request.adobe_credentials_quota_limit.HasValue && request.adobe_credentials_quota_limit.Value < 0)
            return BadRequest(new { message = "adobe_credentials_quota_limit tidak valid" });

        if (request.adobe_credentials_quota_used.HasValue && request.adobe_credentials_quota_used.Value < 0)
            return BadRequest(new { message = "adobe_credentials_quota_used tidak valid" });

        var now = AppClock.Now;
        var credential = new AdobeCredential
        {
            AdobeClientId = request.adobe_client_id.Trim(),
            AdobeClientSecret = request.adobe_client_secret.Trim(),
            AdobeCredentialsStatus = string.IsNullOrWhiteSpace(request.adobe_credentials_status)
                ? "active"
                : request.adobe_credentials_status,
            AdobeCredentialsQuotaLimit = request.adobe_credentials_quota_limit ?? 500,
            AdobeCredentialsQuotaUsed = request.adobe_credentials_quota_used ?? 0,
            AdobeCredentialsResetDate = request.adobe_credentials_reset_date,
            AdobeCredentialsCreatedAt = now,
            AdobeCredentialsUpdatedAt = now
        };

        _db.AdobeCredentials.Add(credential);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Credential berhasil dibuat", id = credential.AdobeCredentialsId });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] AdobeCredentialUpdateRequest request)
    {
        if (!IsAdmin())
            return Forbid();

        var credential = await _db.AdobeCredentials.FindAsync(id);
        if (credential == null)
            return NotFound(new { message = "Credential tidak ditemukan" });

        if (request.adobe_client_id != null)
        {
            if (string.IsNullOrWhiteSpace(request.adobe_client_id))
                return BadRequest(new { message = "adobe_client_id tidak boleh kosong" });
            credential.AdobeClientId = request.adobe_client_id.Trim();
        }

        if (request.adobe_client_secret != null)
        {
            if (string.IsNullOrWhiteSpace(request.adobe_client_secret))
                return BadRequest(new { message = "adobe_client_secret tidak boleh kosong" });
            credential.AdobeClientSecret = request.adobe_client_secret.Trim();
        }

        if (request.adobe_credentials_status != null)
        {
            if (string.IsNullOrWhiteSpace(request.adobe_credentials_status))
                return BadRequest(new { message = "adobe_credentials_status tidak boleh kosong" });
            credential.AdobeCredentialsStatus = request.adobe_credentials_status;
        }

        if (request.adobe_credentials_quota_limit.HasValue)
        {
            if (request.adobe_credentials_quota_limit.Value < 0)
                return BadRequest(new { message = "adobe_credentials_quota_limit tidak valid" });
            credential.AdobeCredentialsQuotaLimit = request.adobe_credentials_quota_limit.Value;
        }

        if (request.adobe_credentials_quota_used.HasValue)
        {
            if (request.adobe_credentials_quota_used.Value < 0)
                return BadRequest(new { message = "adobe_credentials_quota_used tidak valid" });
            credential.AdobeCredentialsQuotaUsed = request.adobe_credentials_quota_used.Value;
        }

        if (request.clear_reset_date == true)
        {
            credential.AdobeCredentialsResetDate = null;
        }
        else if (request.adobe_credentials_reset_date.HasValue)
        {
            credential.AdobeCredentialsResetDate = request.adobe_credentials_reset_date;
        }

        credential.AdobeCredentialsUpdatedAt = AppClock.Now;

        await _db.SaveChangesAsync();

        return Ok(new { message = "Credential berhasil diupdate", id = credential.AdobeCredentialsId });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!IsAdmin())
            return Forbid();

        var credential = await _db.AdobeCredentials.FindAsync(id);
        if (credential == null)
            return NotFound(new { message = "Credential tidak ditemukan" });

        _db.AdobeCredentials.Remove(credential);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Credential berhasil dihapus" });
    }
}

public class AdobeCredentialCreateRequest
{
    public string adobe_client_id { get; set; } = string.Empty;
    public string adobe_client_secret { get; set; } = string.Empty;
    public string? adobe_credentials_status { get; set; }
    public int? adobe_credentials_quota_used { get; set; }
    public int? adobe_credentials_quota_limit { get; set; }
    public DateTime? adobe_credentials_reset_date { get; set; }
}

public class AdobeCredentialUpdateRequest
{
    public string? adobe_client_id { get; set; }
    public string? adobe_client_secret { get; set; }
    public string? adobe_credentials_status { get; set; }
    public int? adobe_credentials_quota_used { get; set; }
    public int? adobe_credentials_quota_limit { get; set; }
    public DateTime? adobe_credentials_reset_date { get; set; }
    public bool? clear_reset_date { get; set; }
}

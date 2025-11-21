using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/internal")]
public class InternalController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly IConfiguration _configuration;

    public InternalController(KorektorBukuDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    private bool ValidateApiKey()
    {
        var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
        var validApiKey = _configuration["InternalApiKey"];
        return apiKey == validApiKey && !string.IsNullOrEmpty(validApiKey);
    }

    [HttpGet("antrian")]
    public async Task<IActionResult> GetAntrian([FromQuery] string worker, [FromQuery] string status = "in_queue")
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { message = "Invalid API Key" });
        }

        var query = _db.Antrians.Where(a => a.AntrianWorker == worker);
        
        query = worker switch
        {
            "convert_pdf" => query.Where(a => a.AntrianConvertStatus == status),
            "visual" => query.Where(a => a.AntrianVisualStatus == status),
            "struktur" => query.Where(a => a.AntrianStrukturStatus == status),
            _ => query.Where(a => false) // Invalid worker
        };

        var antrian = await query.OrderBy(a => a.AntrianId).FirstOrDefaultAsync();

        if (antrian == null)
        {
            return NotFound(new { message = "Tidak ada antrian" });
        }

        return Ok(antrian);
    }

    [HttpPatch("antrian/{id}/status")]
    public async Task<IActionResult> UpdateAntrianStatus(int id, [FromBody] UpdateAntrianStatusRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { message = "Invalid API Key" });
        }

        var antrian = await _db.Antrians.FindAsync(id);
        if (antrian == null)
        {
            return NotFound(new { message = "Antrian tidak ditemukan" });
        }

        // Update status berdasarkan worker
        switch (antrian.AntrianWorker)
        {
            case "convert_pdf":
                antrian.AntrianConvertStatus = request.status;
                break;
            case "visual":
                antrian.AntrianVisualStatus = request.status;
                break;
            case "struktur":
                antrian.AntrianStrukturStatus = request.status;
                break;
        }
        
        antrian.AntrianErrorMessage = request.error_message;
        antrian.AntrianUpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();

        return Ok(new { message = "Status updated", antrian });
    }
}

public class UpdateAntrianStatusRequest
{
    public string status { get; set; } = string.Empty;
    public string? error_message { get; set; }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TemplateController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly IHighlightedTextExtractor _highlightExtractor;

    public TemplateController(KorektorBukuDbContext db, IHighlightedTextExtractor highlightExtractor)
    {
        _db = db;
        _highlightExtractor = highlightExtractor;
    }

    // GET: api/template (Admin only)
    [HttpGet]
    public async Task<IActionResult> GetAllTemplates()
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var templates = await _db.Templates.ToListAsync();

        var result = templates.Select(t => new
        {
            id = t.TemplateId,
            name = t.TemplateName,
            status = t.TemplateStatus,
            filepath = t.TemplateFilepath
        }).ToList();

        return Ok(new { data = result, total = result.Count });
    }

    // GET: api/template/{id} (Admin only)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTemplateById(uint id)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var template = await _db.Templates.FindAsync(id);

        if (template == null)
            return NotFound(new { message = "Template tidak ditemukan" });

        return Ok(new
        {
            id = template.TemplateId,
            name = template.TemplateName,
            status = template.TemplateStatus,
            filepath = template.TemplateFilepath
        });
    }

    // POST: api/template (Admin only) - Upload file docx, extract highlighted text
    [HttpPost]
    public async Task<IActionResult> CreateTemplate([FromForm] IFormFile file, [FromForm] string name, [FromForm] string? status = null)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });

        // Validate file extension
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".docx")
            return BadRequest(new { message = "File harus berformat .docx" });

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Nama template wajib diisi" });

        try
        {
            // Create storage path for templates
            var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
            var templatesDir = Path.Combine(storagePath, "templates");
            
            if (!Directory.Exists(templatesDir))
                Directory.CreateDirectory(templatesDir);

            // Generate unique filename
            var uniqueFilename = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(templatesDir, uniqueFilename);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Extract highlighted texts from DOCX
            var highlightedTexts = new List<string>();
            try
            {
                using var docStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                highlightedTexts = _highlightExtractor.ExtractHighlightedTexts(docStream);
            }
            catch (Exception ex)
            {
                // Log but don't fail - extraction is optional
                Console.WriteLine($"Warning: Failed to extract highlights: {ex.Message}");
            }

            // Save template to database
            var template = new Template
            {
                TemplateName = name,
                TemplateStatus = status ?? "draft",
                TemplateFilepath = filePath
            };

            _db.Templates.Add(template);
            await _db.SaveChangesAsync();

            // Save highlighted texts as template fields (with key = null)
            var savedFields = new List<object>();
            foreach (var text in highlightedTexts.Distinct())
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var field = new TemplateField
                {
                    TemplateFieldText = text.Length > 100 ? text.Substring(0, 100) : text,
                    TemplateFieldKey = null, // Key is null for now
                    TemplateId = template.TemplateId
                };
                _db.TemplateFields.Add(field);
                await _db.SaveChangesAsync();

                savedFields.Add(new
                {
                    id = field.TemplateFieldId,
                    text = field.TemplateFieldText
                });
            }

            return Ok(new
            {
                message = "Template berhasil dibuat",
                id = template.TemplateId,
                name = template.TemplateName,
                status = template.TemplateStatus,
                filepath = template.TemplateFilepath,
                fields = savedFields,
                total_fields = savedFields.Count
            });
        }
        catch (Exception ex)
        {
            var innerMessage = ex.InnerException?.Message ?? ex.Message;
            return BadRequest(new { message = $"Gagal menyimpan template: {innerMessage}" });
        }
    }

    // PATCH: api/template/{id} (Admin only)
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateTemplate(uint id, [FromBody] UpdateTemplateRequest request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var template = await _db.Templates.FindAsync(id);

        if (template == null)
            return NotFound(new { message = "Template tidak ditemukan" });

        if (request.name != null)
            template.TemplateName = request.name;

        if (request.status != null)
            template.TemplateStatus = request.status;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Template berhasil diupdate",
            id = template.TemplateId,
            name = template.TemplateName,
            status = template.TemplateStatus,
            filepath = template.TemplateFilepath
        });
    }

    // DELETE: api/template/{id} (Admin only)
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTemplate(uint id)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var template = await _db.Templates.FindAsync(id);

        if (template == null)
            return NotFound(new { message = "Template tidak ditemukan" });

        _db.Templates.Remove(template);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Template berhasil dihapus" });
    }

    // GET: api/template/fields (Admin only) - Get all template fields
    [HttpGet("fields")]
    public async Task<IActionResult> GetTemplateFields([FromQuery] uint? templateId = null, [FromQuery] string? key = null)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var query = _db.TemplateFields.AsQueryable();

        if (templateId.HasValue)
            query = query.Where(f => f.TemplateId == templateId.Value);

        if (!string.IsNullOrWhiteSpace(key))
            query = query.Where(f => f.TemplateFieldKey == key);

        var fields = await query.Select(f => new
        {
            id = f.TemplateFieldId,
            template_id = f.TemplateId,
            text = f.TemplateFieldText,
            key = f.TemplateFieldKey
        }).ToListAsync();

        return Ok(new { data = fields, total = fields.Count });
    }

    // DELETE: api/template/fields/{id} (Admin only)
    [HttpDelete("fields/{id}")]
    public async Task<IActionResult> DeleteTemplateField(uint id)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var field = await _db.TemplateFields.FindAsync(id);
        if (field == null)
            return NotFound(new { message = "Template field tidak ditemukan" });

        _db.TemplateFields.Remove(field);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Template field berhasil dihapus" });
    }
}

// Request DTOs
public class UpdateTemplateRequest
{
    public string? name { get; set; }
    public string? status { get; set; }
}

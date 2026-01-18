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
    private readonly IPdfConversionService _pdfConversionService;
    private readonly ILogger<TemplateController> _logger;

    public TemplateController(
        KorektorBukuDbContext db, 
        IHighlightedTextExtractor highlightExtractor,
        IPdfConversionService pdfConversionService,
        ILogger<TemplateController> logger)
    {
        _db = db;
        _highlightExtractor = highlightExtractor;
        _pdfConversionService = pdfConversionService;
        _logger = logger;
    }

    // GET: api/template (Admin only)
    [HttpGet]
    public async Task<IActionResult> GetAllTemplates([FromQuery] int limit = 10, [FromQuery] int offset = 0)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        // Get total count for pagination
        var totalCount = await _db.Templates.CountAsync();

        var templates = await _db.Templates
            .OrderByDescending(t => t.TemplateCreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(t => new
            {
                id = t.TemplateId,
                name = t.TemplateName,
                status = t.TemplateStatus,
                created_at = t.TemplateCreatedAt
            })
            .ToListAsync();

        return Ok(new { data = templates, total = totalCount, limit = limit, offset = offset });
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

        // Read PDF file and convert to base64 if exists
        string? pdfBase64 = null;
        if (!string.IsNullOrEmpty(template.TemplatePdfPath) && System.IO.File.Exists(template.TemplatePdfPath))
        {
            try
            {
                var pdfBytes = await System.IO.File.ReadAllBytesAsync(template.TemplatePdfPath);
                pdfBase64 = Convert.ToBase64String(pdfBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read PDF file for template {Id}", id);
            }
        }

        // Get template fields
        var fields = await _db.TemplateFields
            .Where(f => f.TemplateId == id)
            .Select(f => new
            {
                id = f.TemplateFieldId,
                text = f.TemplateFieldText,
                key = f.TemplateFieldKey
            })
            .ToListAsync();

        return Ok(new
        {
            id = template.TemplateId,
            name = template.TemplateName,
            status = template.TemplateStatus,
            created_at = template.TemplateCreatedAt,
            pdf_base64 = pdfBase64,
            fields = fields,
            total_fields = fields.Count
        });
    }

    // POST: api/template (Admin only) - Upload file docx, convert to PDF, extract highlighted text
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

            // Generate unique filename (without extension)
            var uniqueId = Guid.NewGuid().ToString();
            var docxFilename = $"{uniqueId}.docx";
            var pdfFilename = $"{uniqueId}.pdf";
            var docxFilePath = Path.Combine(templatesDir, docxFilename);
            var pdfFilePath = Path.Combine(templatesDir, pdfFilename);

            // Save DOCX file
            _logger.LogInformation("Saving template DOCX file: {Path}", docxFilePath);
            using (var stream = new FileStream(docxFilePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Convert DOCX to PDF using Adobe API
            string? savedPdfPath = null;
            byte[]? pdfBytes = null;
            try
            {
                _logger.LogInformation("Converting template DOCX to PDF...");
                pdfBytes = await _pdfConversionService.ConvertDocxToPdf(docxFilePath);
                
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    await System.IO.File.WriteAllBytesAsync(pdfFilePath, pdfBytes);
                    savedPdfPath = pdfFilePath;
                    _logger.LogInformation("Template PDF saved: {Path}, Size: {Size} bytes", pdfFilePath, pdfBytes.Length);
                }
                else
                {
                    _logger.LogWarning("PDF conversion returned empty result");
                    pdfBytes = null;
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - PDF conversion is optional, DOCX is still saved
                _logger.LogError(ex, "Failed to convert template to PDF. DOCX saved, PDF skipped.");
                pdfBytes = null;
            }

            // Extract highlighted texts from DOCX
            var highlightedTexts = new List<string>();
            try
            {
                using var docStream = new FileStream(docxFilePath, FileMode.Open, FileAccess.Read);
                highlightedTexts = _highlightExtractor.ExtractHighlightedTexts(docStream);
            }
            catch (Exception ex)
            {
                // Log but don't fail - extraction is optional
                _logger.LogWarning(ex, "Failed to extract highlights from template");
            }

            // Save template to database
            var template = new Template
            {
                TemplateName = name,
                TemplateStatus = status ?? "draft",
                TemplateDocxPath = docxFilePath,
                TemplatePdfPath = savedPdfPath
            };

            _db.Templates.Add(template);
            await _db.SaveChangesAsync();

            // Save highlighted texts as template fields (with key = null)
            var savedFields = new List<object>();
            foreach (var text in highlightedTexts)
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
                created_at = template.TemplateCreatedAt,
                pdf_converted = savedPdfPath != null,
                pdf_base64 = pdfBytes != null ? Convert.ToBase64String(pdfBytes) : null,
                fields = savedFields,
                total_fields = savedFields.Count
            });
        }
        catch (Exception ex)
        {
            var innerMessage = ex.InnerException?.Message ?? ex.Message;
            _logger.LogError(ex, "Failed to create template");
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
            docx_path = template.TemplateDocxPath,
            pdf_path = template.TemplatePdfPath
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

        // Delete files if they exist
        try
        {
            if (!string.IsNullOrEmpty(template.TemplateDocxPath) && System.IO.File.Exists(template.TemplateDocxPath))
            {
                System.IO.File.Delete(template.TemplateDocxPath);
                _logger.LogInformation("Deleted template DOCX: {Path}", template.TemplateDocxPath);
            }
            if (!string.IsNullOrEmpty(template.TemplatePdfPath) && System.IO.File.Exists(template.TemplatePdfPath))
            {
                System.IO.File.Delete(template.TemplatePdfPath);
                _logger.LogInformation("Deleted template PDF: {Path}", template.TemplatePdfPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete template files");
        }

        // Delete template fields first
        var fields = await _db.TemplateFields.Where(f => f.TemplateId == id).ToListAsync();
        _db.TemplateFields.RemoveRange(fields);

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

    // GET: api/template/{id}/pdf - Download/view PDF file
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetTemplatePdf(uint id)
    {
        var template = await _db.Templates.FindAsync(id);

        if (template == null)
            return NotFound(new { message = "Template tidak ditemukan" });

        if (string.IsNullOrEmpty(template.TemplatePdfPath) || !System.IO.File.Exists(template.TemplatePdfPath))
            return NotFound(new { message = "PDF template belum tersedia" });

        var pdfBytes = await System.IO.File.ReadAllBytesAsync(template.TemplatePdfPath);
        return File(pdfBytes, "application/pdf", $"{template.TemplateName}.pdf");
    }

    // GET: api/template/{id}/docx - Download DOCX file
    [HttpGet("{id}/docx")]
    public async Task<IActionResult> GetTemplateDocx(uint id)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var template = await _db.Templates.FindAsync(id);

        if (template == null)
            return NotFound(new { message = "Template tidak ditemukan" });

        if (string.IsNullOrEmpty(template.TemplateDocxPath) || !System.IO.File.Exists(template.TemplateDocxPath))
            return NotFound(new { message = "File DOCX template tidak ditemukan" });

        var docxBytes = await System.IO.File.ReadAllBytesAsync(template.TemplateDocxPath);
        return File(docxBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{template.TemplateName}.docx");
    }
}

// Request DTOs
public class UpdateTemplateRequest
{
    public string? name { get; set; }
    public string? status { get; set; }
}

using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;
using System.Text.RegularExpressions;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TemplateController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;
    private readonly IHighlightedTextExtractor _highlightExtractor;
    private readonly IPdfConversionService _pdfConversionService;
    private readonly ILogger<TemplateController> _logger;

    public TemplateController(
        KorektorBukuDbContext db, 
        SttsDbContext sttsDb,
        IHighlightedTextExtractor highlightExtractor,
        IPdfConversionService pdfConversionService,
        ILogger<TemplateController> logger)
    {
        _db = db;
        _sttsDb = sttsDb;
        _highlightExtractor = highlightExtractor;
        _pdfConversionService = pdfConversionService;
        _logger = logger;
    }

    // GET: api/template (Admin: all templates, Mahasiswa: active templates only with name)
    [HttpGet]
    public async Task<IActionResult> GetAllTemplates([FromQuery] int limit = 10, [FromQuery] int offset = 0)
    {
        var role = HttpContext.Items["Role"]?.ToString();

        // Mahasiswa: return only active templates with limited fields
        if (role == "mahasiswa")
        {
            var nrpStr = HttpContext.Items["Nrp"]?.ToString();
            var nrpUint = uint.TryParse(nrpStr, out var n) ? n : 0;
            
            var activeTemplates = await _db.Templates
                .Where(t => t.TemplateStatus == "active")
                .OrderByDescending(t => t.TemplateCreatedAt)
                .Select(t => new
                {
                    id = t.TemplateId,
                    name = t.TemplateName,
                    has_generated = _db.TemplateGenerations.Any(g => g.TemplateId == t.TemplateId && g.MhsNrp == nrpUint),
                    penguji = _db.TemplateDetails.Count(d => d.TemplateId == t.TemplateId && d.TemplateDetailField == "[penguji]")
                })
                .ToListAsync();

            return Ok(new { data = activeTemplates });
        }

        // Admin: return all templates with full details
        if (role != "admin")
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

        // Get template details
        var details = await _db.TemplateDetails
            .Where(d => d.TemplateId == id)
            .Select(d => new
            {
                id = d.TemplateDetailId,
                text = d.TemplateDetailText,
                field = d.TemplateDetailField,
                catatan = d.TemplateDetailCatatan,
                optional = d.TemplateDetailOptional
            })
            .ToListAsync();

        return Ok(new
        {
            id = template.TemplateId,
            name = template.TemplateName,
            status = template.TemplateStatus,
            created_at = template.TemplateCreatedAt,
            pdf_base64 = pdfBase64,
            details = details,
            total_details = details.Count
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

        // Check if name is already taken
        if (await _db.Templates.AnyAsync(t => t.TemplateName == name))
            return BadRequest(new { message = "Nama template sudah digunakan" });

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

            // Save highlighted texts as template details (with field = null)
            var savedDetails = new List<object>();
            foreach (var text in highlightedTexts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var detail = new TemplateDetail
                {
                    TemplateDetailText = text.Length > 100 ? text.Substring(0, 100) : text,
                    TemplateDetailField = null, // Field is null for now
                    TemplateDetailCatatan = null, // Catatan is null for now
                    TemplateId = template.TemplateId
                };
                _db.TemplateDetails.Add(detail);
                await _db.SaveChangesAsync();

                savedDetails.Add(new
                {
                    id = detail.TemplateDetailId,
                    text = detail.TemplateDetailText
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
                details = savedDetails,
                total_details = savedDetails.Count
            });
        }
        catch (Exception ex)
        {
            var innerMessage = ex.InnerException?.Message ?? ex.Message;
            _logger.LogError(ex, "Failed to create template");
            return BadRequest(new { message = $"Gagal menyimpan template: {innerMessage}" });
        }
    }

    // PATCH: api/template/{id} (Admin only) - Update template with name, status, and details
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateTemplate(uint id, [FromBody] UpdateTemplateRequest request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var template = await _db.Templates.FindAsync(id);

        if (template == null)
            return NotFound(new { message = "Template tidak ditemukan" });

        // Update name if provided
        if (request.name != null)
        {
            if (request.name != template.TemplateName && await _db.Templates.AnyAsync(t => t.TemplateName == request.name))
                return BadRequest(new { message = "Nama template sudah digunakan" });
            template.TemplateName = request.name;
        }

        // Update status if provided
        if (request.status != null)
            template.TemplateStatus = request.status;

        await _db.SaveChangesAsync();

        // Update details if provided
        var updatedDetails = new List<object>();
        if (request.details != null && request.details.Count > 0)
        {
            foreach (var detailUpdate in request.details)
            {
                if (detailUpdate.id == null)
                    continue;

                var detail = await _db.TemplateDetails.FindAsync(detailUpdate.id.Value);
                if (detail == null || detail.TemplateId != id)
                    continue;

                if (detailUpdate.field != null)
                    detail.TemplateDetailField = detailUpdate.field;

                if (detailUpdate.catatan != null)
                    detail.TemplateDetailCatatan = detailUpdate.catatan;

                if (detailUpdate.optional != null)
                    detail.TemplateDetailOptional = detailUpdate.optional.Value;

                await _db.SaveChangesAsync();

                updatedDetails.Add(new
                {
                    id = detail.TemplateDetailId,
                    text = detail.TemplateDetailText,
                    field = detail.TemplateDetailField,
                    catatan = detail.TemplateDetailCatatan,
                    optional = detail.TemplateDetailOptional
                });
            }
        }

        return Ok(new { message = "Template berhasil diupdate" });
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

        // Delete template details first
        var details = await _db.TemplateDetails.Where(d => d.TemplateId == id).ToListAsync();
        _db.TemplateDetails.RemoveRange(details);

        _db.Templates.Remove(template);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Template berhasil dihapus" });
    }

    // GET: api/template/details (Admin only) - Get all template details
    [HttpGet("details")]
    public async Task<IActionResult> GetTemplateDetails([FromQuery] uint? templateId = null, [FromQuery] string? field = null)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var query = _db.TemplateDetails.AsQueryable();

        if (templateId.HasValue)
            query = query.Where(d => d.TemplateId == templateId.Value);

        if (!string.IsNullOrWhiteSpace(field))
            query = query.Where(d => d.TemplateDetailField == field);

        var details = await query.Select(d => new
        {
            id = d.TemplateDetailId,
            template_id = d.TemplateId,
            text = d.TemplateDetailText,
            field = d.TemplateDetailField,
            catatan = d.TemplateDetailCatatan,
            optional = d.TemplateDetailOptional
        }).ToListAsync();

        return Ok(new { data = details, total = details.Count });
    }

    // DELETE: api/template/details/{id} (Admin only)
    [HttpDelete("details/{id}")]
    public async Task<IActionResult> DeleteTemplateDetail(uint id)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var detail = await _db.TemplateDetails.FindAsync(id);
        if (detail == null)
            return NotFound(new { message = "Template detail tidak ditemukan" });

        _db.TemplateDetails.Remove(detail);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Template detail berhasil dihapus" });
    }

    // PATCH: api/template/details/{id} (Admin only)
    [HttpPatch("details/{id}")]
    public async Task<IActionResult> UpdateTemplateDetail(uint id, [FromBody] UpdateTemplateDetailRequest request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var detail = await _db.TemplateDetails.FindAsync(id);
        if (detail == null)
            return NotFound(new { message = "Template detail tidak ditemukan" });

        if (request.field != null)
            detail.TemplateDetailField = request.field;

        if (request.catatan != null)
            detail.TemplateDetailCatatan = request.catatan;

        if (request.optional != null)
            detail.TemplateDetailOptional = request.optional.Value;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Template detail berhasil diupdate",
            id = detail.TemplateDetailId,
            text = detail.TemplateDetailText,
            field = detail.TemplateDetailField,
            catatan = detail.TemplateDetailCatatan,
            optional = detail.TemplateDetailOptional
        });
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

    // POST: api/template/{id}/generate - Generate filled template document
    [HttpPost("{id}/generate")]
    public async Task<IActionResult> GenerateTemplate(uint id, [FromBody] GenerateTemplateRequest? request)
    {
        // Get mahasiswa NRP from authenticated user
        var nrpStr = HttpContext.Items["Nrp"]?.ToString();
        if (string.IsNullOrEmpty(nrpStr))
            return Unauthorized(new { message = "Unauthorized - NRP tidak ditemukan" });

        // Get template
        var template = await _db.Templates.FindAsync(id);
        if (template == null)
            return NotFound(new { message = "Template tidak ditemukan" });

        // Check if template is active
        if (template.TemplateStatus != "active")
            return BadRequest(new { message = "Template belum aktif" });

        // Check if DOCX file exists
        if (string.IsNullOrEmpty(template.TemplateDocxPath) || !System.IO.File.Exists(template.TemplateDocxPath))
            return NotFound(new { message = "File DOCX template tidak ditemukan" });

        try
        {
            // Check if user has already generated this template - if so, delete old files
            var nrpUint = uint.TryParse(nrpStr, out var n) ? n : 0;
            var existingGeneration = await _db.TemplateGenerations
                .FirstOrDefaultAsync(g => g.TemplateId == id && g.MhsNrp == nrpUint);
            
            if (existingGeneration != null)
            {
                // Delete old files
                if (!string.IsNullOrEmpty(existingGeneration.TemplateGenerationDocxFilepath) && 
                    System.IO.File.Exists(existingGeneration.TemplateGenerationDocxFilepath))
                {
                    System.IO.File.Delete(existingGeneration.TemplateGenerationDocxFilepath);
                    _logger.LogInformation("Deleted old DOCX: {Path}", existingGeneration.TemplateGenerationDocxFilepath);
                }
                if (!string.IsNullOrEmpty(existingGeneration.TemplateGenerationPdfFilepath) && 
                    System.IO.File.Exists(existingGeneration.TemplateGenerationPdfFilepath))
                {
                    System.IO.File.Delete(existingGeneration.TemplateGenerationPdfFilepath);
                    _logger.LogInformation("Deleted old PDF: {Path}", existingGeneration.TemplateGenerationPdfFilepath);
                }
            }

            // Get template details to know which fields to replace
            var templateDetails = await _db.TemplateDetails
                .Where(d => d.TemplateId == id && !string.IsNullOrEmpty(d.TemplateDetailField))
                .OrderBy(d => d.TemplateDetailId)
                .ToListAsync();

            // Get mahasiswa data
            var mahasiswa = await _sttsDb.Mahasiswas.FirstOrDefaultAsync(m => m.MhsNrp == nrpStr);
            if (mahasiswa == null)
                return NotFound(new { message = "Data mahasiswa tidak ditemukan" });

            // Get jurusan data
            var jurusan = await _sttsDb.Jurusans.FirstOrDefaultAsync(j => j.JurKode == mahasiswa.JurKode);

            // Get proposal data
            var proposal = await _sttsDb.Proposals.FirstOrDefaultAsync(p => p.MhsNrp == nrpStr);

            // 1. ta_tesis: If jur_kode doesn't start with "2", then "Tugas Akhir", otherwise "Tesis"
            var taTesis = (mahasiswa.JurKode != null && mahasiswa.JurKode.StartsWith("2")) ? "Tesis" : "Tugas Akhir";

            // 3. Jenjang: First word from gelar (e.g., "Sarjana Teknik" -> "Sarjana")
            var gelarFull = jurusan?.JurGelar ?? "";
            var jenjang = gelarFull.Split(' ').FirstOrDefault() ?? "";

            // 4. Gelar from jurusan.JurGelar
            var gelar = gelarFull;

            // 5. Program studi: Split jur_nama by "-" and take the last part
            var jurNama = jurusan?.JurNama ?? "";
            var programStudi = jurNama.Contains("-") 
                ? jurNama.Split('-').Last().Trim() 
                : jurNama;

            // 6. Fakultas: If "TEKNIK", change to "Sains dan Teknologi", otherwise keep as is
            var fakultasRaw = jurusan?.JurFakultas ?? "";
            var fakultas = fakultasRaw.Equals("TEKNIK", StringComparison.OrdinalIgnoreCase) 
                ? "Sains dan Teknologi" 
                : fakultasRaw;

            // 8 & 9. Get pembimbing and co-pembimbing names from dosen table
            var pembimbingNama = "";
            var coPembimbingNama = "";
            
            if (!string.IsNullOrEmpty(proposal?.DosenPembimbing))
            {
                var pembimbing = await _sttsDb.Dosens.FirstOrDefaultAsync(d => d.DosenKode == proposal.DosenPembimbing);
                pembimbingNama = pembimbing?.DosenNamaSk ?? "";
            }
            
            if (!string.IsNullOrEmpty(proposal?.DosenCoPembimbing))
            {
                var coPembimbing = await _sttsDb.Dosens.FirstOrDefaultAsync(d => d.DosenKode == proposal.DosenCoPembimbing);
                coPembimbingNama = coPembimbing?.DosenNamaSk ?? "";
            }

            // 7. Get current date for tanggal/bulan/tahun
            var now = DateTime.Now;
            var bulanNames = new[] { "", "Januari", "Februari", "Maret", "April", "Mei", "Juni", 
                                      "Juli", "Agustus", "September", "Oktober", "November", "Desember" };

            // Build field values dictionary
            var fieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "[ta_tesis]", taTesis },
                { "[judul]", proposal?.ProposalJudulBaru ?? "" },
                { "[nama]", mahasiswa.MhsNama ?? "" },
                { "[nrp]", mahasiswa.MhsNrp ?? "" },
                { "[jenjang]", jenjang },
                { "[gelar]", gelar },
                { "[program_studi]", programStudi },
                { "[fakultas]", fakultas },
                { "[tanggal]", now.Day.ToString() },
                { "[bulan]", bulanNames[now.Month] },
                { "[tahun]", now.Year.ToString() },
                { "[pembimbing]", pembimbingNama },
                { "[co_pembimbing]", coPembimbingNama }
            };

            // Add penguji from request - lookup dosen_nama_sk from dosen_kode array
            // Template details with [penguji] field will be replaced in order
            var pengujiNames = new List<string>();
            if (request?.penguji != null && request.penguji.Count > 0)
            {
                foreach (var dosenKode in request.penguji)
                {
                    if (!string.IsNullOrEmpty(dosenKode))
                    {
                        var dosen = await _sttsDb.Dosens.FirstOrDefaultAsync(d => d.DosenKode == dosenKode);
                        pengujiNames.Add(dosen?.DosenNamaSk ?? "");
                    }
                    else
                    {
                        pengujiNames.Add("");
                    }
                }
            }

            // Create storage path for generated files: storage/template-generation/{nrp}/
            var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
            var generationsDir = Path.Combine(storagePath, "template-generation", nrpStr);
            
            if (!Directory.Exists(generationsDir))
                Directory.CreateDirectory(generationsDir);

            // Generate unique filename
            var uniqueId = Guid.NewGuid().ToString();
            var docxFilename = $"{uniqueId}.docx";
            var pdfFilename = $"{uniqueId}.pdf";
            var docxFilePath = Path.Combine(generationsDir, docxFilename);
            var pdfFilePath = Path.Combine(generationsDir, pdfFilename);

            // Copy template DOCX to new location
            System.IO.File.Copy(template.TemplateDocxPath, docxFilePath, true);

            // Replace highlighted text with field values and remove highlights
            using (var doc = WordprocessingDocument.Open(docxFilePath, true))
            {
                var mainPart = doc.MainDocumentPart;
                var body = mainPart?.Document?.Body;
                if (mainPart != null && body != null)
                {
                    // Build a list of (highlightedText, fieldValue) pairs
                    var replacements = new List<(string highlightedText, string fieldValue, bool preserveDbCaseUnlessUpper)>();
                    var pengujiIndex = 0;
                    
                    foreach (var detail in templateDetails)
                    {
                        var fieldPattern = detail.TemplateDetailField;
                        if (string.IsNullOrEmpty(fieldPattern))
                            continue;

                        var fieldValue = BuildFieldValue(fieldPattern, fieldValues, pengujiNames, ref pengujiIndex);
                        var preserveDbCaseUnlessUpper = ShouldPreserveDbCaseUnlessUpper(fieldPattern);
                        
                        // Extract the highlighted text from TemplateDetailText 
                        // Format: "context [highlightedText]" - extract text inside brackets
                        var detailText = detail.TemplateDetailText;
                        if (!string.IsNullOrEmpty(detailText))
                        {
                            var startBracket = detailText.LastIndexOf('[');
                            var endBracket = detailText.LastIndexOf(']');
                            if (startBracket >= 0 && endBracket > startBracket)
                            {
                                var highlightedText = detailText.Substring(startBracket + 1, endBracket - startBracket - 1);
                                var normalizedHighlight = NormalizeHighlightText(highlightedText);
                                if (!string.IsNullOrEmpty(normalizedHighlight))
                                    replacements.Add((normalizedHighlight, fieldValue, preserveDbCaseUnlessUpper));
                            }
                        }
                    }

                    var stylesPart = mainPart.StyleDefinitionsPart;
                    var stylesWithEffectsPart = mainPart.StylesWithEffectsPart;
                    var themeResolver = ThemeFontResolver.FromThemePart(mainPart.ThemePart);
                    var styleResolver = new StyleResolver(stylesPart, stylesWithEffectsPart, themeResolver);

                    ReplaceHighlightedText(body, styleResolver, replacements);
                    mainPart.Document.Save();
                }
            }

            _logger.LogInformation("Generated DOCX file: {Path}", docxFilePath);

            // Convert to PDF
            string? savedPdfPath = null;
            byte[]? pdfBytes = null;
            try
            {
                _logger.LogInformation("Converting generated DOCX to PDF...");
                pdfBytes = await _pdfConversionService.ConvertDocxToPdf(docxFilePath);
                
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    await System.IO.File.WriteAllBytesAsync(pdfFilePath, pdfBytes);
                    savedPdfPath = pdfFilePath;
                    _logger.LogInformation("Generated PDF saved: {Path}, Size: {Size} bytes", pdfFilePath, pdfBytes.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert generated document to PDF");
            }

            // Save or update generation record in database
            TemplateGeneration generation;
            if (existingGeneration != null)
            {
                // Update existing record
                existingGeneration.TemplateGenerationDocxFilepath = docxFilePath;
                existingGeneration.TemplateGenerationPdfFilepath = savedPdfPath;
                existingGeneration.TemplateGenerationUpdatedAt = DateTime.Now;
                generation = existingGeneration;
            }
            else
            {
                // Create new record
                generation = new TemplateGeneration
                {
                    TemplateId = id,
                    MhsNrp = nrpUint,
                    TemplateGenerationDocxFilepath = docxFilePath,
                    TemplateGenerationPdfFilepath = savedPdfPath
                };
                _db.TemplateGenerations.Add(generation);
            }
            
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = existingGeneration != null ? "Template berhasil di-regenerate" : "Template berhasil di-generate",
                id = generation.TemplateGenerationId,
                template_id = generation.TemplateId,
                mhs_nrp = generation.MhsNrp,
                docx_generated = true,
                pdf_generated = savedPdfPath != null,
                pdf_base64 = pdfBytes != null ? Convert.ToBase64String(pdfBytes) : null,
                created_at = generation.TemplateGenerationCreatedAt,
                updated_at = generation.TemplateGenerationUpdatedAt
            });
        }
        catch (Exception ex)
        {
            var innerMessage = ex.InnerException?.Message ?? ex.Message;
            _logger.LogError(ex, "Failed to generate template");
            return BadRequest(new { message = $"Gagal generate template: {innerMessage}" });
        }
    }

    private static void ReplaceHighlightedText(
        Body body,
        StyleResolver styleResolver,
        List<(string highlightedText, string fieldValue, bool preserveDbCaseUnlessUpper)> replacements)
    {
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var pPr = paragraph.ParagraphProperties;
            var segmentRuns = new List<Run>();
            var segmentText = new StringBuilder();
            var inHighlight = false;

            void FlushHighlightSegment()
            {
                if (segmentRuns.Count == 0)
                    return;

                var normalized = NormalizeHighlightText(segmentText.ToString());
                if (!string.IsNullOrEmpty(normalized))
                {
                    for (int i = 0; i < replacements.Count; i++)
                    {
                        if (string.Equals(replacements[i].highlightedText, normalized, StringComparison.Ordinal))
                        {
                            var replacementValue = ApplyDetectedFormat(segmentText.ToString(), replacements[i].fieldValue);
                            if (replacements[i].preserveDbCaseUnlessUpper)
                            {
                                var style = DetectCaseStyle(TrimHighlightDelimiters(segmentText.ToString()));
                                if (style == CaseStyle.Upper)
                                    replacementValue = (replacements[i].fieldValue ?? string.Empty).ToUpperInvariant();
                                else
                                    replacementValue = replacements[i].fieldValue ?? string.Empty;
                            }
                            replacements.RemoveAt(i);
                            ApplyReplacement(segmentRuns, replacementValue);
                            break;
                        }
                    }
                }

                ClearHighlight(segmentRuns);
                segmentRuns.Clear();
                segmentText.Clear();
            }

            foreach (var run in paragraph.Descendants<Run>())
            {
                if (IsRunHighlighted(styleResolver, run, pPr))
                {
                    segmentRuns.Add(run);
                    segmentText.Append(run.InnerText);
                    inHighlight = true;
                    continue;
                }

                if (inHighlight)
                {
                    FlushHighlightSegment();
                    inHighlight = false;
                }
            }

            if (inHighlight)
                FlushHighlightSegment();
        }
    }

    private static bool IsRunHighlighted(StyleResolver styleResolver, Run run, ParagraphProperties? paragraphProps)
    {
        var effective = styleResolver.GetEffectiveRunProperties(run, paragraphProps);
        var color = effective.HighlightColor;
        return !string.IsNullOrWhiteSpace(color) &&
               !color.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyReplacement(IReadOnlyList<Run> runs, string fieldValue)
    {
        if (runs.Count == 0)
            return;

        var textNodes = new List<Text>();
        foreach (var run in runs)
            textNodes.AddRange(run.Descendants<Text>());

        if (textNodes.Count == 0)
            return;

        var replacement = fieldValue ?? string.Empty;
        ClearCapsIfNeeded(runs, replacement);
        textNodes[0].Text = replacement;
        if (replacement.StartsWith(" ", StringComparison.Ordinal) ||
            replacement.EndsWith(" ", StringComparison.Ordinal))
        {
            textNodes[0].Space = SpaceProcessingModeValues.Preserve;
        }

        for (int i = 1; i < textNodes.Count; i++)
            textNodes[i].Text = string.Empty;
    }

    private static void ClearHighlight(IEnumerable<Run> runs)
    {
        foreach (var run in runs)
        {
            run.RunProperties ??= new RunProperties();
            var highlight = run.RunProperties.GetFirstChild<Highlight>();
            if (highlight == null)
            {
                run.RunProperties.AppendChild(new Highlight { Val = HighlightColorValues.None });
            }
            else
            {
                highlight.Val = HighlightColorValues.None;
            }
        }
    }

    private static void ClearCapsIfNeeded(IEnumerable<Run> runs, string replacement)
    {
        if (!HasLowercaseLetters(replacement))
            return;

        foreach (var run in runs)
        {
            run.RunProperties ??= new RunProperties();

            var caps = run.RunProperties.GetFirstChild<Caps>();
            if (caps != null)
                caps.Val = false;
            else
                run.RunProperties.AppendChild(new Caps { Val = false });

            var smallCaps = run.RunProperties.GetFirstChild<SmallCaps>();
            if (smallCaps != null)
                smallCaps.Val = false;
            else
                run.RunProperties.AppendChild(new SmallCaps { Val = false });
        }
    }

    private static bool HasLowercaseLetters(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetter(ch) && char.IsLower(ch))
                return true;
        }

        return false;
    }

    private static string NormalizeHighlightText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var inWhitespace = false;

        foreach (var ch in text)
        {
            var current = ch == '\u00A0' ? ' ' : ch;
            if (char.IsWhiteSpace(current))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
                continue;
            }

            sb.Append(current);
            inWhitespace = false;
        }

        return sb.ToString().Trim();
    }

    private enum CaseStyle
    {
        None,
        Upper,
        Lower,
        Title
    }

    private enum DatePatternKind
    {
        DayMonthYearNumeric,
        DayMonthYearName,
        MonthYearNumeric,
        MonthYearName
    }

    private sealed class DatePattern
    {
        public DatePatternKind Kind { get; init; }
        public string Separator1 { get; init; } = string.Empty;
        public string Separator2 { get; init; } = string.Empty;
        public int DayDigits { get; init; }
        public int MonthDigits { get; init; }
        public int YearDigits { get; init; }
        public CaseStyle MonthCase { get; init; } = CaseStyle.None;
        public bool MonthAbbrev { get; init; }
        public int MonthTokenLength { get; init; }
    }

    private sealed class DateParts
    {
        public int? Day { get; set; }
        public int? Month { get; set; }
        public int? Year { get; set; }
    }

    private static string ApplyDetectedFormat(string originalHighlight, string replacementValue)
    {
        if (string.IsNullOrWhiteSpace(replacementValue))
            return replacementValue ?? string.Empty;

        var source = TrimHighlightDelimiters(originalHighlight).Trim();
        if (source.Length == 0)
            return replacementValue;

        var formatted = replacementValue;
        if (TryFormatDateLike(source, replacementValue, out var dateFormatted))
            formatted = dateFormatted;

        var style = DetectCaseStyle(source);
        var lowerConnectors = style == CaseStyle.Title && UsesLowercaseConnectors(source);
        return ApplyCaseStyle(formatted, style, lowerConnectors);
    }

    private static string TrimHighlightDelimiters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            return trimmed[1..^1].Trim();

        return trimmed;
    }

    private static CaseStyle DetectCaseStyle(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0)
            return CaseStyle.None;

        if (letters.All(char.IsUpper))
            return CaseStyle.Upper;

        if (letters.All(char.IsLower))
            return CaseStyle.Lower;

        return IsTitleCaseText(text) ? CaseStyle.Title : CaseStyle.None;
    }

    private static readonly HashSet<string> TitleCaseLowerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "dan",
        "di",
        "ke",
        "dari",
        "untuk",
        "yang",
        "atau",
        "dengan",
        "pada",
        "oleh",
        "per",
        "sebagai",
        "atas",
        "bawah",
        "dalam",
        "antara",
        "tentang",
        "sampai",
        "hingga",
        "sejak",
        "tanpa",
        "bagi",
        "agar",
        "karena"
    };

    private static bool IsTitleCaseText(string text)
    {
        var matches = Regex.Matches(text, "\\p{L}+");
        if (matches.Count == 0)
            return false;

        var hasTitleWord = false;
        var wordIndex = 0;

        foreach (Match match in matches)
        {
            var word = match.Value;
            if (string.IsNullOrWhiteSpace(word))
                continue;

            if (word.All(char.IsUpper))
            {
                hasTitleWord = true;
                wordIndex++;
                continue;
            }

            if (word.All(char.IsLower) && TitleCaseLowerWords.Contains(word))
            {
                if (wordIndex == 0)
                    return false;

                wordIndex++;
                continue;
            }

            if (!char.IsUpper(word[0]))
                return false;

            if (word.Length > 1 && !word.Substring(1).All(char.IsLower))
                return false;

            hasTitleWord = true;
            wordIndex++;
        }

        return hasTitleWord;
    }

    private static bool UsesLowercaseConnectors(string text)
    {
        var matches = Regex.Matches(text, "\\p{L}+");
        foreach (Match match in matches)
        {
            var word = match.Value;
            if (string.IsNullOrWhiteSpace(word))
                continue;

            if (word.All(char.IsLower) && TitleCaseLowerWords.Contains(word))
                return true;
        }

        return false;
    }

    private static string ApplyCaseStyle(string value, CaseStyle style, bool lowerConnectors)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return style switch
        {
            CaseStyle.Upper => value.ToUpperInvariant(),
            CaseStyle.Lower => value.ToLowerInvariant(),
            CaseStyle.Title => ToTitleCase(value, lowerConnectors),
            _ => value
        };
    }

    private static string ToTitleCase(string value, bool lowerConnectors)
    {
        var sb = new StringBuilder(value.Length);
        var word = new StringBuilder();
        var isFirstWord = true;

        void FlushWord()
        {
            if (word.Length == 0)
                return;

            var token = word.ToString();
            if (lowerConnectors && !isFirstWord && TitleCaseLowerWords.Contains(token))
            {
                sb.Append(token.ToLowerInvariant());
            }
            else if (token.All(char.IsUpper))
            {
                if (token.Length <= 4 || IsRomanNumeral(token))
                {
                    sb.Append(token);
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(token[0]));
                    if (token.Length > 1)
                        sb.Append(token.Substring(1).ToLowerInvariant());
                }
            }
            else
            {
                sb.Append(char.ToUpperInvariant(token[0]));
                if (token.Length > 1)
                    sb.Append(token.Substring(1).ToLowerInvariant());
            }

            word.Clear();
            isFirstWord = false;
        }

        foreach (var ch in value)
        {
            if (char.IsLetter(ch))
            {
                word.Append(ch);
            }
            else
            {
                FlushWord();
                sb.Append(ch);
            }
        }

        FlushWord();
        return sb.ToString();
    }

    private static bool IsRomanNumeral(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return Regex.IsMatch(token, "^[IVXLCDM]+$");
    }

    private static bool TryFormatDateLike(string originalText, string replacementValue, out string formatted)
    {
        formatted = replacementValue;
        if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(replacementValue))
            return false;

        if (TryFormatNumericOnly(originalText, replacementValue, out formatted))
            return true;

        if (!TryFindDatePattern(originalText, out var pattern, out var match))
            return false;

        if (!TryExtractDateParts(replacementValue, out var parts))
            return false;

        var formattedDate = BuildFormattedDate(parts, pattern);
        if (string.IsNullOrWhiteSpace(formattedDate))
            return false;

        if (match.Index == 0 && match.Length == originalText.Length)
        {
            formatted = formattedDate;
            return true;
        }

        if (TryReplaceDateSubstring(replacementValue, formattedDate, out var replaced))
        {
            formatted = replaced;
            return true;
        }

        return false;
    }

    private static bool TryFormatNumericOnly(string originalText, string replacementValue, out string formatted)
    {
        formatted = replacementValue;
        if (!Regex.IsMatch(originalText, "^\\d+$"))
            return false;

        var targetLength = originalText.Length;
        if (targetLength == 0)
            return false;

        var replacementTrim = replacementValue.Trim();
        if (TryParseMonthName(replacementTrim, out var month))
        {
            if (targetLength <= 2)
            {
                formatted = month.ToString("D" + targetLength);
                return true;
            }
        }

        if (!Regex.IsMatch(replacementTrim, "^\\d+$"))
            return false;

        if (!int.TryParse(replacementTrim, out var number))
            return false;

        if (targetLength == 2 && replacementTrim.Length == 4)
        {
            formatted = (number % 100).ToString("D2");
            return true;
        }

        if (targetLength <= 2)
        {
            formatted = number.ToString("D" + targetLength);
            return true;
        }

        return false;
    }

    private static bool TryFindDatePattern(string text, out DatePattern pattern, out Match match)
    {
        pattern = new DatePattern();
        match = Match.Empty;

        var m = Regex.Match(text, "(?<day>\\d{1,2})(?<sep1>[-/.])(?<month>\\d{1,2})(?<sep2>[-/.])(?<year>\\d{2,4})");
        if (m.Success)
        {
            pattern = new DatePattern
            {
                Kind = DatePatternKind.DayMonthYearNumeric,
                Separator1 = m.Groups["sep1"].Value,
                Separator2 = m.Groups["sep2"].Value,
                DayDigits = m.Groups["day"].Value.Length,
                MonthDigits = m.Groups["month"].Value.Length,
                YearDigits = m.Groups["year"].Value.Length
            };
            match = m;
            return true;
        }

        m = Regex.Match(text, "(?<day>\\d{1,2})(?<sep1>\\s+)(?<month>[A-Za-z]+)(?<sep2>\\s+)(?<year>\\d{2,4})");
        if (m.Success && TryParseMonthName(m.Groups["month"].Value, out var parsedMonth))
        {
            var monthToken = m.Groups["month"].Value;
            pattern = new DatePattern
            {
                Kind = DatePatternKind.DayMonthYearName,
                Separator1 = m.Groups["sep1"].Value,
                Separator2 = m.Groups["sep2"].Value,
                DayDigits = m.Groups["day"].Value.Length,
                YearDigits = m.Groups["year"].Value.Length,
                MonthCase = DetectCaseStyle(monthToken),
                MonthAbbrev = IsMonthAbbreviation(monthToken),
                MonthTokenLength = monthToken.Length
            };
            match = m;
            return true;
        }

        m = Regex.Match(text, "(?<month>[A-Za-z]+)(?<sep>\\s+)(?<year>\\d{2,4})");
        if (m.Success && TryParseMonthName(m.Groups["month"].Value, out var parsedMonth2))
        {
            var monthToken = m.Groups["month"].Value;
            pattern = new DatePattern
            {
                Kind = DatePatternKind.MonthYearName,
                Separator1 = m.Groups["sep"].Value,
                YearDigits = m.Groups["year"].Value.Length,
                MonthCase = DetectCaseStyle(monthToken),
                MonthAbbrev = IsMonthAbbreviation(monthToken),
                MonthTokenLength = monthToken.Length
            };
            match = m;
            return true;
        }

        m = Regex.Match(text, "(?<month>\\d{1,2})(?<sep>[-/.])(?<year>\\d{2,4})");
        if (m.Success)
        {
            pattern = new DatePattern
            {
                Kind = DatePatternKind.MonthYearNumeric,
                Separator1 = m.Groups["sep"].Value,
                MonthDigits = m.Groups["month"].Value.Length,
                YearDigits = m.Groups["year"].Value.Length
            };
            match = m;
            return true;
        }

        return false;
    }

    private static bool TryExtractDateParts(string text, out DateParts parts)
    {
        parts = new DateParts();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var m = Regex.Match(text, "(?<day>\\d{1,2})(?<sep1>[-/.])(?<month>\\d{1,2})(?<sep2>[-/.])(?<year>\\d{2,4})");
        if (m.Success &&
            int.TryParse(m.Groups["day"].Value, out var day) &&
            int.TryParse(m.Groups["month"].Value, out var month) &&
            int.TryParse(m.Groups["year"].Value, out var year))
        {
            parts.Day = day;
            parts.Month = month;
            parts.Year = year;
            return true;
        }

        m = Regex.Match(text, "(?<day>\\d{1,2})\\s+(?<month>[A-Za-z]+)\\s+(?<year>\\d{2,4})");
        if (m.Success &&
            int.TryParse(m.Groups["day"].Value, out day) &&
            TryParseMonthName(m.Groups["month"].Value, out month) &&
            int.TryParse(m.Groups["year"].Value, out year))
        {
            parts.Day = day;
            parts.Month = month;
            parts.Year = year;
            return true;
        }

        m = Regex.Match(text, "(?<day>\\d{1,2})\\s*[-/.]\\s*(?<month>[A-Za-z]+)\\s*[-/.]\\s*(?<year>\\d{2,4})");
        if (m.Success &&
            int.TryParse(m.Groups["day"].Value, out day) &&
            TryParseMonthName(m.Groups["month"].Value, out month) &&
            int.TryParse(m.Groups["year"].Value, out year))
        {
            parts.Day = day;
            parts.Month = month;
            parts.Year = year;
            return true;
        }

        m = Regex.Match(text, "(?<month>[A-Za-z]+)\\s+(?<year>\\d{2,4})");
        if (m.Success &&
            TryParseMonthName(m.Groups["month"].Value, out month) &&
            int.TryParse(m.Groups["year"].Value, out year))
        {
            parts.Month = month;
            parts.Year = year;
            return true;
        }

        m = Regex.Match(text, "(?<month>[A-Za-z]+)\\s*[-/.]\\s*(?<year>\\d{2,4})");
        if (m.Success &&
            TryParseMonthName(m.Groups["month"].Value, out month) &&
            int.TryParse(m.Groups["year"].Value, out year))
        {
            parts.Month = month;
            parts.Year = year;
            return true;
        }

        m = Regex.Match(text, "(?<month>\\d{1,2})(?<sep>[-/.])(?<year>\\d{2,4})");
        if (m.Success &&
            int.TryParse(m.Groups["month"].Value, out month) &&
            int.TryParse(m.Groups["year"].Value, out year))
        {
            parts.Month = month;
            parts.Year = year;
            return true;
        }

        m = Regex.Match(text, "(?<month>[A-Za-z]+)");
        if (m.Success && TryParseMonthName(m.Groups["month"].Value, out month))
        {
            parts.Month = month;
            return true;
        }

        if (Regex.IsMatch(text.Trim(), "^\\d+$") && int.TryParse(text.Trim(), out var numeric))
        {
            if (text.Trim().Length <= 2)
            {
                parts.Day = numeric;
                return true;
            }

            parts.Year = numeric;
            return true;
        }

        return false;
    }

    private static string? BuildFormattedDate(DateParts parts, DatePattern pattern)
    {
        switch (pattern.Kind)
        {
            case DatePatternKind.DayMonthYearNumeric:
                if (!parts.Day.HasValue || !parts.Month.HasValue || !parts.Year.HasValue)
                    return null;
                return FormatNumber(parts.Day.Value, pattern.DayDigits) +
                       pattern.Separator1 +
                       FormatNumber(parts.Month.Value, pattern.MonthDigits) +
                       pattern.Separator2 +
                       FormatYear(parts.Year.Value, pattern.YearDigits);
            case DatePatternKind.DayMonthYearName:
                if (!parts.Day.HasValue || !parts.Month.HasValue || !parts.Year.HasValue)
                    return null;
                var monthName = ApplyCaseStyle(
                    GetMonthName(parts.Month.Value, pattern.MonthAbbrev, pattern.MonthTokenLength),
                    pattern.MonthCase,
                    false);
                return FormatNumber(parts.Day.Value, pattern.DayDigits) +
                       pattern.Separator1 +
                       monthName +
                       pattern.Separator2 +
                       FormatYear(parts.Year.Value, pattern.YearDigits);
            case DatePatternKind.MonthYearNumeric:
                if (!parts.Month.HasValue || !parts.Year.HasValue)
                    return null;
                return FormatNumber(parts.Month.Value, pattern.MonthDigits) +
                       pattern.Separator1 +
                       FormatYear(parts.Year.Value, pattern.YearDigits);
            case DatePatternKind.MonthYearName:
                if (!parts.Month.HasValue || !parts.Year.HasValue)
                    return null;
                var monthText = ApplyCaseStyle(
                    GetMonthName(parts.Month.Value, pattern.MonthAbbrev, pattern.MonthTokenLength),
                    pattern.MonthCase,
                    false);
                return monthText + pattern.Separator1 + FormatYear(parts.Year.Value, pattern.YearDigits);
            default:
                return null;
        }
    }

    private static bool TryReplaceDateSubstring(string input, string replacement, out string result)
    {
        var patterns = new[]
        {
            "\\d{1,2}[-/.]\\d{1,2}[-/.]\\d{2,4}",
            "\\d{1,2}\\s+[A-Za-z]+\\s+\\d{2,4}",
            "\\d{1,2}\\s*[-/.]\\s*[A-Za-z]+\\s*[-/.]\\s*\\d{2,4}",
            "[A-Za-z]+\\s+\\d{2,4}",
            "[A-Za-z]+\\s*[-/.]\\s*\\d{2,4}",
            "\\d{1,2}[-/.]\\d{2,4}"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern);
            if (!match.Success)
                continue;

            result = input.Substring(0, match.Index) + replacement + input.Substring(match.Index + match.Length);
            return true;
        }

        result = input;
        return false;
    }

    private static string FormatNumber(int value, int digits)
    {
        if (digits <= 1)
            return value.ToString();

        return value.ToString("D" + digits);
    }

    private static string FormatYear(int year, int digits)
    {
        if (digits <= 2)
            return (year % 100).ToString("D2");

        return year.ToString("D4");
    }

    private static bool TryParseMonthName(string value, out int month)
    {
        month = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();

        if (MonthNameToNumber.TryGetValue(normalized, out month))
            return true;

        return false;
    }

    private static string GetMonthName(int month, bool abbrev, int tokenLength)
    {
        if (month < 1 || month > 12)
            return string.Empty;

        if (!abbrev)
        {
            return MonthNamesId[month];
        }

        var name = MonthAbbrevId[month];

        if (string.IsNullOrWhiteSpace(name))
            name = MonthNamesId[month];

        if (month == 9 && tokenLength == 4)
            return "Sept";

        if (tokenLength > 0 && tokenLength < name.Length)
            return name.Substring(0, tokenLength);

        return name;
    }

    private static bool IsMonthAbbreviation(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var letters = new string(token.Where(char.IsLetter).ToArray());
        if (letters.Length == 0)
            return false;

        if (letters.Length <= 3)
            return true;

        return letters.Equals("Sept", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] MonthNamesId =
    {
        "",
        "Januari",
        "Februari",
        "Maret",
        "April",
        "Mei",
        "Juni",
        "Juli",
        "Agustus",
        "September",
        "Oktober",
        "November",
        "Desember"
    };

    private static readonly string[] MonthAbbrevId =
    {
        "",
        "Jan",
        "Feb",
        "Mar",
        "Apr",
        "Mei",
        "Jun",
        "Jul",
        "Agu",
        "Sep",
        "Okt",
        "Nov",
        "Des"
    };

    private static readonly Dictionary<string, int> MonthNameToNumber = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Januari", 1 },
        { "Februari", 2 },
        { "Maret", 3 },
        { "April", 4 },
        { "Mei", 5 },
        { "Juni", 6 },
        { "Juli", 7 },
        { "Agustus", 8 },
        { "September", 9 },
        { "Oktober", 10 },
        { "November", 11 },
        { "Desember", 12 },
        { "Agu", 8 },
        { "Okt", 10 },
        { "Des", 12 },
        { "Jan", 1 },
        { "Feb", 2 },
        { "Mar", 3 },
        { "Apr", 4 },
        { "Jun", 6 },
        { "Jul", 7 },
        { "Sep", 9 },
        { "Nov", 11 }
    };

    private static readonly Regex FieldTokenRegex = new(@"\[[^\[\]]+\]", RegexOptions.Compiled);

    private static string BuildFieldValue(
        string fieldPattern,
        IReadOnlyDictionary<string, string> fieldValues,
        IReadOnlyList<string> pengujiNames,
        ref int pengujiIndex)
    {
        if (string.IsNullOrWhiteSpace(fieldPattern))
            return string.Empty;

        var matches = FieldTokenRegex.Matches(fieldPattern);
        if (matches.Count == 0)
        {
            var direct = ResolveTokenValue(fieldPattern, fieldValues, pengujiNames, ref pengujiIndex);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct.Trim();

            var bracketed = $"[{fieldPattern.Trim()}]";
            var alt = ResolveTokenValue(bracketed, fieldValues, pengujiNames, ref pengujiIndex);
            return alt?.Trim() ?? string.Empty;
        }

        var segment = BuildTokenSegment(fieldPattern, matches);
        var sb = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in FieldTokenRegex.Matches(segment))
        {
            if (match.Index > lastIndex)
                sb.Append(segment, lastIndex, match.Index - lastIndex);

            var replacement = ResolveTokenValue(match.Value, fieldValues, pengujiNames, ref pengujiIndex);
            sb.Append(replacement);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < segment.Length)
            sb.Append(segment, lastIndex, segment.Length - lastIndex);

        return sb.ToString().Trim();
    }

    private static string BuildTokenSegment(string fieldPattern, MatchCollection matches)
    {
        var first = matches[0];
        var last = matches[^1];
        var start = first.Index;
        var end = last.Index + last.Length;

        var segment = fieldPattern.Substring(start, end - start);

        var prefix = fieldPattern[..start];
        if (IsPunctuationOrWhitespace(prefix))
            segment = prefix + segment;

        var suffix = fieldPattern[end..];
        if (IsPunctuationOrWhitespace(suffix))
            segment += suffix;

        return segment;
    }

    private static string ResolveTokenValue(
        string token,
        IReadOnlyDictionary<string, string> fieldValues,
        IReadOnlyList<string> pengujiNames,
        ref int pengujiIndex)
    {
        if (string.Equals(token, "[penguji]", StringComparison.OrdinalIgnoreCase))
        {
            var value = pengujiIndex < pengujiNames.Count ? pengujiNames[pengujiIndex] : string.Empty;
            pengujiIndex++;
            return value ?? string.Empty;
        }

        if (fieldValues.TryGetValue(token, out var valueFromMap))
            return valueFromMap ?? string.Empty;

        return string.Empty;
    }

    private static bool IsPunctuationOrWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                return false;
        }

        return true;
    }

    private static readonly HashSet<string> PreserveDbCaseUnlessUpperTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "[pembimbing]",
        "[co_pembimbing]",
        "[penguji]"
    };

    private static bool ShouldPreserveDbCaseUnlessUpper(string fieldPattern)
    {
        if (string.IsNullOrWhiteSpace(fieldPattern))
            return false;

        var matches = FieldTokenRegex.Matches(fieldPattern);
        if (matches.Count == 0)
        {
            var token = fieldPattern.Trim();
            if (!token.StartsWith("[", StringComparison.Ordinal))
                token = $"[{token}]";
            return PreserveDbCaseUnlessUpperTokens.Contains(token);
        }

        foreach (Match match in matches)
        {
            if (PreserveDbCaseUnlessUpperTokens.Contains(match.Value))
                return true;
        }

        return false;
    }
}

// Request DTOs
public class UpdateTemplateRequest
{
    public string? name { get; set; }
    public string? status { get; set; }
    public List<UpdateTemplateDetailItem>? details { get; set; }
}

public class UpdateTemplateDetailItem
{
    public uint? id { get; set; }
    public string? field { get; set; }
    public string? catatan { get; set; }
    public bool? optional { get; set; }
}

public class UpdateTemplateDetailRequest
{
    public string? field { get; set; }
    public string? catatan { get; set; }
    public bool? optional { get; set; }
}

public class GenerateTemplateRequest
{
    public List<string>? penguji { get; set; }
}

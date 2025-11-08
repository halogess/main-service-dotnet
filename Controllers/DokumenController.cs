using Microsoft.AspNetCore.Mvc;
using _.Services;

namespace _.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DokumenController : ControllerBase
{
    private readonly IDokumenService _dokumenService;
    private readonly KorektorBukuDbContext _db;
    private readonly IConfiguration _configuration;

    public DokumenController(IDokumenService dokumenService, KorektorBukuDbContext db, IConfiguration configuration)
    {
        _dokumenService = dokumenService;
        _db = db;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> UploadDokumen(IFormFile file)
    {
        try
        {
            var nrp = HttpContext.Items["Nrp"]?.ToString();

            if (string.IsNullOrEmpty(nrp))
            {
                return Unauthorized(new { message = "NRP tidak ditemukan" });
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "File tidak boleh kosong" });
            }

            var dokumen = await _dokumenService.UploadDokumen(nrp, file);

            return Ok(new 
            { 
                message = "Dokumen berhasil diupload", 
                dokumen_id = dokumen.DokumenId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Terjadi kesalahan", error = ex.Message });
        }
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            var dokumen = await _dokumenService.UpdateStatus(id, request.status);

            return Ok(new 
            { 
                message = "Status dokumen berhasil diupdate", 
                data = new 
                {
                    dokumen_id = dokumen.DokumenId,
                    mhs_nrp = dokumen.MhsNrp,
                    dokumen_filename = dokumen.DokumenFilename,
                    dokumen_status = dokumen.DokumenStatus,
                    dokumen_created_at = dokumen.DokumenCreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    dokumen_updated_at = dokumen.DokumenUpdatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Terjadi kesalahan", error = ex.Message });
        }
    }

    [HttpGet("{id}/file")]
    public IActionResult GetFile(int id, [FromQuery] string type)
    {
        try
        {
            var dokumen = _db.Dokumens.Find(id);
            if (dokumen == null)
            {
                return NotFound(new { message = "Dokumen tidak ditemukan" });
            }

            // Cek autentikasi: JWT (web) atau API Key (AI service)
            var nrp = HttpContext.Items["Nrp"]?.ToString();
            var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
            var validApiKey = _configuration["InternalApiKey"];

            bool isAuthenticated = false;
            
            // Validasi JWT dari web
            if (!string.IsNullOrEmpty(nrp) && dokumen.MhsNrp == nrp)
            {
                isAuthenticated = true;
            }
            // Validasi API Key dari AI service
            else if (!string.IsNullOrEmpty(apiKey) && apiKey == validApiKey && !string.IsNullOrEmpty(validApiKey))
            {
                isAuthenticated = true;
            }

            if (!isAuthenticated)
            {
                return Unauthorized(new { message = "Tidak memiliki akses" });
            }

            string? filePath;
            string contentType;
            string fileName;

            if (type == "pdf")
            {
                filePath = dokumen.DokumenPdfPath;
                Console.WriteLine($"[DOWNLOAD] PDF Path from DB: {filePath}");
                
                // Fallback ke path lama jika kolom pdf_path masih NULL
                if (string.IsNullOrEmpty(filePath))
                {
                    var pdfFilename = Path.ChangeExtension(dokumen.DokumenFilename, ".pdf");
                    filePath = Path.Combine("pdf", dokumen.MhsNrp, pdfFilename);
                    Console.WriteLine($"[DOWNLOAD] DokumenFilename: {dokumen.DokumenFilename}");
                    Console.WriteLine($"[DOWNLOAD] PDF Filename: {pdfFilename}");
                    Console.WriteLine($"[DOWNLOAD] Using fallback PDF path: {filePath}");
                }
                
                contentType = "application/pdf";
                fileName = Path.GetFileName(filePath);
            }
            else
            {
                filePath = dokumen.DokumenDocxPath;
                
                // Fallback ke path lama jika kolom docx_path masih NULL
                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = Path.Combine("uploads", dokumen.MhsNrp, dokumen.DokumenFilename);
                    Console.WriteLine($"[DOWNLOAD] Using fallback DOCX path: {filePath}");
                }
                
                contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                fileName = Path.GetFileName(filePath);
            }

            if (!System.IO.File.Exists(filePath))
            {
                Console.WriteLine($"[DOWNLOAD] File not found: {filePath}");
                return NotFound(new { message = "File tidak ditemukan" });
            }

            Console.WriteLine($"[DOWNLOAD] Checking file: {filePath}");
            Console.WriteLine($"[DOWNLOAD] File exists: {System.IO.File.Exists(filePath)}");
            
            var fileInfo = new FileInfo(filePath);
            Console.WriteLine($"[DOWNLOAD] File size: {fileInfo.Length} bytes, Type: {type}");

            if (type == "pdf" && fileInfo.Length < 100)
            {
                Console.WriteLine($"[DOWNLOAD] PDF file too small, possibly corrupt");
                return StatusCode(500, new { message = "PDF corrupt atau belum selesai di-generate" });
            }

            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            return File(fileStream, contentType);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DOWNLOAD] Exception: {ex.Message}");
            Console.WriteLine($"[DOWNLOAD] StackTrace: {ex.StackTrace}");
            return StatusCode(500, new { message = "Terjadi kesalahan", error = ex.Message });
        }
    }
}

public class UpdateStatusRequest
{
    public string status { get; set; } = string.Empty;
}

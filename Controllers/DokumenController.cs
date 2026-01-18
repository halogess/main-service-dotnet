using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DokumenController : ControllerBase
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff",
        ".webp"
    };

    private readonly KorektorBukuDbContext _db;
    private readonly IDokumenService _dokumenService;

    public DokumenController(KorektorBukuDbContext db, IDokumenService dokumenService)
    {
        _db = db;
        _dokumenService = dokumenService;
    }

    [HttpPost]
    public async Task<IActionResult> UploadDokumen(IFormFile file, [FromForm] string? tipe = null)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });
        
        if (_dokumenService.HasDokumenInQueue(nrp!))
            return BadRequest(new { message = "Masih ada dokumen dalam antrian" });
        
        try
        {
            var dokumen = await _dokumenService.UploadDokumen(nrp!, file, tipe);
            return Ok(new { message = "Dokumen berhasil diupload", dokumen_id = dokumen.DokumenId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id}/batal")]
    public async Task<IActionResult> BatalDokumen(int id)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        try
        {
            await _dokumenService.BatalDokumen(nrp!, id);
            return Ok(new { message = "Dokumen berhasil dibatalkan" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("can-upload")]
    public IActionResult CanUpload()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        return Ok(new { can_upload = _dokumenService.CanUpload(nrp!) });
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var stats = _dokumenService.GetStats(nrp!);
        
        return Ok(new
        {
            total = stats.Total,
            dibatalkan = stats.Dibatalkan,
            dalam_antrian = stats.DalamAntrian,
            diproses = stats.Diproses,
            lolos = stats.Lolos,
            tidak_lolos = stats.TidakLolos
        });
    }

    [HttpGet]
    public IActionResult GetDokumen([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var result = _dokumenService.GetDokumenList(nrp!, status, sort, limit, offset);

        return Ok(new
        {
            data = result.Data.Select(d => new
            {
                id = d.Id,
                filename = d.Filename,
                tanggal_upload = d.TanggalUpload,
                ukuran_file = d.UkuranFile,
                status = d.Status,
                jumlah_kesalahan = d.JumlahKesalahan
            }),
            total = result.Total,
            limit = result.Limit,
            offset = result.Offset
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDokumenById(int id)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(id);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        // Check authorization: admin can access all, user can only access their own
        if (role != "admin" && dokumen.MhsNrp != nrp)
            return Forbid();

        // Base response
        object? kesalahanData = null;

        // Include kesalahan list if document has been validated (lolos or tidak_lolos)
        if (dokumen.DokumenStatus == "lolos" || dokumen.DokumenStatus == "tidak_lolos")
        {
            // Fetch all kesalahan for this dokumen (no details - use /api/kesalahan/{id} for details)
            var kesalahanList = await _db.Kesalahans
                .Where(k => k.KesalahanRefTipe == Models.KesalahanRefTipe.dokumen && k.KesalahanRefId == (uint)id)
                .ToListAsync();

            // Group by halaman_ke (each Kesalahan = 1 elemen)
            // Use -1 to represent null/unknown halaman
            var pageGroups = new Dictionary<int, List<object>>();

            foreach (var kesalahan in kesalahanList)
            {
                // Parse lokasi to extract halaman_ke
                int halamanKe = -1; // -1 means unknown page
                if (!string.IsNullOrEmpty(kesalahan.KesalahanLokasi))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(kesalahan.KesalahanLokasi);
                        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                        {
                            var firstLoc = doc.RootElement[0];
                            if (firstLoc.TryGetProperty("halaman_ke", out var halamanEl) && halamanEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                halamanKe = halamanEl.GetInt32();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors
                    }
                }

                if (!pageGroups.ContainsKey(halamanKe))
                {
                    pageGroups[halamanKe] = new List<object>();
                }

                // Each Kesalahan represents 1 elemen (details via /api/kesalahan/{id})
                pageGroups[halamanKe].Add(new
                {
                    kesalahan_id = kesalahan.KesalahanId,
                    kategori = kesalahan.KesalahanKategori,
                    lokasi = kesalahan.KesalahanLokasi
                });
            }

            // Convert to response format, ordered by halaman_ke (-1 means unknown, sort last)
            kesalahanData = pageGroups
                .OrderBy(g => g.Key == -1 ? int.MaxValue : g.Key)
                .Select(g => new
                {
                    halaman_ke = g.Key == -1 ? (int?)null : g.Key,
                    elemen = g.Value  // list of Kesalahan, each represents 1 elemen
                })
                .ToList();
        }

        return Ok(new
        {
            id = dokumen.DokumenId,
            tipe = dokumen.DokumenTipe,
            filename = dokumen.DokumenFilename,
            filesize_bytes = dokumen.DokumenFilesizeBytes,
            status = dokumen.DokumenStatus,
            skor = dokumen.DokumenSkor,
            jumlah_kesalahan = dokumen.DokumenJumlahKesalahan,
            created_at = dokumen.DokumenCreatedAt,
            updated_at = dokumen.DokumenUpdatedAt,
            kesalahan = kesalahanData
        });
    }

    [HttpGet("{dokumenId}/image/{page}")]
    public async Task<IActionResult> GetDokumenImage(int dokumenId, int page)
    {
        if (page <= 0)
            return BadRequest(new { message = "Page harus lebih dari 0" });

        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(dokumenId);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != currentNrp)
            return Forbid();

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        
        // Path: storage/dokumen/{nrp}/{dokumenId}/images/{page}.jpg
        var imagesDir = Path.GetFullPath(Path.Combine(storagePath, "dokumen", dokumen.MhsNrp!, dokumenId.ToString(), "images"));

        if (!imagesDir.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Path image tidak valid" });

        if (!Directory.Exists(imagesDir))
            return NotFound(new { message = "Folder image tidak ditemukan" });

        // Try different extensions in order of preference
        string? filePath = null;
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff" })
        {
            var candidate = Path.Combine(imagesDir, $"{page}{ext}");
            if (System.IO.File.Exists(candidate))
            {
                filePath = candidate;
                break;
            }
        }

        if (filePath == null)
            return NotFound(new { message = $"Image halaman {page} tidak ditemukan" });

        var contentType = GetImageContentType(filePath);
        return PhysicalFile(filePath, contentType);
    }

    private static string GetImageContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static (bool HasNumber, int Number, string Name) GetImageSortKey(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var number = TryGetTrailingNumber(name);
        return number.HasValue ? (true, number.Value, name) : (false, int.MaxValue, name);
    }

    private static int? TryGetTrailingNumber(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var index = name.Length - 1;
        while (index >= 0 && char.IsDigit(name[index]))
            index--;

        var start = index + 1;
        if (start >= name.Length)
            return null;

        var digits = name[start..];
        return int.TryParse(digits, out var number) ? number : null;
    }
}

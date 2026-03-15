using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    private readonly IValidationReportService _reportService;

    public DokumenController(
        KorektorBukuDbContext db,
        IDokumenService dokumenService,
        IValidationReportService reportService)
    {
        _db = db;
        _dokumenService = dokumenService;
        _reportService = reportService;
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

            var orderedKesalahan = kesalahanList
                .SelectMany(k => ParseSortKeys(k.KesalahanLokasi).Select(sortKey => new
                {
                    Kesalahan = k,
                    HalamanKe = sortKey.HalamanKe,
                    YTerkecil = sortKey.YTerkecil
                }))
                .OrderBy(x => NormalizeSortPage(x.HalamanKe))
                .ThenBy(x => x.YTerkecil)
                .ThenBy(x => x.Kesalahan.KesalahanId)
                .ToList();

            // Convert to response format, ordered by halaman_ke then y terkecil
            // Use -1 to represent null/unknown halaman
            kesalahanData = orderedKesalahan
                .GroupBy(x => x.HalamanKe)
                .OrderBy(g => NormalizeSortPage(g.Key))
                .Select(g => new
                {
                    halaman_ke = g.Key == -1 ? (int?)null : g.Key,
                    elemen = g.Select(x => new
                    {
                        kesalahan_id = x.Kesalahan.KesalahanId,
                        kategori = x.Kesalahan.KesalahanKategori,
                        lokasi = x.Kesalahan.KesalahanLokasi
                    }).ToList() // each Kesalahan represents 1 elemen (details via /api/kesalahan/{id})
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
            skor_minimal = dokumen.DokumenSkorMinimal,
            jumlah_kesalahan = dokumen.DokumenJumlahKesalahan,
            created_at = dokumen.DokumenCreatedAt,
            updated_at = dokumen.DokumenUpdatedAt,
            kesalahan = kesalahanData
        });
    }

    [HttpGet("{id}/report")]
    public async Task<IActionResult> GetValidationReport(int id, [FromQuery] bool refresh = false)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        try
        {
            var report = await _reportService.GenerateDokumenReportAsync(
                id,
                nrp,
                role,
                refresh,
                HttpContext.RequestAborted);

            return File(report.Content, "application/pdf", report.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}/docx")]
    public async Task<IActionResult> DownloadDokumenDocx(int id)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(id);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != currentNrp)
            return Forbid();

        if (string.IsNullOrWhiteSpace(dokumen.DokumenDocxPath))
            return NotFound(new { message = "File DOCX dokumen tidak ditemukan" });

        if (!TryResolveStorageFilePath(dokumen.DokumenDocxPath, out var fullPath))
            return BadRequest(new { message = "Path file tidak valid" });

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "File DOCX dokumen tidak ditemukan" });

        var downloadName = BuildDocxDownloadFileName(dokumen.DokumenFilename, dokumen.DokumenId);
        return PhysicalFile(
            fullPath,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            downloadName);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> DownloadDokumenPdf(int id)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(id);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != currentNrp)
            return Forbid();

        if (string.IsNullOrWhiteSpace(dokumen.DokumenPdfPath))
            return NotFound(new { message = "File PDF dokumen belum tersedia" });

        if (!TryResolveStorageFilePath(dokumen.DokumenPdfPath, out var fullPath))
            return BadRequest(new { message = "Path file tidak valid" });

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "File PDF dokumen belum tersedia" });

        var downloadName = BuildPdfDownloadFileName(dokumen.DokumenFilename, dokumen.DokumenId);
        return PhysicalFile(fullPath, "application/pdf", downloadName);
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

    private static bool TryResolveStorageFilePath(string filePath, out string fullPath)
    {
        fullPath = string.Empty;

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        var candidatePath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(storagePath, filePath);

        var resolved = Path.GetFullPath(candidatePath);
        if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = resolved;
        return true;
    }

    private static string BuildDocxDownloadFileName(string? dokumenFilename, int dokumenId)
    {
        if (!string.IsNullOrWhiteSpace(dokumenFilename))
        {
            var fileName = Path.GetFileName(dokumenFilename.Trim());
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;
        }

        return $"dokumen_{dokumenId}.docx";
    }

    private static string BuildPdfDownloadFileName(string? dokumenFilename, int dokumenId)
    {
        if (!string.IsNullOrWhiteSpace(dokumenFilename))
        {
            var baseName = Path.GetFileNameWithoutExtension(dokumenFilename.Trim());
            if (!string.IsNullOrWhiteSpace(baseName))
                return baseName + ".pdf";
        }

        return $"dokumen_{dokumenId}.pdf";
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

    private static int NormalizeSortPage(int halamanKe)
        => halamanKe <= 0 ? int.MaxValue : halamanKe;

    private static List<(int HalamanKe, double YTerkecil)> ParseSortKeys(string? lokasiJson)
    {
        if (string.IsNullOrWhiteSpace(lokasiJson))
            return new List<(int HalamanKe, double YTerkecil)> { (-1, double.MaxValue) };

        var bestByPage = new Dictionary<int, double>();

        try
        {
            using var doc = JsonDocument.Parse(lokasiJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<(int HalamanKe, double YTerkecil)> { (-1, double.MaxValue) };

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var halamanKe = -1;
                if (item.TryGetProperty("halaman_ke", out var halamanEl) &&
                    halamanEl.ValueKind == JsonValueKind.Number &&
                    halamanEl.TryGetInt32(out var parsedPage) &&
                    parsedPage > 0)
                {
                    halamanKe = parsedPage;
                }

                var yTerkecil = double.MaxValue;
                if (item.TryGetProperty("bbox", out var bboxEl) &&
                    bboxEl.ValueKind == JsonValueKind.Object &&
                    bboxEl.TryGetProperty("y0", out var y0El) &&
                    y0El.ValueKind == JsonValueKind.Number &&
                    y0El.TryGetDouble(out var parsedY))
                {
                    yTerkecil = parsedY;
                }

                if (bestByPage.TryGetValue(halamanKe, out var existingY))
                    bestByPage[halamanKe] = Math.Min(existingY, yTerkecil);
                else
                    bestByPage[halamanKe] = yTerkecil;
            }
        }
        catch (JsonException)
        {
            // Ignore parsing errors
        }

        if (bestByPage.Count == 0)
            return new List<(int HalamanKe, double YTerkecil)> { (-1, double.MaxValue) };

        if (bestByPage.Keys.Any(page => page > 0))
            bestByPage.Remove(-1);

        return bestByPage
            .Select(kv => (HalamanKe: kv.Key, YTerkecil: kv.Value))
            .OrderBy(x => NormalizeSortPage(x.HalamanKe))
            .ThenBy(x => x.YTerkecil)
            .ToList();
    }
}

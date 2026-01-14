using Microsoft.AspNetCore.Mvc;
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

        var response = new
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
            kesalahan = (object?)null
        };

        // Include kesalahan list if document has been validated (lolos or tidak_lolos)
        if (dokumen.DokumenStatus == "lolos" || dokumen.DokumenStatus == "tidak_lolos")
        {
            var kesalahanList = _db.Kesalahans
                .Where(k => k.KesalahanRefTipe == Models.KesalahanRefTipe.dokumen && k.KesalahanRefId == (uint)id)
                .Select(k => new
                {
                    id = k.KesalahanId,
                    kategori = k.KesalahanKategori,
                    judul = k.KesalahanJudul,
                    penjelasan = k.KesalahanPenjelasan,
                    lokasi = k.KesalahanLokasi,
                    bbox_visual = k.KesalahanBboxVisual,
                    steps = k.KesalahanSteps
                })
                .ToList();

            response = new
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
                kesalahan = (object?)kesalahanList
            };
        }

        return Ok(response);
    }

    [HttpGet("{dokumenId}/image/{urutanImage}")]
    public async Task<IActionResult> GetDokumenImage(int dokumenId, int urutanImage)
    {
        if (urutanImage <= 0)
            return BadRequest(new { message = "Urutan image harus lebih dari 0" });

        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(dokumenId);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != currentNrp)
            return Forbid();

        if (string.IsNullOrWhiteSpace(dokumen.DokumenImagesPath))
            return NotFound(new { message = "Path image tidak ditemukan" });

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        var imagesDir = Path.GetFullPath(Path.Combine(storagePath, dokumen.DokumenImagesPath));

        if (!imagesDir.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Path image tidak valid" });

        if (!Directory.Exists(imagesDir))
            return NotFound(new { message = "Folder image tidak ditemukan" });

        var imageFiles = Directory.EnumerateFiles(imagesDir)
            .Where(path => AllowedImageExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new { Path = path, SortKey = GetImageSortKey(path) })
            .OrderBy(item => item.SortKey.HasNumber ? 0 : 1)
            .ThenBy(item => item.SortKey.Number)
            .ThenBy(item => item.SortKey.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .ToList();

        if (urutanImage > imageFiles.Count)
            return NotFound(new { message = "Image tidak ditemukan" });

        var filePath = imageFiles[urutanImage - 1];
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

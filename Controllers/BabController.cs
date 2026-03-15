using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BabController : ControllerBase
{
    private static readonly string[] PreferredImageExtensions =
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff"
    };

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

    public BabController(KorektorBukuDbContext db)
    {
        _db = db;
    }

    [HttpGet("{babId}")]
    public async Task<IActionResult> GetKesalahanByBab(uint babId)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var bab = await _db.Babs
            .AsNoTracking()
            .Where(b => b.BabId == babId)
            .Select(b => new
            {
                b.BabId,
                b.BukuId,
                b.BabOrder,
                b.BabFilename,
                b.BabSkor,
                b.BabSkorMinimal,
                b.BabJumlahKesalahan
            })
            .FirstOrDefaultAsync();

        if (bab == null)
            return NotFound(new { message = "Bab tidak ditemukan" });

        var bukuOwner = await _db.Bukus
            .AsNoTracking()
            .Where(b => (uint)b.BukuId == bab.BukuId)
            .Select(b => new { b.BukuId, b.MhsNrp, b.BukuStatus })
            .FirstOrDefaultAsync();

        if (bukuOwner == null)
            return NotFound(new { message = "Buku induk tidak ditemukan" });

        if (role != "admin" && !string.Equals(bukuOwner.MhsNrp, currentNrp, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var kesalahanList = await _db.Kesalahans
            .AsNoTracking()
            .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.bab && k.KesalahanRefId == babId)
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

        var kesalahanData = orderedKesalahan
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
                }).ToList()
            })
            .ToList();

        var jumlahKesalahan = await (
            from detail in _db.KesalahanDetails.AsNoTracking()
            join parent in _db.Kesalahans.AsNoTracking() on detail.KesalahanId equals parent.KesalahanId
            where parent.KesalahanRefTipe == KesalahanRefTipe.bab && parent.KesalahanRefId == babId
            select detail.KesalahanDetailId
        ).CountAsync();

        return Ok(new
        {
            id = bab.BabId,
            filename = bab.BabFilename,
            status = bukuOwner.BukuStatus,
            skor = bab.BabSkor,
            skor_minimal = bab.BabSkorMinimal,
            jumlah_kesalahan = jumlahKesalahan,
            kesalahan = kesalahanData
        });
    }

    [HttpGet("{babId}/image/{image}")]
    public async Task<IActionResult> GetBabImage(uint babId, string image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return BadRequest(new { message = "Nama image tidak boleh kosong" });

        var requestedName = image.Trim();
        var safeImageName = Path.GetFileName(requestedName);
        if (!string.Equals(safeImageName, requestedName, StringComparison.Ordinal))
            return BadRequest(new { message = "Path image tidak valid" });

        if (safeImageName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return BadRequest(new { message = "Nama image tidak valid" });

        var extension = Path.GetExtension(safeImageName);
        if (!string.IsNullOrWhiteSpace(extension) && !AllowedImageExtensions.Contains(extension))
            return BadRequest(new { message = "Ekstensi image tidak didukung" });

        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var babInfo = await _db.Babs
            .AsNoTracking()
            .Where(b => b.BabId == babId)
            .Join(
                _db.Bukus.AsNoTracking(),
                bab => bab.BukuId,
                buku => (uint)buku.BukuId,
                (bab, buku) => new
                {
                    bab.BabId,
                    bab.BukuId,
                    bab.BabOrder,
                    bab.BabImagesPath,
                    buku.MhsNrp
                })
            .FirstOrDefaultAsync();

        if (babInfo == null)
            return NotFound(new { message = "Bab tidak ditemukan" });

        if (role != "admin" && !string.Equals(babInfo.MhsNrp, currentNrp, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);

        var candidateDirs = new List<string>();
        var configuredImagesDir = ResolveConfiguredImagesDirectory(storagePath, fullStoragePath, babInfo.BabImagesPath);
        if (!string.IsNullOrWhiteSpace(configuredImagesDir))
            candidateDirs.Add(configuredImagesDir);

        var baseImagesDir = Path.GetFullPath(Path.Combine(
            storagePath,
            "buku",
            babInfo.MhsNrp!,
            babInfo.BukuId.ToString(),
            "images"));
        if (babInfo.BabOrder.HasValue)
        {
            var byBabOrder = Path.GetFullPath(Path.Combine(baseImagesDir, babInfo.BabOrder.Value.ToString()));
            candidateDirs.Add(byBabOrder);
        }
        candidateDirs.Add(baseImagesDir);

        var safeCandidateDirs = new List<string>();
        foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!dir.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
                continue;

            safeCandidateDirs.Add(dir);
        }

        if (safeCandidateDirs.Count == 0)
            return BadRequest(new { message = "Path image tidak valid" });

        var filePath = ResolveBabImageFile(safeCandidateDirs, safeImageName);
        if (filePath == null)
            return NotFound(new { message = $"Image '{safeImageName}' tidak ditemukan" });

        if (!filePath.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Path image tidak valid" });

        return PhysicalFile(filePath, GetImageContentType(filePath));
    }

    private static int NormalizeSortPage(int halamanKe)
        => halamanKe <= 0 ? int.MaxValue : halamanKe;

    private static string? ResolveConfiguredImagesDirectory(string storagePath, string fullStoragePath, string? babImagesPath)
    {
        if (string.IsNullOrWhiteSpace(babImagesPath))
            return null;

        var rawPath = babImagesPath.Trim();
        var resolved = Path.IsPathRooted(rawPath)
            ? Path.GetFullPath(rawPath)
            : Path.GetFullPath(Path.Combine(storagePath, rawPath));

        if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return null;

        var extension = Path.GetExtension(resolved);
        if (!string.IsNullOrWhiteSpace(extension) && AllowedImageExtensions.Contains(extension))
        {
            var dir = Path.GetDirectoryName(resolved);
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            resolved = Path.GetFullPath(dir);
            if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return resolved;
    }

    private static string? ResolveBabImageFile(IEnumerable<string> candidateDirs, string imageName)
    {
        var extension = Path.GetExtension(imageName);
        var stem = Path.GetFileNameWithoutExtension(imageName);
        var hasExtension = !string.IsNullOrWhiteSpace(extension);

        foreach (var dir in candidateDirs.Where(Directory.Exists))
        {
            if (hasExtension)
            {
                var exact = Path.Combine(dir, imageName);
                if (System.IO.File.Exists(exact))
                    return exact;

                if (!string.IsNullOrWhiteSpace(stem))
                {
                    foreach (var ext in PreferredImageExtensions.Where(ext => !ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                    {
                        var fallback = Path.Combine(dir, $"{stem}{ext}");
                        if (System.IO.File.Exists(fallback))
                            return fallback;
                    }
                }

                continue;
            }

            foreach (var ext in PreferredImageExtensions)
            {
                var candidate = Path.Combine(dir, $"{imageName}{ext}");
                if (System.IO.File.Exists(candidate))
                    return candidate;
            }

            var direct = Path.Combine(dir, imageName);
            if (System.IO.File.Exists(direct))
                return direct;
        }

        return null;
    }

    private static string GetImageContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
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
            // Ignore parsing errors.
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

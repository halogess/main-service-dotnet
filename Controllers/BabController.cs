using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BabController : ControllerBase
{
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
                b.BabImagesPath,
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
            .Where(x => x.HalamanKe > 0)
            .OrderBy(x => NormalizeSortPage(x.HalamanKe))
            .ThenBy(x => x.YTerkecil)
            .ThenBy(x => x.Kesalahan.KesalahanId)
            .ToList();

        var kesalahanData = orderedKesalahan
            .GroupBy(x => x.HalamanKe)
            .OrderBy(g => NormalizeSortPage(g.Key))
            .Select(g => new
            {
                halaman_ke = (int?)g.Key,
                elemen = g.Select(x => new
                {
                    kesalahan_id = x.Kesalahan.KesalahanId,
                    kategori = x.Kesalahan.KesalahanKategori,
                    lokasi = x.Kesalahan.KesalahanLokasi
                }).ToList()
            })
            .ToList();

        var visibleKesalahanIds = orderedKesalahan
            .Select(x => x.Kesalahan.KesalahanId)
            .Distinct()
            .ToList();
        var jumlahKesalahan = visibleKesalahanIds.Count == 0
            ? 0
            : await _db.KesalahanDetails
                .AsNoTracking()
                .Where(detail => visibleKesalahanIds.Contains(detail.KesalahanId))
                .CountAsync();

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        var safeImageDirs = BuildBabImageDirectories(
            storagePath,
            fullStoragePath,
            bab.BabImagesPath,
            bukuOwner.MhsNrp,
            bab.BukuId,
            bab.BabOrder);
        var fallbackPages = orderedKesalahan
            .Select(x => x.HalamanKe)
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToList();
        var availablePages = PreviewImageHelper.EnumerateAvailablePages(safeImageDirs);
        if (availablePages.Count == 0 && fallbackPages.Count > 0)
            availablePages = fallbackPages;

        return Ok(new
        {
            id = bab.BabId,
            filename = bab.BabFilename,
            status = bukuOwner.BukuStatus,
            skor = bab.BabSkor,
            skor_minimal = bab.BabSkorMinimal,
            jumlah_kesalahan = jumlahKesalahan,
            total_halaman = availablePages.Count,
            available_pages = availablePages,
            kesalahan = kesalahanData
        });
    }

    [HttpGet("{babId}/pages/{page:int:min(1)}/kesalahan-details")]
    public async Task<IActionResult> GetKesalahanDetailsByBabPage(uint babId, int page)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var bab = await _db.Babs
            .AsNoTracking()
            .Where(b => b.BabId == babId)
            .Select(b => new { b.BabId, b.BukuId })
            .FirstOrDefaultAsync();

        if (bab == null)
            return NotFound(new { message = "Bab tidak ditemukan" });

        var bukuOwner = await _db.Bukus
            .AsNoTracking()
            .Where(b => (uint)b.BukuId == bab.BukuId)
            .Select(b => b.MhsNrp)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(bukuOwner))
            return NotFound(new { message = "Buku induk tidak ditemukan" });

        if (role != "admin" && !string.Equals(bukuOwner, currentNrp, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var items = (await _db.Kesalahans
                .AsNoTracking()
                .Include(k => k.Details)
                .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.bab && k.KesalahanRefId == babId)
                .ToListAsync())
            .Select(k => new
            {
                Kesalahan = k,
                SortY = GetSortYForPage(k.KesalahanLokasi, page)
            })
            .Where(x => x.SortY.HasValue)
            .OrderBy(x => x.SortY!.Value)
            .ThenBy(x => x.Kesalahan.KesalahanId)
            .Select(x => new
            {
                kesalahan_id = x.Kesalahan.KesalahanId,
                kategori = x.Kesalahan.KesalahanKategori,
                details = x.Kesalahan.Details
                    .OrderBy(d => d.KesalahanDetailId)
                    .Select(d => new
                    {
                        id = d.KesalahanDetailId,
                        judul = d.KesalahanDetailJudul,
                        penjelasan = d.KesalahanDetailPenjelasan,
                        steps = d.KesalahanDetailSteps,
                        is_hard_constraint = d.KesalahanIsHardConstraint
                    })
                    .ToList()
            })
            .ToList();

        return Ok(new
        {
            halaman_ke = page,
            items
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
        if (!PreviewImageHelper.IsAllowedImageExtension(extension))
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
        var safeCandidateDirs = BuildBabImageDirectories(
            storagePath,
            fullStoragePath,
            babInfo.BabImagesPath,
            babInfo.MhsNrp,
            babInfo.BukuId,
            babInfo.BabOrder);

        if (safeCandidateDirs.Count == 0)
            return BadRequest(new { message = "Path image tidak valid" });

        var filePath = PreviewImageHelper.ResolveImageFile(safeCandidateDirs, safeImageName);
        if (filePath == null)
            return NotFound(new { message = $"Image '{safeImageName}' tidak ditemukan" });

        if (!filePath.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Path image tidak valid" });

        return PhysicalFile(filePath, PreviewImageHelper.GetImageContentType(filePath));
    }

    private static int NormalizeSortPage(int halamanKe)
        => halamanKe <= 0 ? int.MaxValue : halamanKe;

    private static double? GetSortYForPage(string? lokasiJson, int requestedPage)
        => ParseSortKeys(lokasiJson)
            .Where(x => x.HalamanKe == requestedPage)
            .Select(x => (double?)x.YTerkecil)
            .FirstOrDefault();

    private static IReadOnlyList<string> BuildBabImageDirectories(
        string storagePath,
        string fullStoragePath,
        string? babImagesPath,
        string nrp,
        uint bukuId,
        byte? babOrder)
    {
        var candidateDirs = new List<string?>();
        var configuredImagesDir = PreviewImageHelper.ResolveConfiguredImagesDirectory(storagePath, fullStoragePath, babImagesPath);
        if (!string.IsNullOrWhiteSpace(configuredImagesDir))
            candidateDirs.Add(configuredImagesDir);

        var baseImagesDir = Path.Combine(storagePath, "buku", nrp, bukuId.ToString(), "images");
        if (babOrder.HasValue)
            candidateDirs.Add(Path.Combine(baseImagesDir, babOrder.Value.ToString()));

        candidateDirs.Add(baseImagesDir);
        return PreviewImageHelper.BuildSafeCandidateDirectories(fullStoragePath, candidateDirs);
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

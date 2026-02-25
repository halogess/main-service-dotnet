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
                b.BabFilename,
                b.BabSkor,
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
            jumlah_kesalahan = jumlahKesalahan,
            kesalahan = kesalahanData
        });
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

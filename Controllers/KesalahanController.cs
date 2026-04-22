using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class KesalahanController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;

    public KesalahanController(KorektorBukuDbContext db)
    {
        _db = db;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetKesalahanStats()
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        List<KesalahanStatSource> sources;
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            sources = await (from detail in _db.KesalahanDetails.AsNoTracking()
                             join kesalahan in _db.Kesalahans.AsNoTracking()
                                 on detail.KesalahanId equals kesalahan.KesalahanId
                             select new KesalahanStatSource
                             {
                                 Kategori = kesalahan.KesalahanKategori,
                                 Judul = detail.KesalahanDetailJudul,
                                 Penjelasan = detail.KesalahanDetailPenjelasan
                             })
                .ToListAsync();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(currentNrp))
                return Unauthorized(new { message = "NRP tidak ditemukan" });

            var dokumenSources = await (from detail in _db.KesalahanDetails.AsNoTracking()
                                        join kesalahan in _db.Kesalahans.AsNoTracking()
                                            on detail.KesalahanId equals kesalahan.KesalahanId
                                        join dokumen in _db.Dokumens.AsNoTracking()
                                            on kesalahan.KesalahanRefId equals (uint)dokumen.DokumenId
                                        where kesalahan.KesalahanRefTipe == KesalahanRefTipe.dokumen &&
                                              dokumen.MhsNrp == currentNrp
                                        select new KesalahanStatSource
                                        {
                                            Kategori = kesalahan.KesalahanKategori,
                                            Judul = detail.KesalahanDetailJudul,
                                            Penjelasan = detail.KesalahanDetailPenjelasan
                                        })
                .ToListAsync();

            var babSources = await (from detail in _db.KesalahanDetails.AsNoTracking()
                                    join kesalahan in _db.Kesalahans.AsNoTracking()
                                        on detail.KesalahanId equals kesalahan.KesalahanId
                                    join bab in _db.Babs.AsNoTracking()
                                        on kesalahan.KesalahanRefId equals bab.BabId
                                    join buku in _db.Bukus.AsNoTracking()
                                        on bab.BukuId equals (uint)buku.BukuId
                                    where kesalahan.KesalahanRefTipe == KesalahanRefTipe.bab &&
                                          buku.MhsNrp == currentNrp
                                    select new KesalahanStatSource
                                    {
                                        Kategori = kesalahan.KesalahanKategori,
                                        Judul = detail.KesalahanDetailJudul,
                                        Penjelasan = detail.KesalahanDetailPenjelasan
                                    })
                .ToListAsync();

            sources = dokumenSources.Concat(babSources).ToList();
        }

        if (sources.Count == 0)
            return Ok(Array.Empty<object>());

        var grouped = sources
            .GroupBy(
                source => string.IsNullOrWhiteSpace(source.Kategori) ? "umum" : source.Kategori.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new KesalahanStatsBucket
            {
                Name = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name)
            .ToList();

        var result = ApplyPercentages(grouped)
            .Select(item => new
            {
                name = item.Name,
                count = item.Count,
                percentage = item.Percentage
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("{id:int:min(1)}")]
    public async Task<IActionResult> GetKesalahanById(int id)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();
        var kesalahan = await _db.Kesalahans
            .Include(k => k.Details)
            .FirstOrDefaultAsync(k => k.KesalahanId == (uint)id);

        if (kesalahan == null)
            return NotFound(new { message = "Kesalahan tidak ditemukan" });

        if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(currentNrp))
                return Unauthorized(new { message = "NRP tidak ditemukan" });

            var isAuthorized = kesalahan.KesalahanRefTipe switch
            {
                KesalahanRefTipe.dokumen => await _db.Dokumens
                    .AsNoTracking()
                    .AnyAsync(d => d.DokumenId == (int)kesalahan.KesalahanRefId && d.MhsNrp == currentNrp),
                KesalahanRefTipe.bab => await (
                    from bab in _db.Babs.AsNoTracking()
                    join buku in _db.Bukus.AsNoTracking() on bab.BukuId equals (uint)buku.BukuId
                    where bab.BabId == kesalahan.KesalahanRefId && buku.MhsNrp == currentNrp
                    select bab.BabId
                ).AnyAsync(),
                _ => false
            };

            if (!isAuthorized)
                return Forbid();
        }

        return Ok(new
        {
            id = kesalahan.KesalahanId,
            kategori = kesalahan.KesalahanKategori,
            lokasi = kesalahan.KesalahanLokasi,
            details = kesalahan.Details.Select(d => new
            {
                id = d.KesalahanDetailId,
                judul = d.KesalahanDetailJudul,
                penjelasan = d.KesalahanDetailPenjelasan,
                steps = d.KesalahanDetailSteps,
                is_hard_constraint = d.KesalahanIsHardConstraint
            }).ToList()
        });
    }

    private static IReadOnlyList<KesalahanStatsResponse> ApplyPercentages(IReadOnlyList<KesalahanStatsBucket> grouped)
    {
        var total = grouped.Sum(item => item.Count);
        if (total <= 0)
            return Array.Empty<KesalahanStatsResponse>();

        var allocations = grouped
            .Select((item, index) => new PercentageAllocation
            {
                Index = index,
                Name = item.Name,
                Count = item.Count,
                RawPercentage = item.Count * 100m / total,
                Percentage = 0
            })
            .ToList();

        foreach (var allocation in allocations)
            allocation.Percentage = (int)Math.Floor(allocation.RawPercentage);

        var remaining = 100 - allocations.Sum(item => item.Percentage);
        foreach (var allocation in allocations
                     .OrderByDescending(item => item.RawPercentage - item.Percentage)
                     .ThenByDescending(item => item.Count)
                     .ThenBy(item => item.Name)
                     .Take(remaining))
        {
            allocation.Percentage++;
        }

        return allocations
            .OrderBy(item => item.Index)
            .Select(item => new KesalahanStatsResponse
            {
                Name = item.Name,
                Count = item.Count,
                Percentage = item.Percentage
            })
            .ToList();
    }

    private sealed class KesalahanStatSource
    {
        public string Kategori { get; init; } = string.Empty;
        public string? Judul { get; init; }
        public string? Penjelasan { get; init; }
    }

    private sealed class KesalahanStatsBucket
    {
        public string Name { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    private sealed class KesalahanStatsResponse
    {
        public string Name { get; init; } = string.Empty;
        public int Count { get; init; }
        public int Percentage { get; init; }
    }

    private sealed class PercentageAllocation
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Count { get; init; }
        public decimal RawPercentage { get; init; }
        public int Percentage { get; set; }
    }
}


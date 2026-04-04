using ValidasiTugasAkhir.MainService.Models;
using _.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MahasiswaController : ControllerBase
    {
        private readonly IMahasiswaService _mahasiswaService;
        private readonly KorektorBukuDbContext _db;
        private readonly SttsDbContext _sttsDb;
        private readonly IExtractionArtifactCleanupService _cleanupService;

        public MahasiswaController(
            IMahasiswaService mahasiswaService,
            KorektorBukuDbContext db,
            SttsDbContext sttsDb,
            IExtractionArtifactCleanupService cleanupService)
        {
            _mahasiswaService = mahasiswaService;
            _db = db;
            _sttsDb = sttsDb;
            _cleanupService = cleanupService;
        }





        [HttpGet("nonaktif/angkatan")]
        public IActionResult GetNonaktifAngkatan()
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var nrpsWithBuku = _db.Bukus.Select(b => b.MhsNrp).Distinct().ToList();
            var angkatanList = _sttsDb.Mahasiswas
                .Where(m => nrpsWithBuku.Contains(m.MhsNrp) && m.MhsStatus != 1)
                .Select(m => m.MhsAngkatan)
                .Distinct()
                .OrderBy(a => a)
                .ToList();

            return Ok(angkatanList);
        }

        [HttpGet("nonaktif/jurusan")]
        public IActionResult GetNonaktifJurusan()
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var nrpsWithBuku = _db.Bukus.Select(b => b.MhsNrp).Distinct().ToList();
            var jurKodes = _sttsDb.Mahasiswas
                .Where(m => nrpsWithBuku.Contains(m.MhsNrp) && m.MhsStatus != 1 && m.JurKode != null)
                .Select(m => m.JurKode)
                .Distinct()
                .ToList();

            var jurusanList = _sttsDb.Jurusans
                .Where(j => jurKodes.Contains(j.JurKode))
                .OrderBy(j => j.JurKode)
                .Select(j => new {
                    kode = j.JurKode,
                    nama = j.JurNama,
                    singkatan = j.JurSingkat
                })
                .ToList();

            return Ok(jurusanList);
        }

        [HttpDelete("nonaktif/buku")]
        public async Task<IActionResult> HapusBukuNonaktif([FromBody] HapusBukuRequest? request)
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            if (request?.mahasiswa == null || request.mahasiswa.Count == 0)
                return BadRequest(new { message = "Daftar mahasiswa yang akan dihapus tidak boleh kosong" });

            var deleted = 0;
            var errors = new List<string>();
            var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
            var fullStoragePath = Path.GetFullPath(storagePath);

            foreach (var mhs in request.mahasiswa)
            {
                if (string.IsNullOrWhiteSpace(mhs.nrp))
                {
                    errors.Add("NRP mahasiswa tidak boleh kosong");
                    continue;
                }

                var requestedBukuIds = mhs.buku_ids
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                if (requestedBukuIds.Count == 0)
                {
                    errors.Add($"{mhs.nrp}: buku_ids tidak boleh kosong");
                    continue;
                }

                try
                {
                    var mahasiswa = await _sttsDb.Mahasiswas
                        .AsNoTracking()
                        .Where(item => item.MhsNrp == mhs.nrp)
                        .Select(item => new
                        {
                            item.MhsNrp,
                            item.MhsStatus,
                            item.MhsLulusTahun
                        })
                        .FirstOrDefaultAsync();

                    if (mahasiswa == null)
                    {
                        errors.Add($"{mhs.nrp}: mahasiswa tidak ditemukan");
                        continue;
                    }

                    if (mahasiswa.MhsStatus == 1 && string.IsNullOrWhiteSpace(mahasiswa.MhsLulusTahun))
                    {
                        errors.Add($"{mhs.nrp}: mahasiswa masih aktif");
                        continue;
                    }

                    await using var transaction = _db.Database.IsRelational()
                        ? await _db.Database.BeginTransactionAsync(HttpContext.RequestAborted)
                        : null;

                    var bukus = await _db.Bukus
                        .Where(b => b.MhsNrp == mhs.nrp && requestedBukuIds.Contains(b.BukuId))
                        .ToListAsync();

                    if (bukus.Count == 0)
                    {
                        errors.Add($"{mhs.nrp}: buku tidak ditemukan");
                        continue;
                    }

                    var resolvedBukuIds = bukus
                        .Select(b => b.BukuId)
                        .Distinct()
                        .ToList();

                    var missingBukuIds = requestedBukuIds
                        .Except(resolvedBukuIds)
                        .ToList();

                    if (missingBukuIds.Count > 0)
                    {
                        errors.Add(
                            $"{mhs.nrp}: buku {string.Join(", ", missingBukuIds)} tidak ditemukan atau bukan milik mahasiswa");
                    }

                    var babs = await _db.Babs
                        .Where(b => resolvedBukuIds.Contains((int)b.BukuId))
                        .ToListAsync();
                    var babIds = babs
                        .Select(b => b.BabId)
                        .Distinct()
                        .ToList();

                    foreach (var babId in babIds)
                        await _cleanupService.ResetAsync("bab", babId, HttpContext.RequestAborted);

                    var kesalahans = babIds.Count == 0
                        ? new List<Kesalahan>()
                        : await _db.Kesalahans
                            .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.bab && babIds.Contains(k.KesalahanRefId))
                            .ToListAsync();

                    var antrians = await _db.Antrians
                        .Where(a => a.AntrianTipe == "buku" &&
                                    ((a.BukuId.HasValue && resolvedBukuIds.Contains((int)a.BukuId.Value)) ||
                                     (a.BabId.HasValue && babIds.Contains(a.BabId.Value))))
                        .ToListAsync();

                    if (kesalahans.Count > 0)
                        _db.Kesalahans.RemoveRange(kesalahans);

                    if (antrians.Count > 0)
                        _db.Antrians.RemoveRange(antrians);

                    _db.Babs.RemoveRange(babs);
                    _db.Bukus.RemoveRange(bukus);
                    await _db.SaveChangesAsync();

                    if (transaction != null)
                        await transaction.CommitAsync(HttpContext.RequestAborted);

                    foreach (var bukuId in resolvedBukuIds)
                    {
                        var bukuDir = BuildSafeBukuDirectory(fullStoragePath, storagePath, mhs.nrp, bukuId);
                        if (string.IsNullOrWhiteSpace(bukuDir))
                        {
                            errors.Add($"{mhs.nrp}: path storage buku {bukuId} tidak valid");
                            continue;
                        }

                        if (Directory.Exists(bukuDir))
                            Directory.Delete(bukuDir, true);
                    }

                    deleted += bukus.Count;
                }
                catch (Exception ex)
                {
                    errors.Add($"{mhs.nrp}: {ex.Message}");
                }
            }

            return Ok(new { message = "Hapus buku selesai", deleted, errors });
        }

        [HttpGet("nonaktif/buku")]
        public IActionResult GetNonaktifBuku([FromQuery] string? angkatan = null, [FromQuery] string? jurusan = null, [FromQuery] string? search = null, [FromQuery] int limit = 10, [FromQuery] int offset = 0)
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var bukuList = _db.Bukus.Where(b => b.BukuStatus != "dibatalkan").ToList();
            var allNrps = bukuList.Select(b => b.MhsNrp).Distinct().ToList();

            var mahasiswaQuery = _sttsDb.Mahasiswas
                .Where(m => allNrps.Contains(m.MhsNrp) && (!m.MhsStatus.HasValue || m.MhsStatus.Value != 1));

            var angkatanFilters = ParseCsvIntValues(angkatan);
            if (angkatanFilters.Count > 0)
                mahasiswaQuery = mahasiswaQuery.Where(m => m.MhsAngkatan.HasValue && angkatanFilters.Contains(m.MhsAngkatan.Value));

            var jurusanFilters = ParseCsvValues(jurusan);
            if (jurusanFilters.Count > 0)
                mahasiswaQuery = mahasiswaQuery.Where(m => m.JurKode != null && jurusanFilters.Contains(m.JurKode));
            
            if (!string.IsNullOrEmpty(search))
                mahasiswaQuery = mahasiswaQuery.Where(m => m.MhsNrp.Contains(search) || (m.MhsNama != null && m.MhsNama.Contains(search)));

            var mahasiswaNonAktif = mahasiswaQuery.Select(m => m.MhsNrp).ToList();
            var filteredBukus = bukuList.Where(b => mahasiswaNonAktif.Contains(b.MhsNrp)).ToList();

            var groupedByNrp = filteredBukus
                .GroupBy(b => b.MhsNrp)
                .OrderByDescending(g => g.Max(b => b.BukuCreatedAt))
                .Skip(offset)
                .Take(limit)
                .ToList();

            var totalCount = filteredBukus.Select(b => b.MhsNrp).Distinct().Count();
            var nrps = groupedByNrp.Select(g => g.Key).ToList();

            var mahasiswaJurusan = (from m in _sttsDb.Mahasiswas
                where nrps.Contains(m.MhsNrp)
                join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode into jGroup
                from j in jGroup.DefaultIfEmpty()
                select new {
                    m.MhsNrp,
                    m.MhsNama,
                    m.MhsStatus,
                    m.MhsAngkatan,
                    m.MhsLulusTahun,
                    JurKode = j != null ? j.JurKode : null,
                    JurNama = j != null ? j.JurNama : "Unknown",
                    JurSingkat = j != null ? j.JurSingkat : "Unknown"
                }).ToDictionary(x => x.MhsNrp);

            var result = groupedByNrp.Select(g => {
                var mhs = mahasiswaJurusan.GetValueOrDefault(g.Key);
                var bukuListData = g.OrderByDescending(b => b.BukuCreatedAt).Select(b => new {
                    id = b.BukuId,
                    judul = b.BukuJudul,
                    tanggal_upload = b.BukuCreatedAt,
                    jumlah_bab = b.BukuJumlahBab,
                    status = b.BukuStatus,
                    skor = b.BukuSkor ?? 0,
                    jumlah_kesalahan = b.BukuJumlahKesalahan ?? 0
                }).ToList();

                return new {
                    nrp = g.Key,
                    nama = mhs?.MhsNama ?? "Unknown",
                    angkatan = mhs?.MhsAngkatan,
                    status_mahasiswa = GetMahasiswaStatusLabel(mhs?.MhsStatus, mhs?.MhsLulusTahun),
                    jurusan = new {
                        kode = mhs?.JurKode,
                        nama = mhs?.JurNama ?? "Unknown",
                        singkatan = mhs?.JurSingkat ?? "Unknown"
                    },
                    total_buku = bukuListData.Count,
                    riwayat_validasi = bukuListData
                };
            }).ToList();

            return Ok(new { data = result, total = totalCount, limit, offset });
        }

        private static string GetMahasiswaStatusLabel(int? status, string? lulusTahun)
        {
            if (status == 9 || !string.IsNullOrEmpty(lulusTahun))
                return "alumni";
            
            return status switch
            {
                0 => "tidak-aktif",
                2 => "mengundurkan-diri",
                3 => "DO",
                4 => "cuti",
                6 => "transfer",
                7 => "tidak-perwalian",
                _ => "tidak-aktif"
            };
        }

        private static HashSet<string> ParseCsvValues(string? rawValue)
        {
            return rawValue?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<int> ParseCsvIntValues(string? rawValue)
        {
            return rawValue?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => int.TryParse(item, out var value) ? (int?)value : null)
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToHashSet()
                ?? new HashSet<int>();
        }

        private static string? BuildSafeBukuDirectory(string fullStoragePath, string storagePath, string nrp, int bukuId)
        {
            var candidate = Path.GetFullPath(Path.Combine(storagePath, "buku", nrp, bukuId.ToString()));
            return candidate.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
    }
}

public class HapusBukuRequest
{
    public List<MahasiswaBuku> mahasiswa { get; set; } = new();
}

public class MahasiswaBuku
{
    public string nrp { get; set; } = null!;
    public List<int> buku_ids { get; set; } = new();
}

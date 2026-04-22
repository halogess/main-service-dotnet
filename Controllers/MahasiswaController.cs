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
        private readonly INonActiveBookHistoryPurgeService _bookHistoryPurgeService;

        public MahasiswaController(
            IMahasiswaService mahasiswaService,
            KorektorBukuDbContext db,
            SttsDbContext sttsDb,
            INonActiveBookHistoryPurgeService bookHistoryPurgeService)
        {
            _mahasiswaService = mahasiswaService;
            _db = db;
            _sttsDb = sttsDb;
            _bookHistoryPurgeService = bookHistoryPurgeService;
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

            var purgeResult = await _bookHistoryPurgeService.PurgeAsync(
                request.mahasiswa
                    .Select(item => new NonActiveBookHistoryPurgeRequestItem
                    {
                        Nrp = item.nrp,
                        BukuIds = item.buku_ids
                    })
                    .ToList(),
                HttpContext.RequestAborted);

            var hasStorageFailure = purgeResult.FailedStorageDirectories.Count > 0;
            var message = hasStorageFailure
                ? "Hapus buku selesai, tetapi ada folder storage yang gagal dihapus."
                : "Hapus buku selesai";

            return Ok(new
            {
                message,
                deleted = purgeResult.Deleted,
                deleted_storage_directories = purgeResult.DeletedStorageDirectories,
                failed_storage_directories = purgeResult.FailedStorageDirectories,
                skipped_books = purgeResult.SkippedBooks.Select(item => new
                {
                    nrp = item.Nrp,
                    buku_id = item.BukuId,
                    reason = item.Reason
                }),
                errors = purgeResult.Errors
            });
        }

        [HttpGet("nonaktif/buku")]
        public async Task<IActionResult> GetNonaktifBuku([FromQuery] string? angkatan = null, [FromQuery] string? jurusan = null, [FromQuery] string? search = null, [FromQuery] int limit = 10, [FromQuery] int offset = 0)
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var bukuList = await _db.Bukus
                .AsNoTracking()
                .ToListAsync();
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

            var mahasiswaNonAktif = await mahasiswaQuery
                .Select(m => m.MhsNrp)
                .ToListAsync();
            var filteredBukus = bukuList.Where(b => mahasiswaNonAktif.Contains(b.MhsNrp)).ToList();

            var groupedByNrp = filteredBukus
                .GroupBy(b => b.MhsNrp)
                .OrderByDescending(g => g.Max(b => b.BukuCreatedAt))
                .Skip(offset)
                .Take(limit)
                .ToList();

            var totalCount = filteredBukus.Select(b => b.MhsNrp).Distinct().Count();
            var nrps = groupedByNrp.Select(g => g.Key).ToList();

            var mahasiswaJurusan = await (from m in _sttsDb.Mahasiswas
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
                }).ToDictionaryAsync(x => x.MhsNrp);

            var groupedBukuIds = groupedByNrp
                .SelectMany(group => group.Select(item => item.BukuId))
                .Distinct()
                .ToList();

            var babMappings = groupedBukuIds.Count == 0
                ? new List<(int BukuId, uint BabId)>()
                : (await _db.Babs
                    .AsNoTracking()
                    .Where(b => groupedBukuIds.Contains((int)b.BukuId))
                    .Select(b => new { BukuId = (int)b.BukuId, b.BabId })
                    .ToListAsync())
                    .Select(item => (item.BukuId, item.BabId))
                    .ToList();

            var babIds = babMappings
                .Select(item => item.BabId)
                .Distinct()
                .ToList();

            var queueRows = groupedBukuIds.Count == 0
                ? new List<Antrian>()
                : await _db.Antrians
                    .AsNoTracking()
                    .Where(a => a.AntrianTipe == "buku" &&
                                ((a.BukuId.HasValue && groupedBukuIds.Contains((int)a.BukuId.Value)) ||
                                 (a.BabId.HasValue && babIds.Contains(a.BabId.Value))))
                    .ToListAsync();

            var babToBukuMap = babMappings.ToDictionary(item => item.BabId, item => item.BukuId);
            var queuesByBukuId = new Dictionary<int, List<Antrian>>();
            foreach (var queue in queueRows)
            {
                int? targetBukuId = queue.BukuId.HasValue
                    ? (int)queue.BukuId.Value
                    : queue.BabId.HasValue && babToBukuMap.TryGetValue(queue.BabId.Value, out var mappedBukuId)
                        ? mappedBukuId
                        : null;

                if (!targetBukuId.HasValue)
                    continue;

                if (!queuesByBukuId.TryGetValue(targetBukuId.Value, out var queueList))
                {
                    queueList = new List<Antrian>();
                    queuesByBukuId[targetBukuId.Value] = queueList;
                }

                queueList.Add(queue);
            }

            var result = groupedByNrp.Select(g => {
                var mhs = mahasiswaJurusan.GetValueOrDefault(g.Key);
                var bukuListData = g.OrderByDescending(b => b.BukuCreatedAt).Select(b => new {
                    eligibility = BukuHistoryDeletionPolicy.Evaluate(
                        b.BukuStatus,
                        queuesByBukuId.GetValueOrDefault(b.BukuId) ?? []),
                    id = b.BukuId,
                    judul = b.BukuJudul,
                    tanggal_upload = b.BukuCreatedAt,
                    jumlah_bab = b.BukuJumlahBab,
                    status = b.BukuStatus,
                    skor = b.BukuSkor ?? 0,
                    jumlah_kesalahan = b.BukuJumlahKesalahan ?? 0
                }).Select(item => new {
                    item.id,
                    item.judul,
                    item.tanggal_upload,
                    item.jumlah_bab,
                    item.status,
                    item.skor,
                    item.jumlah_kesalahan,
                    has_failed_bab = item.eligibility.HasFailedBab,
                    can_delete = item.eligibility.CanDelete,
                    delete_block_reason = item.eligibility.DeleteBlockReason
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

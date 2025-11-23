using ValidasiTugasAkhir.MainService.Models;
using _.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MahasiswaController : ControllerBase
    {
        private readonly IMahasiswaService _mahasiswaService;
        private readonly KorektorBukuDbContext _db;
        private readonly SttsDbContext _sttsDb;

        public MahasiswaController(IMahasiswaService mahasiswaService, KorektorBukuDbContext db, SttsDbContext sttsDb)
        {
            _mahasiswaService = mahasiswaService;
            _db = db;
            _sttsDb = sttsDb;
        }

        // Endpoint: GET /api/mahasiswa
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Mahasiswa>>> GetAll()
        {
            try
            {
                var mahasiswa = await _mahasiswaService.GetMahasiswasAsync();

                return Ok(mahasiswa.Select(m => new 
                {
                    mhs_nrp = m.MhsNrp,
                    mhs_nama = m.MhsNama,
                    mhs_email = m.MhsEmail,
                    mhs_hp = m.MhsHp,
                    mhs_status = m.MhsStatus,
                    jur_kode = m.JurKode,
                    mhs_ipk = m.MhsIpk
                }));
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Terjadi kesalahan internal" });
            }
        }

        [HttpGet("status/{status}")]
        public async Task<IActionResult> GetMahasiswaByStatus(int status)
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var mahasiswaByStatus = await _sttsDb.Mahasiswas
                .Where(m => m.MhsStatus == status)
                .ToListAsync();
            
            var nrps = mahasiswaByStatus.Select(m => m.MhsNrp).ToList();
            var proposals = await _sttsDb.Proposals
                .Where(p => nrps.Contains(p.MhsNrp) && p.ProposalPerpanjangan == 0 && p.ProposalJudulBaru != null)
                .ToListAsync();
            
            var mahasiswaList = mahasiswaByStatus
                .Where(m => proposals.Any(p => p.MhsNrp == m.MhsNrp))
                .Select(m => new {
                    nrp = m.MhsNrp,
                    nama = m.MhsNama,
                    status = m.MhsStatus,
                    jur_kode = m.JurKode,
                    proposal_count = proposals.Count(p => p.MhsNrp == m.MhsNrp),
                    latest_judul = proposals.Where(p => p.MhsNrp == m.MhsNrp).OrderByDescending(p => p.ProposalTglDoc).First().ProposalJudulBaru
                })
                .Take(50)
                .ToList();

            return Ok(new { status, count = mahasiswaList.Count, data = mahasiswaList });
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

        [HttpGet("nonaktif/status")]
        public IActionResult GetNonaktifStatus()
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var nrpsWithBuku = _db.Bukus.Select(b => b.MhsNrp).Distinct().ToList();
            var allStatus = _sttsDb.Mahasiswas
                .Where(m => nrpsWithBuku.Contains(m.MhsNrp))
                .Select(m => new { m.MhsStatus, m.MhsLulusTahun })
                .ToList();
            
            var distinctStatus = allStatus
                .Where(m => m.MhsStatus != 1)
                .Select(m => !m.MhsStatus.HasValue && !string.IsNullOrEmpty(m.MhsLulusTahun) ? (int)9 : (int)(m.MhsStatus ?? 0))
                .Distinct()
                .OrderBy(s => s)
                .Select(status => new {
                    value = status,
                    label = GetStatusLabel(status)
                }).ToList();

            return Ok(distinctStatus);
        }

        private static string GetStatusLabel(int status) => status switch
        {
            0 => "tidak-aktif",
            2 => "mengundurkan-diri",
            3 => "DO",
            4 => "cuti",
            6 => "transfer",
            7 => "tidak-perwalian",
            9 => "alumni",
            _ => "unknown"
        };

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
        public async Task<IActionResult> HapusBukuNonaktif([FromBody] HapusBukuRequest request)
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var deleted = 0;
            var errors = new List<string>();
            var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";

            foreach (var mhs in request.mahasiswa)
            {
                try
                {
                    var bukus = await _db.Bukus.Where(b => mhs.buku_ids.Contains(b.BukuId)).ToListAsync();
                    var babs = await _db.Babs.Where(b => mhs.buku_ids.Contains((int)b.BukuId)).ToListAsync();

                    _db.Babs.RemoveRange(babs);
                    _db.Bukus.RemoveRange(bukus);
                    await _db.SaveChangesAsync();

                    foreach (var bukuId in mhs.buku_ids)
                    {
                        var bukuDir = Path.Combine(storagePath, "buku", mhs.nrp, bukuId.ToString());
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
        public IActionResult GetNonaktifBuku([FromQuery] int? status = null, [FromQuery] string? angkatan = null, [FromQuery] string? jurusan = null, [FromQuery] string? search = null, [FromQuery] int limit = 10, [FromQuery] int offset = 0)
        {
            if (HttpContext.Items["Role"]?.ToString() != "admin")
                return Forbid();

            var bukuList = _db.Bukus.Where(b => b.BukuStatus != "dibatalkan").ToList();
            var allNrps = bukuList.Select(b => b.MhsNrp).Distinct().ToList();

            var mahasiswaQuery = _sttsDb.Mahasiswas
                .Where(m => allNrps.Contains(m.MhsNrp) && (!m.MhsStatus.HasValue || m.MhsStatus.Value != 1));

            if (status.HasValue)
                mahasiswaQuery = mahasiswaQuery.Where(m => m.MhsStatus == status.Value);
            
            if (!string.IsNullOrEmpty(angkatan))
                mahasiswaQuery = mahasiswaQuery.Where(m => m.MhsAngkatan.ToString() == angkatan);
            
            if (!string.IsNullOrEmpty(jurusan))
                mahasiswaQuery = mahasiswaQuery.Where(m => m.JurKode == jurusan);
            
            if (!string.IsNullOrEmpty(search))
                mahasiswaQuery = mahasiswaQuery.Where(m => m.MhsNrp.Contains(search) || m.MhsNama.Contains(search));

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

                var mhsData = mhs != null ? mhs : null;
                return new {
                    nrp = g.Key,
                    nama = mhsData?.MhsNama ?? "Unknown",
                    angkatan = mhsData?.MhsAngkatan,
                    status_mahasiswa = GetMahasiswaStatusLabel(mhsData?.MhsStatus, mhsData?.MhsLulusTahun),
                    jurusan = new {
                        kode = mhsData?.JurKode,
                        nama = mhsData?.JurNama ?? "Unknown",
                        singkatan = mhsData?.JurSingkat ?? "Unknown"
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
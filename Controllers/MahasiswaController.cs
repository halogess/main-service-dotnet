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
            var role = HttpContext.Items["Role"]?.ToString();
            if (role != "admin")
                return Forbid();

            // Ambil mahasiswa dengan status tertentu
            var mahasiswaByStatus = await _sttsDb.Mahasiswas
                .Where(m => m.MhsStatus == status)
                .ToListAsync();
            
            Console.WriteLine($"[DEBUG] Mahasiswa with status {status}: {mahasiswaByStatus.Count}, NRPs: {string.Join(", ", mahasiswaByStatus.Select(m => $"{m.MhsNrp}={m.MhsStatus}"))}");
            
            // Ambil proposal mereka
            var nrps = mahasiswaByStatus.Select(m => m.MhsNrp).ToList();
            var proposals = await _sttsDb.Proposals
                .Where(p => nrps.Contains(p.MhsNrp) && p.ProposalPerpanjangan == 0 && p.ProposalJudulBaru != null)
                .ToListAsync();
            
            Console.WriteLine($"[DEBUG] Proposals found: {proposals.Count}");
            
            // Join di client-side
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
            
            Console.WriteLine($"[DEBUG] Final result: {mahasiswaList.Count}, Statuses: {string.Join(", ", mahasiswaList.Select(m => m.status).Distinct())}");

            return Ok(new {
                status = status,
                count = mahasiswaList.Count,
                data = mahasiswaList
            });
        }

        [HttpGet("nonaktif/angkatan")]
        public IActionResult GetNonaktifAngkatan()
        {
            var role = HttpContext.Items["Role"]?.ToString();
            if (role != "admin")
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
            var role = HttpContext.Items["Role"]?.ToString();
            if (role != "admin")
                return Forbid();

            var nrpsWithBuku = _db.Bukus.Select(b => b.MhsNrp).Distinct().ToList();
            Console.WriteLine($"[STATUS] Total NRPs with buku: {nrpsWithBuku.Count}");
            
            var allStatus = _sttsDb.Mahasiswas
                .Where(m => nrpsWithBuku.Contains(m.MhsNrp))
                .Select(m => new { m.MhsNrp, m.MhsStatus, m.MhsLulusTahun })
                .ToList();
            
            Console.WriteLine($"[STATUS] All mahasiswa status: {string.Join(", ", allStatus.Select(m => $"{m.MhsNrp}={m.MhsStatus}"))}");
            
            var nullCount = allStatus.Count(m => !m.MhsStatus.HasValue);
            var status1Count = allStatus.Count(m => m.MhsStatus == 1);
            var otherCount = allStatus.Count(m => m.MhsStatus.HasValue && m.MhsStatus.Value != 1);
            
            Console.WriteLine($"[STATUS] Null: {nullCount}, Status 1: {status1Count}, Other: {otherCount}");
            
            var distinctStatus = allStatus
                .Where(m => m.MhsStatus != 1)
                .Select(m => {
                    // Jika status null tapi ada tahun lulus, anggap alumni (9)
                    if (!m.MhsStatus.HasValue && !string.IsNullOrEmpty(m.MhsLulusTahun))
                        return (byte)9;
                    return m.MhsStatus ?? 0; // null tanpa lulus = tidak aktif
                })
                .Distinct()
                .ToList();
            
            Console.WriteLine($"[STATUS] Distinct non-active status: {string.Join(", ", distinctStatus)}");
            
            var statusList = distinctStatus
                .OrderBy(s => s)
                .Select(status => new {
                    value = status,
                    label = status switch {
                        0 => "tidak-aktif",
                        2 => "mengundurkan-diri",
                        3 => "DO",
                        4 => "cuti",
                        6 => "transfer",
                        7 => "tidak-perwalian",
                        9 => "alumni",
                        _ => "unknown"
                    }
                }).ToList();

            return Ok(statusList);
        }

        [HttpGet("nonaktif/jurusan")]
        public IActionResult GetNonaktifJurusan()
        {
            var role = HttpContext.Items["Role"]?.ToString();
            if (role != "admin")
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
            var role = HttpContext.Items["Role"]?.ToString();
            if (role != "admin")
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

                    // Hapus direktori per buku
                    foreach (var bukuId in mhs.buku_ids)
                    {
                        var bukuDir = Path.Combine(storagePath, "buku", mhs.nrp, bukuId.ToString());
                        if (Directory.Exists(bukuDir))
                        {
                            Directory.Delete(bukuDir, true);
                        }
                    }

                    deleted += bukus.Count;
                }
                catch (Exception ex)
                {
                    errors.Add($"{mhs.nrp}: {ex.Message}");
                }
            }

            return Ok(new {
                message = "Hapus buku selesai",
                deleted = deleted,
                errors = errors
            });
        }

        [HttpGet("nonaktif/buku")]
        public IActionResult GetNonaktifBuku([FromQuery] int? status = null, [FromQuery] string? angkatan = null, [FromQuery] string? jurusan = null, [FromQuery] string? search = null, [FromQuery] int limit = 10, [FromQuery] int offset = 0)
        {
            var role = HttpContext.Items["Role"]?.ToString();
            if (role != "admin")
                return Forbid();

            // Ambil buku yang bukan dibatalkan
            var bukuList = _db.Bukus.Where(b => b.BukuStatus != "dibatalkan").ToList();
            var allNrps = bukuList.Select(b => b.MhsNrp).Distinct().ToList();

            // Filter mahasiswa non-aktif
            var mahasiswaQuery = _sttsDb.Mahasiswas
                .Where(m => allNrps.Contains(m.MhsNrp) && (!m.MhsStatus.HasValue || m.MhsStatus.Value != 1));

            // Apply filters
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

            // Group by NRP
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
                var mhs = mahasiswaJurusan.ContainsKey(g.Key) ? mahasiswaJurusan[g.Key] : null;
                var bukuList = g.OrderByDescending(b => b.BukuCreatedAt).Select(b => new {
                    id = b.BukuId,
                    judul = b.BukuJudul,
                    tanggal_upload = b.BukuCreatedAt,
                    jumlah_bab = b.BukuJumlahBab,
                    status = b.BukuStatus,
                    skor = b.BukuSkor ?? 0,
                    jumlah_kesalahan = b.BukuJumlahKesalahan ?? 0
                }).ToList();

                var statusMhs = "tidak-aktif";
                if (mhs != null)
                {
                    if (mhs.MhsStatus == 9 || (!string.IsNullOrEmpty(mhs.MhsLulusTahun)))
                        statusMhs = "alumni";
                    else if (mhs.MhsStatus == 0)
                        statusMhs = "tidak-aktif";
                    else if (mhs.MhsStatus == 2)
                        statusMhs = "mengundurkan-diri";
                    else if (mhs.MhsStatus == 3)
                        statusMhs = "DO";
                    else if (mhs.MhsStatus == 4)
                        statusMhs = "cuti";
                    else if (mhs.MhsStatus == 6)
                        statusMhs = "transfer";
                    else if (mhs.MhsStatus == 7)
                        statusMhs = "tidak-perwalian";
                }

                return new {
                    nrp = g.Key,
                    nama = mhs?.MhsNama ?? "Unknown",
                    angkatan = mhs?.MhsAngkatan,
                    status_mahasiswa = statusMhs,
                    jurusan = new {
                        kode = mhs?.JurKode,
                        nama = mhs?.JurNama ?? "Unknown",
                        singkatan = mhs?.JurSingkat ?? "Unknown"
                    },
                    total_buku = bukuList.Count,
                    riwayat_validasi = bukuList
                };
            }).ToList();

            return Ok(new {
                data = result,
                total = totalCount,
                limit = limit,
                offset = offset
            });
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
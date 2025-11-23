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

        [HttpGet("buku")]
        public IActionResult GetBuku([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0, [FromQuery] string? jurusan = null, [FromQuery] string? search = null)
        {
            var role = HttpContext.Items["Role"]?.ToString();
            
            if (role != "admin")
                return Forbid();
            
            // Filter buku dulu (lebih sedikit data)
            var bukuList = _db.Bukus.AsQueryable();
            
            if (!string.IsNullOrEmpty(status))
            {
                var statuses = status.Split(',').Select(s => s.Trim()).ToList();
                bukuList = bukuList.Where(b => statuses.Contains(b.BukuStatus));
            }
            
            var allBukus = bukuList.ToList();
            var allNrps = allBukus.Select(b => b.MhsNrp).Distinct().ToList();
            
            // Filter mahasiswa yang bukan aktif
            var mahasiswaBukanAktif = _sttsDb.Mahasiswas
                .Where(m => allNrps.Contains(m.MhsNrp) && m.MhsStatus != 1)
                .Select(m => m.MhsNrp)
                .ToList();
            
            allBukus = allBukus.Where(b => mahasiswaBukanAktif.Contains(b.MhsNrp)).ToList();
            
            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                var nrpsBySearch = _sttsDb.Mahasiswas
                    .Where(m => mahasiswaBukanAktif.Contains(m.MhsNrp) && (m.MhsNrp!.Contains(search) || m.MhsNama!.Contains(search)))
                    .Select(m => m.MhsNrp)
                    .ToList();
                allBukus = allBukus.Where(b => nrpsBySearch.Contains(b.MhsNrp)).ToList();
            }
            
            // Apply jurusan filter
            if (!string.IsNullOrEmpty(jurusan))
            {
                var nrpsByJurusan = _sttsDb.Mahasiswas
                    .Where(m => mahasiswaBukanAktif.Contains(m.MhsNrp) && m.JurKode == jurusan)
                    .Select(m => m.MhsNrp)
                    .ToList();
                allBukus = allBukus.Where(b => nrpsByJurusan.Contains(b.MhsNrp)).ToList();
            }
            
            // Group by NRP
            var groupedByNrp = allBukus
                .GroupBy(b => b.MhsNrp)
                .OrderByDescending(g => g.Max(b => b.BukuCreatedAt))
                .Skip(offset)
                .Take(limit)
                .ToList();
            
            var totalCount = allBukus.Select(b => b.MhsNrp).Distinct().Count();
            var nrps = groupedByNrp.Select(g => g.Key).ToList();
            
            var mahasiswaJurusan = (from m in _sttsDb.Mahasiswas
                where nrps.Contains(m.MhsNrp)
                join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode into jGroup
                from j in jGroup.DefaultIfEmpty()
                select new {
                    m.MhsNrp,
                    m.MhsNama,
                    m.MhsStatus,
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
                        statusMhs = "yudisium";
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
                    status_mahasiswa = statusMhs,
                    jurusan = new {
                        kode = mhs?.JurKode,
                        nama = mhs?.JurNama ?? "Unknown",
                        singkatan = mhs?.JurSingkat ?? "Unknown"
                    },
                    total_buku = bukuList.Count,
                    buku = bukuList
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

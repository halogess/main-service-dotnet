using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Services;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BukuController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;
    private readonly IBukuService _bukuService;
    private readonly IWebSocketService _wsService;

    public BukuController(KorektorBukuDbContext db, SttsDbContext sttsDb, IBukuService bukuService, IWebSocketService wsService)
    {
        _db = db;
        _sttsDb = sttsDb;
        _bukuService = bukuService;
        _wsService = wsService;
    }

    [HttpPost]
    public async Task<IActionResult> UploadBuku([FromForm] string judul, [FromForm] List<IFormFile> files)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();

        Console.WriteLine($"[DEBUG] Received judul: {judul}");
        Console.WriteLine($"[DEBUG] Files count: {files?.Count ?? 0}");
        if (files != null)
        {
            foreach (var f in files)
            {
                Console.WriteLine($"[DEBUG] File: {f.FileName}, Size: {f.Length}");
            }
        }

        if (files == null || files.Count == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });

        if (string.IsNullOrWhiteSpace(judul))
            return BadRequest(new { message = "Judul tidak boleh kosong" });

        var hasQueue = _db.Bukus.Any(b => b.MhsNrp == nrp && b.BukuStatus == "dalam_antrian");
        if (hasQueue)
            return BadRequest(new { message = "Masih ada buku dalam antrian" });

        try
        {
            var buku = await _bukuService.UploadBuku(nrp!, judul, files);
            return Ok(new { message = "Buku berhasil diupload", buku_id = buku.BukuId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("can-upload")]
    public IActionResult CanUpload()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var hasQueue = _db.Bukus.Any(b => b.MhsNrp == nrp && b.BukuStatus == "dalam_antrian");
        
        return Ok(new { can_upload = !hasQueue });
    }

    [HttpGet("judul")]
    public IActionResult GetJudul()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        var judul = _sttsDb.Proposals
            .Where(p => p.MhsNrp == nrp && p.ProposalPerpanjangan == 0)
            .OrderByDescending(p => p.ProposalTglDoc)
            .Select(p => p.ProposalJudulBaru)
            .FirstOrDefault();
        
        return Ok(new { judul = judul });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> HapusBuku(int id)
    {
        var role = HttpContext.Items["Role"]?.ToString();
        
        if (role != "admin")
            return Forbid();
        
        var buku = await _db.Bukus.FindAsync(id);
        
        if (buku == null)
            return NotFound(new { message = "Buku tidak ditemukan" });
        
        // Cek apakah mahasiswa bukan aktif (status != 1)
        var mahasiswa = await _sttsDb.Mahasiswas.FirstOrDefaultAsync(m => m.MhsNrp == buku.MhsNrp);
        
        if (mahasiswa == null)
            return NotFound(new { message = "Mahasiswa tidak ditemukan" });
        
        if (mahasiswa.MhsStatus == 1)
            return BadRequest(new { message = "Hanya buku mahasiswa yang bukan aktif yang bisa dihapus" });
        
        // Hapus bab-bab terkait
        var babs = await _db.Babs.Where(b => b.BukuId == (uint)buku.BukuId).ToListAsync();
        _db.Babs.RemoveRange(babs);
        
        // Hapus buku
        _db.Bukus.Remove(buku);
        await _db.SaveChangesAsync();
        
        return Ok(new { message = "Buku berhasil dihapus" });
    }

    [HttpPatch("{id}/batal")]
    public async Task<IActionResult> BatalBuku(int id)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        var buku = await _db.Bukus.FindAsync(id);
        
        if (buku == null)
            return NotFound(new { message = "Buku tidak ditemukan" });
        
        if (buku.MhsNrp != nrp)
            return Forbid();
        
        if (buku.BukuStatus != "dalam_antrian")
            return BadRequest(new { message = "Hanya buku dalam antrian yang bisa dibatalkan" });
        
        buku.BukuStatus = "dibatalkan";
        buku.BukuUpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        
        await _wsService.NotifyBukuCancelled(nrp!, id);
        
        return Ok(new { message = "Buku berhasil dibatalkan" });
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();
        
        var query = _db.Bukus.AsQueryable();
        
        // Jika bukan admin, filter berdasarkan NRP user yang login
        if (role != "admin")
        {
            query = query.Where(b => b.MhsNrp == currentNrp);
        }
        
        var bukus = query.ToList();
        var grouped = bukus.GroupBy(b => b.BukuStatus).ToDictionary(g => g.Key ?? "unknown", g => g.Count());
        
        var result = new {
            total = bukus.Count,
            dalam_antrian = grouped.GetValueOrDefault("dalam_antrian", 0),
            diproses = grouped.GetValueOrDefault("diproses", 0),
            lolos = grouped.GetValueOrDefault("lolos", 0),
            tidak_lolos = grouped.GetValueOrDefault("tidak_lolos", 0),
            dibatalkan = grouped.GetValueOrDefault("dibatalkan", 0)
        };
        
        // Jika admin, tambahkan count per jurusan
        if (role == "admin")
        {
            try
            {
                var nrps = bukus.Select(b => b.MhsNrp).Distinct().ToList();
                
                if (nrps.Count == 0)
                {
                    return Ok(new {
                        result.total,
                        result.dalam_antrian,
                        result.diproses,
                        result.lolos,
                        result.tidak_lolos,
                        result.dibatalkan,
                        per_jurusan = new List<object>()
                    });
                }
                
                var bukuPerJurusan = (from m in _sttsDb.Mahasiswas
                    where nrps.Contains(m.MhsNrp) && m.JurKode != null
                    join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode
                    group m by new { m.JurKode, j.JurSingkat } into g
                    select new {
                        kode = g.Key.JurKode,
                        singkatan = g.Key.JurSingkat,
                        total_mhs = g.Count()
                    }).ToList();
                
                return Ok(new {
                    result.total,
                    result.dalam_antrian,
                    result.diproses,
                    result.lolos,
                    result.tidak_lolos,
                    result.dibatalkan,
                    per_jurusan = bukuPerJurusan
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Stats per jurusan: {ex.Message}");
                return Ok(new {
                    result.total,
                    result.dalam_antrian,
                    result.diproses,
                    result.lolos,
                    result.tidak_lolos,
                    result.dibatalkan,
                    per_jurusan = new List<object>()
                });
            }
        }
        
        return Ok(result);
    }

    [HttpGet]
    public IActionResult GetBuku([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0, [FromQuery] string? nrp = null, [FromQuery] string? jurusan = null, [FromQuery] string? search = null, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();
        
        // Jika admin, join dengan tabel mahasiswa untuk mendapatkan nama dan jurusan
        if (role == "admin")
        {
            var bukuList = _db.Bukus.AsQueryable();
            
            if (!string.IsNullOrEmpty(nrp))
            {
                bukuList = bukuList.Where(b => b.MhsNrp == nrp);
            }
            
            if (!string.IsNullOrEmpty(search))
            {
                var nrpsBySearch = _sttsDb.Mahasiswas.Where(m => m.MhsNrp!.Contains(search) || m.MhsNama!.Contains(search)).Select(m => m.MhsNrp).ToList();
                bukuList = bukuList.Where(b => nrpsBySearch.Contains(b.MhsNrp));
            }
            
            if (!string.IsNullOrEmpty(jurusan))
            {
                var nrpsByJurusan = _sttsDb.Mahasiswas.Where(m => m.JurKode == jurusan).Select(m => m.MhsNrp).ToList();
                bukuList = bukuList.Where(b => nrpsByJurusan.Contains(b.MhsNrp));
            }
            
            if (startDate.HasValue)
            {
                bukuList = bukuList.Where(b => b.BukuCreatedAt >= startDate.Value);
            }
            
            if (endDate.HasValue)
            {
                var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                bukuList = bukuList.Where(b => b.BukuCreatedAt <= endOfDay);
            }
            
            if (!string.IsNullOrEmpty(status))
            {
                var statuses = status.Split(',').Select(s => s.Trim()).ToList();
                bukuList = bukuList.Where(b => statuses.Contains(b.BukuStatus));
            }
            
            bukuList = sort.ToLower() == "asc" 
                ? bukuList.OrderBy(b => b.BukuCreatedAt)
                : bukuList.OrderByDescending(b => b.BukuCreatedAt);
            
            var totalCount = bukuList.Count();
            var bukus = bukuList.Skip(offset).Take(limit).ToList();
            var nrps = bukus.Select(b => b.MhsNrp).Distinct().ToList();
            
            var mahasiswaJurusan = (from m in _sttsDb.Mahasiswas
                where nrps.Contains(m.MhsNrp)
                join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode into jGroup
                from j in jGroup.DefaultIfEmpty()
                select new {
                    m.MhsNrp,
                    m.MhsNama,
                    JurSingkat = j != null ? j.JurSingkat : "Unknown"
                }).ToDictionary(x => x.MhsNrp);
            
            var result = bukus.Select(b => {
                var mhs = mahasiswaJurusan.ContainsKey(b.MhsNrp) ? mahasiswaJurusan[b.MhsNrp] : null;
                
                return new {
                    id = b.BukuId,
                    judul = b.BukuJudul,
                    nrp = b.MhsNrp,
                    nama = mhs?.MhsNama ?? "Unknown",
                    jurusan = mhs?.JurSingkat ?? "Unknown",
                    tanggal_upload = b.BukuCreatedAt,
                    jumlah_bab = b.BukuJumlahBab,
                    status = b.BukuStatus,
                    skor = b.BukuSkor ?? 0,
                    jumlah_kesalahan = b.BukuJumlahKesalahan ?? 0
                };
            }).ToList();

            return Ok(new {
                data = result,
                total = totalCount,
                limit = limit,
                offset = offset
            });
        }

        
        // Untuk mahasiswa, tetap menggunakan query lama
        var query = _db.Bukus.Where(b => b.MhsNrp == currentNrp);
        
        if (!string.IsNullOrEmpty(status))
        {
            var statuses = status.Split(',').Select(s => s.Trim()).ToList();
            query = query.Where(b => statuses.Contains(b.BukuStatus));
        }
        
        query = sort.ToLower() == "asc" 
            ? query.OrderBy(b => b.BukuCreatedAt)
            : query.OrderByDescending(b => b.BukuCreatedAt);
        
        var mahasiswaTotal = query.Count();
        
        var mahasiswaBukus = query.Skip(offset).Take(limit).ToList();
        var bukuIds = mahasiswaBukus.Select(b => b.BukuId).ToList();
        var babs = _db.Babs.Where(b => bukuIds.Contains((int)b.BukuId)).OrderBy(b => b.BabOrder).ToList();
        
        var mahasiswaBukuList = mahasiswaBukus.Select(b => new {
            id = b.BukuId,
            judul = b.BukuJudul,
            nrp = b.MhsNrp,
            tanggal_upload = b.BukuCreatedAt,
            jumlah_bab = b.BukuJumlahBab,
            status = b.BukuStatus,
            skor = b.BukuSkor ?? 0,
            jumlah_kesalahan = b.BukuJumlahKesalahan ?? 0,
            bab = babs.Where(bab => bab.BukuId == b.BukuId).Select(bab => bab.BabFilename).ToList()
        }).ToList();

        return Ok(new {
            data = mahasiswaBukuList,
            total = mahasiswaTotal,
            limit = limit,
            offset = offset
        });
    }
}

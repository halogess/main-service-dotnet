using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Services;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BukuController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly IBukuService _bukuService;

    public BukuController(KorektorBukuDbContext db, IBukuService bukuService)
    {
        _db = db;
        _bukuService = bukuService;
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

    [HttpGet("stats")]
    public IActionResult GetStats([FromQuery] string? nrp = null)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();
        
        var query = _db.Bukus.AsQueryable();
        
        // Jika bukan admin, filter berdasarkan NRP user yang login
        if (role != "admin")
        {
            query = query.Where(b => b.MhsNrp == currentNrp);
        }
        // Jika admin dan ada parameter nrp, filter berdasarkan NRP tertentu
        else if (role == "admin" && !string.IsNullOrEmpty(nrp))
        {
            query = query.Where(b => b.MhsNrp == nrp);
        }
        
        var bukus = query.ToList();
        var grouped = bukus.GroupBy(b => b.BukuStatus).ToDictionary(g => g.Key ?? "unknown", g => g.Count());
        
        // Untuk admin, tambahkan statistik khusus dashboard
        var result = new {
            total = bukus.Count,
            dalam_antrian = grouped.GetValueOrDefault("dalam_antrian", 0),
            diproses = grouped.GetValueOrDefault("diproses", 0),
            selesai_convert = grouped.GetValueOrDefault("selesai_convert", 0),
            lolos = grouped.GetValueOrDefault("lolos", 0),
            tidak_lolos = grouped.GetValueOrDefault("tidak_lolos", 0)
        };
        
        // Jika admin, tambahkan statistik dashboard
        if (role == "admin")
        {
            return Ok(new {
                result.total,
                result.dalam_antrian,
                result.diproses,
                result.selesai_convert,
                result.lolos,
                result.tidak_lolos,
                // Dashboard cards
                menunggu_validasi = result.dalam_antrian + result.diproses + result.selesai_convert
            });
        }
        
        return Ok(result);
    }

    [HttpGet]
    public IActionResult GetBuku([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0, [FromQuery] string? nrp = null)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();
        
        // Jika admin, join dengan tabel mahasiswa untuk mendapatkan nama dan jurusan
        if (role == "admin")
        {
            var adminQuery = from b in _db.Bukus
                           join m in _db.Mahasiswas on b.MhsNrp equals m.MhsNrp into mGroup
                           from m in mGroup.DefaultIfEmpty()
                           select new { Buku = b, Mahasiswa = m };
            
            // Filter berdasarkan NRP jika ada
            if (!string.IsNullOrEmpty(nrp))
            {
                adminQuery = adminQuery.Where(x => x.Buku.MhsNrp == nrp);
            }
            
            // Filter berdasarkan status
            if (!string.IsNullOrEmpty(status))
            {
                var statuses = status.Split(',').Select(s => s.Trim()).ToList();
                adminQuery = adminQuery.Where(x => statuses.Contains(x.Buku.BukuStatus));
            }
            
            // Sorting
            adminQuery = sort.ToLower() == "asc" 
                ? adminQuery.OrderBy(x => x.Buku.BukuCreatedAt)
                : adminQuery.OrderByDescending(x => x.Buku.BukuCreatedAt);
            
            var totalCount = adminQuery.Count();
            
            var bukuList = adminQuery
                .Skip(offset)
                .Take(limit)
                .Select(x => new {
                    id = x.Buku.BukuId,
                    judul = x.Buku.BukuJudul,
                    nrp = x.Buku.MhsNrp,
                    nama = x.Mahasiswa != null ? x.Mahasiswa.MhsNama : "Unknown",
                    jurusan = x.Mahasiswa != null ? x.Mahasiswa.JurKode : "Unknown",
                    tanggal_upload = x.Buku.BukuCreatedAt,
                    jumlah_bab = x.Buku.BukuJumlahBab,
                    status = x.Buku.BukuStatus,
                    skor = x.Buku.BukuSkor ?? 0,
                    jumlah_kesalahan = x.Buku.BukuJumlahKesalahan ?? 0
                })
                .ToList();

            return Ok(new {
                data = bukuList,
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
        
        var mahasiswaBukuList = query
            .Skip(offset)
            .Take(limit)
            .Select(b => new {
                id = b.BukuId,
                judul = b.BukuJudul,
                nrp = b.MhsNrp,
                tanggal_upload = b.BukuCreatedAt,
                jumlah_bab = b.BukuJumlahBab,
                status = b.BukuStatus,
                skor = b.BukuSkor ?? 0,
                jumlah_kesalahan = b.BukuJumlahKesalahan ?? 0
            })
            .ToList();

        return Ok(new {
            data = mahasiswaBukuList,
            total = mahasiswaTotal,
            limit = limit,
            offset = offset
        });
    }
}

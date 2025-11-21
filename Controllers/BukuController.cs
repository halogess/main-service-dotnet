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

    public BukuController(KorektorBukuDbContext db, SttsDbContext sttsDb, IBukuService bukuService)
    {
        _db = db;
        _sttsDb = sttsDb;
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
            var mahasiswas = _sttsDb.Mahasiswas.Where(m => nrps.Contains(m.MhsNrp)).ToDictionary(m => m.MhsNrp);
            var jurKodes = mahasiswas.Values.Where(m => m.JurKode != null).Select(m => m.JurKode!).Distinct().ToList();
            var jurusans = _sttsDb.Jurusans.Where(j => jurKodes.Contains(j.JurKode)).ToDictionary(j => j.JurKode);
            
            var result = bukus.Select(b => {
                var mhs = mahasiswas.ContainsKey(b.MhsNrp) ? mahasiswas[b.MhsNrp] : null;
                var jurKode = mhs?.JurKode;
                var jurNama = jurKode != null && jurusans.ContainsKey(jurKode) ? jurusans[jurKode].JurNama : "Unknown";
                
                return new {
                    id = b.BukuId,
                    judul = b.BukuJudul,
                    nrp = b.MhsNrp,
                    nama = mhs?.MhsNama ?? "Unknown",
                    jurusan = jurNama,
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
        
        var bukuIds = query.Skip(offset).Take(limit).Select(b => b.BukuId).ToList();
        var mahasiswaBukus = _db.Bukus.Where(b => bukuIds.Contains(b.BukuId)).ToList();
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

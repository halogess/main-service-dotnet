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

        if (files == null || files.Count == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });

        if (string.IsNullOrWhiteSpace(judul))
            return BadRequest(new { message = "Judul tidak boleh kosong" });

        if (_db.Bukus.Any(b => b.MhsNrp == nrp && b.BukuStatus == "dalam_antrian"))
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
        return Ok(new { can_upload = !_db.Bukus.Any(b => b.MhsNrp == nrp && b.BukuStatus == "dalam_antrian") });
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
        
        var query = role == "admin" ? _db.Bukus.AsQueryable() : _db.Bukus.Where(b => b.MhsNrp == currentNrp);
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
        
        if (role != "admin")
            return Ok(result);

        try
        {
            var nrps = bukus.Select(b => b.MhsNrp).Distinct().ToList();
            var bukuPerJurusan = nrps.Count == 0 ? new List<object>() : GetBukuPerJurusan(nrps);
            
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
        catch
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
    }

    private List<object> GetBukuPerJurusan(List<string> nrps)
    {
        return (from m in _sttsDb.Mahasiswas
            where nrps.Contains(m.MhsNrp) && m.JurKode != null
            join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode
            group m by new { m.JurKode, j.JurSingkat } into g
            select new {
                kode = g.Key.JurKode,
                singkatan = g.Key.JurSingkat,
                total_mhs = g.Count()
            }).ToList<object>();
    }

    [HttpGet]
    public IActionResult GetBuku([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0, [FromQuery] string? nrp = null, [FromQuery] string? jurusan = null, [FromQuery] string? search = null, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();
        
        return role == "admin" 
            ? GetBukuForAdmin(status, sort, limit, offset, nrp, jurusan, search, startDate, endDate)
            : GetBukuForMahasiswa(currentNrp, status, sort, limit, offset);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBukuById(int id)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var buku = await _db.Bukus.FindAsync(id);
        if (buku == null)
            return NotFound(new { message = "Buku tidak ditemukan" });

        if (role != "admin" && buku.MhsNrp != currentNrp)
            return Forbid();

        var bukuId = (uint)id;
        var babs = await _db.Babs
            .Where(b => b.BukuId == bukuId)
            .OrderBy(b => b.BabOrder)
            .ToListAsync();

        var antrianList = await _db.Antrians
            .Where(a => a.AntrianTipe == "buku" && a.BukuId == bukuId)
            .OrderByDescending(a => a.AntrianCreatedAt)
            .ThenByDescending(a => a.AntrianId)
            .ToListAsync();

        var queueByBabId = antrianList
            .Where(a => a.BabId.HasValue)
            .GroupBy(a => a.BabId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var mahasiswa = await (from m in _sttsDb.Mahasiswas
                               where m.MhsNrp == buku.MhsNrp
                               join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode into jGroup
                               from j in jGroup.DefaultIfEmpty()
                               select new
                               {
                                   m.MhsNama,
                                   JurSingkat = j != null ? j.JurSingkat : "Unknown"
                               })
            .FirstOrDefaultAsync();

        var babData = babs.Select(b =>
        {
            queueByBabId.TryGetValue(b.BabId, out var queue);
            return new
            {
                bab_id = b.BabId,
                bab_order = b.BabOrder,
                filename = b.BabFilename,
                has_pdf = !string.IsNullOrWhiteSpace(b.BabPdfPath),
                extraction_status = queue?.AntrianExtractionStatus,
                labeling_status = queue?.AntrianLabelingStatus,
                validation_status = queue?.AntrianValidationStatus,
                error_message = queue?.AntrianErrorMessage,
                queue_updated_at = queue?.AntrianUpdatedAt
            };
        }).ToList();

        return Ok(new
        {
            id = buku.BukuId,
            judul = buku.BukuJudul,
            nrp = buku.MhsNrp,
            nama = mahasiswa?.MhsNama ?? "Unknown",
            jurusan = mahasiswa?.JurSingkat ?? "Unknown",
            status = buku.BukuStatus,
            skor = buku.BukuSkor ?? 0,
            jumlah_kesalahan = buku.BukuJumlahKesalahan ?? 0,
            jumlah_bab = buku.BukuJumlahBab,
            created_at = buku.BukuCreatedAt,
            updated_at = buku.BukuUpdatedAt,
            progress = new
            {
                extraction = BuildQueueProgress(antrianList.Select(a => a.AntrianExtractionStatus).ToList()),
                labeling = BuildQueueProgress(antrianList.Select(a => a.AntrianLabelingStatus).ToList()),
                validation = BuildQueueProgress(antrianList.Select(a => a.AntrianValidationStatus).ToList())
            },
            bab = babData
        });
    }

    private IActionResult GetBukuForAdmin(string? status, string sort, int limit, int offset, string? nrp, string? jurusan, string? search, DateTime? startDate, DateTime? endDate)
    {
        var bukuList = _db.Bukus.Where(b => b.BukuStatus != "dibatalkan").AsQueryable();
        
        if (!string.IsNullOrEmpty(nrp))
            bukuList = bukuList.Where(b => b.MhsNrp == nrp);
        
        if (!string.IsNullOrEmpty(search))
        {
            var nrpsBySearch = _sttsDb.Mahasiswas
                .Where(m => m.MhsNrp!.Contains(search) || m.MhsNama!.Contains(search))
                .Select(m => m.MhsNrp).ToList();
            bukuList = bukuList.Where(b => nrpsBySearch.Contains(b.MhsNrp));
        }
        
        if (!string.IsNullOrEmpty(jurusan))
        {
            var nrpsByJurusan = _sttsDb.Mahasiswas.Where(m => m.JurKode == jurusan).Select(m => m.MhsNrp).ToList();
            bukuList = bukuList.Where(b => nrpsByJurusan.Contains(b.MhsNrp));
        }
        
        if (startDate.HasValue)
            bukuList = bukuList.Where(b => b.BukuCreatedAt >= startDate.Value);
        
        if (endDate.HasValue)
            bukuList = bukuList.Where(b => b.BukuCreatedAt <= endDate.Value.Date.AddDays(1).AddTicks(-1));
        
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
            var mhs = mahasiswaJurusan.GetValueOrDefault(b.MhsNrp);
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

        return Ok(new { data = result, total = totalCount, limit, offset });
    }

    private IActionResult GetBukuForMahasiswa(string? currentNrp, string? status, string sort, int limit, int offset)
    {
        var query = _db.Bukus.Where(b => b.MhsNrp == currentNrp);
        
        if (!string.IsNullOrEmpty(status))
        {
            var statuses = status.Split(',').Select(s => s.Trim()).ToList();
            query = query.Where(b => statuses.Contains(b.BukuStatus));
        }
        
        query = sort.ToLower() == "asc" 
            ? query.OrderBy(b => b.BukuCreatedAt)
            : query.OrderByDescending(b => b.BukuCreatedAt);
        
        var totalCount = query.Count();
        var bukus = query.Skip(offset).Take(limit).ToList();
        var bukuIds = bukus.Select(b => b.BukuId).ToList();
        var babs = _db.Babs.Where(b => bukuIds.Contains((int)b.BukuId)).OrderBy(b => b.BabOrder).ToList();
        
        var result = bukus.Select(b => new {
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

        return Ok(new { data = result, total = totalCount, limit, offset });
    }

    private static object BuildQueueProgress(List<string?> statuses)
    {
        return new
        {
            total = statuses.Count,
            in_queue = statuses.Count(s => s == "in_queue"),
            processing = statuses.Count(s => s == "processing"),
            completed = statuses.Count(s => s == "completed"),
            failed = statuses.Count(s => s == "failed")
        };
    }
}

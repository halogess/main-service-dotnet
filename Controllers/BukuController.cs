using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Services;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BukuController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;
    private readonly IBukuService _bukuService;
    private readonly IWebSocketService _wsService;
    private readonly IValidationReportService _reportService;
    private readonly IBukuArchiveService _bukuArchiveService;

    public BukuController(
        KorektorBukuDbContext db,
        SttsDbContext sttsDb,
        IBukuService bukuService,
        IWebSocketService wsService,
        IValidationReportService reportService,
        IBukuArchiveService bukuArchiveService)
    {
        _db = db;
        _sttsDb = sttsDb;
        _bukuService = bukuService;
        _wsService = wsService;
        _reportService = reportService;
        _bukuArchiveService = bukuArchiveService;
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
            var notificationEmail = await GetNotificationEmailAsync(nrp!);

            return Ok(new
            {
                message = $"{BuildProcessingNoticeMessage("File BAB", notificationEmail)} Hanya isi buku yang divalidasi.",
                buku_id = buku.BukuId,
                notification_email = notificationEmail
            });
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
        var isFinalStatus =
            string.Equals(buku.BukuStatus, "lolos", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(buku.BukuStatus, "tidak_lolos", StringComparison.OrdinalIgnoreCase);
        var antrianList = await _db.Antrians
            .Where(a => a.AntrianTipe == "buku" && a.BukuId == bukuId)
            .OrderByDescending(a => a.AntrianCreatedAt)
            .ThenByDescending(a => a.AntrianId)
            .ToListAsync();
        var hasFailedBabQueue = antrianList.Any(a =>
            a.BabId.HasValue &&
            (string.Equals(a.AntrianExtractionStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(a.AntrianLabelingStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(a.AntrianValidationStatus, "failed", StringComparison.OrdinalIgnoreCase)));
        var includeBabState = isFinalStatus || hasFailedBabQueue;
        var effectiveStatus = !isFinalStatus && hasFailedBabQueue
            ? "tidak_lolos"
            : buku.BukuStatus;

        var mahasiswa = await _sttsDb.Mahasiswas
            .Where(m => m.MhsNrp == buku.MhsNrp)
            .Select(m => new { m.MhsNama, m.MhsEmail })
            .FirstOrDefaultAsync();

        var docxArchiveReady = HasArchiveFile(
            string.IsNullOrWhiteSpace(buku.BukuDocxZipPath)
                ? _bukuArchiveService.GetDocxArchiveRelativePath(buku.MhsNrp, buku.BukuId)
                : buku.BukuDocxZipPath);
        var pdfArchiveReady = HasArchiveFile(
            string.IsNullOrWhiteSpace(buku.BukuPdfZipPath)
                ? _bukuArchiveService.GetPdfArchiveRelativePath(buku.MhsNrp, buku.BukuId)
                : buku.BukuPdfZipPath);

        var response = new Dictionary<string, object?>
        {
            ["id"] = buku.BukuId,
            ["judul"] = buku.BukuJudul,
            ["nama"] = mahasiswa?.MhsNama ?? "Unknown",
            ["notification_email"] = mahasiswa?.MhsEmail,
            ["status"] = effectiveStatus,
            ["has_failed_bab"] = hasFailedBabQueue,
            ["jumlah_bab"] = buku.BukuJumlahBab,
            ["docx_archive_path"] = buku.BukuDocxZipPath,
            ["pdf_archive_path"] = buku.BukuPdfZipPath,
            ["docx_archive_ready"] = docxArchiveReady,
            ["pdf_archive_ready"] = pdfArchiveReady,
            ["created_at"] = buku.BukuCreatedAt,
            ["updated_at"] = buku.BukuUpdatedAt
        };

        if (includeBabState)
        {
            var babs = await _db.Babs
                .Where(b => b.BukuId == bukuId)
                .OrderBy(b => b.BabOrder)
                .ToListAsync();

            var queueByBabId = antrianList
                .Where(a => a.BabId.HasValue)
                .GroupBy(a => a.BabId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var babData = babs.Select(b =>
            {
                queueByBabId.TryGetValue(b.BabId, out var queue);
                return (object)new
                {
                    bab_id = b.BabId,
                    bab_order = b.BabOrder,
                    bab_skor = b.BabSkor,
                    bab_skor_minimal = b.BabSkorMinimal,
                    bab_jumlah_kesalahan = b.BabJumlahKesalahan,
                    filename = b.BabFilename,
                    has_pdf = !string.IsNullOrWhiteSpace(b.BabPdfPath),
                    extraction_status = queue?.AntrianExtractionStatus,
                    labeling_status = queue?.AntrianLabelingStatus,
                    validation_status = queue?.AntrianValidationStatus,
                    error_message = queue?.AntrianErrorMessage,
                    queue_updated_at = queue?.AntrianUpdatedAt
                };
            }).ToList();

            response["skor"] = buku.BukuSkor;
            response["jumlah_kesalahan"] = buku.BukuJumlahKesalahan;
            response["bab"] = babData;
        }

        return Ok(response);
    }

    [HttpGet("{id:int}/report")]
    public async Task<IActionResult> GetValidationReport(int id, [FromQuery] bool refresh = false)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        try
        {
            var report = await _reportService.GenerateBukuReportAsync(
                id,
                nrp,
                role,
                refresh,
                HttpContext.RequestAborted);

            return File(report.Content, "application/pdf", report.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/docx")]
    public Task<IActionResult> DownloadBukuDocxArchive(int id)
        => DownloadBukuArchive(id, "docx", "File DOCX buku tidak ditemukan", HttpContext.RequestAborted);

    [HttpGet("{id:int}/pdf")]
    public Task<IActionResult> DownloadBukuPdfArchive(int id)
        => DownloadBukuArchive(id, "pdf", "File PDF buku belum tersedia", HttpContext.RequestAborted);

    private async Task<IActionResult> DownloadBukuArchive(
        int id,
        string archiveKind,
        string emptyArchiveMessage,
        CancellationToken cancellationToken)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var buku = await _db.Bukus
            .FirstOrDefaultAsync(b => b.BukuId == id, cancellationToken);

        if (buku == null)
            return NotFound(new { message = "Buku tidak ditemukan" });

        if (role != "admin" && !string.Equals(buku.MhsNrp, currentNrp, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var archiveRelativePath = NormalizeRelativePath(
            archiveKind == "docx"
                ? buku.BukuDocxZipPath
                : buku.BukuPdfZipPath);

        if (string.IsNullOrWhiteSpace(archiveRelativePath))
        {
            archiveRelativePath = archiveKind == "docx"
                ? _bukuArchiveService.GetDocxArchiveRelativePath(buku.MhsNrp, buku.BukuId)
                : _bukuArchiveService.GetPdfArchiveRelativePath(buku.MhsNrp, buku.BukuId);
        }

        if (!_bukuArchiveService.TryResolveStorageFilePath(archiveRelativePath, out var archiveFullPath))
            return BadRequest(new { message = "Path file ZIP tidak valid" });

        if (!System.IO.File.Exists(archiveFullPath) || new FileInfo(archiveFullPath).Length == 0)
        {
            archiveRelativePath = archiveKind == "docx"
                ? await _bukuArchiveService.RefreshDocxArchiveAsync(id, cancellationToken)
                : await _bukuArchiveService.RefreshPdfArchiveAsync(id, cancellationToken);

            if (string.IsNullOrWhiteSpace(archiveRelativePath))
                return NotFound(new { message = emptyArchiveMessage });

            if (!_bukuArchiveService.TryResolveStorageFilePath(archiveRelativePath, out archiveFullPath))
                return BadRequest(new { message = "Path file ZIP tidak valid" });
        }

        var archiveInfo = new FileInfo(archiveFullPath);
        if (!archiveInfo.Exists || archiveInfo.Length == 0)
            return NotFound(new { message = emptyArchiveMessage });

        var downloadFileName = BuildArchiveDownloadFileName(buku.MhsNrp, archiveKind, archiveInfo.LastWriteTime);
        return PhysicalFile(archiveFullPath, "application/zip", downloadFileName);
    }

    private static string NormalizeRelativePath(string? filePath)
        => string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : filePath.Trim().Replace('\\', '/');

    private bool HasArchiveFile(string? archiveRelativePath)
    {
        var normalizedPath = NormalizeRelativePath(archiveRelativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        if (!_bukuArchiveService.TryResolveStorageFilePath(normalizedPath, out var archiveFullPath))
            return false;

        var archiveInfo = new FileInfo(archiveFullPath);
        return archiveInfo.Exists && archiveInfo.Length > 0;
    }

    private static string BuildArchiveDownloadFileName(string? nrp, string archiveKind, DateTime timestamp)
        => $"{SanitizeFileNameSegment(nrp)}_{SanitizeFileNameSegment(archiveKind)}_{timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.zip";

    private static string SanitizeFileNameSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return string.Concat(
            value.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
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

    private async Task<string?> GetNotificationEmailAsync(string nrp)
    {
        if (string.IsNullOrWhiteSpace(nrp))
            return null;

        return await _sttsDb.Mahasiswas
            .AsNoTracking()
            .Where(m => m.MhsNrp == nrp)
            .Select(m => m.MhsEmail)
            .FirstOrDefaultAsync();
    }

    private static string BuildProcessingNoticeMessage(string resourceLabel, string? notificationEmail)
    {
        if (!string.IsNullOrWhiteSpace(notificationEmail))
            return $"{resourceLabel} berhasil disubmit. Notifikasi akan dikirim ke {notificationEmail.Trim()} setelah selesai.";

        return $"{resourceLabel} berhasil disubmit. Notifikasi akan dikirim ke email STTS Anda setelah selesai.";
    }

}

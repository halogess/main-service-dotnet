using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DokumenController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;
    private readonly IDokumenService _dokumenService;
    private readonly IDokumenImportService _dokumenImportService;
    private readonly IDokumenHistoryPurgeService _dokumenHistoryPurgeService;
    private readonly IValidationReportService _reportService;

    public DokumenController(
        KorektorBukuDbContext db,
        SttsDbContext sttsDb,
        IDokumenService dokumenService,
        IDokumenImportService dokumenImportService,
        IDokumenHistoryPurgeService dokumenHistoryPurgeService,
        IValidationReportService reportService)
    {
        _db = db;
        _sttsDb = sttsDb;
        _dokumenService = dokumenService;
        _dokumenImportService = dokumenImportService;
        _dokumenHistoryPurgeService = dokumenHistoryPurgeService;
        _reportService = reportService;
    }

    [HttpPost]
    public async Task<IActionResult> UploadDokumen(IFormFile file)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });
        
        if (_dokumenService.HasDokumenInQueue(nrp!))
            return BadRequest(new { message = "Masih ada dokumen dalam antrian" });
        
        try
        {
            var dokumen = await _dokumenService.UploadDokumen(nrp!, file);
            var notificationEmail = await GetNotificationEmailAsync(nrp!);

            return Ok(new
            {
                message = BuildProcessingNoticeMessage("Dokumen", notificationEmail),
                dokumen_id = dokumen.DokumenId,
                notification_email = notificationEmail
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id}/batal")]
    public async Task<IActionResult> BatalDokumen(int id)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        
        try
        {
            await _dokumenService.BatalDokumen(nrp!, id);
            return Ok(new { message = "Dokumen berhasil dibatalkan" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("can-upload")]
    public IActionResult CanUpload()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        return Ok(new { can_upload = _dokumenService.CanUpload(nrp!) });
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var stats = _dokumenService.GetStats(nrp!);
        
        return Ok(new
        {
            total = stats.Total,
            dibatalkan = stats.Dibatalkan,
            dalam_antrian = stats.DalamAntrian,
            diproses = stats.Diproses,
            lolos = stats.Lolos,
            tidak_lolos = stats.TidakLolos
        });
    }

    [HttpGet("purge-summary")]
    public async Task<IActionResult> GetPurgeSummary()
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        var summary = await _dokumenHistoryPurgeService.GetSummaryAsync(HttpContext.RequestAborted);
        return Ok(new
        {
            total_dokumen = summary.TotalDokumen,
            total_antrian = summary.TotalAntrian,
            total_active_queue = summary.TotalActiveQueue,
            total_section = summary.TotalSection,
            total_part = summary.TotalPart,
            total_elemen = summary.TotalElemen,
            total_visual = summary.TotalVisual,
            total_note = summary.TotalNote,
            total_kesalahan = summary.TotalKesalahan,
            total_kesalahan_detail = summary.TotalKesalahanDetail,
            total_adobe_log = summary.TotalAdobeLog,
            total_llm_log = summary.TotalLlmLog,
            total_paragraph_format = summary.TotalParagraphFormat,
            total_text_format = summary.TotalTextFormat,
            total_table_format = summary.TotalTableFormat,
            total_drawing_format = summary.TotalDrawingFormat,
            total_storage_targets = summary.TotalStorageTargets,
            existing_storage_directories = summary.ExistingStorageDirectories
        });
    }

    [HttpPost("import-preview")]
    public async Task<IActionResult> PreviewLocalDokumenImport([FromBody] DokumenImportRequest? request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        if (string.IsNullOrWhiteSpace(request?.SourcePath))
            return BadRequest(new { message = "Path sumber tidak boleh kosong" });

        try
        {
            var preview = await _dokumenImportService.PreviewAsync(request.SourcePath, HttpContext.RequestAborted);
            return Ok(ToPreviewResponse(preview));
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("import-directory")]
    public async Task<IActionResult> ImportDokumenFromDirectory([FromBody] DokumenImportRequest? request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        if (string.IsNullOrWhiteSpace(request?.SourcePath))
            return BadRequest(new { message = "Path sumber tidak boleh kosong" });

        try
        {
            var result = await _dokumenImportService.ImportAsync(
                request.SourcePath,
                request.SelectedRelativePaths,
                HttpContext.RequestAborted);
            return Ok(ToImportResponse(result));
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("purge-all")]
    public async Task<IActionResult> PurgeAllDokumenHistory([FromBody] PurgeDokumenHistoryRequest? request)
    {
        if (HttpContext.Items["Role"]?.ToString() != "admin")
            return Forbid();

        if (!string.Equals(request?.ConfirmationText?.Trim(), PurgeDokumenHistoryRequest.RequiredConfirmationText, StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                message = $"Ketik '{PurgeDokumenHistoryRequest.RequiredConfirmationText}' untuk melanjutkan purge."
            });
        }

        try
        {
            var result = await _dokumenHistoryPurgeService.PurgeAllAsync(HttpContext.RequestAborted);
            var message = result.FailedStorageDirectories.Count == 0
                ? "Purge riwayat dokumen selesai."
                : "Purge riwayat dokumen selesai, tetapi ada folder storage yang gagal dihapus.";

            return Ok(new
            {
                message,
                deleted_dokumen = result.DeletedDokumen,
                deleted_antrian = result.DeletedAntrian,
                deleted_section = result.DeletedSection,
                deleted_part = result.DeletedPart,
                deleted_elemen = result.DeletedElemen,
                deleted_visual = result.DeletedVisual,
                deleted_note = result.DeletedNote,
                deleted_kesalahan = result.DeletedKesalahan,
                deleted_kesalahan_detail = result.DeletedKesalahanDetail,
                deleted_adobe_log = result.DeletedAdobeLog,
                deleted_llm_log = result.DeletedLlmLog,
                deleted_paragraph_format = result.DeletedParagraphFormat,
                deleted_text_format = result.DeletedTextFormat,
                deleted_table_format = result.DeletedTableFormat,
                deleted_drawing_format = result.DeletedDrawingFormat,
                storage_targets = result.StorageTargets,
                deleted_storage_directories = result.DeletedStorageDirectories,
                failed_storage_directories = result.FailedStorageDirectories
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = $"Purge riwayat dokumen gagal: {ex.Message}"
            });
        }
    }

    [HttpGet]
    public IActionResult GetDokumen([FromQuery] string? status = null, [FromQuery] string sort = "desc", [FromQuery] int limit = 10, [FromQuery] int offset = 0)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var result = _dokumenService.GetDokumenList(nrp!, status, sort, limit, offset);

        return Ok(new
        {
            data = result.Data.Select(d => new
            {
                id = d.Id,
                filename = d.Filename,
                tanggal_upload = d.TanggalUpload,
                ukuran_file = d.UkuranFile,
                status = d.Status,
                jumlah_kesalahan = d.JumlahKesalahan,
                has_failed_queue = d.HasFailedQueue,
                error_message = d.ErrorMessage
            }),
            total = result.Total,
            limit = result.Limit,
            offset = result.Offset
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDokumenById(int id)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(id);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        // Check authorization: admin can access all, user can only access their own
        if (role != "admin" && dokumen.MhsNrp != nrp)
            return Forbid();

        var latestQueue = await GetLatestDokumenQueueAsync(id);

        // Base response
        object? kesalahanData = null;
        List<int> fallbackPages = [];
        int? visibleJumlahKesalahan = null;

        // Include kesalahan list if document has been validated (lolos or tidak_lolos)
        if (dokumen.DokumenStatus == "lolos" || dokumen.DokumenStatus == "tidak_lolos")
        {
            // Fetch all kesalahan for this dokumen (no details - use /api/kesalahan/{id} for details)
            var kesalahanList = await _db.Kesalahans
                .Where(k => k.KesalahanRefTipe == Models.KesalahanRefTipe.dokumen && k.KesalahanRefId == (uint)id)
                .ToListAsync();

            var orderedKesalahan = kesalahanList
                .SelectMany(k => ParseSortKeys(k.KesalahanLokasi).Select(sortKey => new
                {
                    Kesalahan = k,
                    HalamanKe = sortKey.HalamanKe,
                    YTerkecil = sortKey.YTerkecil
                }))
                .Where(x => x.HalamanKe > 0)
                .OrderBy(x => NormalizeSortPage(x.HalamanKe))
                .ThenBy(x => x.YTerkecil)
                .ThenBy(x => x.Kesalahan.KesalahanId)
                .ToList();

            // Convert to response format, ordered by halaman_ke then y terkecil
            // Use -1 to represent null/unknown halaman
            kesalahanData = orderedKesalahan
                .GroupBy(x => x.HalamanKe)
                .OrderBy(g => NormalizeSortPage(g.Key))
                .Select(g => new
                {
                    halaman_ke = (int?)g.Key,
                    elemen = g.Select(x => new
                    {
                        kesalahan_id = x.Kesalahan.KesalahanId,
                        kategori = x.Kesalahan.KesalahanKategori,
                        lokasi = x.Kesalahan.KesalahanLokasi
                    }).ToList() // each Kesalahan represents 1 elemen (details via /api/kesalahan/{id})
                })
                .ToList();

            fallbackPages = orderedKesalahan
                .Select(x => x.HalamanKe)
                .Where(page => page > 0)
                .Distinct()
                .OrderBy(page => page)
                .ToList();

            var visibleKesalahanIds = orderedKesalahan
                .Select(x => x.Kesalahan.KesalahanId)
                .Distinct()
                .ToList();
            visibleJumlahKesalahan = visibleKesalahanIds.Count == 0
                ? 0
                : await _db.KesalahanDetails
                    .Where(detail => visibleKesalahanIds.Contains(detail.KesalahanId))
                    .CountAsync();
        }

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        var safeImageDirs = BuildDokumenImageDirectories(dokumen, storagePath, fullStoragePath);
        var availablePages = PreviewImageHelper.EnumerateAvailablePages(safeImageDirs);
        if (availablePages.Count == 0 && fallbackPages.Count > 0)
            availablePages = fallbackPages;

        var notificationEmail = await GetNotificationEmailAsync(dokumen.MhsNrp);
        var docxReady = IsStorageFileReady(dokumen.DokumenDocxPath);
        var pdfReady = IsStorageFileReady(dokumen.DokumenPdfPath);

        return Ok(new
        {
            id = dokumen.DokumenId,
            filename = dokumen.DokumenFilename,
            filesize_bytes = dokumen.DokumenFilesizeBytes,
            status = dokumen.DokumenStatus,
            skor = dokumen.DokumenSkor,
            skor_minimal = dokumen.DokumenSkorMinimal,
            jumlah_kesalahan = visibleJumlahKesalahan ?? dokumen.DokumenJumlahKesalahan,
            has_failed_queue = QueueCancellationHelper.HasFailedStage(latestQueue),
            extraction_status = latestQueue?.AntrianExtractionStatus,
            labeling_status = latestQueue?.AntrianLabelingStatus,
            validation_status = latestQueue?.AntrianValidationStatus,
            error_message = latestQueue?.AntrianErrorMessage,
            total_halaman = availablePages.Count,
            available_pages = availablePages,
            created_at = dokumen.DokumenCreatedAt,
            updated_at = dokumen.DokumenUpdatedAt,
            docx_ready = docxReady,
            pdf_ready = pdfReady,
            notification_email = notificationEmail,
            kesalahan = kesalahanData
        });
    }

    [HttpGet("{id}/pages/{page:int:min(1)}/kesalahan-details")]
    public async Task<IActionResult> GetKesalahanDetailsByDokumenPage(int id, int page)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DokumenId == id);

        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != nrp)
            return Forbid();

        var items = (await _db.Kesalahans
                .AsNoTracking()
                .Include(k => k.Details)
                .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen && k.KesalahanRefId == (uint)id)
                .ToListAsync())
            .Select(k => new
            {
                Kesalahan = k,
                SortY = GetSortYForPage(k.KesalahanLokasi, page)
            })
            .Where(x => x.SortY.HasValue)
            .OrderBy(x => x.SortY!.Value)
            .ThenBy(x => x.Kesalahan.KesalahanId)
            .Select(x => new
            {
                kesalahan_id = x.Kesalahan.KesalahanId,
                kategori = x.Kesalahan.KesalahanKategori,
                details = x.Kesalahan.Details
                    .OrderBy(d => d.KesalahanDetailId)
                    .Select(d => new
                    {
                        id = d.KesalahanDetailId,
                        judul = d.KesalahanDetailJudul,
                        penjelasan = d.KesalahanDetailPenjelasan,
                        steps = d.KesalahanDetailSteps,
                        is_hard_constraint = d.KesalahanIsHardConstraint
                    })
                    .ToList()
            })
            .ToList();

        return Ok(new
        {
            halaman_ke = page,
            items
        });
    }

    [HttpGet("{id}/report")]
    public async Task<IActionResult> GetValidationReport(int id, [FromQuery] bool refresh = false)
    {
        var nrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        try
        {
            var report = await _reportService.GenerateDokumenReportAsync(
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

    [HttpGet("{id}/docx")]
    public async Task<IActionResult> DownloadDokumenDocx(int id)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(id);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != currentNrp)
            return Forbid();

        if (string.IsNullOrWhiteSpace(dokumen.DokumenDocxPath))
            return NotFound(new { message = "File DOCX dokumen tidak ditemukan" });

        if (!TryResolveStorageFilePath(dokumen.DokumenDocxPath, out var fullPath))
            return BadRequest(new { message = "Path file tidak valid" });

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "File DOCX dokumen tidak ditemukan" });

        var downloadName = BuildDocxDownloadFileName(dokumen.DokumenFilename, dokumen.DokumenId);
        return PhysicalFile(
            fullPath,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            downloadName);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> DownloadDokumenPdf(int id)
    {
        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(id);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != currentNrp)
            return Forbid();

        if (string.IsNullOrWhiteSpace(dokumen.DokumenPdfPath))
            return NotFound(new { message = "File PDF dokumen belum tersedia" });

        if (!TryResolveStorageFilePath(dokumen.DokumenPdfPath, out var fullPath))
            return BadRequest(new { message = "Path file tidak valid" });

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "File PDF dokumen belum tersedia" });

        var downloadName = BuildPdfDownloadFileName(dokumen.DokumenFilename, dokumen.DokumenId);
        return PhysicalFile(fullPath, "application/pdf", downloadName);
    }

    [HttpGet("{dokumenId}/image/{page}")]
    public async Task<IActionResult> GetDokumenImage(int dokumenId, int page)
    {
        if (page <= 0)
            return BadRequest(new { message = "Page harus lebih dari 0" });

        var currentNrp = HttpContext.Items["Nrp"]?.ToString();
        var role = HttpContext.Items["Role"]?.ToString();

        var dokumen = await _db.Dokumens.FindAsync(dokumenId);
        if (dokumen == null)
            return NotFound(new { message = "Dokumen tidak ditemukan" });

        if (role != "admin" && dokumen.MhsNrp != currentNrp)
            return Forbid();

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        var safeImageDirs = BuildDokumenImageDirectories(dokumen, storagePath, fullStoragePath);
        if (safeImageDirs.Count == 0)
            return BadRequest(new { message = "Path image tidak valid" });

        var filePath = PreviewImageHelper.ResolveImageFile(safeImageDirs, page.ToString());

        if (filePath == null)
            return NotFound(new { message = $"Image halaman {page} tidak ditemukan" });

        var contentType = PreviewImageHelper.GetImageContentType(filePath);
        return PhysicalFile(filePath, contentType);
    }

    private static bool TryResolveStorageFilePath(string filePath, out string fullPath)
    {
        fullPath = string.Empty;

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        var candidatePath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(storagePath, filePath);

        var resolved = Path.GetFullPath(candidatePath);
        if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = resolved;
        return true;
    }

    private static bool IsStorageFileReady(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        if (!TryResolveStorageFilePath(relativePath, out var fullPath))
            return false;

        var fileInfo = new FileInfo(fullPath);
        return fileInfo.Exists && fileInfo.Length > 0;
    }

    private static string BuildDocxDownloadFileName(string? dokumenFilename, int dokumenId)
    {
        if (!string.IsNullOrWhiteSpace(dokumenFilename))
        {
            var fileName = Path.GetFileName(dokumenFilename.Trim());
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;
        }

        return $"dokumen_{dokumenId}.docx";
    }

    private static string BuildPdfDownloadFileName(string? dokumenFilename, int dokumenId)
    {
        if (!string.IsNullOrWhiteSpace(dokumenFilename))
        {
            var baseName = Path.GetFileNameWithoutExtension(dokumenFilename.Trim());
            if (!string.IsNullOrWhiteSpace(baseName))
                return baseName + ".pdf";
        }

        return $"dokumen_{dokumenId}.pdf";
    }

    private static object ToPreviewResponse(DokumenImportPreviewResult preview)
    {
        return new
        {
            source_path = preview.SourcePath,
            total_files = preview.TotalFiles,
            docx_files = preview.DocxFiles,
            unsupported_files = preview.UnsupportedFiles,
            importable_files = preview.ImportableFiles,
            duplicate_existing_files = preview.DuplicateExistingFiles,
            duplicate_source_files = preview.DuplicateSourceFiles,
            missing_nrp_files = preview.MissingNrpFiles,
            missing_mahasiswa_files = preview.MissingMahasiswaFiles,
            items = preview.Items.Select(item => new
            {
                relative_path = item.RelativePath,
                source_group = item.SourceGroup,
                filename = item.Filename,
                extension = item.Extension,
                size_bytes = item.SizeBytes,
                nrp = item.Nrp,
                mahasiswa_name = item.MahasiswaName,
                can_import = item.CanImport,
                status = item.Status,
                reason = item.Reason
            })
        };
    }

    private static object ToImportResponse(DokumenImportExecutionResult result)
    {
        return new
        {
            source_path = result.SourcePath,
            total_files = result.TotalFiles,
            importable_files = result.ImportableFiles,
            imported_files = result.ImportedFiles,
            skipped_files = result.SkippedFiles,
            failed_files = result.FailedFiles,
            items = result.Items.Select(item => new
            {
                relative_path = item.RelativePath,
                filename = item.Filename,
                nrp = item.Nrp,
                status = item.Status,
                message = item.Message,
                dokumen_id = item.DokumenId
            })
        };
    }

    private static IReadOnlyList<string> BuildDokumenImageDirectories(Dokumen dokumen, string storagePath, string fullStoragePath)
    {
        var candidateDirs = new List<string?>();
        var configuredImagesDir = PreviewImageHelper.ResolveConfiguredImagesDirectory(storagePath, fullStoragePath, dokumen.DokumenImagesPath);
        if (!string.IsNullOrWhiteSpace(configuredImagesDir))
            candidateDirs.Add(configuredImagesDir);

        candidateDirs.Add(Path.Combine(storagePath, "dokumen", dokumen.MhsNrp, dokumen.DokumenId.ToString(), "images"));
        return PreviewImageHelper.BuildSafeCandidateDirectories(fullStoragePath, candidateDirs);
    }

    private static int NormalizeSortPage(int halamanKe)
        => halamanKe <= 0 ? int.MaxValue : halamanKe;

    private static double? GetSortYForPage(string? lokasiJson, int requestedPage)
        => ParseSortKeys(lokasiJson)
            .Where(x => x.HalamanKe == requestedPage)
            .Select(x => (double?)x.YTerkecil)
            .FirstOrDefault();

    private static List<(int HalamanKe, double YTerkecil)> ParseSortKeys(string? lokasiJson)
    {
        if (string.IsNullOrWhiteSpace(lokasiJson))
            return new List<(int HalamanKe, double YTerkecil)> { (-1, double.MaxValue) };

        var bestByPage = new Dictionary<int, double>();

        try
        {
            using var doc = JsonDocument.Parse(lokasiJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<(int HalamanKe, double YTerkecil)> { (-1, double.MaxValue) };

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var halamanKe = -1;
                if (item.TryGetProperty("halaman_ke", out var halamanEl) &&
                    halamanEl.ValueKind == JsonValueKind.Number &&
                    halamanEl.TryGetInt32(out var parsedPage) &&
                    parsedPage > 0)
                {
                    halamanKe = parsedPage;
                }

                var yTerkecil = double.MaxValue;
                if (item.TryGetProperty("bbox", out var bboxEl) &&
                    bboxEl.ValueKind == JsonValueKind.Object &&
                    bboxEl.TryGetProperty("y0", out var y0El) &&
                    y0El.ValueKind == JsonValueKind.Number &&
                    y0El.TryGetDouble(out var parsedY))
                {
                    yTerkecil = parsedY;
                }

                if (bestByPage.TryGetValue(halamanKe, out var existingY))
                    bestByPage[halamanKe] = Math.Min(existingY, yTerkecil);
                else
                    bestByPage[halamanKe] = yTerkecil;
            }
        }
        catch (JsonException)
        {
            // Ignore parsing errors
        }

        if (bestByPage.Count == 0)
            return new List<(int HalamanKe, double YTerkecil)> { (-1, double.MaxValue) };

        if (bestByPage.Keys.Any(page => page > 0))
            bestByPage.Remove(-1);

        return bestByPage
            .Select(kv => (HalamanKe: kv.Key, YTerkecil: kv.Value))
            .OrderBy(x => NormalizeSortPage(x.HalamanKe))
            .ThenBy(x => x.YTerkecil)
            .ToList();
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

    private Task<Antrian?> GetLatestDokumenQueueAsync(int dokumenId)
        => _db.Antrians
            .AsNoTracking()
            .Where(a => a.DokumenId == (uint)dokumenId)
            .OrderByDescending(a => a.AntrianCreatedAt)
            .ThenByDescending(a => a.AntrianId)
            .FirstOrDefaultAsync();
}

public sealed class PurgeDokumenHistoryRequest
{
    public const string RequiredConfirmationText = "HAPUS SEMUA RIWAYAT DOKUMEN";

    public string? ConfirmationText { get; set; }
}

public sealed class DokumenImportRequest
{
    public string? SourcePath { get; set; }
    public List<string>? SelectedRelativePaths { get; set; }
}

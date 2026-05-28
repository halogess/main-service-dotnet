using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

public class PdfQueueBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PdfQueueBackgroundService> _logger;
    private readonly DateTime _workerStartedAt;

    public PdfQueueBackgroundService(IServiceProvider serviceProvider, ILogger<PdfQueueBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _workerStartedAt = AppClock.Now;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PDF Queue Background Service started");
        await RecoverOrphanedProcessingQueuesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
                var pdfService = scope.ServiceProvider.GetRequiredService<IPdfConversionService>();
                var bukuArchiveService = scope.ServiceProvider.GetRequiredService<IBukuArchiveService>();
                var extractionCleanupService = scope.ServiceProvider.GetRequiredService<IExtractionArtifactCleanupService>();
                var wsService = scope.ServiceProvider.GetRequiredService<IWebSocketService>();

                var queue = await db.Antrians
                    .Where(a => a.AntrianExtractionStatus == "in_queue")
                    .OrderBy(a => a.AntrianCreatedAt)
                    .FirstOrDefaultAsync(stoppingToken);

                if (queue != null)
                {
                    if (await QueueCancellationHelper.TryHandleCancelledResourceAsync(db, queue, stoppingToken))
                    {
                        _logger.LogInformation(
                            "Skipping extraction for antrian ID: {AntrianId} because the resource was cancelled",
                            queue.AntrianId);
                        continue;
                    }

                    string? bukuNrpForArchiveReady = null;
                    var shouldNotifyBukuArchiveReady = false;
                    DokumenFailureNotification? pendingDokumenFailureNotification = null;
                    BukuFailureNotification? pendingBukuFailureNotification = null;

                    string filePath = queue.AntrianTipe == "buku" && queue.BabId.HasValue
                        ? (await db.Babs.FindAsync(queue.BabId.Value))?.BabDocxPath ?? ""
                        : queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue
                            ? (await db.Dokumens.FindAsync((int)queue.DokumenId.Value))?.DokumenDocxPath ?? ""
                            : queue.AntrianTipe == "aturan" && queue.AturanId.HasValue
                                ? (await db.Aturans.FindAsync(new object[] { queue.AturanId.Value }, stoppingToken))?.AturanTemplateFilePath ?? ""
                                : "";

                    _logger.LogInformation("Processing antrian ID: {AntrianId}, File: {FilePath}", queue.AntrianId, filePath);

                    queue.AntrianExtractionStatus = "processing";
                    queue.AntrianUpdatedAt = AppClock.Now;

                    if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                    {
                        var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                        if (dokumen != null &&
                            !string.Equals(dokumen.DokumenStatus, "dibatalkan", StringComparison.OrdinalIgnoreCase))
                        {
                            dokumen.DokumenStatus = "diproses";
                            dokumen.DokumenUpdatedAt = AppClock.Now;
                            _logger.LogInformation("Updated dokumen ID: {DokumenId}, status: diproses", queue.DokumenId);
                            await wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "diproses");
                        }
                    }
                    else if (queue.AntrianTipe == "buku" && queue.BukuId.HasValue)
                    {
                        var buku = await db.Bukus.FindAsync(new object[] { (int)queue.BukuId.Value }, stoppingToken);
                        if (buku != null && buku.BukuStatus == "dalam_antrian")
                        {
                            buku.BukuStatus = "diproses";
                            buku.BukuUpdatedAt = AppClock.Now;
                            _logger.LogInformation("Updated buku ID: {BukuId}, status: diproses", queue.BukuId);
                            await wsService.NotifyBukuStatusChanged(buku.MhsNrp, (int)queue.BukuId.Value, "diproses");
                        }
                    }
                    else if (queue.AntrianTipe == "aturan" && queue.AturanId.HasValue)
                    {
                        var aturan = await db.Aturans.FindAsync(new object[] { queue.AturanId.Value }, stoppingToken);
                        if (aturan != null)
                        {
                            aturan.AturanStatus = AturanStatusValues.Diproses;
                            aturan.AturanUpdatedAt = AppClock.Now;
                        }
                    }

                    await db.SaveChangesAsync(stoppingToken);

                    try
                    {
                        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
                        var fullFilePath = Path.Combine(storagePath, filePath);
                        if (!File.Exists(fullFilePath))
                        {
                            throw new FileNotFoundException($"File tidak ditemukan: {fullFilePath}");
                        }

                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var docxExtraction = scope.ServiceProvider.GetRequiredService<IDocxExtractionService>();
                            await extractionCleanupService.ResetAsync("dokumen", queue.DokumenId.Value, stoppingToken);
                            await docxExtraction.ExtractDocxToDatabase(fullFilePath, (int)queue.DokumenId.Value);
                            _logger.LogInformation("Extracted DOCX elements for dokumen ID: {DokumenId}", queue.DokumenId);
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BabId.HasValue)
                        {
                            var docxExtraction = scope.ServiceProvider.GetRequiredService<IDocxExtractionService>();
                            await extractionCleanupService.ResetAsync("bab", queue.BabId.Value, stoppingToken);
                            await docxExtraction.ExtractDocxToDatabase(fullFilePath, "bab", queue.BabId.Value);
                            _logger.LogInformation("Extracted DOCX elements for bab ID: {BabId}", queue.BabId);
                        }
                        else if (queue.AntrianTipe == "aturan" && queue.AturanId.HasValue)
                        {
                            var docxExtraction = scope.ServiceProvider.GetRequiredService<IDocxExtractionService>();
                            await extractionCleanupService.ResetAsync("aturan", queue.AturanId.Value, stoppingToken);
                            await docxExtraction.ExtractDocxToDatabase(fullFilePath, "aturan", queue.AturanId.Value);
                            _logger.LogInformation("Extracted DOCX elements for aturan ID: {AturanId}", queue.AturanId);
                        }

                        var credential = await db.AdobeCredentials
                            .Where(c => c.AdobeCredentialsStatus == "active" && c.AdobeCredentialsQuotaUsed < c.AdobeCredentialsQuotaLimit)
                            .OrderBy(c => c.AdobeCredentialsQuotaUsed)
                            .FirstOrDefaultAsync(stoppingToken);

                        if (credential == null)
                        {
                            throw new InvalidOperationException("Tidak ada Adobe credentials yang tersedia atau quota habis");
                        }

                        _logger.LogInformation("Using credential ID: {CredentialId}, Quota: {Used}/{Limit}", credential.AdobeCredentialsId, credential.AdobeCredentialsQuotaUsed, credential.AdobeCredentialsQuotaLimit);

                        var pdfBytes = await pdfService.ConvertDocxToPdfWithCredential(
                            fullFilePath,
                            credential.AdobeClientId,
                            credential.AdobeClientSecret,
                            credential.AdobeCredentialsId,
                            queue.AntrianId,
                            stoppingToken);

                        credential.AdobeCredentialsQuotaUsed++;
                        credential.AdobeCredentialsUpdatedAt = AppClock.Now;

                        var fileService = scope.ServiceProvider.GetRequiredService<IFileService>();
                        var pdfPath = fileService.GetPdfPath(filePath);
                        var fullPdfPath = Path.Combine(storagePath, pdfPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPdfPath)!);
                        await File.WriteAllBytesAsync(fullPdfPath, pdfBytes, stoppingToken);

                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                dokumen.DokumenPdfPath = pdfPath;
                                dokumen.DokumenUpdatedAt = AppClock.Now;
                                _logger.LogInformation("Updated dokumen ID: {DokumenId}, pdf_path: {PdfPath}", queue.DokumenId, pdfPath);
                            }
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BabId.HasValue)
                        {
                            var bab = await db.Babs.FindAsync(new object[] { queue.BabId.Value }, stoppingToken);
                            if (bab != null)
                            {
                                bab.BabPdfPath = pdfPath;
                                _logger.LogInformation("Updated bab ID: {BabId}, pdf_path: {PdfPath}", queue.BabId, pdfPath);

                                var allBabsCompleted = !await db.Antrians
                                    .AnyAsync(a => a.BukuId == queue.BukuId && 
                                                   a.AntrianTipe == "buku" && 
                                                   a.AntrianId != queue.AntrianId &&
                                                   a.AntrianExtractionStatus != "completed", stoppingToken);

                                if (allBabsCompleted && queue.BukuId.HasValue)
                                {
                                    var buku = await db.Bukus.FindAsync(new object[] { (int)queue.BukuId.Value }, stoppingToken);
                                    if (buku != null &&
                                        !string.Equals(buku.BukuStatus, "dibatalkan", StringComparison.OrdinalIgnoreCase))
                                    {
                                        bukuNrpForArchiveReady = buku.MhsNrp;
                                        shouldNotifyBukuArchiveReady = true;
                                        buku.BukuStatus = "diproses";
                                        buku.BukuUpdatedAt = AppClock.Now;
                                        _logger.LogInformation("All babs completed for buku ID: {BukuId}", queue.BukuId);
                                    }
                                }
                            }
                        }
                        else if (queue.AntrianTipe == "aturan" && queue.AturanId.HasValue)
                        {
                            var aturan = await db.Aturans.FindAsync(new object[] { queue.AturanId.Value }, stoppingToken);
                            if (aturan != null)
                            {
                                aturan.AturanTemplatePdfPath = pdfPath;
                                aturan.AturanUpdatedAt = AppClock.Now;
                                _logger.LogInformation("Updated aturan ID: {AturanId}, pdf_path: {PdfPath}", queue.AturanId, pdfPath);
                            }
                        }

                        await db.SaveChangesAsync(stoppingToken);

                        if (await QueueCancellationHelper.TryHandleCancelledResourceAsync(db, queue, stoppingToken))
                        {
                            _logger.LogInformation(
                                "Stopping extraction handoff for antrian ID: {AntrianId} because the resource was cancelled mid-process",
                                queue.AntrianId);
                            continue;
                        }

                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                await wsService.NotifyDokumenFileReady(
                                    dokumen.MhsNrp,
                                    (int)queue.DokumenId.Value,
                                    docxReady: !string.IsNullOrWhiteSpace(dokumen.DokumenDocxPath),
                                    pdfReady: !string.IsNullOrWhiteSpace(dokumen.DokumenPdfPath));
                            }
                        }

                        if (queue.AntrianTipe == "buku" && queue.BukuId.HasValue)
                        {
                            await EnsureBukuDocxArchiveReadyAsync(
                                db,
                                bukuArchiveService,
                                (int)queue.BukuId.Value,
                                stoppingToken);

                            var pdfArchivePath = await bukuArchiveService.RefreshPdfArchiveAsync(
                                (int)queue.BukuId.Value,
                                stoppingToken);

                            if (string.IsNullOrWhiteSpace(pdfArchivePath))
                            {
                                throw new InvalidOperationException(
                                    $"ZIP PDF buku {queue.BukuId.Value} tidak berhasil dibentuk");
                            }

                            if (shouldNotifyBukuArchiveReady && !string.IsNullOrWhiteSpace(bukuNrpForArchiveReady))
                            {
                                await wsService.NotifyBukuArchiveReady(
                                    bukuNrpForArchiveReady,
                                    (int)queue.BukuId.Value,
                                    docxReady: true,
                                    pdfReady: true);
                            }
                        }

                        queue.AntrianExtractionStatus = "completed";
                        queue.AntrianLabelingStatus = "in_queue";
                        queue.AntrianUpdatedAt = AppClock.Now;
                        queue.AntrianErrorMessage = null;
                        _logger.LogInformation("Completed antrian ID: {AntrianId}, PDF: {PdfPath}", queue.AntrianId, pdfPath);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Pembatalan diterima saat memproses antrian ID: {AntrianId}", queue.AntrianId);
                        throw;
                    }
                    catch (FileNotFoundException ex)
                    {
                        var failureState = await HandleQueueFailureAsync(
                            db,
                            queue,
                            BuildQueueErrorMessage("File tidak ditemukan: ", ex.Message),
                            stoppingToken);
                        pendingDokumenFailureNotification = failureState.DokumenFailureNotification;
                        pendingBukuFailureNotification = failureState.BukuFailureNotification;
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (HttpRequestException ex)
                    {
                        var failureState = await HandleQueueFailureAsync(
                            db,
                            queue,
                            BuildQueueErrorMessage("Adobe API error: ", BuildExceptionDetail(ex)),
                            stoppingToken);
                        pendingDokumenFailureNotification = failureState.DokumenFailureNotification;
                        pendingBukuFailureNotification = failureState.BukuFailureNotification;
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (TimeoutException ex)
                    {
                        var failureState = await HandleQueueFailureAsync(
                            db,
                            queue,
                            BuildQueueErrorMessage("Konversi gagal: ", ex.Message),
                            stoppingToken);
                        pendingDokumenFailureNotification = failureState.DokumenFailureNotification;
                        pendingBukuFailureNotification = failureState.BukuFailureNotification;
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        var failureState = await HandleQueueFailureAsync(
                            db,
                            queue,
                            BuildQueueErrorMessage("Konversi gagal: ", ex.Message),
                            stoppingToken);
                        pendingDokumenFailureNotification = failureState.DokumenFailureNotification;
                        pendingBukuFailureNotification = failureState.BukuFailureNotification;
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (Exception ex)
                    {
                        var failureState = await HandleQueueFailureAsync(
                            db,
                            queue,
                            BuildQueueErrorMessage(string.Empty, ex.Message),
                            stoppingToken);
                        pendingDokumenFailureNotification = failureState.DokumenFailureNotification;
                        pendingBukuFailureNotification = failureState.BukuFailureNotification;
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }

                    await db.SaveChangesAsync(stoppingToken);

                    if (pendingDokumenFailureNotification is { } dokumenFailureNotification)
                    {
                        await wsService.NotifyDokumenStatusChanged(
                            dokumenFailureNotification.Nrp,
                            dokumenFailureNotification.DokumenId,
                            "tidak_lolos");
                    }

                    if (pendingBukuFailureNotification is { } bukuFailureNotification)
                    {
                        await wsService.NotifyBukuStatusChanged(
                            bukuFailureNotification.Nrp,
                            bukuFailureNotification.BukuId,
                            "tidak_lolos");
                    }
                }

                await Task.Delay(5000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal during hot reload or shutdown
                _logger.LogInformation("PDF Queue service stopping (cancelled)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PDF Queue Background Service");
                
                try
                {
                    await Task.Delay(10000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown during error recovery
                }
            }
        }

        _logger.LogInformation("PDF Queue Background Service stopped");
    }

    private async Task RecoverOrphanedProcessingQueuesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();

        var orphanedQueues = await db.Antrians
            .Where(queue => queue.AntrianExtractionStatus == "processing" &&
                (!queue.AntrianUpdatedAt.HasValue || queue.AntrianUpdatedAt.Value < _workerStartedAt))
            .OrderBy(queue => queue.AntrianUpdatedAt)
            .ToListAsync(stoppingToken);

        if (orphanedQueues.Count == 0)
            return;

        var recoveredAt = AppClock.Now;
        foreach (var queue in orphanedQueues)
        {
            queue.AntrianExtractionStatus = "in_queue";
            queue.AntrianLabelingStatus = null;
            queue.AntrianValidationStatus = null;
            queue.AntrianUpdatedAt = recoveredAt;
            queue.AntrianErrorMessage = "Queue dipulihkan setelah service restart saat ekstraksi masih berjalan.";
        }

        await db.SaveChangesAsync(stoppingToken);

        _logger.LogWarning(
            "Recovered {Count} orphaned extraction queue(s) left in processing before startup: {QueueIds}",
            orphanedQueues.Count,
            string.Join(", ", orphanedQueues.Select(queue => queue.AntrianId)));
    }

    private static async Task EnsureBukuDocxArchiveReadyAsync(
        KorektorBukuDbContext db,
        IBukuArchiveService bukuArchiveService,
        int bukuId,
        CancellationToken cancellationToken)
    {
        var buku = await db.Bukus
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BukuId == bukuId, cancellationToken)
            ?? throw new KeyNotFoundException($"Buku {bukuId} tidak ditemukan");

        var archiveRelativePath = string.IsNullOrWhiteSpace(buku.BukuDocxZipPath)
            ? bukuArchiveService.GetDocxArchiveRelativePath(buku.MhsNrp, buku.BukuId)
            : buku.BukuDocxZipPath;

        if (HasArchiveFile(bukuArchiveService, archiveRelativePath))
            return;

        var refreshedPath = await bukuArchiveService.RefreshDocxArchiveAsync(bukuId, cancellationToken);
        if (string.IsNullOrWhiteSpace(refreshedPath))
        {
            throw new InvalidOperationException(
                $"ZIP DOCX buku {bukuId} tidak berhasil dibentuk");
        }
    }

    private static bool HasArchiveFile(IBukuArchiveService bukuArchiveService, string? archiveRelativePath)
    {
        if (string.IsNullOrWhiteSpace(archiveRelativePath))
            return false;

        if (!bukuArchiveService.TryResolveStorageFilePath(archiveRelativePath, out var archiveFullPath))
            return false;

        return File.Exists(archiveFullPath) && new FileInfo(archiveFullPath).Length > 0;
    }

    private static async Task MarkAturanFailedAsync(
        KorektorBukuDbContext db,
        Antrian queue,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(queue.AntrianTipe, "aturan", StringComparison.OrdinalIgnoreCase) ||
            !queue.AturanId.HasValue)
        {
            return;
        }

        var aturan = await db.Aturans.FindAsync(new object[] { queue.AturanId.Value }, cancellationToken);
        if (aturan == null)
            return;

        aturan.AturanStatus = AturanStatusValues.Gagal;
        aturan.AturanUpdatedAt = AppClock.Now;
    }

    private static async Task<(DokumenFailureNotification? DokumenFailureNotification, BukuFailureNotification? BukuFailureNotification)> HandleQueueFailureAsync(
        KorektorBukuDbContext db,
        Antrian queue,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        queue.AntrianExtractionStatus = "failed";
        queue.AntrianUpdatedAt = AppClock.Now;
        queue.AntrianErrorMessage = errorMessage;

        await MarkAturanFailedAsync(db, queue, cancellationToken);

        var dokumenFailureNotification = await DokumenFailureStatusHelper.TryMarkDokumenTidakLolosAsync(
            db,
            queue.DokumenId,
            cancellationToken);
        var bukuFailureNotification = await BukuFailureStatusHelper.TryMarkBukuTidakLolosAsync(
            db,
            queue.BukuId,
            cancellationToken);

        return (dokumenFailureNotification, bukuFailureNotification);
    }

    private static string BuildQueueErrorMessage(string prefix, string? detail)
    {
        var message = string.IsNullOrWhiteSpace(prefix)
            ? detail ?? string.Empty
            : prefix + (detail ?? string.Empty);

        if (message.Length <= 255)
        {
            return message;
        }

        return message[..252] + "...";
    }

    private static string BuildExceptionDetail(Exception exception)
    {
        var details = new List<string>();
        for (var current = exception; current != null && details.Count < 3; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) &&
                !details.Any(existing => string.Equals(existing, current.Message, StringComparison.OrdinalIgnoreCase)))
            {
                details.Add(current.Message);
            }
        }

        return details.Count == 0 ? exception.Message : string.Join(" | ", details);
    }
}

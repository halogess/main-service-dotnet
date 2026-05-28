using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;
using System.Data;
using System.Text;
using System.Text.Json;

namespace ValidasiTugasAkhir.MainService.Services;

public class ValidationQueueBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ValidationQueueBackgroundService> _logger;
    private readonly int _geminiBatchSize;
    private readonly int _maxBatchRetries;
    private readonly TimeSpan _batchDelay;
    private readonly int _maxParallelBatches;

    private sealed class PendingValidationEmailNotification
    {
        public string MahasiswaNrp { get; }
        public string ResourceType { get; }
        public int ResourceId { get; }
        public string ResourceTitle { get; }
        public bool IsLolos { get; }
        public int ErrorCount { get; }

        public PendingValidationEmailNotification(
            string mahasiswaNrp,
            string resourceType,
            int resourceId,
            string resourceTitle,
            bool isLolos,
            int errorCount)
        {
            MahasiswaNrp = mahasiswaNrp;
            ResourceType = resourceType;
            ResourceId = resourceId;
            ResourceTitle = resourceTitle;
            IsLolos = isLolos;
            ErrorCount = errorCount;
        }
    }

    private sealed class BatchErrorItem
    {
        public ValidationError Error { get; }
        public int OriginalIndex { get; }
        public int RetryCount { get; set; }

        public BatchErrorItem(ValidationError error, int originalIndex)
        {
            Error = error;
            OriginalIndex = originalIndex;
        }
    }

    private sealed class ErrorStorageResult
    {
        public static readonly ErrorStorageResult Empty = new(0, 0, 0, Array.Empty<ValidationError>());

        public ErrorStorageResult(
            int storedDetailCount,
            int enrichedErrorCount,
            int locationFilteredErrorCount,
            IReadOnlyList<ValidationError> storableErrors)
        {
            StoredDetailCount = storedDetailCount;
            EnrichedErrorCount = enrichedErrorCount;
            LocationFilteredErrorCount = locationFilteredErrorCount;
            StorableErrors = storableErrors;
        }

        public int StoredDetailCount { get; }
        public int EnrichedErrorCount { get; }
        public int LocationFilteredErrorCount { get; }
        public IReadOnlyList<ValidationError> StorableErrors { get; }
    }

    public ValidationQueueBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ValidationQueueBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _geminiBatchSize = Math.Max(1, GeminiConfigurationDefaults.BatchSize);
        _maxBatchRetries = Math.Max(0, GeminiConfigurationDefaults.MaxBatchRetries);
        var delaySeconds = Math.Max(0, GeminiConfigurationDefaults.BatchDelaySeconds);
        _batchDelay = TimeSpan.FromSeconds(delaySeconds);
        _maxParallelBatches = Math.Max(1, GeminiConfigurationDefaults.MaxParallelBatches);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Validation Queue Background Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
                var wsService = scope.ServiceProvider.GetRequiredService<IWebSocketService>();
                var validationService = scope.ServiceProvider.GetRequiredService<IValidationService>();
                var reportService = scope.ServiceProvider.GetRequiredService<IValidationReportService>();
                var aturanImportService = scope.ServiceProvider.GetRequiredService<IAturanImportService>();

                var queue = await db.Antrians
                    .Where(a => a.AntrianValidationStatus == "in_queue")
                    .OrderBy(a => a.AntrianCreatedAt)
                    .FirstOrDefaultAsync(stoppingToken);

                if (queue != null)
                {
                    if (await QueueCancellationHelper.TryHandleCancelledResourceAsync(db, queue, stoppingToken))
                    {
                        _logger.LogInformation(
                            "Skipping validation for antrian ID: {AntrianId} because the resource was cancelled",
                            queue.AntrianId);
                        continue;
                    }

                    _logger.LogInformation("Processing validation for antrian ID: {AntrianId}", queue.AntrianId);

                    queue.AntrianValidationStatus = "processing";
                    queue.AntrianUpdatedAt = AppClock.Now;
                    await db.SaveChangesAsync(stoppingToken);
                    PendingValidationEmailNotification? pendingValidationEmail = null;
                    DokumenFailureNotification? pendingDokumenFailureNotification = null;
                    BukuFailureNotification? pendingBukuFailureNotification = null;

                    try
                    {
                        ValidationResult? validationResult = null;

                        if (queue.AntrianTipe == "aturan" && queue.AturanId.HasValue)
                        {
                            await aturanImportService.ImportFromArtifactsAsync(queue.AturanId.Value, stoppingToken);
                        }
                        else if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                // Notify: Validation started
                                await wsService.NotifyValidationProgress(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "started", 0);

                                // Perform validation on dokumen
                                validationResult = await validationService.ValidateDokumenAsync((int)queue.DokumenId.Value, stoppingToken);

                                if (await QueueCancellationHelper.TryHandleCancelledResourceAsync(db, queue, stoppingToken))
                                {
                                    _logger.LogInformation(
                                        "Stopping validation result persistence for antrian ID: {AntrianId} because the dokumen was cancelled",
                                        queue.AntrianId);
                                    continue;
                                }
                                
                                // Notify: Validation complete, processing results
                                await wsService.NotifyValidationProgress(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "processing_results", 70);
                                
                                // Update dokumen with validation results
                                var rawScore = validationResult.Score;
                                var rawErrorCount = validationResult.Errors.Count;
                                var errorStorageResult = ErrorStorageResult.Empty;
                                if (rawErrorCount > 0)
                                {
                                    // Notify: Enriching errors with AI
                                    await wsService.NotifyValidationProgress(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "enriching", 85);
                                    var (sectionRefType, sectionRefId) = ResolveSectionRef(queue);

                                    errorStorageResult = await EnrichAndStoreErrorsAsync(
                                        db,
                                        queue.AntrianId,
                                        queue.DokumenId.Value,
                                        sectionRefType,
                                        sectionRefId,
                                        KesalahanRefTipe.dokumen,
                                        queue.DokumenId.Value,
                                        validationResult.Errors,
                                        stoppingToken);
                                }
                                var effectiveScore = validationResult.GetEffectiveScore(errorStorageResult.StorableErrors);
                                var roundedScore = (int)Math.Round(effectiveScore);
                                var hasEffectiveHardConstraintViolation = validationResult.HasEffectiveHardConstraintViolation(errorStorageResult.StorableErrors);
                                var storedErrorCount = errorStorageResult.StoredDetailCount;
                                dokumen.DokumenSkor = roundedScore;
                                dokumen.DokumenUpdatedAt = AppClock.Now;
                                dokumen.DokumenJumlahKesalahan = storedErrorCount;

                                _logger.LogInformation(
                                    "Validated dokumen ID: {DokumenId}, RawScore: {RawScore}, EffectiveScore: {EffectiveScore}, RawErrors: {RawErrorCount}, EnrichedErrors: {EnrichedErrorCount}, LocationFilteredErrors: {LocationFilteredErrorCount}, StoredErrors: {StoredErrorCount}",
                                    queue.DokumenId,
                                    rawScore,
                                    effectiveScore,
                                    rawErrorCount,
                                    errorStorageResult.EnrichedErrorCount,
                                    errorStorageResult.LocationFilteredErrorCount,
                                    storedErrorCount);
                                if (rawErrorCount > 0 && storedErrorCount == 0)
                                {
                                    _logger.LogWarning(
                                        "No located kesalahan details persisted for dokumen ID: {DokumenId} despite {RawErrorCount} raw validation errors",
                                        queue.DokumenId,
                                        rawErrorCount);
                                }

                                var activeAturan = await db.Aturans
                                    .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
                                    .OrderByDescending(a => a.AturanCreatedAt)
                                    .FirstOrDefaultAsync(stoppingToken);

                                var minimumScoreValue = (int)(activeAturan?.AturanSkorMinimum ?? 80u);
                                dokumen.DokumenSkorMinimal = minimumScoreValue;
                                var isLolos = IsValidationPassed(
                                    roundedScore,
                                    minimumScoreValue,
                                    hasEffectiveHardConstraintViolation);
                                dokumen.DokumenStatus = isLolos ? "lolos" : "tidak_lolos";
                                dokumen.DokumenUpdatedAt = AppClock.Now;

                                if (activeAturan == null)
                                {
                                    _logger.LogWarning(
                                        "No active aturan found when determining validation status for dokumen ID: {DokumenId}. Using default minimum score: {MinimumScore}",
                                        queue.DokumenId,
                                        minimumScoreValue);
                                }
                                else
                                {
                                    _logger.LogInformation(
                                        "Validation status for dokumen ID: {DokumenId} determined by score and hard constraints. Score: {Score}, MinimumScore: {MinimumScore}, HasHardConstraintViolation: {HasHardConstraintViolation}, AturanId: {AturanId}, Status: {Status}",
                                        queue.DokumenId,
                                        roundedScore,
                                        minimumScoreValue,
                                        hasEffectiveHardConstraintViolation,
                                        activeAturan.AturanId,
                                        dokumen.DokumenStatus);
                                }

                                // Generate validation report after status is updated
                                await reportService.GenerateDokumenReportAsync(
                                    (int)queue.DokumenId.Value,
                                    dokumen.MhsNrp,
                                    "admin",
                                    refresh: true,
                                    cancellationToken: stoppingToken);

                                // Notify: Completed
                                await wsService.NotifyValidationProgress(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "completed", 100);
                            }
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BabId.HasValue)
                        {
                            var bab = await db.Babs.FindAsync(new object[] { queue.BabId.Value }, stoppingToken);
                            if (bab != null)
                            {
                                validationResult = await validationService.ValidateBabAsync(queue.BabId.Value, stoppingToken);

                                if (await QueueCancellationHelper.TryHandleCancelledResourceAsync(db, queue, stoppingToken))
                                {
                                    _logger.LogInformation(
                                        "Stopping validation result persistence for antrian ID: {AntrianId} because the buku was cancelled",
                                        queue.AntrianId);
                                    continue;
                                }

                                var rawScore = validationResult.Score;

                                var activeAturan = await db.Aturans
                                    .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
                                    .OrderByDescending(a => a.AturanCreatedAt)
                                    .FirstOrDefaultAsync(stoppingToken);
                                var minimumScoreValue = (int)(activeAturan?.AturanSkorMinimum ?? 80u);
                                bab.BabSkorMinimal = minimumScoreValue;

                                var rawErrorCount = validationResult.Errors.Count;
                                var errorStorageResult = ErrorStorageResult.Empty;
                                if (rawErrorCount > 0)
                                {
                                    var (sectionRefType, sectionRefId) = ResolveSectionRef(queue);
                                    errorStorageResult = await EnrichAndStoreErrorsAsync(
                                        db,
                                        queue.AntrianId,
                                        queue.BabId.Value,
                                        sectionRefType,
                                        sectionRefId,
                                        KesalahanRefTipe.bab,
                                        queue.BabId.Value,
                                        validationResult.Errors,
                                        stoppingToken,
                                        babOrder: bab.BabOrder);
                                }
                                var effectiveScore = validationResult.GetEffectiveScore(errorStorageResult.StorableErrors);
                                var roundedScore = (int)Math.Round(effectiveScore);
                                var hasEffectiveHardConstraintViolation = validationResult.HasEffectiveHardConstraintViolation(errorStorageResult.StorableErrors);
                                var storedErrorCount = errorStorageResult.StoredDetailCount;
                                bab.BabSkor = roundedScore;
                                bab.BabHasHardConstraintViolation = hasEffectiveHardConstraintViolation;
                                bab.BabJumlahKesalahan = storedErrorCount;

                                _logger.LogInformation(
                                    "Validated bab ID: {BabId} (BukuId: {BukuId}), RawScore: {RawScore}, EffectiveScore: {EffectiveScore}, MinimumScoreAtValidation: {MinimumScore}, HasHardConstraintViolation: {HasHardConstraintViolation}, RawErrors: {RawErrorCount}, EnrichedErrors: {EnrichedErrorCount}, LocationFilteredErrors: {LocationFilteredErrorCount}, StoredErrors: {StoredErrorCount}",
                                    queue.BabId,
                                    queue.BukuId,
                                    rawScore,
                                    effectiveScore,
                                    minimumScoreValue,
                                    hasEffectiveHardConstraintViolation,
                                    rawErrorCount,
                                    errorStorageResult.EnrichedErrorCount,
                                    errorStorageResult.LocationFilteredErrorCount,
                                    storedErrorCount);
                                if (rawErrorCount > 0 && storedErrorCount == 0)
                                {
                                    _logger.LogWarning(
                                        "No located kesalahan details persisted for bab ID: {BabId} despite {RawErrorCount} raw validation errors",
                                        queue.BabId,
                                        rawErrorCount);
                                }
                            }
                        }

                        if (await QueueCancellationHelper.TryHandleCancelledResourceAsync(db, queue, stoppingToken))
                        {
                            _logger.LogInformation(
                                "Stopping validation finalization for antrian ID: {AntrianId} because the resource was cancelled",
                                queue.AntrianId);
                            continue;
                        }

                        queue.AntrianValidationStatus = "completed";
                        queue.AntrianUpdatedAt = AppClock.Now;
                        queue.AntrianErrorMessage = null;
                        _logger.LogInformation("Completed validation for antrian ID: {AntrianId}", queue.AntrianId);

                        // Notify via WebSocket
                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                await wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, (int)queue.DokumenId.Value, dokumen.DokumenStatus);
                                pendingValidationEmail = BuildPendingValidationEmailNotification(dokumen);
                            }
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BukuId.HasValue)
                        {
                            var (allBabsValidated, babs) = await AreAllBukuBabsValidationCompletedAsync(
                                db,
                                queue,
                                stoppingToken);

                            if (allBabsValidated)
                            {
                                var buku = await db.Bukus.FindAsync(new object[] { (int)queue.BukuId.Value }, stoppingToken);
                                if (buku != null &&
                                    !string.Equals(buku.BukuStatus, "dibatalkan", StringComparison.OrdinalIgnoreCase))
                                {
                                    var bukuRefId = (uint)queue.BukuId.Value;
                                    var scoredBabScores = babs
                                        .Where(b => b.BabSkor.HasValue)
                                        .Select(b => (decimal)b.BabSkor!.Value)
                                        .ToList();
                                    var allBabsHaveScore = babs.Count > 0 && babs.Count == scoredBabScores.Count;
                                    var averageScore = allBabsHaveScore
                                        ? scoredBabScores.Average()
                                        : 0m;

                                    var activeAturan = await db.Aturans
                                        .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
                                        .OrderByDescending(a => a.AturanCreatedAt)
                                        .FirstOrDefaultAsync(stoppingToken);
                                    var defaultMinimumScore = (int)(activeAturan?.AturanSkorMinimum ?? 80u);
                                    var useFallbackMinimum = babs.Any(b => !b.BabSkorMinimal.HasValue);
                                    var hasAnyHardConstraintViolation = babs.Any(b => b.BabHasHardConstraintViolation);
                                    var isLolos = allBabsHaveScore && babs.All(b => IsBabValidationPassed(b, defaultMinimumScore));

                                    var totalKesalahan = await (
                                        from detail in db.KesalahanDetails
                                        join parent in db.Kesalahans on detail.KesalahanId equals parent.KesalahanId
                                        join babRef in db.Babs on parent.KesalahanRefId equals babRef.BabId
                                        where parent.KesalahanRefTipe == KesalahanRefTipe.bab &&
                                              babRef.BukuId == bukuRefId
                                        select detail.KesalahanDetailId
                                    ).CountAsync(stoppingToken);

                                    var finalStatus = isLolos ? "lolos" : "tidak_lolos";
                                    buku.BukuStatus = finalStatus;
                                    buku.BukuJumlahKesalahan = totalKesalahan;
                                    buku.BukuSkor = (int)Math.Round(averageScore);
                                    buku.BukuUpdatedAt = AppClock.Now;

                                    await reportService.GenerateBukuReportAsync(
                                        (int)queue.BukuId.Value,
                                        buku.MhsNrp,
                                        "admin",
                                        refresh: true,
                                        cancellationToken: stoppingToken);

                                    await wsService.NotifyBukuStatusChanged(buku.MhsNrp, (int)queue.BukuId.Value, finalStatus);
                                    _logger.LogInformation(
                                        "All babs validated for buku ID: {BukuId}. FinalStatus={FinalStatus}, BookScore={BookScore}, DefaultMinimumScore={DefaultMinimumScore}, FallbackMinimumUsed={FallbackMinimumUsed}, HasAnyHardConstraintViolation={HasAnyHardConstraintViolation}, TotalKesalahan={TotalKesalahan}",
                                        queue.BukuId,
                                        finalStatus,
                                        buku.BukuSkor,
                                        defaultMinimumScore,
                                        useFallbackMinimum,
                                        hasAnyHardConstraintViolation,
                                        totalKesalahan);
                                    pendingValidationEmail = BuildPendingValidationEmailNotification(buku);
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        if (await QueueCancellationHelper.TryHandleCancelledResourceAsync(db, queue, stoppingToken))
                        {
                            _logger.LogInformation(
                                "Ignoring validation failure for antrian ID: {AntrianId} because the resource was cancelled",
                                queue.AntrianId);
                            continue;
                        }

                        queue.AntrianValidationStatus = "failed";
                        queue.AntrianUpdatedAt = AppClock.Now;
                        queue.AntrianErrorMessage = ex.Message.Length > 255 ? ex.Message[..252] + "..." : ex.Message;
                        _logger.LogError(ex, "Failed validation for antrian ID: {AntrianId}", queue.AntrianId);

                        if (queue.AntrianTipe == "aturan" && queue.AturanId.HasValue)
                        {
                            var aturan = await db.Aturans.FindAsync(new object[] { queue.AturanId.Value }, stoppingToken);
                            if (aturan != null)
                            {
                                aturan.AturanStatus = AturanStatusValues.Gagal;
                                aturan.AturanUpdatedAt = AppClock.Now;
                            }
                        }

                        // Notify failure via WebSocket
                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            pendingDokumenFailureNotification = await DokumenFailureStatusHelper.TryMarkDokumenTidakLolosAsync(
                                db,
                                queue.DokumenId,
                                stoppingToken);
                        }
                        else if (queue.AntrianTipe == "buku")
                        {
                            pendingBukuFailureNotification = await BukuFailureStatusHelper.TryMarkBukuTidakLolosAsync(
                                db,
                                queue.BukuId,
                                stoppingToken);
                        }
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

                    if (pendingValidationEmail != null &&
                        string.Equals(queue.AntrianValidationStatus, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        var sttsDb = scope.ServiceProvider.GetRequiredService<SttsDbContext>();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        await SendValidationEmailNotificationAsync(
                            sttsDb,
                            emailService,
                            pendingValidationEmail,
                            stoppingToken);
                    }
                }

                await Task.Delay(5000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Validation Queue service stopping (cancelled)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Validation Queue Background Service");
                
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

        _logger.LogInformation("Validation Queue Background Service stopped");
    }

    private PendingValidationEmailNotification BuildPendingValidationEmailNotification(Dokumen dokumen)
    {
        var dokumenTitle = BuildDokumenEmailTitle(dokumen);
        var isLolos = string.Equals(dokumen.DokumenStatus, "lolos", StringComparison.OrdinalIgnoreCase);
        var errorCount = dokumen.DokumenJumlahKesalahan ?? 0;

        return new PendingValidationEmailNotification(
            dokumen.MhsNrp,
            "dokumen",
            dokumen.DokumenId,
            dokumenTitle,
            isLolos,
            errorCount);
    }

    private static bool IsValidationPassed(int score, int minimumScore, bool hasHardConstraintViolation)
        => score >= minimumScore && !hasHardConstraintViolation;

    private static bool IsBabValidationPassed(Bab bab, int defaultMinimumScore)
    {
        if (!bab.BabSkor.HasValue)
            return false;

        var minimum = bab.BabSkorMinimal ?? defaultMinimumScore;
        return IsValidationPassed(
            bab.BabSkor.Value,
            minimum,
            bab.BabHasHardConstraintViolation);
    }

    private PendingValidationEmailNotification BuildPendingValidationEmailNotification(Buku buku)
    {
        var bukuTitle = BuildBukuEmailTitle(buku);
        var isLolos = string.Equals(buku.BukuStatus, "lolos", StringComparison.OrdinalIgnoreCase);
        var errorCount = buku.BukuJumlahKesalahan ?? 0;

        return new PendingValidationEmailNotification(
            buku.MhsNrp,
            "buku",
            buku.BukuId,
            bukuTitle,
            isLolos,
            errorCount);
    }

    private async Task SendValidationEmailNotificationAsync(
        SttsDbContext sttsDb,
        IEmailService emailService,
        PendingValidationEmailNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.MahasiswaNrp))
        {
            _logger.LogWarning("Skipping validation email notification because mahasiswa NRP is empty");
            return;
        }

        try
        {
            var mahasiswa = await sttsDb.Mahasiswas
                .AsNoTracking()
                .Where(m => m.MhsNrp == notification.MahasiswaNrp)
                .Select(m => new { m.MhsNama, m.MhsEmail, m.JurKode })
                .FirstOrDefaultAsync(cancellationToken);

            if (mahasiswa == null)
            {
                _logger.LogWarning(
                    "Skipping validation email notification because mahasiswa data was not found for NRP: {Nrp}",
                    notification.MahasiswaNrp);
                return;
            }

            if (string.IsNullOrWhiteSpace(mahasiswa.MhsEmail))
            {
                _logger.LogWarning(
                    "Skipping validation email notification because mahasiswa email is empty for NRP: {Nrp}",
                    notification.MahasiswaNrp);
                return;
            }

            var recipientName = string.IsNullOrWhiteSpace(mahasiswa.MhsNama)
                ? notification.MahasiswaNrp
                : mahasiswa.MhsNama;
            var jurusan = string.IsNullOrWhiteSpace(mahasiswa.JurKode)
                ? null
                : await sttsDb.Jurusans
                    .AsNoTracking()
                    .Where(j => j.JurKode == mahasiswa.JurKode)
                    .Select(j => new { j.JurNama, j.JurSingkat })
                    .FirstOrDefaultAsync(cancellationToken);
            var academicWorkLabel = DetermineAcademicWorkLabel(
                mahasiswa.JurKode,
                jurusan?.JurNama,
                jurusan?.JurSingkat);

            var sent = await emailService.SendValidationCompleteNotificationAsync(
                mahasiswa.MhsEmail,
                recipientName,
                notification.ResourceType,
                notification.ResourceId,
                notification.ResourceTitle,
                notification.IsLolos,
                notification.ErrorCount,
                academicWorkLabel);

            if (!sent)
            {
                _logger.LogWarning(
                    "Validation email notification failed for NRP: {Nrp}, ResourceType: {ResourceType}, ResourceId: {ResourceId}, Title: {ResourceTitle}",
                    notification.MahasiswaNrp,
                    notification.ResourceType,
                    notification.ResourceId,
                    notification.ResourceTitle);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while sending validation email notification for NRP: {Nrp}",
                notification.MahasiswaNrp);
        }
    }

    private static string BuildDokumenEmailTitle(Dokumen dokumen)
    {
        var rawTitle = string.IsNullOrWhiteSpace(dokumen.DokumenFilename)
            ? null
            : Path.GetFileNameWithoutExtension(dokumen.DokumenFilename.Trim());

        return string.IsNullOrWhiteSpace(rawTitle)
            ? $"Dokumen #{dokumen.DokumenId}"
            : rawTitle;
    }

    private static string BuildBukuEmailTitle(Buku buku)
    {
        var rawTitle = string.IsNullOrWhiteSpace(buku.BukuJudul)
            ? null
            : buku.BukuJudul.Trim();

        return string.IsNullOrWhiteSpace(rawTitle)
            ? $"Buku #{buku.BukuId}"
            : rawTitle;
    }

    private static string DetermineAcademicWorkLabel(string? jurKode, string? jurNama, string? jurSingkat)
    {
        return HasPostgraduateMarker(jurKode) ||
               HasPostgraduateMarker(jurNama) ||
               HasPostgraduateMarker(jurSingkat)
            ? "Tesis"
            : "Tugas Akhir";
    }

    private static bool HasPostgraduateMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Contains("S2", StringComparison.Ordinal) ||
               normalized.Contains("MAGISTER", StringComparison.Ordinal) ||
               normalized.Contains("MASTER", StringComparison.Ordinal) ||
               normalized.Contains("PASCASARJANA", StringComparison.Ordinal);
    }

    private async Task<(bool AllBabsCompleted, List<Bab> Babs)> AreAllBukuBabsValidationCompletedAsync(
        KorektorBukuDbContext db,
        Antrian currentQueue,
        CancellationToken cancellationToken)
    {
        if (!currentQueue.BukuId.HasValue)
            return (false, new List<Bab>());

        var bukuId = currentQueue.BukuId.Value;
        var babs = await db.Babs
            .Where(b => b.BukuId == bukuId)
            .OrderBy(b => b.BabOrder)
            .ThenBy(b => b.BabId)
            .ToListAsync(cancellationToken);

        if (babs.Count == 0)
        {
            _logger.LogWarning(
                "Skipping buku finalization because no bab rows were found for buku ID: {BukuId}",
                bukuId);
            return (false, babs);
        }

        var babIds = babs.Select(b => b.BabId).ToList();
        var queues = await db.Antrians
            .Where(a => a.AntrianTipe == "buku" &&
                        a.BukuId == bukuId &&
                        a.BabId.HasValue &&
                        babIds.Contains(a.BabId.Value))
            .ToListAsync(cancellationToken);

        var currentQueueIndex = queues.FindIndex(a => a.AntrianId == currentQueue.AntrianId);
        if (currentQueueIndex >= 0)
        {
            queues[currentQueueIndex] = currentQueue;
        }
        else if (currentQueue.BabId.HasValue && babIds.Contains(currentQueue.BabId.Value))
        {
            queues.Add(currentQueue);
        }

        var missingQueueBabIds = babIds
            .Where(babId => !queues.Any(a => a.BabId == babId))
            .ToList();
        var incompleteQueueStates = queues
            .Where(a => a.BabId.HasValue &&
                        !string.Equals(a.AntrianValidationStatus, "completed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.BabId)
            .ThenBy(a => a.AntrianCreatedAt ?? DateTime.MinValue)
            .ThenBy(a => a.AntrianId)
            .Select(a => $"{a.BabId}:{a.AntrianId}:{a.AntrianValidationStatus ?? "null"}")
            .ToList();

        if (missingQueueBabIds.Count > 0 || incompleteQueueStates.Count > 0)
        {
            _logger.LogDebug(
                "Skipping buku finalization for buku ID: {BukuId}. MissingBabQueues={MissingBabQueues}. IncompleteQueues={IncompleteQueues}",
                bukuId,
                missingQueueBabIds.Count == 0 ? "-" : string.Join(",", missingQueueBabIds),
                incompleteQueueStates.Count == 0 ? "-" : string.Join(",", incompleteQueueStates));
            return (false, babs);
        }

        return (true, babs);
    }

    private async Task<ErrorStorageResult> EnrichAndStoreErrorsAsync(
        KorektorBukuDbContext db,
        uint antrianId,
        uint dokumenId,
        string sectionRefType,
        uint sectionRefId,
        KesalahanRefTipe kesalahanRefTipe,
        uint kesalahanRefId,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken,
        byte? babOrder = null)
    {
        if (errors.Count == 0)
            return ErrorStorageResult.Empty;

        var aturan = await db.Aturans
            .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        List<AturanDetail> aturanDetails = new();
        if (aturan != null)
        {
            aturanDetails = AturanDetailVisibility.FilterVisible(await db.AturanDetails
                .Where(d => d.AturanId == aturan.AturanId)
                .ToListAsync(cancellationToken));
        }

        var llmErrors = await BuildLlmErrorsAsync(
            db,
            dokumenId,
            sectionRefType,
            sectionRefId,
            errors,
            cancellationToken);
        if (llmErrors.Count == 0)
            return new ErrorStorageResult(0, 0, 0, Array.Empty<ValidationError>());

        var storableErrors = llmErrors
            .Where(HasKnownLocation)
            .ToList();
        var locationFilteredErrorCount = llmErrors.Count - storableErrors.Count;
        if (storableErrors.Count == 0)
            return new ErrorStorageResult(0, llmErrors.Count, locationFilteredErrorCount, storableErrors);

        var detailByIndex = new ConcurrentDictionary<int, GeminiErrorDetail>();
        var pending = new ConcurrentQueue<BatchErrorItem>(
            storableErrors.Select((error, index) => new BatchErrorItem(error, index)));
        var batchNumber = 0;
        var inFlight = 0;

        var activeKeyCount = await db.GeminiApiKeys
            .Where(k => k.GeminiApiKeyStatus == 1)
            .CountAsync(cancellationToken);
        var maxParallelBatches = Math.Min(_maxParallelBatches, Math.Max(1, activeKeyCount));

        var tasks = new List<Task>();
        using var semaphore = new SemaphoreSlim(maxParallelBatches);

        while (!pending.IsEmpty || Volatile.Read(ref inFlight) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pending.IsEmpty)
            {
                await Task.Delay(100, cancellationToken);
                tasks.RemoveAll(t => t.IsCompleted);
                continue;
            }

            await semaphore.WaitAsync(cancellationToken);

            var batchItems = new List<BatchErrorItem>(_geminiBatchSize);
            while (batchItems.Count < _geminiBatchSize && pending.TryDequeue(out var item))
                batchItems.Add(item);

            if (batchItems.Count == 0)
            {
                semaphore.Release();
                continue;
            }

            var currentBatchNumber = Interlocked.Increment(ref batchNumber);
            var remainingCount = pending.Count;
            var totalBatchesSnapshot = Math.Max(
                currentBatchNumber,
                currentBatchNumber + (int)Math.Ceiling((double)remainingCount / _geminiBatchSize));

            Interlocked.Increment(ref inFlight);

            var task = Task.Run(async () =>
            {
                try
                {
                    var batch = batchItems.Select(item => item.Error).ToList();
                    var minIndex = batchItems.Min(item => item.OriginalIndex);
                    var maxIndex = batchItems.Max(item => item.OriginalIndex);

                    _logger.LogInformation(
                        "[BATCH] Processing batch {BatchNum}/{TotalBatches}: {BatchSize} errors (llm index {Start}-{End} of {Total})",
                        currentBatchNumber,
                        totalBatchesSnapshot,
                        batch.Count,
                        minIndex,
                        maxIndex,
                        storableErrors.Count);

                    var batchDetails = await FetchGuidanceForBatchAsync(
                        batch,
                        aturanDetails,
                        antrianId,
                        dokumenId,
                        minIndex,
                        currentBatchNumber,
                        totalBatchesSnapshot,
                        cancellationToken);

                    var detailByBatchIndex = batchDetails
                        .Where(d => d.Index >= 0 && d.Index < batchItems.Count)
                        .GroupBy(d => d.Index)
                        .ToDictionary(g => g.Key, g => g.First());

                    static bool HasEmptySteps(GeminiErrorDetail detail)
                        => detail.Steps == null ||
                           detail.Steps.Count == 0 ||
                           detail.Steps.Count > 6 ||
                           detail.Steps.Any(step => string.IsNullOrWhiteSpace(step));

                    static bool HasMissingRequiredFields(GeminiErrorDetail detail)
                    {
                        if (string.IsNullOrWhiteSpace(detail.Title))
                            return true;
                        if (string.IsNullOrWhiteSpace(detail.Explanation))
                            return true;
                        return false;
                    }

                    static bool ShouldRequeue(GeminiErrorDetail detail)
                    {
                        if (HasMissingRequiredFields(detail))
                            return true;
                        return HasEmptySteps(detail);
                    }

                    var emptyStepIndices = detailByBatchIndex
                        .Where(pair => ShouldRequeue(pair.Value))
                        .Select(pair => pair.Key)
                        .ToList();
                    foreach (var emptyIndex in emptyStepIndices)
                        detailByBatchIndex.Remove(emptyIndex);

                    foreach (var pair in detailByBatchIndex)
                    {
                        var item = batchItems[pair.Key];
                        var detail = pair.Value;
                        detail.Index = item.OriginalIndex;
                        detailByIndex.TryAdd(detail.Index, detail);
                    }

                    var missing = GetMissingIndices(detailByBatchIndex.Keys, batchItems.Count);
                    if (emptyStepIndices.Count > 0)
                    {
                        _logger.LogWarning(
                            "[BATCH] Empty/invalid required fields for {EmptyCount}/{BatchSize} items in batch {BatchNum}; requeueing",
                            emptyStepIndices.Count,
                            batchItems.Count,
                            currentBatchNumber);
                    }
                    if (missing.Count > 0)
                    {
                        _logger.LogWarning(
                            "[BATCH] Missing {MissingCount}/{BatchSize} items in batch {BatchNum}; requeueing",
                            missing.Count,
                            batchItems.Count,
                            currentBatchNumber);

                        foreach (var missingIndex in missing)
                        {
                            var item = batchItems[missingIndex];
                            if (detailByIndex.ContainsKey(item.OriginalIndex))
                                continue;

                            item.RetryCount++;
                            if (item.RetryCount <= _maxBatchRetries)
                            {
                                pending.Enqueue(item);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "[BATCH] Dropping error at llm index {Index} after {Retries} retries",
                                    item.OriginalIndex,
                                    item.RetryCount);
                            }
                        }
                    }

                    _logger.LogInformation(
                        "[BATCH] Batch {BatchNum} complete: {ParsedCount}/{BatchSize} parsed",
                        currentBatchNumber,
                        detailByBatchIndex.Count,
                        batchItems.Count);

                    if (!pending.IsEmpty)
                    {
                        _logger.LogInformation(
                            "[BATCH] Waiting {Delay}s before next batch...",
                            _batchDelay.TotalSeconds);
                        await Task.Delay(_batchDelay, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested; exit quietly.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[BATCH] Failed batch {BatchNum}", currentBatchNumber);

                    foreach (var item in batchItems)
                    {
                        if (detailByIndex.ContainsKey(item.OriginalIndex))
                            continue;

                        item.RetryCount++;
                        if (item.RetryCount <= _maxBatchRetries)
                        {
                            pending.Enqueue(item);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[BATCH] Dropping error at llm index {Index} after {Retries} retries",
                                item.OriginalIndex,
                                item.RetryCount);
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                    semaphore.Release();
                }
            }, cancellationToken);

            tasks.Add(task);
            tasks.RemoveAll(t => t.IsCompleted);
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);

        // Group errors by category + field + elemen_id (fallback to lokasi when elemen_id is missing).
        // For page-settings errors we keep expected in the key to preserve per-rule separation.
        var errorGroups = new Dictionary<string, (Kesalahan Parent, List<KesalahanDetail> Details)>();
        var storedDetailCount = 0;
        var errorsToStore = storableErrors;
        for (int i = 0; i < errorsToStore.Count; i++)
        {
            var error = errorsToStore[i];
            GeminiErrorDetail? detail = null;
            if (detailByIndex.TryGetValue(i, out var found))
                detail = found;

            var title = !string.IsNullOrWhiteSpace(detail?.Title) ? detail!.Title : error.Message;
            title = Truncate(title, 255);

            var explanation = !string.IsNullOrWhiteSpace(detail?.Explanation)
                ? detail!.Explanation
                : BuildFallbackExplanation(error);
            explanation = Truncate(explanation, 255);

            var steps = detail?.Steps ?? new List<string>();
            var stepsJson = JsonSerializer.Serialize(steps);

            var category = GetDisplayCategory(error);
            category = Truncate(category, 100);

            var lokasi = BuildKesalahanLokasi(error, babOrder);
            if (lokasi == null)
                continue;

            var fieldKey = string.IsNullOrWhiteSpace(error.Field) ? "-" : error.Field.Trim().ToLowerInvariant();
            var expectedKey = string.IsNullOrWhiteSpace(error.Expected) ? "-" : error.Expected.Trim().ToLowerInvariant();
            
            var useExpectedInGroupKey = IsPageSettingsError(error);
            var groupKey = error.DokumenElemenId.HasValue
                ? (useExpectedInGroupKey
                    ? $"{category}|field:{fieldKey}|expected:{expectedKey}|elemen:{error.DokumenElemenId.Value}"
                    : $"{category}|field:{fieldKey}|elemen:{error.DokumenElemenId.Value}")
                : (useExpectedInGroupKey
                    ? $"{category}|field:{fieldKey}|expected:{expectedKey}|{lokasi ?? "null"}"
                    : $"{category}|field:{fieldKey}|{lokasi ?? "null"}");

            if (!errorGroups.TryGetValue(groupKey, out var group))
            {
                var parent = new Kesalahan
                {
                    KesalahanKategori = category,
                    KesalahanRefTipe = kesalahanRefTipe,
                    KesalahanRefId = kesalahanRefId,
                    KesalahanLokasi = lokasi
                };
                group = (parent, new List<KesalahanDetail>());
                errorGroups[groupKey] = group;
            }

            group.Details.Add(new KesalahanDetail
            {
                KesalahanDetailJudul = title,
                KesalahanDetailPenjelasan = explanation,
                KesalahanDetailSteps = stepsJson,
                KesalahanIsHardConstraint = error.IsHardConstraint
            });
            storedDetailCount++;
        }

        // Save to database
        foreach (var (_, group) in errorGroups)
        {
            db.Kesalahans.Add(group.Parent);
            await db.SaveChangesAsync(cancellationToken);

            // Assign parent ID to details and add them
            foreach (var detail in group.Details)
            {
                detail.KesalahanId = group.Parent.KesalahanId;
            }
            db.KesalahanDetails.AddRange(group.Details);
        }

        return new ErrorStorageResult(storedDetailCount, llmErrors.Count, locationFilteredErrorCount, storableErrors);
    }

    private async Task<List<GeminiErrorDetail>> FetchGuidanceForBatchAsync(
        IReadOnlyList<ValidationError> batch,
        IReadOnlyList<AturanDetail> aturanDetails,
        uint antrianId,
        uint dokumenId,
        int startIndex,
        int batchNumber,
        int totalBatches,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Requesting Gemini guidance for dokumen ID: {DokumenId} (batch min index: {StartIndex}, count: {BatchCount})",
            dokumenId,
            startIndex,
            batch.Count);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var geminiService = scope.ServiceProvider.GetRequiredService<IGeminiService>();
            return await geminiService.GenerateErrorGuidanceAsync(
                batch,
                aturanDetails,
                cancellationToken,
                antrianId,
                batchNumber,
                totalBatches,
                dokumenId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to generate Gemini guidance for dokumen ID: {DokumenId} (batch min index: {StartIndex})",
                dokumenId,
                startIndex);
            return new List<GeminiErrorDetail>();
        }
    }

    private static List<int> GetMissingIndices(IEnumerable<int> presentIndices, int expectedCount)
    {
        var missing = new List<int>();
        if (expectedCount <= 0)
            return missing;

        var present = new HashSet<int>(presentIndices);
        for (var i = 0; i < expectedCount; i++)
        {
            if (!present.Contains(i))
                missing.Add(i);
        }

        return missing;
    }

    private sealed class SectionContext
    {
        public ulong? ElementId { get; set; }
        public string? AnchorText { get; set; }
        public int? PageNumber { get; set; }
    }

    private async Task<List<ValidationError>> BuildLlmErrorsAsync(
        KorektorBukuDbContext db,
        uint dokumenId,
        string sectionRefType,
        uint sectionRefId,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var llmErrors = new List<ValidationError>(errors.Count);

        if (errors.Count == 0)
            return llmErrors;

        var pageErrorIndices = new List<int>();
        for (var i = 0; i < errors.Count; i++)
        {
            if (ShouldAggregatePageSettingsError(errors[i]))
                pageErrorIndices.Add(i);
        }

        var pageGroups = pageErrorIndices
            .GroupBy(i => BuildPageSettingsGroupKey(errors[i]))
            .ToDictionary(g => g.Key, g => g.ToList());

        var handledPageKeys = new HashSet<string>(StringComparer.Ordinal);

        var sectionIndices = pageErrorIndices
            .Select(i => errors[i].SectionIndex)
            .Where(i => i.HasValue)
            .Select(i => i!.Value)
            .ToHashSet();

        var sectionContexts = await LoadSectionContextsAsync(
            db,
            sectionRefType,
            sectionRefId,
            sectionIndices,
            cancellationToken);

        for (var i = 0; i < errors.Count; i++)
        {
            var error = errors[i];
            if (!IsPageSettingsError(error) || !ShouldAggregatePageSettingsError(error))
            {
                llmErrors.Add(error);
                continue;
            }

            var groupKey = BuildPageSettingsGroupKey(error);
            if (!handledPageKeys.Add(groupKey))
                continue;

            if (!pageGroups.TryGetValue(groupKey, out var groupIndices) || groupIndices.Count == 0)
                continue;

            var groupErrors = groupIndices.Select(index => errors[index]).ToList();
            var aggregated = BuildAggregatedPageSettingsError(error, groupErrors, sectionContexts);
            llmErrors.Add(aggregated);
        }

        await EnrichMissingLocationsAsync(
            db,
            sectionRefType,
            sectionRefId,
            llmErrors,
            cancellationToken);

        return llmErrors;
    }

    private async Task EnrichMissingLocationsAsync(
        KorektorBukuDbContext db,
        string refType,
        uint refId,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var targetElementIds = errors
            .Where(error => error.DokumenElemenId.HasValue)
            .Where(error => !error.Locations.Any(loc => loc != null && loc.HalamanKe > 0))
            .Select(error => error.DokumenElemenId!.Value)
            .Distinct()
            .ToList();

        if (targetElementIds.Count == 0)
            return;

        var fallbackLocationsByElementId = await LoadPrimaryLocationsByElementIdAsync(
            db,
            refType,
            refId,
            targetElementIds,
            cancellationToken);

        foreach (var error in errors)
        {
            if (!error.DokumenElemenId.HasValue)
                continue;

            if (error.Locations.Any(loc => loc != null && loc.HalamanKe > 0))
                continue;

            if (!fallbackLocationsByElementId.TryGetValue(error.DokumenElemenId.Value, out var location))
                continue;

            error.Locations =
            [
                new ErrorLocation
                {
                    HalamanKe = location.HalamanKe,
                    Bbox = location.Bbox != null
                        ? new ErrorBbox
                        {
                            X0 = location.Bbox.X0,
                            Y0 = location.Bbox.Y0,
                            X1 = location.Bbox.X1,
                            Y1 = location.Bbox.Y1
                        }
                        : null
                }
            ];
        }
    }

    private static bool ShouldAggregatePageSettingsError(ValidationError error)
    {
        if (!IsPageSettingsError(error))
            return false;

        // Page-end blank-line errors are page-specific. Aggregating them across a section
        // adds anchor pages that do not actually contain the empty-space issue.
        return !string.Equals(
            error.Field,
            "max_baris_kosong_akhir_halaman",
            StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   error.Field,
                   "cegah_halaman_kosong",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPageSettingsError(ValidationError error)
    {
        var field = error.Field ?? string.Empty;
        if (IsPageSettingsField(field))
            return true;

        return string.Equals(error.Category, "Pengaturan Halaman", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPageSettingsGroupKey(ValidationError error)
    {
        var field = (error.Field ?? string.Empty).Trim().ToLowerInvariant();
        var expected = string.IsNullOrWhiteSpace(error.Expected) ? "-" : error.Expected.Trim().ToLowerInvariant();
        return $"{field}|{expected}";
    }

    private static ValidationError BuildAggregatedPageSettingsError(
        ValidationError sample,
        IReadOnlyList<ValidationError> groupErrors,
        IReadOnlyDictionary<int, SectionContext> sectionContexts)
    {
        var aggregated = CloneValidationError(sample);
        foreach (var groupError in groupErrors)
            aggregated.AddValidationCheckKeys(groupError.ValidationCheckKeys);
        aggregated.IsHardConstraint = groupErrors.Any(error => error.IsHardConstraint);
        aggregated.ScopeHint = BuildPageSettingsScopeHint(sample.Field, groupErrors, sectionContexts);

        var locations = BuildAggregatedLocations(groupErrors, sectionContexts);
        if (locations.Count > 0)
            aggregated.Locations = locations;

        if (groupErrors.Count > 1)
        {
            aggregated.SectionIndex = null;
            aggregated.Expected = MergeDistinctValues(groupErrors.Select(e => e.Expected));
            aggregated.Actual = MergeDistinctValues(groupErrors.Select(e => e.Actual));
            aggregated.Message = BuildPageSettingsMessage(
                sample.Field,
                sample.Message,
                groupErrors.Count,
                aggregated.Expected);
        }

        return aggregated;
    }

    private static string BuildPageSettingsMessage(string field, string? sampleMessage, int count, string? expected)
    {
        if (count <= 1)
            return sampleMessage ?? string.Empty;

        var label = GetPageSettingsFieldLabel(field);
        if (!string.IsNullOrWhiteSpace(label))
        {
            if (!string.IsNullOrWhiteSpace(expected))
                return $"{label} tidak sesuai di beberapa section (seharusnya {expected})";
            return $"{label} tidak sesuai di beberapa section";
        }

        if (string.IsNullOrWhiteSpace(sampleMessage))
            return "Pengaturan halaman tidak sesuai di beberapa section";

        return $"{sampleMessage} (beberapa section)";
    }

    private static string? GetPageSettingsFieldLabel(string field)
    {
        var normalized = (field ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "paper" => "Ukuran kertas",
            "margin_top" => "Margin atas",
            "margin_bottom" => "Margin bawah",
            "margin_left" => "Margin kiri",
            "margin_right" => "Margin kanan",
            "header_from_top" => "Jarak header dari atas",
            "footer_from_bottom" => "Jarak footer dari bawah",
            "different_first_page" => "Pengaturan first page berbeda",
            "different_odd_even" => "Pengaturan nomor halaman ganjil-genap",
            "gutter" => "Gutter",
            "gutter_position" => "Posisi gutter",
            "column_count" => "Jumlah kolom",
            "max_baris_kosong_akhir_halaman" => "Maksimal baris kosong pada akhir halaman",
            "cegah_halaman_kosong" => "Cegah halaman kosong",
            "page_number_format" => "Format nomor halaman",
            "page_number_location" => "Letak nomor halaman",
            "page_number_alignment" => "Alignment nomor halaman",
            "page_number_font_name" => "Font nomor halaman",
            "page_number_font_size" => "Ukuran font nomor halaman",
            "page_number_bold" => "Bold nomor halaman",
            "page_number_italic" => "Italic nomor halaman",
            "page_number_underline" => "Underline nomor halaman",
            "page_number_left_indent" => "Left indent nomor halaman",
            "page_number_right_indent" => "Right indent nomor halaman",
            "page_number_first_line_indent" => "First line indent nomor halaman",
            "page_number_line_spacing" => "Line spacing nomor halaman",
            "page_number_spacing_before" => "Spacing before nomor halaman",
            "page_number_spacing_after" => "Spacing after nomor halaman",
            "page_number_structure" => "Struktur konten nomor halaman",
            "page_numbering" => "Penomoran halaman",
            _ => null
        };
    }

    private static string? MergeDistinctValues(IEnumerable<string?> values)
    {
        var list = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
            return null;
        if (list.Count == 1)
            return list[0];

        return string.Join("; ", list);
    }

    private static string BuildPageSettingsScopeHint(
        string field,
        IReadOnlyList<ValidationError> groupErrors,
        IReadOnlyDictionary<int, SectionContext> sectionContexts)
    {
        var label = GetPageSettingsFieldLabel(field);
        var sectionIndices = groupErrors
            .Select(e => e.SectionIndex)
            .Where(i => i.HasValue)
            .Select(i => i!.Value)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(label))
            parts.Add($"Jenis kesalahan: {label}.");
        if (sectionIndices.Count > 0)
        {
            var notes = new List<string>();
            const int maxSections = 6;
            var displayed = sectionIndices.Take(maxSections);
            foreach (var sectionIndex in displayed)
            {
                notes.Add(BuildSectionNote(sectionIndex, sectionContexts));
            }

            if (sectionIndices.Count > maxSections)
                notes.Add($"dan {sectionIndices.Count - maxSections} section lain");

            parts.Add($"Section terkait: {string.Join("; ", notes)}.");
        }

        parts.Add("Pengaturan halaman seharusnya konsisten di seluruh dokumen; boleh diterapkan ke seluruh dokumen (Ctrl+A) kecuali aturan hanya untuk section tertentu.");
        return string.Join(" ", parts);
    }

    private static string BuildSectionNote(
        int sectionIndex,
        IReadOnlyDictionary<int, SectionContext> sectionContexts)
    {
        if (!sectionContexts.TryGetValue(sectionIndex, out var context))
            return $"Section {sectionIndex}";

        var sb = new StringBuilder();
        sb.Append("Section ").Append(sectionIndex);

        if (context.PageNumber.HasValue)
            sb.Append(" (halaman ").Append(context.PageNumber.Value).Append(')');

        if (!string.IsNullOrWhiteSpace(context.AnchorText))
            sb.Append(" dekat paragraf diawali \"").Append(context.AnchorText).Append('"');

        return sb.ToString();
    }

    private static List<ErrorLocation> BuildAggregatedLocations(
        IReadOnlyList<ValidationError> groupErrors,
        IReadOnlyDictionary<int, SectionContext> sectionContexts)
    {
        var byPage = new Dictionary<int, ErrorLocation>();

        foreach (var error in groupErrors)
        {
            foreach (var loc in error.Locations)
            {
                if (loc == null)
                    continue;

                var page = loc.HalamanKe;
                if (page <= 0)
                    continue;

                if (!byPage.TryGetValue(page, out var existing))
                {
                    byPage[page] = new ErrorLocation { HalamanKe = page, Bbox = loc.Bbox };
                }
                else if (existing.Bbox == null && loc.Bbox != null)
                {
                    existing.Bbox = loc.Bbox;
                }
            }
        }

        var sectionIndices = groupErrors
            .Select(e => e.SectionIndex)
            .Where(i => i.HasValue)
            .Select(i => i!.Value)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        foreach (var sectionIndex in sectionIndices)
        {
            if (!sectionContexts.TryGetValue(sectionIndex, out var context))
                continue;

            if (!context.PageNumber.HasValue)
                continue;

            var page = context.PageNumber.Value;
            if (!byPage.ContainsKey(page))
                byPage[page] = new ErrorLocation { HalamanKe = page };
        }

        return byPage.Values.OrderBy(loc => loc.HalamanKe).ToList();
    }

    private async Task<Dictionary<int, SectionContext>> LoadSectionContextsAsync(
        KorektorBukuDbContext db,
        string sectionRefType,
        uint sectionRefId,
        IEnumerable<int> sectionIndices,
        CancellationToken cancellationToken)
    {
        var indexSet = new HashSet<int>(sectionIndices.Where(i => i > 0));
        if (indexSet.Count == 0)
            return new Dictionary<int, SectionContext>();

        var sections = await db.DokumenSections
            .Where(s => s.DsecRefTipe == sectionRefType && s.DsecRefId == sectionRefId)
            .OrderBy(s => s.DsecIndex)
            .Select(s => s.DsecId)
            .ToListAsync(cancellationToken);

        var sectionIdByIndex = new Dictionary<int, uint>();
        for (var i = 0; i < sections.Count; i++)
            sectionIdByIndex[i + 1] = sections[i];

        var contexts = new Dictionary<int, SectionContext>();
        foreach (var sectionIndex in indexSet.OrderBy(i => i))
        {
            if (!sectionIdByIndex.TryGetValue(sectionIndex, out var sectionId))
                continue;

            var bodyPartId = await db.DokumenParts
                .Where(p => p.DsecId == sectionId && p.DpartType == "body")
                .Select(p => (uint?)p.DpartId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!bodyPartId.HasValue)
                continue;

            var element = await db.DokumenElemens
                .Where(e => e.DpartId == bodyPartId.Value &&
                            e.DelemenType == "paragraph" &&
                            e.DelemenJsonTree != null)
                .OrderBy(e => e.DelemenSequence)
                .Select(e => new { e.DelemenId, e.DelemenJsonTree })
                .FirstOrDefaultAsync(cancellationToken);

            if (element == null)
            {
                element = await db.DokumenElemens
                    .Where(e => e.DpartId == bodyPartId.Value && e.DelemenJsonTree != null)
                    .OrderBy(e => e.DelemenSequence)
                    .Select(e => new { e.DelemenId, e.DelemenJsonTree })
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var text = NormalizeContextText(ExtractPlainText(element?.DelemenJsonTree));
            contexts[sectionIndex] = new SectionContext
            {
                ElementId = element?.DelemenId,
                AnchorText = text
            };
        }

        var elementIds = contexts.Values
            .Select(c => c.ElementId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var pageNumbers = await LoadPageNumbersByElementIdAsync(
            db,
            sectionRefType,
            sectionRefId,
            elementIds,
            cancellationToken);
        foreach (var context in contexts.Values)
        {
            if (!context.ElementId.HasValue)
                continue;

            if (pageNumbers.TryGetValue(context.ElementId.Value, out var page))
                context.PageNumber = page;
        }

        return contexts;
    }

    private async Task<Dictionary<ulong, int>> LoadPageNumbersByElementIdAsync(
        KorektorBukuDbContext db,
        string refType,
        uint refId,
        IEnumerable<ulong> elementIds,
        CancellationToken cancellationToken)
    {
        var ids = elementIds.Distinct().ToList();
        var pageNumbers = new Dictionary<ulong, int>();

        if (ids.Count == 0)
            return pageNumbers;

        var idColumn = await ResolveVisualIdColumnAsync(db, cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn))
            return pageNumbers;
        var (refTypeColumn, refIdColumn) = await ResolveVisualRefColumnsAsync(db, cancellationToken);
        var refFilter = BuildVisualRefFilterClause(refTypeColumn, refIdColumn, refType, refId);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            foreach (var chunk in ids.Chunk(500))
            {
                var idList = string.Join(",", chunk);
                var sql = $"SELECT `{idColumn}` AS delemen_id, `dev_page` " +
                          $"FROM `dokumen_elemen_visual` WHERE `{idColumn}` IN ({idList}) {refFilter}AND `dev_page` IS NOT NULL";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader["delemen_id"] == DBNull.Value || reader["dev_page"] == DBNull.Value)
                        continue;

                    var id = Convert.ToUInt64(reader["delemen_id"]);
                    var page = Convert.ToInt32(reader["dev_page"]);
                    if (pageNumbers.TryGetValue(id, out var existingPage))
                        pageNumbers[id] = Math.Min(existingPage, page);
                    else
                        pageNumbers[id] = page;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load page numbers from dokumen_elemen_visual");
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }

        return pageNumbers;
    }

    private async Task<Dictionary<ulong, ErrorLocation>> LoadPrimaryLocationsByElementIdAsync(
        KorektorBukuDbContext db,
        string refType,
        uint refId,
        IEnumerable<ulong> elementIds,
        CancellationToken cancellationToken)
    {
        var ids = elementIds.Distinct().ToList();
        var locations = new Dictionary<ulong, ErrorLocation>();

        if (ids.Count == 0)
            return locations;

        var idColumn = await ResolveVisualIdColumnAsync(db, cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn))
            return locations;
        var (refTypeColumn, refIdColumn) = await ResolveVisualRefColumnsAsync(db, cancellationToken);
        var refFilter = BuildVisualRefFilterClause(refTypeColumn, refIdColumn, refType, refId);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            foreach (var chunk in ids.Chunk(500))
            {
                var idList = string.Join(",", chunk);
                var sql = $"SELECT `{idColumn}` AS delemen_id, `dev_page`, `dev_bbox_x0`, `dev_bbox_y0`, `dev_bbox_x1`, `dev_bbox_y1` " +
                          $"FROM `dokumen_elemen_visual` WHERE `{idColumn}` IN ({idList}) {refFilter}AND `dev_page` IS NOT NULL";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader["delemen_id"] == DBNull.Value || reader["dev_page"] == DBNull.Value)
                        continue;

                    var elementId = Convert.ToUInt64(reader["delemen_id"]);
                    var page = Convert.ToInt32(reader["dev_page"]);

                    var bbox = reader["dev_bbox_x0"] != DBNull.Value &&
                               reader["dev_bbox_y0"] != DBNull.Value &&
                               reader["dev_bbox_x1"] != DBNull.Value &&
                               reader["dev_bbox_y1"] != DBNull.Value
                        ? new ErrorBbox
                        {
                            X0 = Convert.ToDecimal(reader["dev_bbox_x0"]),
                            Y0 = Convert.ToDecimal(reader["dev_bbox_y0"]),
                            X1 = Convert.ToDecimal(reader["dev_bbox_x1"]),
                            Y1 = Convert.ToDecimal(reader["dev_bbox_y1"])
                        }
                        : null;

                    if (!locations.TryGetValue(elementId, out var existing))
                    {
                        locations[elementId] = new ErrorLocation
                        {
                            HalamanKe = page,
                            Bbox = bbox
                        };
                        continue;
                    }

                    if (page < existing.HalamanKe)
                    {
                        existing.HalamanKe = page;
                        existing.Bbox = bbox;
                        continue;
                    }

                    if (page == existing.HalamanKe)
                    {
                        if (existing.Bbox == null)
                        {
                            existing.Bbox = bbox;
                        }
                        else if (bbox != null)
                        {
                            existing.Bbox = new ErrorBbox
                            {
                                X0 = Math.Min(existing.Bbox.X0, bbox.X0),
                                Y0 = Math.Min(existing.Bbox.Y0, bbox.Y0),
                                X1 = Math.Max(existing.Bbox.X1, bbox.X1),
                                Y1 = Math.Max(existing.Bbox.Y1, bbox.Y1)
                            };
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load fallback locations from dokumen_elemen_visual");
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }

        return locations;
    }

    private async Task<string?> ResolveVisualIdColumnAsync(
        KorektorBukuDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync(cancellationToken);

            var columns = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                                  "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dokumen_elemen_visual'";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name))
                        columns.Add(name);
                }
            }

            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();

            var resolved = ResolveVisualIdColumnFromNames(columns);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
            return ResolveVisualIdColumnFromNames(GetVisualModelColumns(db));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve dokumen_elemen_visual columns");
            return ResolveVisualIdColumnFromNames(GetVisualModelColumns(db));
        }
    }

    private async Task<(string? RefTypeColumn, string? RefIdColumn)> ResolveVisualRefColumnsAsync(
        KorektorBukuDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync(cancellationToken);

            var columns = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                                  "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dokumen_elemen_visual'";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name))
                        columns.Add(name);
                }
            }

            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();

            var resolved = ResolveVisualRefColumnsFromNames(columns);
            if (resolved.RefTypeColumn is not null || resolved.RefIdColumn is not null)
                return resolved;
            return ResolveVisualRefColumnsFromNames(GetVisualModelColumns(db));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve dokumen_elemen_visual ref columns");
            return ResolveVisualRefColumnsFromNames(GetVisualModelColumns(db));
        }
    }

    private static IReadOnlyList<string> GetVisualModelColumns(KorektorBukuDbContext db)
    {
        var entityType = db.Model.FindEntityType(typeof(DokumenElemenVisual));
        var tableName = entityType?.GetTableName();
        if (entityType is null || string.IsNullOrWhiteSpace(tableName))
            return Array.Empty<string>();

        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        return entityType
            .GetProperties()
            .Select(property => property.GetColumnName(storeObject))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static string? ResolveVisualIdColumnFromNames(IEnumerable<string>? columns)
    {
        var columnList = columns?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList() ?? new List<string>();
        if (columnList.Count == 0)
            return null;

        return columnList.FirstOrDefault(c => c.Equals("delemen_id", StringComparison.OrdinalIgnoreCase))
            ?? columnList.FirstOrDefault(c => c.EndsWith("delemen_id", StringComparison.OrdinalIgnoreCase))
            ?? columnList.FirstOrDefault(c =>
                c.IndexOf("elemen", StringComparison.OrdinalIgnoreCase) >= 0 &&
                c.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static (string? RefTypeColumn, string? RefIdColumn) ResolveVisualRefColumnsFromNames(IEnumerable<string>? columns)
    {
        var columnList = columns?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList() ?? new List<string>();
        if (columnList.Count == 0)
            return (null, null);

        var refTypeColumn = columnList.FirstOrDefault(c => c.Equals("dev_ref_tipe", StringComparison.OrdinalIgnoreCase));
        var refIdColumn = columnList.FirstOrDefault(c => c.Equals("dev_ref_id", StringComparison.OrdinalIgnoreCase))
            ?? columnList.FirstOrDefault(c => c.Equals("dokumen_id", StringComparison.OrdinalIgnoreCase));
        return (refTypeColumn, refIdColumn);
    }

    private static string BuildVisualRefFilterClause(
        string? refTypeColumn,
        string? refIdColumn,
        string? refType,
        uint? refId)
    {
        if (string.IsNullOrWhiteSpace(refTypeColumn) && string.IsNullOrWhiteSpace(refIdColumn))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(refIdColumn) && refId.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(refTypeColumn) && !string.IsNullOrWhiteSpace(refType))
            {
                var escapedRefType = refType.Replace("'", "''");
                return $"AND `{refTypeColumn}` = '{escapedRefType}' AND `{refIdColumn}` = {refId.Value} ";
            }

            return $"AND `{refIdColumn}` = {refId.Value} ";
        }

        if (!string.IsNullOrWhiteSpace(refTypeColumn) && !string.IsNullOrWhiteSpace(refType))
        {
            var escapedRefType = refType.Replace("'", "''");
            return $"AND `{refTypeColumn}` = '{escapedRefType}' ";
        }

        return string.Empty;
    }

    private static string? ExtractPlainText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                return textEl.GetString();

            if (root.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var item in contentEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var type = item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                        ? typeEl.GetString()
                        : null;

                    if (type == "text" || type == "field")
                    {
                        if (item.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.String)
                            sb.Append(valueEl.GetString());
                    }
                    else if (type == "math")
                    {
                        if (item.TryGetProperty("text", out var mathEl) && mathEl.ValueKind == JsonValueKind.String)
                            sb.Append(mathEl.GetString());
                    }
                }

                var combined = sb.ToString();
                return string.IsNullOrWhiteSpace(combined) ? null : combined;
            }
        }
        catch (JsonException)
        {
            // Ignore invalid JSON and use defaults.
        }

        return null;
    }

    private static string? NormalizeContextText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        const int maxLength = 160;
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength] + "...";
    }

    private static ValidationError CloneValidationError(ValidationError source)
    {
        var clone = new ValidationError
        {
            Category = source.Category,
            Field = source.Field,
            Message = source.Message,
            Expected = source.Expected,
            Actual = source.Actual,
            SectionIndex = source.SectionIndex,
            Locations = source.Locations
                .Select(loc => new ErrorLocation
                {
                    HalamanKe = loc.HalamanKe,
                    Bbox = loc.Bbox != null
                        ? new ErrorBbox
                        {
                            X0 = loc.Bbox.X0,
                            Y0 = loc.Bbox.Y0,
                            X1 = loc.Bbox.X1,
                            Y1 = loc.Bbox.Y1
                        }
                        : null
                })
                .ToList(),
            DiffType = source.DiffType,
            Cause = source.Cause,
            HasNumbering = source.HasNumbering,
            StyleName = source.StyleName,
            StyleId = source.StyleId,
            Evidence = source.Evidence,
            ToolRequirement = source.ToolRequirement,
            FeatureName = source.FeatureName,
            AllowedActions = source.AllowedActions != null ? new List<string>(source.AllowedActions) : null,
            DisallowedActions = source.DisallowedActions != null ? new List<string>(source.DisallowedActions) : null,
            ScopeHint = source.ScopeHint,
            PageRange = source.PageRange,
            PrevElementText = source.PrevElementText,
            PrevElementLabel = source.PrevElementLabel,
            NextElementText = source.NextElementText,
            NextElementLabel = source.NextElementLabel,
            PageMarginTopCm = source.PageMarginTopCm,
            PageMarginBottomCm = source.PageMarginBottomCm,
            PageMarginLeftCm = source.PageMarginLeftCm,
            PageMarginRightCm = source.PageMarginRightCm,
            DokumenElemenId = source.DokumenElemenId,
            IsHardConstraint = source.IsHardConstraint
        };
        clone.AddValidationCheckKeys(source.ValidationCheckKeys);
        return clone;
    }

    private static bool HasKnownLocation(ValidationError error)
        => error.Locations.Any(loc => loc != null && loc.HalamanKe > 0);

    private static string? BuildKesalahanLokasi(ValidationError error, byte? babOrder = null)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        var evidence = NormalizeEvidence(error.Evidence);

        var orderedLocations = error.Locations
            .Where(loc => loc != null && loc.HalamanKe > 0)
            .OrderBy(loc => loc.HalamanKe)
            .ToList();
        if (orderedLocations.Count == 0)
            return null;

        var locations = orderedLocations.Select(loc =>
        {
            var payload = new Dictionary<string, object?>
            {
                ["halaman_ke"] = loc.HalamanKe,
                ["bbox"] = loc.Bbox != null ? new
                {
                    x0 = loc.Bbox.X0,
                    y0 = loc.Bbox.Y0,
                    x1 = loc.Bbox.X1,
                    y1 = loc.Bbox.Y1
                } : null
            };

            if (babOrder.HasValue)
                payload["bab_order"] = babOrder.Value;
            if (!string.IsNullOrWhiteSpace(evidence))
                payload["evidence"] = evidence;

            return payload;
        }).ToList();

        return JsonSerializer.Serialize(locations, jsonOptions);
    }

    private static string? NormalizeEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return null;

        var normalized = evidence
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string GetDisplayCategory(ValidationError error)
    {
        var field = error.Field ?? string.Empty;
        if (field.Equals("judul_bab", StringComparison.OrdinalIgnoreCase))
            return "judul bab";
        if (field.Equals("judul_subbab", StringComparison.OrdinalIgnoreCase))
            return "judul subbab";
        if (field.Equals("paragraf", StringComparison.OrdinalIgnoreCase))
            return "paragraf";
        if (IsPageSettingsField(field) ||
            string.Equals(error.Category, "Pengaturan Halaman", StringComparison.OrdinalIgnoreCase))
            return "pengaturan halaman";

        return string.IsNullOrWhiteSpace(error.Category) ? "umum" : error.Category;
    }

    private static bool IsPageSettingsField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return false;

        if (field.StartsWith("margin_", StringComparison.OrdinalIgnoreCase))
            return true;

        return field.Equals("paper", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("header_from_top", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("footer_from_bottom", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("different_first_page", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("different_odd_even", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("gutter", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("gutter_position", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("column_count", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("max_baris_kosong_akhir_halaman", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("cegah_halaman_kosong", StringComparison.OrdinalIgnoreCase) ||
               field.StartsWith("page_number", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("page_numbering", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("section", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFallbackExplanation(ValidationError error)
    {
        if (string.IsNullOrWhiteSpace(error.Expected) && string.IsNullOrWhiteSpace(error.Actual))
            return error.Message;

        var expected = string.IsNullOrWhiteSpace(error.Expected) ? "-" : error.Expected;
        var actual = string.IsNullOrWhiteSpace(error.Actual) ? "-" : error.Actual;
        return $"{error.Message} (expected: {expected}, actual: {actual})";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static (string RefType, uint RefId) ResolveSectionRef(Antrian queue)
    {
        if (string.Equals(queue.AntrianTipe, "buku", StringComparison.OrdinalIgnoreCase) &&
            queue.BabId.HasValue)
        {
            return ("bab", queue.BabId.Value);
        }

        if (string.Equals(queue.AntrianTipe, "dokumen", StringComparison.OrdinalIgnoreCase) &&
            queue.DokumenId.HasValue)
        {
            return ("dokumen", queue.DokumenId.Value);
        }

        // Best-effort fallback for inconsistent queue payload.
        var fallbackId = queue.DokumenId ?? queue.BabId ?? 0u;
        return ("dokumen", fallbackId);
    }
}


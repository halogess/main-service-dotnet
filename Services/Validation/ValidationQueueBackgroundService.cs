using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;
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

    public ValidationQueueBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ValidationQueueBackgroundService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _geminiBatchSize = Math.Max(1, configuration.GetValue("Gemini:BatchSize", 20));
        _maxBatchRetries = Math.Max(0, configuration.GetValue("Gemini:MaxBatchRetries", 2));
        var delaySeconds = Math.Max(0, configuration.GetValue("Gemini:BatchDelaySeconds", 3));
        _batchDelay = TimeSpan.FromSeconds(delaySeconds);
        _maxParallelBatches = Math.Max(1, configuration.GetValue("Gemini:MaxParallelBatches", 3));
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

                var queue = await db.Antrians
                    .Where(a => a.AntrianValidationStatus == "in_queue")
                    .OrderBy(a => a.AntrianCreatedAt)
                    .FirstOrDefaultAsync(stoppingToken);

                if (queue != null)
                {
                    _logger.LogInformation("Processing validation for antrian ID: {AntrianId}", queue.AntrianId);

                    queue.AntrianValidationStatus = "processing";
                    queue.AntrianUpdatedAt = DateTime.Now;
                    await db.SaveChangesAsync(stoppingToken);

                    try
                    {
                        ValidationResult? validationResult = null;

                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                // Notify: Validation started
                                await wsService.NotifyValidationProgress(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "started", 0);

                                // Perform validation on dokumen
                                validationResult = await validationService.ValidateDokumenAsync((int)queue.DokumenId.Value, stoppingToken);
                                
                                // Notify: Validation complete, processing results
                                await wsService.NotifyValidationProgress(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "processing_results", 70);
                                
                                // Update dokumen with validation results
                                dokumen.DokumenSkor = (int)Math.Round(validationResult.Score);
                                dokumen.DokumenUpdatedAt = DateTime.Now;

                                var rawErrorCount = validationResult.Errors.Count;
                                var storedErrorCount = 0;
                                if (rawErrorCount > 0)
                                {
                                    // Notify: Enriching errors with AI
                                    await wsService.NotifyValidationProgress(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "enriching", 85);
                                    var (sectionRefType, sectionRefId) = ResolveSectionRef(queue);

                                    storedErrorCount = await EnrichAndStoreErrorsAsync(
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
                                dokumen.DokumenJumlahKesalahan = storedErrorCount;

                                _logger.LogInformation(
                                    "Validated dokumen ID: {DokumenId}, Score: {Score}, RawErrors: {RawErrorCount}, StoredErrors: {StoredErrorCount}",
                                    queue.DokumenId,
                                    validationResult.Score,
                                    rawErrorCount,
                                    storedErrorCount);
                                if (rawErrorCount > 0 && storedErrorCount == 0)
                                {
                                    _logger.LogWarning(
                                        "No persisted kesalahan details for dokumen ID: {DokumenId} despite {RawErrorCount} raw validation errors",
                                        queue.DokumenId,
                                        rawErrorCount);
                                }

                                var activeAturan = await db.Aturans
                                    .Where(a => a.AturanStatus == 1)
                                    .OrderByDescending(a => a.AturanCreatedAt)
                                    .FirstOrDefaultAsync(stoppingToken);

                                var minimumScore = (decimal)(activeAturan?.AturanSkorMinimum ?? 80u);
                                var isLolos = validationResult.Score >= minimumScore;
                                dokumen.DokumenStatus = isLolos ? "lolos" : "tidak_lolos";
                                dokumen.DokumenUpdatedAt = DateTime.Now;

                                if (activeAturan == null)
                                {
                                    _logger.LogWarning(
                                        "No active aturan found when determining validation status for dokumen ID: {DokumenId}. Using default minimum score: {MinimumScore}",
                                        queue.DokumenId,
                                        minimumScore);
                                }
                                else
                                {
                                    _logger.LogInformation(
                                        "Validation status for dokumen ID: {DokumenId} determined by score. Score: {Score}, MinimumScore: {MinimumScore}, AturanId: {AturanId}, Status: {Status}",
                                        queue.DokumenId,
                                        validationResult.Score,
                                        minimumScore,
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
                                bab.BabSkor = (int)Math.Round(validationResult.Score);
                                var rawErrorCount = validationResult.Errors.Count;
                                var storedErrorCount = 0;
                                if (rawErrorCount > 0)
                                {
                                    var (sectionRefType, sectionRefId) = ResolveSectionRef(queue);
                                    storedErrorCount = await EnrichAndStoreErrorsAsync(
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
                                bab.BabJumlahKesalahan = storedErrorCount;

                                _logger.LogInformation(
                                    "Validated bab ID: {BabId} (BukuId: {BukuId}), Score: {Score}, RawErrors: {RawErrorCount}, StoredErrors: {StoredErrorCount}",
                                    queue.BabId,
                                    queue.BukuId,
                                    validationResult.Score,
                                    rawErrorCount,
                                    storedErrorCount);
                                if (rawErrorCount > 0 && storedErrorCount == 0)
                                {
                                    _logger.LogWarning(
                                        "No persisted kesalahan details for bab ID: {BabId} despite {RawErrorCount} raw validation errors",
                                        queue.BabId,
                                        rawErrorCount);
                                }
                            }
                        }

                        queue.AntrianValidationStatus = "completed";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = null;
                        _logger.LogInformation("Completed validation for antrian ID: {AntrianId}", queue.AntrianId);

                        // Notify via WebSocket
                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                await wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, (int)queue.DokumenId.Value, dokumen.DokumenStatus);
                                
                                // TODO: Email notification (disabled for now)
                                // await SendValidationEmailNotificationAsync(...);
                            }
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BukuId.HasValue)
                        {
                            // Check if all babs are validated
                            var allBabsValidated = !await db.Antrians
                                .AnyAsync(a => a.BukuId == queue.BukuId && 
                                               a.AntrianTipe == "buku" && 
                                               a.AntrianId != queue.AntrianId &&
                                               a.AntrianValidationStatus != "completed", stoppingToken);

                            if (allBabsValidated)
                            {
                                var buku = await db.Bukus.FindAsync(new object[] { (int)queue.BukuId.Value }, stoppingToken);
                                if (buku != null)
                                {
                                    var bukuRefId = (uint)queue.BukuId.Value;
                                    var babScores = await db.Babs
                                        .Where(b => b.BukuId == bukuRefId)
                                        .Select(b => b.BabSkor)
                                        .ToListAsync(stoppingToken);

                                    var scoredBabScores = babScores
                                        .Where(score => score.HasValue)
                                        .Select(score => (decimal)score!.Value)
                                        .ToList();
                                    var allBabsHaveScore = babScores.Count > 0 && babScores.Count == scoredBabScores.Count;
                                    var averageScore = allBabsHaveScore
                                        ? scoredBabScores.Average()
                                        : 0m;

                                    var activeAturan = await db.Aturans
                                        .Where(a => a.AturanStatus == 1)
                                        .OrderByDescending(a => a.AturanCreatedAt)
                                        .FirstOrDefaultAsync(stoppingToken);
                                    var minimumScore = (decimal)(activeAturan?.AturanSkorMinimum ?? 80u);
                                    var isLolos = allBabsHaveScore && scoredBabScores.All(score => score >= minimumScore);

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
                                    buku.BukuUpdatedAt = DateTime.Now;
                                    await wsService.NotifyBukuStatusChanged(buku.MhsNrp, (int)queue.BukuId.Value, finalStatus);
                                    _logger.LogInformation(
                                        "All babs validated for buku ID: {BukuId}. FinalStatus={FinalStatus}, BookScore={BookScore}, MinimumScore={MinimumScore}, TotalKesalahan={TotalKesalahan}",
                                        queue.BukuId,
                                        finalStatus,
                                        buku.BukuSkor,
                                        minimumScore,
                                        totalKesalahan);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        queue.AntrianValidationStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = ex.Message.Length > 255 ? ex.Message[..252] + "..." : ex.Message;
                        _logger.LogError(ex, "Failed validation for antrian ID: {AntrianId}", queue.AntrianId);

                        // Notify failure via WebSocket
                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                dokumen.DokumenStatus = "tidak_lolos";
                                dokumen.DokumenUpdatedAt = DateTime.Now;
                                await wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "tidak_lolos");
                            }
                        }
                    }

                    await db.SaveChangesAsync(stoppingToken);
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

    // NOTE: Email notification method removed for now. 
    // EmailService is available but not integrated.
    // Re-add SendValidationEmailNotificationAsync when ready to enable email notifications.

    private async Task<int> EnrichAndStoreErrorsAsync(
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
            return 0;

        var aturan = await db.Aturans
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        List<AturanDetail> aturanDetails = new();
        if (aturan != null)
        {
            aturanDetails = await db.AturanDetails
                .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
                .ToListAsync(cancellationToken);
        }

        var llmErrors = await BuildLlmErrorsAsync(
            db,
            dokumenId,
            sectionRefType,
            sectionRefId,
            errors,
            cancellationToken);
        if (llmErrors.Count == 0)
            return 0;

        var detailByIndex = new ConcurrentDictionary<int, GeminiErrorDetail>();
        var pending = new ConcurrentQueue<BatchErrorItem>(
            llmErrors.Select((error, index) => new BatchErrorItem(error, index)));
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
                        llmErrors.Count);

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
                           detail.Steps.All(step => string.IsNullOrWhiteSpace(step));

                    static bool ShouldRequeue(GeminiErrorDetail detail)
                    {
                        if (detail.IsError == false && string.IsNullOrWhiteSpace(detail.SkipReason))
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
                            "[BATCH] Empty steps or missing skip_reason for {EmptyCount}/{BatchSize} items in batch {BatchNum}; requeueing",
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

        // Group errors by category + field/expected + elemen_id (fallback to lokasi when elemen_id is missing).
        // This prevents unrelated page-setting fields with null lokasi from collapsing into one parent row.
        var errorGroups = new Dictionary<string, (Kesalahan Parent, List<KesalahanDetail> Details)>();
        var skipLogs = new List<LlmApiLog>();
        var storedDetailCount = 0;
        var errorsToStore = llmErrors;
        for (int i = 0; i < errorsToStore.Count; i++)
        {
            var error = errorsToStore[i];
            GeminiErrorDetail? detail = null;
            if (detailByIndex.TryGetValue(i, out var found))
                detail = found;

            if (detail?.IsError == false)
            {
                skipLogs.Add(new LlmApiLog
                {
                    LogMessage = BuildSkipLogMessage(detail.SkipReason),
                    LogErrorCode = 0,
                    AntrianId = antrianId,
                    ApiKeyId = 0,
                    LogTokensUsed = 0,
                    LogBatchNumber = 0,
                    LogTotalBatches = 0,
                    LogErrorCount = 0,
                    LogKeyTokensUsed = 0,
                    LogCreatedAt = DateTime.Now
                });
                continue;
            }

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
            var fieldKey = string.IsNullOrWhiteSpace(error.Field) ? "-" : error.Field.Trim().ToLowerInvariant();
            var expectedKey = string.IsNullOrWhiteSpace(error.Expected) ? "-" : error.Expected.Trim().ToLowerInvariant();
            
            var groupKey = error.DokumenElemenId.HasValue
                ? $"{category}|field:{fieldKey}|expected:{expectedKey}|elemen:{error.DokumenElemenId.Value}"
                : $"{category}|field:{fieldKey}|expected:{expectedKey}|{lokasi ?? "null"}";

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
                KesalahanIsRequired = error.IsRequired
            });
            storedDetailCount++;
        }

        if (skipLogs.Count > 0)
            db.LlmApiLogs.AddRange(skipLogs);

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

        if (errorGroups.Count == 0 && skipLogs.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return storedDetailCount;
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
            if (IsPageSettingsError(errors[i]))
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
            if (!IsPageSettingsError(error))
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

        return llmErrors;
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
            "different_odd_even" => "Pengaturan header/footer ganjil-genap",
            "gutter" => "Gutter",
            "gutter_position" => "Posisi gutter",
            "column_count" => "Jumlah kolom",
            "page_number_format" => "Format nomor halaman",
            "page_number_start" => "Nomor halaman awal",
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

            if (columns.Count == 0)
                return null;

            var idColumn = columns.FirstOrDefault(c => c.Equals("delemen_id", StringComparison.OrdinalIgnoreCase))
                ?? columns.FirstOrDefault(c => c.EndsWith("delemen_id", StringComparison.OrdinalIgnoreCase))
                ?? columns.FirstOrDefault(c =>
                    c.IndexOf("elemen", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    c.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0);

            return idColumn;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve dokumen_elemen_visual columns");
            return null;
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

            if (columns.Count == 0)
                return (null, null);

            var refTypeColumn = columns.FirstOrDefault(c => c.Equals("dev_ref_tipe", StringComparison.OrdinalIgnoreCase));
            var refIdColumn = columns.FirstOrDefault(c => c.Equals("dev_ref_id", StringComparison.OrdinalIgnoreCase))
                ?? columns.FirstOrDefault(c => c.Equals("dokumen_id", StringComparison.OrdinalIgnoreCase));

            return (refTypeColumn, refIdColumn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve dokumen_elemen_visual ref columns");
            return (null, null);
        }
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
        return new ValidationError
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
            IsRequired = source.IsRequired
        };
    }

    private static string? BuildKesalahanLokasi(ValidationError error, byte? babOrder = null)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        var evidence = NormalizeEvidence(error.Evidence);

        if (error.Locations.Count == 0)
        {
            if (!babOrder.HasValue)
                return null;

            var fallback = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["halaman_ke"] = null,
                    ["bbox"] = null,
                    ["bab_order"] = babOrder.Value
                }
            };
            if (!string.IsNullOrWhiteSpace(evidence))
                fallback[0]["evidence"] = evidence;

            return JsonSerializer.Serialize(fallback, jsonOptions);
        }

        var orderedLocations = error.Locations
            .Where(loc => loc != null)
            .OrderBy(loc => loc.HalamanKe <= 0 ? int.MaxValue : loc.HalamanKe)
            .ToList();

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
               field.Equals("different_odd_even", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("gutter", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("gutter_position", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("column_count", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("page_number_format", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("page_number_start", StringComparison.OrdinalIgnoreCase) ||
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

    private static string BuildSkipLogMessage(string? skipReason)
    {
        var message = string.IsNullOrWhiteSpace(skipReason)
            ? "skip"
            : $"skip:{skipReason.Trim().Replace('\r', ' ').Replace('\n', ' ')}";

        return message.Length <= 50 ? message : message[..50];
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


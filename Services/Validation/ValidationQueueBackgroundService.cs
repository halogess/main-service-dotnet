using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ValidasiTugasAkhir.MainService.Services;

public class ValidationQueueBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ValidationQueueBackgroundService> _logger;
    private const int GeminiErrorLimit = 20;

    public ValidationQueueBackgroundService(IServiceProvider serviceProvider, ILogger<ValidationQueueBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
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
                var geminiService = scope.ServiceProvider.GetRequiredService<IGeminiService>();

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
                                // Perform validation on dokumen
                                validationResult = await validationService.ValidateDokumenAsync((int)queue.DokumenId.Value, stoppingToken);
                                
                                // Update dokumen with validation results
                                dokumen.DokumenSkor = (int)Math.Round(validationResult.Score);
                                dokumen.DokumenJumlahKesalahan = validationResult.Errors.Count;
                                dokumen.DokumenUpdatedAt = DateTime.Now;

                                _logger.LogInformation("Validated dokumen ID: {DokumenId}, Score: {Score}, Errors: {ErrorCount}", 
                                    queue.DokumenId, validationResult.Score, validationResult.Errors.Count);

                                if (validationResult.Errors.Count > 0)
                                {
                                    await EnrichAndStoreErrorsAsync(
                                        db,
                                        geminiService,
                                        queue.DokumenId.Value,
                                        validationResult.Errors,
                                        stoppingToken);
                                }
                            }
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BabId.HasValue)
                        {
                            var bab = await db.Babs.FindAsync(new object[] { queue.BabId.Value }, stoppingToken);
                            if (bab != null)
                            {
                                // TODO: Implement bab validation
                                _logger.LogInformation("Validated bab ID: {BabId}", queue.BabId);
                            }
                        }

                        queue.AntrianValidationStatus = "completed";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = null;
                        _logger.LogInformation("Completed validation for antrian ID: {AntrianId}", queue.AntrianId);

                        // Notify via WebSocket if needed
                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                dokumen.DokumenStatus = "lolos";
                                dokumen.DokumenUpdatedAt = DateTime.Now;
                                await wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "lolos");
                            }
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BukuId.HasValue)
                        {
                            // Check if all babs are validated
                            var allBabsValidated = !await db.Antrians
                                .AnyAsync(a => a.BukuId == queue.BukuId && 
                                               a.AntrianTipe == "buku" && 
                                               a.AntrianValidationStatus != "completed" && 
                                               a.AntrianValidationStatus != "failed", stoppingToken);

                            if (allBabsValidated)
                            {
                                var buku = await db.Bukus.FindAsync(new object[] { (int)queue.BukuId.Value }, stoppingToken);
                                if (buku != null)
                                {
                                    buku.BukuStatus = "selesai";
                                    buku.BukuUpdatedAt = DateTime.Now;
                                    await wsService.NotifyBukuStatusChanged(buku.MhsNrp, (int)queue.BukuId.Value, "selesai");
                                    _logger.LogInformation("All babs validated for buku ID: {BukuId}", queue.BukuId);
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

    private async Task EnrichAndStoreErrorsAsync(
        KorektorBukuDbContext db,
        IGeminiService geminiService,
        uint dokumenId,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        if (errors.Count == 0)
            return;

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

        var errorsForGemini = errors.Take(GeminiErrorLimit).ToList();
        List<GeminiErrorDetail> geminiDetails = new();
        try
        {
            geminiDetails = await geminiService.GenerateErrorGuidanceAsync(
                errorsForGemini,
                aturanDetails,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate Gemini guidance for dokumen ID: {DokumenId}", dokumenId);
        }

        var detailByIndex = geminiDetails
            .Where(d => d.Index >= 0)
            .GroupBy(d => d.Index)
            .ToDictionary(g => g.Key, g => g.First());

        var kesalahanItems = new List<Kesalahan>();
        for (int i = 0; i < errors.Count; i++)
        {
            var error = errors[i];
            GeminiErrorDetail? detail = null;
            if (i < errorsForGemini.Count && detailByIndex.TryGetValue(i, out var found))
                detail = found;

            var title = !string.IsNullOrWhiteSpace(detail?.Title) ? detail!.Title : error.Message;
            title = Truncate(title, 255);

            var explanation = !string.IsNullOrWhiteSpace(detail?.Explanation)
                ? detail!.Explanation
                : BuildFallbackExplanation(error);

            var steps = detail?.Steps ?? new List<string>();
            var stepsJson = JsonSerializer.Serialize(steps);

            var category = string.IsNullOrWhiteSpace(error.Category) ? "Umum" : error.Category;
            category = Truncate(category, 100);

            kesalahanItems.Add(new Kesalahan
            {
                KesalahanKategori = category,
                KesalahanRefTipe = KesalahanRefTipe.dokumen,
                KesalahanRefId = dokumenId,
                KesalahanJudul = title,
                KesalahanPenjelasan = explanation,
                KesalahanLokasi = BuildKesalahanLokasi(error),
                KesalahanBboxVisual = null,
                KesalahanSteps = stepsJson
            });
        }

        if (kesalahanItems.Count > 0)
            db.Kesalahans.AddRange(kesalahanItems);
    }

    private static string? BuildKesalahanLokasi(ValidationError error)
    {
        var parts = new List<string>();

        if (error.PageNumber.HasValue)
            parts.Add($"halaman={error.PageNumber.Value}");

        if (error.SectionIndex.HasValue)
            parts.Add($"section={error.SectionIndex.Value}");

        if (!string.IsNullOrWhiteSpace(error.Field))
            parts.Add($"field={error.Field}");

        return parts.Count > 0 ? string.Join("; ", parts) : null;
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
}

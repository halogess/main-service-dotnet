using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ValidasiTugasAkhir.MainService.Services;

public class ValidationQueueBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ValidationQueueBackgroundService> _logger;

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
                                dokumen.DokumenStatus = "selesai";
                                dokumen.DokumenUpdatedAt = DateTime.Now;
                                await wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "selesai");
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
                                dokumen.DokumenStatus = "gagal";
                                dokumen.DokumenUpdatedAt = DateTime.Now;
                                await wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, (int)queue.DokumenId.Value, "gagal");
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
}

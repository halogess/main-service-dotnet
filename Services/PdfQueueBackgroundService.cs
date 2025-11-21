using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

public class PdfQueueBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PdfQueueBackgroundService> _logger;

    public PdfQueueBackgroundService(IServiceProvider serviceProvider, ILogger<PdfQueueBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[QUEUE] PDF Queue Background Service started");
        _logger.LogInformation("PDF Queue Background Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
                var pdfService = scope.ServiceProvider.GetRequiredService<IPdfConversionService>();
                var wsService = scope.ServiceProvider.GetRequiredService<IWebSocketService>();

                var queue = await db.Antrians
                    .Where(a => a.AntrianWorker == "convert_pdf" && a.AntrianConvertStatus == "in_queue")
                    .OrderBy(a => a.AntrianCreatedAt)
                    .FirstOrDefaultAsync(stoppingToken);

                if (queue != null)
                {
                    // Get file path from bab or dokumen
                    string filePath = "";
                    if (queue.AntrianTipe == "buku" && queue.BabId.HasValue)
                    {
                        var bab = await db.Babs.FindAsync(queue.BabId.Value);
                        filePath = bab?.BabDocxPath ?? "";
                    }
                    else if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                    {
                        var dokumen = await db.Dokumens.FindAsync((int)queue.DokumenId.Value);
                        filePath = dokumen?.DokumenDocxPath ?? "";
                    }

                    Console.WriteLine($"[QUEUE] Processing antrian ID: {queue.AntrianId}, File: {filePath}");
                    _logger.LogInformation("Processing antrian ID: {AntrianId}, File: {FilePath}", queue.AntrianId, filePath);

                    queue.AntrianConvertStatus = "processing";
                    queue.AntrianUpdatedAt = DateTime.UtcNow;

                    // Update status dokumen/buku ke "diproses"
                    if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                    {
                        var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                        if (dokumen != null)
                        {
                            dokumen.DokumenStatus = "diproses";
                            dokumen.DokumenUpdatedAt = DateTime.UtcNow;
                            Console.WriteLine($"[QUEUE] Updated dokumen ID: {queue.DokumenId}, status: diproses");
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
                            buku.BukuUpdatedAt = DateTime.UtcNow;
                            Console.WriteLine($"[QUEUE] Updated buku ID: {queue.BukuId}, status: diproses");
                            _logger.LogInformation("Updated buku ID: {BukuId}, status: diproses", queue.BukuId);
                            await wsService.NotifyBukuStatusChanged(buku.MhsNrp, (int)queue.BukuId.Value, "diproses");
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

                        var credential = await db.AdobeCredentials
                            .Where(c => c.AdobeCredentialsStatus == "active" && c.AdobeCredentialsQuotaUsed < c.AdobeCredentialsQuotaLimit)
                            .OrderBy(c => c.AdobeCredentialsQuotaUsed)
                            .FirstOrDefaultAsync(stoppingToken);

                        if (credential == null)
                        {
                            throw new InvalidOperationException("Tidak ada Adobe credentials yang tersedia atau quota habis");
                        }

                        Console.WriteLine($"[QUEUE] Using credential ID: {credential.AdobeCredentialsId}, Quota: {credential.AdobeCredentialsQuotaUsed}/{credential.AdobeCredentialsQuotaLimit}");
                        _logger.LogInformation("Using credential ID: {CredentialId}", credential.AdobeCredentialsId);
                        _logger.LogInformation("[TRACE] About to call PDF service with antrian_id: {AntrianId} (type: {Type})", queue.AntrianId, queue.AntrianId.GetType().Name);

                        var pdfBytes = await pdfService.ConvertDocxToPdfWithCredential(fullFilePath, credential.AdobeClientId, credential.AdobeClientSecret, credential.AdobeCredentialsId, queue.AntrianId);

                        credential.AdobeCredentialsQuotaUsed++;
                        credential.AdobeCredentialsUpdatedAt = DateTime.UtcNow;

                        var fileService = scope.ServiceProvider.GetRequiredService<IFileService>();
                        var pdfPath = fileService.GetPdfPath(filePath);
                        var fullPdfPath = Path.Combine(storagePath, pdfPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPdfPath)!);
                        await File.WriteAllBytesAsync(fullPdfPath, pdfBytes, stoppingToken);

                        queue.AntrianConvertStatus = "completed";
                        queue.AntrianVisualStatus = "in_queue";
                        queue.AntrianUpdatedAt = DateTime.UtcNow;
                        queue.AntrianErrorMessage = null;
                        Console.WriteLine($"[QUEUE] Completed antrian ID: {queue.AntrianId}, PDF: {pdfPath}, Visual status: in_queue");
                        _logger.LogInformation("Completed antrian ID: {AntrianId}, PDF: {PdfPath}, Visual status: in_queue", queue.AntrianId, pdfPath);

                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                dokumen.DokumenPdfPath = pdfPath;
                                dokumen.DokumenUpdatedAt = DateTime.UtcNow;
                                Console.WriteLine($"[QUEUE] Updated dokumen ID: {queue.DokumenId}, pdf_path: {pdfPath}");
                                _logger.LogInformation("Updated dokumen ID: {DokumenId}, pdf_path: {PdfPath}", queue.DokumenId, pdfPath);
                            }
                        }
                        else if (queue.AntrianTipe == "buku" && queue.BabId.HasValue)
                        {
                            var bab = await db.Babs.FindAsync(new object[] { queue.BabId.Value }, stoppingToken);
                            if (bab != null)
                            {
                                bab.BabPdfPath = pdfPath;
                                Console.WriteLine($"[QUEUE] Updated bab ID: {queue.BabId}, pdf_path: {pdfPath}");
                                _logger.LogInformation("Updated bab ID: {BabId}, pdf_path: {PdfPath}", queue.BabId, pdfPath);

                                // Cek apakah semua bab sudah selesai convert
                                var allBabsCompleted = !await db.Antrians
                                    .AnyAsync(a => a.BukuId == queue.BukuId && 
                                                   a.AntrianTipe == "buku" && 
                                                   a.AntrianWorker == "convert_pdf" && 
                                                   a.AntrianConvertStatus != "completed" && 
                                                   a.AntrianConvertStatus != "failed", stoppingToken);

                                if (allBabsCompleted && queue.BukuId.HasValue)
                                {
                                    var buku = await db.Bukus.FindAsync(new object[] { (int)queue.BukuId.Value }, stoppingToken);
                                    if (buku != null)
                                    {
                                        buku.BukuStatus = "selesai_convert";
                                        buku.BukuUpdatedAt = DateTime.UtcNow;
                                        Console.WriteLine($"[QUEUE] All babs completed for buku ID: {queue.BukuId}, status: selesai_convert");
                                        _logger.LogInformation("All babs completed for buku ID: {BukuId}", queue.BukuId);
                                    }
                                }
                            }
                        }
                    }
                    catch (FileNotFoundException ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.UtcNow;
                        queue.AntrianErrorMessage = $"File tidak ditemukan: {ex.Message}";
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (HttpRequestException ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.UtcNow;
                        queue.AntrianErrorMessage = $"Adobe API error: {ex.Message}";
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.UtcNow;
                        queue.AntrianErrorMessage = $"Konversi gagal: {ex.Message}";
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (Exception ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.UtcNow;
                        queue.AntrianErrorMessage = ex.Message.Length > 255 ? ex.Message.Substring(0, 252) + "..." : ex.Message;
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }

                    await db.SaveChangesAsync(stoppingToken);
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
                Console.WriteLine($"[QUEUE] Error: {ex.Message}");
                _logger.LogError(ex, "Error in PDF Queue Background Service");
                
                // Don't crash service, wait and retry
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

        Console.WriteLine("[QUEUE] PDF Queue Background Service stopped");
        _logger.LogInformation("PDF Queue Background Service stopped");
    }
}

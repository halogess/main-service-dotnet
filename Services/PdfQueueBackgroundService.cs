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
                    string filePath = queue.AntrianTipe == "buku" && queue.BabId.HasValue
                        ? (await db.Babs.FindAsync(queue.BabId.Value))?.BabDocxPath ?? ""
                        : queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue
                            ? (await db.Dokumens.FindAsync((int)queue.DokumenId.Value))?.DokumenDocxPath ?? ""
                            : "";

                    _logger.LogInformation("Processing antrian ID: {AntrianId}, File: {FilePath}", queue.AntrianId, filePath);

                    queue.AntrianConvertStatus = "processing";
                    queue.AntrianUpdatedAt = DateTime.Now;

                    if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                    {
                        var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                        if (dokumen != null)
                        {
                            dokumen.DokumenStatus = "diproses";
                            dokumen.DokumenUpdatedAt = DateTime.Now;
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
                            buku.BukuUpdatedAt = DateTime.Now;
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

                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var docxExtraction = scope.ServiceProvider.GetRequiredService<IDocxExtractionService>();
                            await docxExtraction.ExtractDocxToDatabase(fullFilePath, (int)queue.DokumenId.Value);
                            _logger.LogInformation("Extracted DOCX elements for dokumen ID: {DokumenId}", queue.DokumenId);
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

                        var pdfBytes = await pdfService.ConvertDocxToPdfWithCredential(fullFilePath, credential.AdobeClientId, credential.AdobeClientSecret, credential.AdobeCredentialsId, queue.AntrianId);

                        credential.AdobeCredentialsQuotaUsed++;
                        credential.AdobeCredentialsUpdatedAt = DateTime.Now;

                        var fileService = scope.ServiceProvider.GetRequiredService<IFileService>();
                        var pdfPath = fileService.GetPdfPath(filePath);
                        var fullPdfPath = Path.Combine(storagePath, pdfPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPdfPath)!);
                        await File.WriteAllBytesAsync(fullPdfPath, pdfBytes, stoppingToken);

                        queue.AntrianConvertStatus = "completed";
                        queue.AntrianVisualStatus = "in_queue";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = null;
                        _logger.LogInformation("Completed antrian ID: {AntrianId}, PDF: {PdfPath}", queue.AntrianId, pdfPath);

                        if (queue.AntrianTipe == "dokumen" && queue.DokumenId.HasValue)
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { (int)queue.DokumenId.Value }, stoppingToken);
                            if (dokumen != null)
                            {
                                dokumen.DokumenPdfPath = pdfPath;
                                dokumen.DokumenUpdatedAt = DateTime.Now;
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
                                                   a.AntrianWorker == "convert_pdf" && 
                                                   a.AntrianConvertStatus != "completed" && 
                                                   a.AntrianConvertStatus != "failed", stoppingToken);

                                if (allBabsCompleted && queue.BukuId.HasValue)
                                {
                                    var buku = await db.Bukus.FindAsync(new object[] { (int)queue.BukuId.Value }, stoppingToken);
                                    if (buku != null)
                                    {
                                        buku.BukuStatus = "selesai_convert";
                                        buku.BukuUpdatedAt = DateTime.Now;
                                        _logger.LogInformation("All babs completed for buku ID: {BukuId}", queue.BukuId);
                                    }
                                }
                            }
                        }
                    }
                    catch (FileNotFoundException ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = $"File tidak ditemukan: {ex.Message}";
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (HttpRequestException ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = $"Adobe API error: {ex.Message}";
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = $"Konversi gagal: {ex.Message}";
                        _logger.LogError(ex, "Failed antrian ID: {AntrianId}", queue.AntrianId);
                    }
                    catch (Exception ex)
                    {
                        queue.AntrianConvertStatus = "failed";
                        queue.AntrianUpdatedAt = DateTime.Now;
                        queue.AntrianErrorMessage = ex.Message.Length > 255 ? ex.Message[..252] + "..." : ex.Message;
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
}

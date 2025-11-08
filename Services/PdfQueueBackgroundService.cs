using _.Models;
using Microsoft.EntityFrameworkCore;

namespace _.Services;

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

                var queue = await db.AntrianPdfs
                    .Where(a => a.AntrianPdfStatus == "in_queue")
                    .OrderBy(a => a.AntrianPdfId)
                    .FirstOrDefaultAsync(stoppingToken);

                if (queue != null)
                {
                    Console.WriteLine($"[QUEUE] Processing queue ID: {queue.AntrianPdfId}, File: {queue.FilePath}");
                    _logger.LogInformation("Processing queue ID: {QueueId}, File: {FilePath}", queue.AntrianPdfId, queue.FilePath);

                    queue.AntrianPdfStatus = "processing";
                    queue.AntrianPdfUpdatedAt = DateTime.Now;

                    // Update status dokumen ke "diproses"
                    if (queue.AntrianPdfTipe == "dokumen")
                    {
                        var filePathParts = queue.FilePath.Split(Path.DirectorySeparatorChar);
                        var dokumenFilename = filePathParts[^1];
                        var dokumenIdStr = dokumenFilename.Split('_')[0];
                        if (int.TryParse(dokumenIdStr, out int dokumenId))
                        {
                            var dokumen = await db.Dokumens.FindAsync(new object[] { dokumenId }, stoppingToken);
                            if (dokumen != null)
                            {
                                dokumen.DokumenStatus = "diproses";
                                dokumen.DokumenUpdatedAt = DateTime.Now;
                                Console.WriteLine($"[QUEUE] Updated dokumen ID: {dokumenId}, status: diproses");
                                _logger.LogInformation("Updated dokumen ID: {DokumenId}, status: diproses", dokumenId);
                            }
                        }
                    }

                    await db.SaveChangesAsync(stoppingToken);

                    try
                    {
                        if (!File.Exists(queue.FilePath))
                        {
                            throw new FileNotFoundException($"File tidak ditemukan: {queue.FilePath}");
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

                        var pdfBytes = await pdfService.ConvertDocxToPdfWithCredential(queue.FilePath, credential.AdobeClientId, credential.AdobeClientSecret, credential.AdobeCredentialsId);

                        credential.AdobeCredentialsQuotaUsed++;
                        credential.AdobeCredentialsUpdatedAt = DateTime.Now;

                        var pathParts = queue.FilePath.Split(Path.DirectorySeparatorChar);
                        var nrp = pathParts[^2];
                        var filename = Path.ChangeExtension(pathParts[^1], ".pdf");
                        var pdfDir = Path.Combine("pdf", nrp);
                        Directory.CreateDirectory(pdfDir);
                        var pdfPath = Path.Combine(pdfDir, filename);
                        await File.WriteAllBytesAsync(pdfPath, pdfBytes, stoppingToken);

                        queue.AntrianPdfStatus = "completed";
                        queue.AntrianPdfUpdatedAt = DateTime.Now;
                        queue.AntrianPdfFailedReason = null;
                        Console.WriteLine($"[QUEUE] Completed queue ID: {queue.AntrianPdfId}, PDF: {pdfPath}, Credential quota: {credential.AdobeCredentialsQuotaUsed}/{credential.AdobeCredentialsQuotaLimit}");
                        _logger.LogInformation("Completed queue ID: {QueueId}, PDF: {PdfPath}", queue.AntrianPdfId, pdfPath);

                        if (queue.AntrianPdfTipe == "dokumen")
                        {
                            var filePathParts = queue.FilePath.Split(Path.DirectorySeparatorChar);
                            var dokumenFilename = filePathParts[^1];
                            var dokumenIdStr = dokumenFilename.Split('_')[0];
                            if (int.TryParse(dokumenIdStr, out int dokumenId))
                            {
                                var dokumen = await db.Dokumens.FindAsync(new object[] { dokumenId }, stoppingToken);
                                if (dokumen != null)
                                {
                                    dokumen.DokumenPdfPath = pdfPath;
                                    dokumen.DokumenUpdatedAt = DateTime.Now;
                                    Console.WriteLine($"[QUEUE] Updated dokumen ID: {dokumenId}, pdf_path: {pdfPath}");
                                    _logger.LogInformation("Updated dokumen ID: {DokumenId}, pdf_path: {PdfPath}", dokumenId, pdfPath);
                                }

                                var antrian = await db.Antrians
                                    .Where(a => a.DokumenId == dokumenId && a.AntrianWorker == "visual" && a.AntrianStatus == "not_start")
                                    .FirstOrDefaultAsync(stoppingToken);

                                if (antrian != null)
                                {
                                    antrian.AntrianStatus = "in_queue";
                                    antrian.AntrianUpdatedAt = DateTime.Now;
                                    Console.WriteLine($"[QUEUE] Updated antrian ID: {antrian.AntrianId}, worker: visual, status: in_queue");
                                    _logger.LogInformation("Updated antrian ID: {AntrianId}, worker: visual, status: in_queue", antrian.AntrianId);
                                }
                            }
                        }
                    }
                    catch (FileNotFoundException ex)
                    {
                        queue.AntrianPdfStatus = "failed";
                        queue.AntrianPdfUpdatedAt = DateTime.Now;
                        queue.AntrianPdfFailedReason = $"File tidak ditemukan: {ex.Message}";
                        Console.WriteLine($"[QUEUE] Failed queue ID: {queue.AntrianPdfId}, Reason: {queue.AntrianPdfFailedReason}");
                        _logger.LogError(ex, "Failed queue ID: {QueueId}", queue.AntrianPdfId);
                    }
                    catch (HttpRequestException ex)
                    {
                        queue.AntrianPdfStatus = "failed";
                        queue.AntrianPdfUpdatedAt = DateTime.Now;
                        queue.AntrianPdfFailedReason = $"Adobe API error: {ex.Message}";
                        Console.WriteLine($"[QUEUE] Failed queue ID: {queue.AntrianPdfId}, Reason: {queue.AntrianPdfFailedReason}");
                        _logger.LogError(ex, "Failed queue ID: {QueueId}", queue.AntrianPdfId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        queue.AntrianPdfStatus = "failed";
                        queue.AntrianPdfUpdatedAt = DateTime.Now;
                        queue.AntrianPdfFailedReason = $"Konversi gagal: {ex.Message}";
                        Console.WriteLine($"[QUEUE] Failed queue ID: {queue.AntrianPdfId}, Reason: {queue.AntrianPdfFailedReason}");
                        _logger.LogError(ex, "Failed queue ID: {QueueId}", queue.AntrianPdfId);
                    }
                    catch (Exception ex)
                    {
                        queue.AntrianPdfStatus = "failed";
                        queue.AntrianPdfUpdatedAt = DateTime.Now;
                        queue.AntrianPdfFailedReason = ex.Message.Length > 100 ? ex.Message.Substring(0, 97) + "..." : ex.Message;
                        Console.WriteLine($"[QUEUE] Failed queue ID: {queue.AntrianPdfId}, Reason: {queue.AntrianPdfFailedReason}");
                        _logger.LogError(ex, "Failed queue ID: {QueueId}", queue.AntrianPdfId);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }

                await Task.Delay(5000, stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QUEUE] Error: {ex.Message}");
                _logger.LogError(ex, "Error in PDF Queue Background Service");
                await Task.Delay(10000, stoppingToken);
            }
        }

        Console.WriteLine("[QUEUE] PDF Queue Background Service stopped");
        _logger.LogInformation("PDF Queue Background Service stopped");
    }
}

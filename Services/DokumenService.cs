using _.Models;
using Microsoft.AspNetCore.Http;

namespace _.Services;

public interface IDokumenService
{
    Task<Dokumen> UploadDokumen(string nrp, IFormFile file);
    Task<Dokumen> UpdateStatus(int dokumenId, string status);
}

public class DokumenService : IDokumenService
{
    private readonly IFileService _fileService;
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<DokumenService> _logger;
    private readonly IWebSocketService _wsService;

    public DokumenService(IFileService fileService, KorektorBukuDbContext db, ILogger<DokumenService> logger, IWebSocketService wsService)
    {
        _fileService = fileService;
        _db = db;
        _logger = logger;
        _wsService = wsService;
    }

    public async Task<Dokumen> UploadDokumen(string nrp, IFormFile file)
    {
        Console.WriteLine($"[UPLOAD] Upload dokumen dimulai: NRP={nrp}, File={file.FileName}");
        _logger.LogInformation("Upload dokumen dimulai: NRP={Nrp}, File={FileName}", nrp, file.FileName);
        
        _fileService.ValidateExtension(file.FileName);
        await _fileService.ValidateDocumentSource(file);
        
        var dokumen = new Dokumen
        {
            MhsNrp = nrp,
            DokumenFilename = "",
            DokumenFilesizeBytes = file.Length,
            DokumenStatus = "dalam_antrian",
            DokumenCreatedAt = DateTime.Now,
            DokumenUpdatedAt = DateTime.Now
        };

        _db.Dokumens.Add(dokumen);
        await _db.SaveChangesAsync();
        Console.WriteLine($"[UPLOAD] Dokumen tersimpan di database: ID={dokumen.DokumenId}");
        _logger.LogInformation("Dokumen tersimpan di database: ID={DokumenId}", dokumen.DokumenId);
        
        var filename = await _fileService.SaveFile(file, nrp, dokumen.DokumenId);
        dokumen.DokumenFilename = filename;
        dokumen.DokumenDocxPath = Path.Combine("uploads", nrp, filename);
        await _db.SaveChangesAsync();
        Console.WriteLine($"[UPLOAD] File tersimpan: {filename}");
        _logger.LogInformation("File tersimpan: {FileName}", filename);

        var extension = Path.GetExtension(file.FileName).ToLower();
        if (extension == ".docx")
        {
            Console.WriteLine("[UPLOAD] Menambahkan ke antrian PDF");
            _logger.LogInformation("Menambahkan ke antrian PDF");
            var uploadDir = Path.Combine("uploads", nrp);
            var filePath = Path.Combine(uploadDir, filename);
            
            var antrianPdf = new AntrianPdf
            {
                AntrianPdfTipe = "dokumen",
                FilePath = filePath,
                AntrianPdfStatus = "in_queue",
                AntrianPdfCreatedAt = DateTime.Now,
                AntrianPdfUpdatedAt = DateTime.Now
            };
            _db.AntrianPdfs.Add(antrianPdf);
            
            var antrian = new Antrian
            {
                AntrianTipe = "dokumen",
                DokumenId = dokumen.DokumenId,
                AntrianWorker = "visual",
                AntrianStatus = "not_start",
                AntrianCreatedAt = DateTime.Now,
                AntrianUpdatedAt = DateTime.Now
            };
            _db.Antrians.Add(antrian);
            
            await _db.SaveChangesAsync();
            Console.WriteLine($"[UPLOAD] Antrian PDF dibuat: ID={antrianPdf.AntrianPdfId}, Antrian visual ID={antrian.AntrianId}");
            _logger.LogInformation("Antrian PDF dibuat: ID={AntrianPdfId}, Antrian visual ID={AntrianId}", antrianPdf.AntrianPdfId, antrian.AntrianId);
        }

        Console.WriteLine($"[UPLOAD] Upload dokumen selesai: ID={dokumen.DokumenId}");
        _logger.LogInformation("Upload dokumen selesai: ID={DokumenId}", dokumen.DokumenId);
        return dokumen;
    }

    public async Task<Dokumen> UpdateStatus(int dokumenId, string status)
    {
        var dokumen = await _db.Dokumens.FindAsync(dokumenId);
        
        if (dokumen == null)
        {
            throw new InvalidOperationException("Dokumen tidak ditemukan");
        }

        var validStatuses = new[] { "dibatalkan", "dalam_antrian", "diproses", "lolos", "tidak_lolos" };
        if (!validStatuses.Contains(status))
        {
            throw new InvalidOperationException($"Status tidak valid. Harus salah satu dari: {string.Join(", ", validStatuses)}");
        }

        dokumen.DokumenStatus = status;
        dokumen.DokumenUpdatedAt = DateTime.Now;
        
        await _db.SaveChangesAsync();
        await _wsService.NotifyDokumenStatusChanged(dokumen.MhsNrp!, dokumenId, status);

        return dokumen;
    }
}

using ValidasiTugasAkhir.MainService.Models;
using Microsoft.AspNetCore.Http;

namespace ValidasiTugasAkhir.MainService.Services;

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
        _logger.LogInformation("Upload dokumen dimulai: NRP={Nrp}, File={FileName}", nrp, file.FileName);
        
        _fileService.ValidateExtension(file.FileName);
        await _fileService.ValidateDocumentSource(file);
        
        var dokumen = new Dokumen
        {
            MhsNrp = nrp,
            DokumenFilename = file.FileName,
            DokumenFilesizeBytes = file.Length,
            DokumenStatus = "dalam_antrian",
            DokumenCreatedAt = DateTime.Now,
            DokumenUpdatedAt = DateTime.Now
        };
        
        _db.Dokumens.Add(dokumen);
        await _db.SaveChangesAsync();
        
        var filePath = await _fileService.SaveFile(file, nrp, dokumen.DokumenId, "dokumen");
        dokumen.DokumenDocxPath = filePath;
        
        var antrian = new Antrian
        {
            AntrianTipe = "dokumen",
            DokumenId = (uint)dokumen.DokumenId,
            AntrianWorker = "convert_pdf",
            AntrianConvertStatus = "in_queue",
            AntrianCreatedAt = DateTime.Now,
            AntrianUpdatedAt = DateTime.Now
        };
        _db.Antrians.Add(antrian);
        await _db.SaveChangesAsync();
        
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

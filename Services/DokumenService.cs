using _.Models;
using Microsoft.AspNetCore.Http;

namespace _.Services;

public interface IDokumenService
{
    Task<Dokumen> UploadDokumen(string nrp, IFormFile file);
    Task<Dokumen> UpdateStatus(int dokumenId, byte status);
}

public class DokumenService : IDokumenService
{
    private readonly IFileService _fileService;
    private readonly KorektorBukuDbContext _db;

    public DokumenService(IFileService fileService, KorektorBukuDbContext db)
    {
        _fileService = fileService;
        _db = db;
    }

    public async Task<Dokumen> UploadDokumen(string nrp, IFormFile file)
    {
        _fileService.ValidateExtension(file.FileName);
        await _fileService.ValidateDocumentSource(file);
        
        var dokumen = new Dokumen
        {
            MhsNrp = nrp,
            DokumenFilename = "",
            DokumenStatus = 1,
            DokumenCreatedAt = DateTime.Now,
            DokumenUpdatedAt = DateTime.Now
        };

        _db.Dokumens.Add(dokumen);
        await _db.SaveChangesAsync();
        
        var filename = await _fileService.SaveFile(file, nrp, dokumen.DokumenId);
        dokumen.DokumenFilename = filename;
        await _db.SaveChangesAsync();

        return dokumen;
    }

    public async Task<Dokumen> UpdateStatus(int dokumenId, byte status)
    {
        var dokumen = await _db.Dokumens.FindAsync(dokumenId);
        
        if (dokumen == null)
        {
            throw new InvalidOperationException("Dokumen tidak ditemukan");
        }

        if (status != 0 && status != 1)
        {
            throw new InvalidOperationException("Status harus 0 (ditolak) atau 1 (diterima)");
        }

        dokumen.DokumenStatus = status;
        dokumen.DokumenUpdatedAt = DateTime.Now;
        
        await _db.SaveChangesAsync();

        return dokumen;
    }
}

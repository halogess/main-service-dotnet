using _.Models;
using Microsoft.AspNetCore.Http;

namespace _.Services;

public interface IDokumenService
{
    Task<Dokumen> UploadDokumen(string nrp, IFormFile file);
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
        
        var filename = await _fileService.SaveFile(file, nrp);

        var dokumen = new Dokumen
        {
            MhsNrp = nrp,
            DokumenFilename = filename,
            DokumenStatus = 0,
            DokumenCreatedAt = DateTime.Now,
            DokumenUpdatedAt = DateTime.Now
        };

        _db.Dokumens.Add(dokumen);
        await _db.SaveChangesAsync();

        return dokumen;
    }
}

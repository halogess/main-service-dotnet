using _.Models;
using Microsoft.AspNetCore.Http;

namespace _.Services;

public interface IBukuService
{
    Task<Buku> UploadBuku(string nrp, string judul, List<IFormFile> files);
}

public class BukuService : IBukuService
{
    private readonly IFileService _fileService;
    private readonly KorektorBukuDbContext _db;

    public BukuService(IFileService fileService, KorektorBukuDbContext db)
    {
        _fileService = fileService;
        _db = db;
    }

    public async Task<Buku> UploadBuku(string nrp, string judul, List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
        {
            throw new InvalidOperationException("Buku harus memiliki minimal 1 dokumen");
        }

        var buku = new Buku
        {
            MhsNrp = nrp,
            BukuJudul = judul,
            BukuStatus = "dalam_antrian",
            BukuJumlahBab = files.Count,
            BukuCreatedAt = DateTime.Now,
            BukuUpdatedAt = DateTime.Now
        };

        _db.Bukus.Add(buku);
        await _db.SaveChangesAsync();

        foreach (var file in files)
        {
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
            
            var filename = await _fileService.SaveFile(file, nrp, dokumen.DokumenId);
            dokumen.DokumenFilename = filename;
        }
        
        await _db.SaveChangesAsync();

        return buku;
    }
}

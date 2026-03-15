using ValidasiTugasAkhir.MainService.Models;
using _.Services;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IBukuService
{
    Task<Buku> UploadBuku(string nrp, string judul, List<IFormFile> files);
}

public class BukuService : IBukuService
{
    private readonly IFileService _fileService;
    private readonly IBukuArchiveService _bukuArchiveService;
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<BukuService> _logger;
    private readonly IWebSocketService _wsService;

    public BukuService(
        IFileService fileService,
        IBukuArchiveService bukuArchiveService,
        KorektorBukuDbContext db,
        ILogger<BukuService> logger,
        IWebSocketService wsService)
    {
        _fileService = fileService;
        _bukuArchiveService = bukuArchiveService;
        _db = db;
        _logger = logger;
        _wsService = wsService;
    }

    public async Task<Buku> UploadBuku(string nrp, string judul, List<IFormFile> files)
    {
        _logger.LogInformation("Upload buku dimulai: NRP={Nrp}, Judul={Judul}, Jumlah file={Count}", nrp, judul, files.Count);

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
        _logger.LogInformation("Buku tersimpan di database: ID={BukuId}", buku.BukuId);

        byte babOrder = 1;
        foreach (var file in files)
        {
            try
            {
                _logger.LogInformation("Processing file {BabOrder}: {FileName}", babOrder, file.FileName);

                _fileService.ValidateExtension(file.FileName);
                await _fileService.ValidateDocumentSource(file);

                var bab = new Bab
                {
                    BukuId = (uint)buku.BukuId,
                    BabOrder = babOrder,
                    BabTipe = "bab",
                    BabImagesPath = Path.Combine("buku", nrp, buku.BukuId.ToString(), "images", babOrder.ToString()).Replace('\\', '/'),
                    BabFilename = ""
                };

                _db.Babs.Add(bab);
                await _db.SaveChangesAsync();

                var docxPath = await _fileService.SaveFile(file, nrp, buku.BukuId, "buku");
                bab.BabDocxPath = docxPath;
                bab.BabFilename = Path.GetFileName(docxPath);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Bab tersimpan: ID={BabId}, Order={BabOrder}", bab.BabId, bab.BabOrder);

                var antrian = new Antrian
                {
                    AntrianTipe = "buku",
                    BukuId = (uint)buku.BukuId,
                    BabId = bab.BabId,
                    AntrianExtractionStatus = "in_queue",
                    AntrianCreatedAt = DateTime.Now,
                    AntrianUpdatedAt = DateTime.Now
                };
                _db.Antrians.Add(antrian);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Antrian dibuat untuk bab: ID={AntrianId}", antrian.AntrianId);
                
                babOrder++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file {BabOrder}", babOrder);
                throw;
            }
        }

        var docxArchivePath = await _bukuArchiveService.RefreshDocxArchiveAsync(buku.BukuId);
        if (!string.IsNullOrWhiteSpace(docxArchivePath))
        {
            await _wsService.NotifyBukuArchiveReady(nrp, buku.BukuId, docxReady: true, pdfReady: false);
        }

        _logger.LogInformation("Upload buku selesai: ID={BukuId}", buku.BukuId);
        return buku;
    }
}

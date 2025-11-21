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
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<BukuService> _logger;
    private readonly IWebSocketService _wsService;

    public BukuService(IFileService fileService, KorektorBukuDbContext db, ILogger<BukuService> logger, IWebSocketService wsService)
    {
        _fileService = fileService;
        _db = db;
        _logger = logger;
        _wsService = wsService;
    }

    public async Task<Buku> UploadBuku(string nrp, string judul, List<IFormFile> files)
    {
        Console.WriteLine($"[UPLOAD] Upload buku dimulai: NRP={nrp}, Judul={judul}, Jumlah file={files.Count}");
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
        Console.WriteLine($"[UPLOAD] Buku tersimpan di database: ID={buku.BukuId}");
        _logger.LogInformation("Buku tersimpan di database: ID={BukuId}", buku.BukuId);

        byte babOrder = 1;
        foreach (var file in files)
        {
            try
            {
                Console.WriteLine($"[UPLOAD] Processing file {babOrder}: {file.FileName}");
                _fileService.ValidateExtension(file.FileName);
                await _fileService.ValidateDocumentSource(file);

                var bab = new Bab
                {
                    BukuId = (uint)buku.BukuId,
                    BabOrder = babOrder,
                    BabFilename = ""
                };

                _db.Babs.Add(bab);
                await _db.SaveChangesAsync();
                Console.WriteLine($"[UPLOAD] Bab created: ID={bab.BabId}, Order={bab.BabOrder}");

                var filename = await _fileService.SaveFile(file, nrp, (int)bab.BabId);
                bab.BabFilename = filename;
                bab.BabDocxPath = Path.Combine("uploads", nrp, filename);
                await _db.SaveChangesAsync();

                Console.WriteLine($"[UPLOAD] Bab tersimpan: ID={bab.BabId}, Order={bab.BabOrder}, File={filename}");
                _logger.LogInformation("Bab tersimpan: ID={BabId}, Order={BabOrder}", bab.BabId, bab.BabOrder);

                var antrian = new Antrian
                {
                    AntrianTipe = "buku",
                    BukuId = (uint)buku.BukuId,
                    BabId = bab.BabId,
                    AntrianWorker = "convert_pdf",
                    AntrianConvertStatus = "in_queue",
                    AntrianCreatedAt = DateTime.Now,
                    AntrianUpdatedAt = DateTime.Now
                };
                _db.Antrians.Add(antrian);

                await _db.SaveChangesAsync();
                Console.WriteLine($"[UPLOAD] Antrian created: ID={antrian.AntrianId}");
                _logger.LogInformation("Antrian dibuat untuk bab: ID={AntrianId}", antrian.AntrianId);
                
                babOrder++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPLOAD] Error processing file {babOrder}: {ex.Message}");
                _logger.LogError(ex, "Error processing file {BabOrder}", babOrder);
                throw;
            }
        }

        Console.WriteLine($"[UPLOAD] Upload buku selesai: ID={buku.BukuId}");
        _logger.LogInformation("Upload buku selesai: ID={BukuId}", buku.BukuId);
        return buku;
    }
}

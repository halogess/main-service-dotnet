using ValidasiTugasAkhir.MainService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IDokumenService
{
    Task<Dokumen> UploadDokumen(string nrp, IFormFile file, string? tipe = null);
    Task<Dokumen> UpdateStatus(int dokumenId, string status);
    Task<Dokumen> BatalDokumen(string nrp, int dokumenId);
    bool CanUpload(string nrp);
    DokumenStats GetStats(string nrp);
    DokumenListResult GetDokumenList(string nrp, string? status, string sort, int limit, int offset);
    bool HasDokumenInQueue(string nrp);
}

public class DokumenStats
{
    public int Total { get; set; }
    public int Dibatalkan { get; set; }
    public int DalamAntrian { get; set; }
    public int Diproses { get; set; }
    public int Lolos { get; set; }
    public int TidakLolos { get; set; }
}

public class DokumenListItem
{
    public int Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public DateTime? TanggalUpload { get; set; }
    public long UkuranFile { get; set; }
    public string Status { get; set; } = string.Empty;
    public int JumlahKesalahan { get; set; }
}

public class DokumenListResult
{
    public List<DokumenListItem> Data { get; set; } = new();
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
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

    public async Task<Dokumen> UploadDokumen(string nrp, IFormFile file, string? tipe = null)
    {
        _logger.LogInformation("Upload dokumen dimulai: NRP={Nrp}, File={FileName}, Tipe={Tipe}", nrp, file.FileName, tipe);
        
        // Validasi tipe jika diberikan
        if (!string.IsNullOrEmpty(tipe))
        {
            var validTipes = new[] { "awal", "isi", "akhir", "lampiran" };
            if (!validTipes.Contains(tipe.ToLower()))
            {
                throw new InvalidOperationException($"Tipe tidak valid. Harus salah satu dari: {string.Join(", ", validTipes)}");
            }
            tipe = tipe.ToLower(); // Normalize to lowercase
        }
        
        _fileService.ValidateExtension(file.FileName);
        await _fileService.ValidateDocumentSource(file);
        
        var dokumen = new Dokumen
        {
            MhsNrp = nrp,
            DokumenFilename = file.FileName,
            DokumenFilesizeBytes = file.Length,
            DokumenStatus = "dalam_antrian",
            DokumenTipe = tipe,
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
            AntrianExtractionStatus = "in_queue",
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

    public async Task<Dokumen> BatalDokumen(string nrp, int dokumenId)
    {
        var dokumen = _db.Dokumens.FirstOrDefault(d => d.DokumenId == dokumenId && d.MhsNrp == nrp);
        
        if (dokumen == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan");
        
        if (dokumen.DokumenStatus != "dalam_antrian")
            throw new InvalidOperationException("Hanya dokumen dalam antrian yang bisa dibatalkan");
        
        dokumen.DokumenStatus = "dibatalkan";
        dokumen.DokumenUpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _wsService.NotifyDokumenCancelled(nrp, dokumenId);
        
        _logger.LogInformation("Dokumen dibatalkan: ID={DokumenId}, NRP={Nrp}", dokumenId, nrp);
        return dokumen;
    }

    public bool CanUpload(string nrp)
    {
        return !HasDokumenInQueue(nrp);
    }

    public bool HasDokumenInQueue(string nrp)
    {
        return _db.Dokumens.Any(d => d.MhsNrp == nrp && d.DokumenStatus == "dalam_antrian");
    }

    public DokumenStats GetStats(string nrp)
    {
        var dokumens = _db.Dokumens.Where(d => d.MhsNrp == nrp).ToList();
        var grouped = dokumens.GroupBy(d => d.DokumenStatus).ToDictionary(g => g.Key ?? "unknown", g => g.Count());
        
        return new DokumenStats
        {
            Total = dokumens.Count,
            Dibatalkan = grouped.GetValueOrDefault("dibatalkan", 0),
            DalamAntrian = grouped.GetValueOrDefault("dalam_antrian", 0),
            Diproses = grouped.GetValueOrDefault("diproses", 0),
            Lolos = grouped.GetValueOrDefault("lolos", 0),
            TidakLolos = grouped.GetValueOrDefault("tidak_lolos", 0)
        };
    }

    public DokumenListResult GetDokumenList(string nrp, string? status, string sort, int limit, int offset)
    {
        var query = _db.Dokumens.Where(d => d.MhsNrp == nrp);
        
        if (!string.IsNullOrEmpty(status))
        {
            var statuses = status.Split(',').Select(s => s.Trim()).ToList();
            query = query.Where(d => statuses.Contains(d.DokumenStatus));
        }
        
        query = sort.ToLower() == "asc" 
            ? query.OrderBy(d => d.DokumenCreatedAt)
            : query.OrderByDescending(d => d.DokumenCreatedAt);
        
        var totalCount = query.Count();
        
        var dokumenList = query
            .Skip(offset)
            .Take(limit)
            .Select(d => new DokumenListItem
            {
                Id = d.DokumenId,
                Filename = d.DokumenFilename,
                TanggalUpload = d.DokumenCreatedAt,
                UkuranFile = d.DokumenFilesizeBytes ?? 0L,
                Status = d.DokumenStatus,
                JumlahKesalahan = d.DokumenJumlahKesalahan ?? 0
            })
            .ToList();

        return new DokumenListResult
        {
            Data = dokumenList,
            Total = totalCount,
            Limit = limit,
            Offset = offset
        };
    }
}

using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IAturanService
{
    Task<List<Aturan>> GetAllAsync();
    Task<Aturan?> GetByIdAsync(uint id);
    Task<AturanWithDetails?> GetByIdWithDetailsAsync(uint id);
    Task<Aturan?> GetAktifAsync();
    Task<AturanWithDetails?> GetAktifWithDetailsAsync();
    Task<Aturan> CreateAsync(string versi, sbyte status, uint skorMinimum, string? templateFilePath);
    Task<Aturan> UpdateAsync(uint id, string? versi, sbyte? status, uint? skorMinimum, string? templateFilePath);
}

public class AturanWithDetails
{
    public Aturan Aturan { get; set; } = null!;
    public List<AturanDetail> Details { get; set; } = new();
}

public class AturanService : IAturanService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<AturanService> _logger;

    public AturanService(KorektorBukuDbContext db, ILogger<AturanService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<Aturan>> GetAllAsync()
    {
        return await _db.Aturans
            .OrderByDescending(a => a.AturanCreatedAt)
            .ToListAsync();
    }

    public async Task<Aturan?> GetByIdAsync(uint id)
    {
        return await _db.Aturans.FindAsync(id);
    }

    public async Task<AturanWithDetails?> GetByIdWithDetailsAsync(uint id)
    {
        var aturan = await _db.Aturans.FindAsync(id);
        if (aturan == null)
            return null;

        var details = await _db.AturanDetails
            .Where(d => d.AturanId == id)
            .OrderBy(d => d.AturanDetailId)
            .ToListAsync();

        return new AturanWithDetails
        {
            Aturan = aturan,
            Details = details
        };
    }

    public async Task<Aturan?> GetAktifAsync()
    {
        return await _db.Aturans
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<AturanWithDetails?> GetAktifWithDetailsAsync()
    {
        var aturan = await _db.Aturans
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync();

        if (aturan == null)
            return null;

        var details = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .OrderBy(d => d.AturanDetailId)
            .ToListAsync();

        return new AturanWithDetails
        {
            Aturan = aturan,
            Details = details
        };
    }

    public async Task<Aturan> CreateAsync(string versi, sbyte status, uint skorMinimum, string? templateFilePath)
    {
        if (string.IsNullOrWhiteSpace(versi))
            throw new ArgumentException("Versi aturan tidak boleh kosong");

        if (await _db.Aturans.AnyAsync(a => a.AturanVersi == versi))
            throw new InvalidOperationException("Versi aturan sudah ada");

        var aturan = new Aturan
        {
            AturanVersi = versi,
            AturanStatus = status,
            AturanSkorMinimum = skorMinimum,
            AturanTemplateFilePath = templateFilePath
        };

        _db.Aturans.Add(aturan);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Aturan berhasil dibuat: ID={AturanId}, Versi={Versi}", aturan.AturanId, aturan.AturanVersi);
        return aturan;
    }

    public async Task<Aturan> UpdateAsync(uint id, string? versi, sbyte? status, uint? skorMinimum, string? templateFilePath)
    {
        var aturan = await _db.Aturans.FindAsync(id);

        if (aturan == null)
            throw new InvalidOperationException("Aturan tidak ditemukan");

        if (!string.IsNullOrWhiteSpace(versi) && versi != aturan.AturanVersi)
        {
            if (await _db.Aturans.AnyAsync(a => a.AturanVersi == versi))
                throw new InvalidOperationException("Versi aturan sudah ada");
            aturan.AturanVersi = versi;
        }

        if (status.HasValue)
            aturan.AturanStatus = status.Value;

        if (skorMinimum.HasValue)
            aturan.AturanSkorMinimum = skorMinimum.Value;

        if (templateFilePath != null)
            aturan.AturanTemplateFilePath = templateFilePath;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Aturan berhasil diupdate: ID={AturanId}", aturan.AturanId);
        return aturan;
    }
}

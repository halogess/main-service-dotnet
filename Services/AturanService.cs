using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IAturanService
{
    Task<AturanListResult> GetAllAsync(int? limit = null, int offset = 0);
    Task<Aturan?> GetByIdAsync(uint id);
    Task<AturanWithDetails?> GetByIdWithDetailsAsync(uint id);
    Task<Aturan?> GetAktifAsync();
    Task<AturanWithDetails?> GetAktifWithDetailsAsync();
    Task<Aturan> CreateAsync(string versi, string status, uint skorMinimum, string? templateFilePath);
    Task<Aturan> UpdateAsync(uint id, string? versi, uint? skorMinimum, string? templateFilePath);
    Task<Aturan> UploadAsync(string versi, uint skorMinimum, IFormFile file, CancellationToken cancellationToken = default);
    Task<Aturan> ActivateAsync(uint id, CancellationToken cancellationToken = default);
    Task SetStatusAsync(uint id, string status, CancellationToken cancellationToken = default);
    Task DeleteAsync(uint id, CancellationToken cancellationToken = default);
}

public class AturanWithDetails
{
    public Aturan Aturan { get; set; } = null!;
    public List<AturanDetail> Details { get; set; } = new();
}

public class AturanListResult
{
    public List<Aturan> Data { get; set; } = new();
    public int Total { get; set; }
    public int? Limit { get; set; }
    public int Offset { get; set; }
}

public class AturanService : IAturanService
{
    private readonly KorektorBukuDbContext _db;
    private readonly IFileService _fileService;
    private readonly IExtractionArtifactCleanupService _cleanupService;
    private readonly ILogger<AturanService> _logger;
    private readonly string _storageBasePath;

    public AturanService(
        KorektorBukuDbContext db,
        IFileService fileService,
        IExtractionArtifactCleanupService cleanupService,
        ILogger<AturanService> logger)
    {
        _db = db;
        _fileService = fileService;
        _cleanupService = cleanupService;
        _logger = logger;
        _storageBasePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
    }

    public async Task<AturanListResult> GetAllAsync(int? limit = null, int offset = 0)
    {
        var normalizedOffset = Math.Max(0, offset);
        int? normalizedLimit = limit.HasValue ? Math.Max(1, limit.Value) : null;

        IQueryable<Aturan> query = _db.Aturans
            .AsNoTracking()
            .OrderBy(a => a.AturanStatus == AturanStatusValues.Aktif ? 0 : 1)
            .ThenByDescending(a => a.AturanCreatedAt)
            .ThenByDescending(a => a.AturanId);

        var total = await query.CountAsync();

        if (normalizedLimit.HasValue)
        {
            query = query
                .Skip(normalizedOffset)
                .Take(normalizedLimit.Value);
        }

        return new AturanListResult
        {
            Data = await query.ToListAsync(),
            Total = total,
            Limit = normalizedLimit,
            Offset = normalizedOffset
        };
    }

    public async Task<Aturan?> GetByIdAsync(uint id)
    {
        return await _db.Aturans.FindAsync(id);
    }

    public async Task<AturanWithDetails?> GetByIdWithDetailsAsync(uint id)
    {
        var aturan = await _db.Aturans
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.AturanId == id);
        if (aturan == null)
            return null;

        var details = CanonicalizeDetailsForRead(await _db.AturanDetails
            .AsNoTracking()
            .Where(d => d.AturanId == id)
            .OrderBy(d => d.AturanDetailId)
            .ToListAsync());

        return new AturanWithDetails
        {
            Aturan = aturan,
            Details = details
        };
    }

    public async Task<Aturan?> GetAktifAsync()
    {
        return await _db.Aturans
            .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<AturanWithDetails?> GetAktifWithDetailsAsync()
    {
        var aturan = await _db.Aturans
            .AsNoTracking()
            .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync();

        if (aturan == null)
            return null;

        var details = CanonicalizeDetailsForRead(await _db.AturanDetails
            .AsNoTracking()
            .Where(d => d.AturanId == aturan.AturanId)
            .OrderBy(d => d.AturanDetailId)
            .ToListAsync());

        return new AturanWithDetails
        {
            Aturan = aturan,
            Details = details
        };
    }

    public async Task<Aturan> CreateAsync(string versi, string status, uint skorMinimum, string? templateFilePath)
    {
        var normalizedStatus = NormalizeStatus(status, AturanStatusValues.TidakAktif);
        await EnsureVersiAvailableAsync(versi);

        var aturan = new Aturan
        {
            AturanVersi = versi.Trim(),
            AturanStatus = normalizedStatus,
            AturanSkorMinimum = skorMinimum,
            AturanTemplateFilePath = templateFilePath
        };

        _db.Aturans.Add(aturan);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Aturan berhasil dibuat: ID={AturanId}, Versi={Versi}, Status={Status}",
            aturan.AturanId,
            aturan.AturanVersi,
            aturan.AturanStatus);
        return aturan;
    }

    public async Task<Aturan> UpdateAsync(uint id, string? versi, uint? skorMinimum, string? templateFilePath)
    {
        var aturan = await _db.Aturans.FindAsync(id);
        if (aturan == null)
            throw new InvalidOperationException("Aturan tidak ditemukan");

        if (!string.IsNullOrWhiteSpace(versi) &&
            !string.Equals(aturan.AturanVersi, versi.Trim(), StringComparison.Ordinal))
        {
            await EnsureVersiAvailableAsync(versi, id);
            aturan.AturanVersi = versi.Trim();
        }

        if (skorMinimum.HasValue)
            aturan.AturanSkorMinimum = skorMinimum.Value;

        if (templateFilePath != null)
            aturan.AturanTemplateFilePath = templateFilePath;

        aturan.AturanUpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Aturan berhasil diupdate: ID={AturanId}", aturan.AturanId);
        return aturan;
    }

    public async Task<Aturan> UploadAsync(
        string versi,
        uint skorMinimum,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File template tidak boleh kosong");

        _fileService.ValidateExtension(file.FileName);
        await _fileService.ValidateDocumentSource(file);
        await EnsureVersiAvailableAsync(versi);

        var strategy = _db.Database.CreateExecutionStrategy();
        Aturan? createdAturan = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            string? savedTemplatePath = null;

            try
            {
                var aturan = new Aturan
                {
                    AturanVersi = versi.Trim(),
                    AturanStatus = AturanStatusValues.Diproses,
                    AturanSkorMinimum = skorMinimum,
                    AturanCreatedAt = DateTime.Now,
                    AturanUpdatedAt = DateTime.Now
                };

                _db.Aturans.Add(aturan);
                await _db.SaveChangesAsync(cancellationToken);

                savedTemplatePath = (await _fileService.SaveFile(file, "admin", (int)aturan.AturanId, "aturan"))
                    .Replace('\\', '/');
                aturan.AturanTemplateFilePath = savedTemplatePath;

                _db.Antrians.Add(new Antrian
                {
                    AntrianTipe = "aturan",
                    AturanId = aturan.AturanId,
                    AntrianExtractionStatus = "in_queue",
                    AntrianCreatedAt = DateTime.Now,
                    AntrianUpdatedAt = DateTime.Now
                });

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                createdAturan = aturan;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(savedTemplatePath))
                    TryDeleteStorageTree(savedTemplatePath);
                _db.ChangeTracker.Clear();
                throw;
            }
        });

        if (createdAturan == null)
            throw new InvalidOperationException("Gagal mengunggah template aturan");

        _logger.LogInformation(
            "Template aturan berhasil diupload: AturanId={AturanId}, Versi={Versi}",
            createdAturan.AturanId,
            createdAturan.AturanVersi);
        return createdAturan;
    }

    public async Task<Aturan> ActivateAsync(uint id, CancellationToken cancellationToken = default)
    {
        var aturan = await _db.Aturans.FirstOrDefaultAsync(a => a.AturanId == id, cancellationToken);
        if (aturan == null)
            throw new InvalidOperationException("Aturan tidak ditemukan");

        if (string.Equals(aturan.AturanStatus, AturanStatusValues.Diproses, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(aturan.AturanStatus, AturanStatusValues.MenungguReview, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(aturan.AturanStatus, AturanStatusValues.Gagal, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Aturan belum bisa diaktifkan");
        }

        var currentlyActive = await _db.Aturans
            .Where(a => a.AturanId != id && a.AturanStatus == AturanStatusValues.Aktif)
            .ToListAsync(cancellationToken);

        foreach (var item in currentlyActive)
        {
            item.AturanStatus = AturanStatusValues.TidakAktif;
            item.AturanUpdatedAt = DateTime.Now;
        }

        aturan.AturanStatus = AturanStatusValues.Aktif;
        aturan.AturanUpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);

        return aturan;
    }

    public async Task SetStatusAsync(uint id, string status, CancellationToken cancellationToken = default)
    {
        var aturan = await _db.Aturans.FindAsync(new object[] { id }, cancellationToken);
        if (aturan == null)
            return;

        aturan.AturanStatus = NormalizeStatus(status, aturan.AturanStatus);
        aturan.AturanUpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(uint id, CancellationToken cancellationToken = default)
    {
        var aturan = await _db.Aturans.FindAsync(new object[] { id }, cancellationToken);
        if (aturan == null)
            throw new InvalidOperationException("Aturan tidak ditemukan");

        if (string.Equals(aturan.AturanStatus, AturanStatusValues.Aktif, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Aturan aktif tidak dapat dihapus");

        if (string.Equals(aturan.AturanStatus, AturanStatusValues.Diproses, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Aturan yang masih diproses tidak dapat dihapus");

        await _cleanupService.ResetAsync("aturan", id, cancellationToken);

        var queues = await _db.Antrians
            .Where(queue => queue.AturanId == id)
            .ToListAsync(cancellationToken);
        if (queues.Count > 0)
            _db.Antrians.RemoveRange(queues);

        var details = await _db.AturanDetails
            .Where(detail => detail.AturanId == id)
            .ToListAsync(cancellationToken);
        if (details.Count > 0)
            _db.AturanDetails.RemoveRange(details);

        _db.Aturans.Remove(aturan);
        await _db.SaveChangesAsync(cancellationToken);

        TryDeleteStorageTree(aturan.AturanTemplateFilePath);
        TryDeleteStorageTree(aturan.AturanTemplatePdfPath);
    }

    private async Task EnsureVersiAvailableAsync(string? versi, uint? exceptId = null)
    {
        if (string.IsNullOrWhiteSpace(versi))
            throw new ArgumentException("Versi aturan tidak boleh kosong");

        var normalizedVersi = versi.Trim();
        var exists = await _db.Aturans.AnyAsync(a =>
            a.AturanVersi == normalizedVersi &&
            (!exceptId.HasValue || a.AturanId != exceptId.Value));

        if (exists)
            throw new InvalidOperationException("Versi aturan sudah ada");
    }

    private static string NormalizeStatus(string? status, string fallback)
    {
        if (string.IsNullOrWhiteSpace(status))
            return fallback;

        var normalized = status.Trim().ToLowerInvariant();
        return AturanStatusValues.All.Contains(normalized)
            ? normalized
            : fallback;
    }

    private static List<AturanDetail> CanonicalizeDetailsForRead(IReadOnlyList<AturanDetail> details)
        => AturanDetailContract.NormalizeDetailsForContract(details);

    private void TryDeleteStorageTree(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        var combined = Path.GetFullPath(Path.Combine(_storageBasePath, relativePath.TrimStart('/', '\\')));
        var storageRoot = Path.GetFullPath(_storageBasePath);
        if (!combined.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
            return;

        var candidate = File.Exists(combined)
            ? Directory.GetParent(combined)?.FullName
            : combined;

        if (string.IsNullOrWhiteSpace(candidate))
            return;

        var candidateRoot = Path.GetFullPath(candidate);
        if (!candidateRoot.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
            return;

        if (Directory.Exists(candidateRoot))
            Directory.Delete(candidateRoot, recursive: true);
    }
}

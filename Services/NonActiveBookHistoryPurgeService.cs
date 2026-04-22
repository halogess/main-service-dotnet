using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface INonActiveBookHistoryPurgeService
{
    Task<NonActiveBookHistoryPurgeResult> PurgeAsync(
        IReadOnlyCollection<NonActiveBookHistoryPurgeRequestItem> requests,
        CancellationToken cancellationToken = default);
}

public sealed class NonActiveBookHistoryPurgeRequestItem
{
    public string Nrp { get; init; } = string.Empty;
    public IReadOnlyList<int> BukuIds { get; init; } = [];
}

public sealed class NonActiveBookHistorySkippedBook
{
    public string Nrp { get; init; } = string.Empty;
    public int BukuId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class NonActiveBookHistoryPurgeResult
{
    public int Deleted { get; init; }
    public int DeletedStorageDirectories { get; init; }
    public IReadOnlyList<string> FailedStorageDirectories { get; init; } = [];
    public IReadOnlyList<NonActiveBookHistorySkippedBook> SkippedBooks { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class NonActiveBookHistoryPurgeService : INonActiveBookHistoryPurgeService
{
    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;
    private readonly IExtractionArtifactCleanupService _cleanupService;
    private readonly ILogger<NonActiveBookHistoryPurgeService> _logger;

    public NonActiveBookHistoryPurgeService(
        KorektorBukuDbContext db,
        SttsDbContext sttsDb,
        IExtractionArtifactCleanupService cleanupService,
        ILogger<NonActiveBookHistoryPurgeService> logger)
    {
        _db = db;
        _sttsDb = sttsDb;
        _cleanupService = cleanupService;
        _logger = logger;
    }

    public async Task<NonActiveBookHistoryPurgeResult> PurgeAsync(
        IReadOnlyCollection<NonActiveBookHistoryPurgeRequestItem> requests,
        CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        var deletedStorageDirectories = 0;
        var failedStorageDirectories = new List<string>();
        var skippedBooks = new List<NonActiveBookHistorySkippedBook>();
        var errors = new List<string>();
        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);

        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Nrp))
            {
                errors.Add("NRP mahasiswa tidak boleh kosong");
                continue;
            }

            var nrp = request.Nrp.Trim();
            var requestedBukuIds = request.BukuIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (requestedBukuIds.Count == 0)
            {
                errors.Add($"{nrp}: buku_ids tidak boleh kosong");
                continue;
            }

            var mahasiswa = await _sttsDb.Mahasiswas
                .AsNoTracking()
                .Where(item => item.MhsNrp == nrp)
                .Select(item => new
                {
                    item.MhsNrp,
                    item.MhsStatus,
                    item.MhsLulusTahun
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (mahasiswa == null)
            {
                errors.Add($"{nrp}: mahasiswa tidak ditemukan");
                continue;
            }

            if (mahasiswa.MhsStatus == 1 && string.IsNullOrWhiteSpace(mahasiswa.MhsLulusTahun))
            {
                errors.Add($"{nrp}: mahasiswa masih aktif");
                continue;
            }

            var resolvedBukuIds = await _db.Bukus
                .AsNoTracking()
                .Where(b => b.MhsNrp == nrp && requestedBukuIds.Contains(b.BukuId))
                .Select(b => b.BukuId)
                .ToListAsync(cancellationToken);

            var missingBukuIds = requestedBukuIds
                .Except(resolvedBukuIds)
                .OrderBy(id => id)
                .ToList();

            if (missingBukuIds.Count > 0)
            {
                errors.Add(
                    $"{nrp}: buku {string.Join(", ", missingBukuIds)} tidak ditemukan atau bukan milik mahasiswa");
            }

            foreach (var bukuId in resolvedBukuIds.OrderBy(id => id))
            {
                try
                {
                    var result = await PurgeSingleBookAsync(
                        nrp,
                        bukuId,
                        fullStoragePath,
                        cancellationToken);

                    if (result.Deleted)
                    {
                        deleted++;
                        deletedStorageDirectories += result.DeletedStorageDirectories;
                    }

                    if (result.SkippedBook != null)
                        skippedBooks.Add(result.SkippedBook);

                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                        errors.Add(result.ErrorMessage);

                    failedStorageDirectories.AddRange(result.FailedStorageDirectories);
                }
                catch (Exception ex)
                {
                    errors.Add($"{nrp}: buku {bukuId}: {ex.Message}");
                }
            }
        }

        return new NonActiveBookHistoryPurgeResult
        {
            Deleted = deleted,
            DeletedStorageDirectories = deletedStorageDirectories,
            FailedStorageDirectories = failedStorageDirectories
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SkippedBooks = skippedBooks,
            Errors = errors
        };
    }

    private async Task<SingleBookPurgeResult> PurgeSingleBookAsync(
        string nrp,
        int bukuId,
        string fullStoragePath,
        CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                async () => await PurgeSingleBookCoreAsync(
                    nrp,
                    bukuId,
                    fullStoragePath,
                    cancellationToken));
        }

        return await PurgeSingleBookCoreAsync(
            nrp,
            bukuId,
            fullStoragePath,
            cancellationToken);
    }

    private async Task<SingleBookPurgeResult> PurgeSingleBookCoreAsync(
        string nrp,
        int bukuId,
        string fullStoragePath,
        CancellationToken cancellationToken)
    {
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var buku = await _db.Bukus
                .FirstOrDefaultAsync(b => b.BukuId == bukuId && b.MhsNrp == nrp, cancellationToken);

            if (buku == null)
            {
                return new SingleBookPurgeResult
                {
                    ErrorMessage = $"{nrp}: buku {bukuId} tidak ditemukan"
                };
            }

            var babs = await _db.Babs
                .Where(b => b.BukuId == (uint)bukuId)
                .ToListAsync(cancellationToken);

            var babIds = babs
                .Select(b => b.BabId)
                .Distinct()
                .ToList();

            var antrians = await _db.Antrians
                .Where(a => a.AntrianTipe == "buku" &&
                            ((a.BukuId.HasValue && a.BukuId.Value == (uint)bukuId) ||
                             (a.BabId.HasValue && babIds.Contains(a.BabId.Value))))
                .ToListAsync(cancellationToken);

            var eligibility = BukuHistoryDeletionPolicy.Evaluate(buku.BukuStatus, antrians);
            if (!eligibility.CanDelete)
            {
                if (transaction != null)
                    await transaction.RollbackAsync(cancellationToken);

                return new SingleBookPurgeResult
                {
                    SkippedBook = new NonActiveBookHistorySkippedBook
                    {
                        Nrp = nrp,
                        BukuId = bukuId,
                        Reason = eligibility.DeleteBlockReason ?? "Buku tidak dapat dihapus."
                    }
                };
            }

            foreach (var babId in babIds)
                await _cleanupService.ResetAsync("bab", babId, cancellationToken);

            await _cleanupService.ResetAsync("buku", (uint)bukuId, cancellationToken);

            var kesalahans = babIds.Count == 0
                ? []
                : await _db.Kesalahans
                    .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.bab && babIds.Contains(k.KesalahanRefId))
                    .ToListAsync(cancellationToken);

            var kesalahanIds = kesalahans
                .Select(k => k.KesalahanId)
                .Distinct()
                .ToList();

            var kesalahanDetails = kesalahanIds.Count == 0
                ? []
                : await _db.KesalahanDetails
                    .Where(detail => kesalahanIds.Contains(detail.KesalahanId))
                    .ToListAsync(cancellationToken);

            var antrianIds = antrians
                .Select(a => a.AntrianId)
                .Distinct()
                .ToList();

            var adobeLogs = antrianIds.Count == 0
                ? []
                : await _db.AdobeApiLogs
                    .Where(log => log.AntrianId.HasValue && antrianIds.Contains(log.AntrianId.Value))
                    .ToListAsync(cancellationToken);

            var llmLogs = antrianIds.Count == 0
                ? []
                : await _db.LlmApiLogs
                    .Where(log => log.AntrianId.HasValue && antrianIds.Contains(log.AntrianId.Value))
                    .ToListAsync(cancellationToken);

            if (kesalahanDetails.Count > 0)
                _db.KesalahanDetails.RemoveRange(kesalahanDetails);

            if (kesalahans.Count > 0)
                _db.Kesalahans.RemoveRange(kesalahans);

            if (adobeLogs.Count > 0)
                _db.AdobeApiLogs.RemoveRange(adobeLogs);

            if (llmLogs.Count > 0)
                _db.LlmApiLogs.RemoveRange(llmLogs);

            if (antrians.Count > 0)
                _db.Antrians.RemoveRange(antrians);

            if (babs.Count > 0)
                _db.Babs.RemoveRange(babs);

            _db.Bukus.Remove(buku);
            await _db.SaveChangesAsync(cancellationToken);

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);

            throw;
        }

        var failedStorageDirectories = new List<string>();
        string? storageError = null;
        var deletedStorageDirectories = 0;
        var bukuDirectory = BuildSafeBukuDirectory(fullStoragePath, nrp, bukuId);

        if (string.IsNullOrWhiteSpace(bukuDirectory))
        {
            storageError = $"{nrp}: path storage buku {bukuId} tidak valid";
        }
        else if (Directory.Exists(bukuDirectory))
        {
            try
            {
                Directory.Delete(bukuDirectory, recursive: true);
                deletedStorageDirectories++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete storage directory {Directory}", bukuDirectory);
                failedStorageDirectories.Add(bukuDirectory);
            }
        }

        return new SingleBookPurgeResult
        {
            Deleted = true,
            DeletedStorageDirectories = deletedStorageDirectories,
            FailedStorageDirectories = failedStorageDirectories,
            ErrorMessage = storageError
        };
    }

    private static string? BuildSafeBukuDirectory(
        string fullStoragePath,
        string nrp,
        int bukuId)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(fullStoragePath, "buku", nrp.Trim(), bukuId.ToString()));

        return candidate.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private sealed class SingleBookPurgeResult
    {
        public bool Deleted { get; init; }
        public int DeletedStorageDirectories { get; init; }
        public IReadOnlyList<string> FailedStorageDirectories { get; init; } = [];
        public NonActiveBookHistorySkippedBook? SkippedBook { get; init; }
        public string? ErrorMessage { get; init; }
    }
}

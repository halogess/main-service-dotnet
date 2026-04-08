using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IDokumenAutoDeleteService
{
    Task<int> ExecuteCleanupAsync(CancellationToken cancellationToken = default);
}

public sealed class DokumenAutoDeleteService : BackgroundService, IDokumenAutoDeleteService
{
    private static readonly HashSet<string> ActiveQueueStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "in_queue",
        "processing",
        "diproses"
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DokumenAutoDeleteService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public DokumenAutoDeleteService(
        IServiceProvider serviceProvider,
        ILogger<DokumenAutoDeleteService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Dokumen auto-delete service started. Running every 24 hours.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                var deletedCount = await ExecuteCleanupAsync(stoppingToken);
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Auto-deleted {Count} dokumen records older than 30 days", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during dokumen auto-delete cleanup");
            }
        }
    }

    public async Task<int> ExecuteCleanupAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();

        var cutoffDate = DateTime.Now.AddDays(-30);

        var oldDokumenIds = await db.Dokumens
            .AsNoTracking()
            .Where(d => d.DokumenCreatedAt.HasValue && d.DokumenCreatedAt.Value < cutoffDate)
            .Select(d => d.DokumenId)
            .ToListAsync(cancellationToken);

        if (oldDokumenIds.Count == 0)
            return 0;

        var activeQueueDokumenIds = await db.Antrians
            .AsNoTracking()
            .Where(a => a.AntrianTipe == "dokumen" &&
                        a.DokumenId.HasValue &&
                        oldDokumenIds.Contains((int)a.DokumenId.Value) &&
                        (ActiveQueueStates.Contains(a.AntrianExtractionStatus ?? string.Empty) ||
                         ActiveQueueStates.Contains(a.AntrianLabelingStatus ?? string.Empty) ||
                         ActiveQueueStates.Contains(a.AntrianValidationStatus ?? string.Empty)))
            .Select(a => (int)a.DokumenId!.Value)
            .ToListAsync(cancellationToken);

        var deletableDokumenIds = oldDokumenIds.Except(activeQueueDokumenIds).ToList();

        if (deletableDokumenIds.Count == 0)
            return 0;

        _logger.LogInformation("Found {Count} dokumen records eligible for auto-deletion", deletableDokumenIds.Count);

        var totalDeleted = 0;

        foreach (var dokumenId in deletableDokumenIds)
        {
            try
            {
                await DeleteDokumenCascadeAsync(db, dokumenId, cancellationToken);
                totalDeleted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-delete dokumen {DokumenId}", dokumenId);
            }
        }

        return totalDeleted;
    }

    private async Task DeleteDokumenCascadeAsync(
        KorektorBukuDbContext db,
        int dokumenId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var dokumen = await db.Dokumens
                .FirstOrDefaultAsync(d => d.DokumenId == dokumenId, cancellationToken);

            if (dokumen == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var dokumenSectionIds = await db.DokumenSections
                .AsNoTracking()
                .Where(s => s.DsecRefTipe == "dokumen" && s.DsecRefId == dokumenId)
                .Select(s => s.DsecId)
                .ToListAsync(cancellationToken);

            var dokumenPartIds = dokumenSectionIds.Count > 0
                ? await db.DokumenParts
                    .AsNoTracking()
                    .Where(p => dokumenSectionIds.Contains(p.DsecId))
                    .Select(p => p.DpartId)
                    .ToListAsync(cancellationToken)
                : new List<uint>();

            var dokumenElementIds = dokumenPartIds.Count > 0
                ? await db.DokumenElemens
                    .AsNoTracking()
                    .Where(e => e.DpartId.HasValue && dokumenPartIds.Contains(e.DpartId.Value))
                    .Select(e => e.DelemenId)
                    .ToListAsync(cancellationToken)
                : new List<ulong>();

            var dokumenKesalahanIds = await db.Kesalahans
                .AsNoTracking()
                .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen && k.KesalahanRefId == dokumenId)
                .Select(k => k.KesalahanId)
                .ToListAsync(cancellationToken);

            var dokumenAntrianIds = await db.Antrians
                .AsNoTracking()
                .Where(a => a.AntrianTipe == "dokumen" && a.DokumenId == dokumenId)
                .Select(a => a.AntrianId)
                .ToListAsync(cancellationToken);

            await db.KesalahanDetails
                .Where(detail => dokumenKesalahanIds.Contains(detail.KesalahanId))
                .ExecuteDeleteAsync(cancellationToken);

            await db.Kesalahans
                .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen && k.KesalahanRefId == dokumenId)
                .ExecuteDeleteAsync(cancellationToken);

            await db.AdobeApiLogs
                .Where(log => log.AntrianId.HasValue && dokumenAntrianIds.Contains(log.AntrianId.Value))
                .ExecuteDeleteAsync(cancellationToken);

            await db.LlmApiLogs
                .Where(log => log.AntrianId.HasValue && dokumenAntrianIds.Contains(log.AntrianId.Value))
                .ExecuteDeleteAsync(cancellationToken);

            await db.DokumenElemenVisuals
                .Where(visual => visual.DevRefTipe == "dokumen" ||
                                 (visual.DokumenElemenId.HasValue && dokumenElementIds.Contains(visual.DokumenElemenId.Value)))
                .ExecuteDeleteAsync(cancellationToken);

            await db.DokumenNotes
                .Where(note => note.DokumenId == dokumenId)
                .ExecuteDeleteAsync(cancellationToken);

            await db.DokumenMedias
                .Where(media => media.DokumenId == dokumenId)
                .ExecuteDeleteAsync(cancellationToken);

            await DeleteFormatRecordsAsync(db, dokumenElementIds, cancellationToken);

            await db.DokumenElemens
                .Where(e => e.DpartId.HasValue && dokumenPartIds.Contains(e.DpartId.Value))
                .ExecuteDeleteAsync(cancellationToken);

            await db.DokumenParts
                .Where(p => dokumenSectionIds.Contains(p.DsecId))
                .ExecuteDeleteAsync(cancellationToken);

            await db.DokumenSections
                .Where(s => s.DsecRefTipe == "dokumen" && s.DsecRefId == dokumenId)
                .ExecuteDeleteAsync(cancellationToken);

            await db.Antrians
                .Where(a => a.AntrianTipe == "dokumen" && a.DokumenId == dokumenId)
                .ExecuteDeleteAsync(cancellationToken);

            await db.Dokumens
                .Where(d => d.DokumenId == dokumenId)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(dokumen.MhsNrp))
            {
                DeleteStorageDirectory(dokumen.MhsNrp, dokumenId.ToString());
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task DeleteFormatRecordsAsync(
        KorektorBukuDbContext db,
        List<ulong> elementIds,
        CancellationToken cancellationToken)
    {
        if (elementIds.Count == 0)
            return;

        var paragraphFormatIds = new HashSet<ulong>();
        var textFormatIds = new HashSet<ulong>();
        var tableFormatIds = new HashSet<ulong>();
        var tableRowFormatIds = new HashSet<ulong>();
        var tableCellFormatIds = new HashSet<ulong>();
        var drawingFormatIds = new HashSet<ulong>();
        var fieldFormatIds = new HashSet<ulong>();

        var elements = await db.DokumenElemens
            .AsNoTracking()
            .Where(e => elementIds.Contains(e.DelemenId))
            .Select(e => e.DelemenJsonTree)
            .ToListAsync(cancellationToken);

        foreach (var json in elements.Where(j => !string.IsNullOrWhiteSpace(j)))
        {
            CollectFormatIds(json!, paragraphFormatIds, textFormatIds, tableFormatIds,
                tableRowFormatIds, tableCellFormatIds, drawingFormatIds, fieldFormatIds);
        }

        var notes = await db.DokumenNotes
            .AsNoTracking()
            .Where(n => n.DokumenId > 0)
            .Select(n => n.DnoteJsonTree)
            .ToListAsync(cancellationToken);

        foreach (var json in notes.Where(j => !string.IsNullOrWhiteSpace(j)))
        {
            CollectFormatIds(json!, paragraphFormatIds, textFormatIds, tableFormatIds,
                tableRowFormatIds, tableCellFormatIds, drawingFormatIds, fieldFormatIds);
        }

        if (paragraphFormatIds.Count > 0)
            foreach (var chunk in paragraphFormatIds.Chunk(500))
                await db.DokumenFormatParagrafs.Where(item => chunk.Contains(item.DfpId)).ExecuteDeleteAsync(cancellationToken);

        if (textFormatIds.Count > 0)
            foreach (var chunk in textFormatIds.Chunk(500))
                await db.DokumenFormatTexts.Where(item => chunk.Contains(item.DftxId)).ExecuteDeleteAsync(cancellationToken);

        if (tableFormatIds.Count > 0)
            foreach (var chunk in tableFormatIds.Chunk(500))
                await db.DokumenFormatTables.Where(item => chunk.Contains(item.DftId)).ExecuteDeleteAsync(cancellationToken);

        if (tableRowFormatIds.Count > 0)
            foreach (var chunk in tableRowFormatIds.Chunk(500))
                await db.DokumenFormatTableRows.Where(item => chunk.Contains(item.DftrId)).ExecuteDeleteAsync(cancellationToken);

        if (tableCellFormatIds.Count > 0)
            foreach (var chunk in tableCellFormatIds.Chunk(500))
                await db.DokumenFormatTableCells.Where(item => chunk.Contains(item.DftcId)).ExecuteDeleteAsync(cancellationToken);

        if (drawingFormatIds.Count > 0)
            foreach (var chunk in drawingFormatIds.Chunk(500))
                await db.DokumenFormatDrawings.Where(item => chunk.Contains(item.DfdrId)).ExecuteDeleteAsync(cancellationToken);

        if (fieldFormatIds.Count > 0)
            foreach (var chunk in fieldFormatIds.Chunk(500))
                await db.DokumenFormatFields.Where(item => chunk.Contains(item.DffdId)).ExecuteDeleteAsync(cancellationToken);
    }

    private static void CollectFormatIds(
        string json,
        HashSet<ulong> paragraphFormatIds,
        HashSet<ulong> textFormatIds,
        HashSet<ulong> tableFormatIds,
        HashSet<ulong> tableRowFormatIds,
        HashSet<ulong> tableCellFormatIds,
        HashSet<ulong> drawingFormatIds,
        HashSet<ulong> fieldFormatIds)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            CollectFormatIdsRecursive(document.RootElement, paragraphFormatIds, textFormatIds, tableFormatIds,
                tableRowFormatIds, tableCellFormatIds, drawingFormatIds, fieldFormatIds);
        }
        catch (System.Text.Json.JsonException) { }
    }

    private static void CollectFormatIdsRecursive(
        System.Text.Json.JsonElement element,
        HashSet<ulong> paragraphFormatIds,
        HashSet<ulong> textFormatIds,
        HashSet<ulong> tableFormatIds,
        HashSet<ulong> tableRowFormatIds,
        HashSet<ulong> tableCellFormatIds,
        HashSet<ulong> drawingFormatIds,
        HashSet<ulong> fieldFormatIds)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "dfp_id":
                            TryAddUInt(property.Value, paragraphFormatIds);
                            break;
                        case "dftx_id":
                        case "result_dftx_id":
                            TryAddUInt(property.Value, textFormatIds);
                            break;
                        case "dft_id":
                            TryAddUInt(property.Value, tableFormatIds);
                            break;
                        case "dftr_id":
                            TryAddUInt(property.Value, tableRowFormatIds);
                            break;
                        case "dftc_id":
                            TryAddUInt(property.Value, tableCellFormatIds);
                            break;
                        case "dfdr_id":
                            TryAddULong(property.Value, drawingFormatIds);
                            break;
                        case "dffd_id":
                            TryAddULong(property.Value, fieldFormatIds);
                            break;
                    }

                    CollectFormatIdsRecursive(property.Value, paragraphFormatIds, textFormatIds, tableFormatIds,
                        tableRowFormatIds, tableCellFormatIds, drawingFormatIds, fieldFormatIds);
                }
                break;

            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectFormatIdsRecursive(item, paragraphFormatIds, textFormatIds, tableFormatIds,
                        tableRowFormatIds, tableCellFormatIds, drawingFormatIds, fieldFormatIds);
                break;
        }
    }

    private static void TryAddUInt(System.Text.Json.JsonElement element, ISet<ulong> target)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetUInt32(out var value))
            target.Add(value);
        else if (element.ValueKind == System.Text.Json.JsonValueKind.String && uint.TryParse(element.GetString(), out var parsedValue))
            target.Add(parsedValue);
    }

    private static void TryAddULong(System.Text.Json.JsonElement element, ISet<ulong> target)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetUInt64(out var value))
            target.Add(value);
        else if (element.ValueKind == System.Text.Json.JsonValueKind.String && ulong.TryParse(element.GetString(), out var parsedValue))
            target.Add(parsedValue);
    }

    private void DeleteStorageDirectory(string nrp, string dokumenId)
    {
        try
        {
            var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
            var fullStoragePath = Path.GetFullPath(storagePath);
            var directory = Path.GetFullPath(Path.Combine(fullStoragePath, "dokumen", nrp.Trim(), dokumenId));

            if (!directory.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Storage directory path escape attempt: {Directory}", directory);
                return;
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
                _logger.LogInformation("Deleted storage directory: {Directory}", directory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete storage directory for dokumen {DokumenId}", dokumenId);
        }
    }
}

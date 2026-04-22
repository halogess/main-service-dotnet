using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IDokumenHistoryPurgeService
{
    Task<DokumenHistoryPurgeSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<DokumenHistoryPurgeResult> PurgeAllAsync(CancellationToken cancellationToken = default);
}

public sealed class DokumenHistoryPurgeSummary
{
    public int TotalDokumen { get; init; }
    public int TotalAntrian { get; init; }
    public int TotalActiveQueue { get; init; }
    public int TotalSection { get; init; }
    public int TotalPart { get; init; }
    public int TotalElemen { get; init; }
    public int TotalVisual { get; init; }
    public int TotalNote { get; init; }
    public int TotalKesalahan { get; init; }
    public int TotalKesalahanDetail { get; init; }
    public int TotalAdobeLog { get; init; }
    public int TotalLlmLog { get; init; }
    public int TotalParagraphFormat { get; init; }
    public int TotalTextFormat { get; init; }
    public int TotalTableFormat { get; init; }
    public int TotalDrawingFormat { get; init; }
    public int TotalStorageTargets { get; init; }
    public int ExistingStorageDirectories { get; init; }
}

public sealed class DokumenHistoryPurgeResult
{
    public int DeletedDokumen { get; init; }
    public int DeletedAntrian { get; init; }
    public int DeletedSection { get; init; }
    public int DeletedPart { get; init; }
    public int DeletedElemen { get; init; }
    public int DeletedVisual { get; init; }
    public int DeletedNote { get; init; }
    public int DeletedKesalahan { get; init; }
    public int DeletedKesalahanDetail { get; init; }
    public int DeletedAdobeLog { get; init; }
    public int DeletedLlmLog { get; init; }
    public int DeletedParagraphFormat { get; init; }
    public int DeletedTextFormat { get; init; }
    public int DeletedTableFormat { get; init; }
    public int DeletedDrawingFormat { get; init; }
    public int StorageTargets { get; init; }
    public int DeletedStorageDirectories { get; init; }
    public IReadOnlyList<string> FailedStorageDirectories { get; init; } = Array.Empty<string>();
}

public sealed class DokumenHistoryPurgeService : IDokumenHistoryPurgeService
{
    private static readonly HashSet<string> ActiveQueueStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "in_queue",
        "processing",
        "diproses"
    };

    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<DokumenHistoryPurgeService> _logger;

    public DokumenHistoryPurgeService(
        KorektorBukuDbContext db,
        ILogger<DokumenHistoryPurgeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DokumenHistoryPurgeSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var context = await BuildContextAsync(cancellationToken);
        return new DokumenHistoryPurgeSummary
        {
            TotalDokumen = context.DokumenCount,
            TotalAntrian = context.AntrianCount,
            TotalActiveQueue = context.ActiveQueueCount,
            TotalSection = context.SectionCount,
            TotalPart = context.PartCount,
            TotalElemen = context.ElementCount,
            TotalVisual = context.VisualCount,
            TotalNote = context.NoteCount,
            TotalKesalahan = context.KesalahanCount,
            TotalKesalahanDetail = context.KesalahanDetailCount,
            TotalAdobeLog = context.AdobeLogCount,
            TotalLlmLog = context.LlmLogCount,
            TotalParagraphFormat = context.FormatRefs.ParagraphFormatIds.Count,
            TotalTextFormat = context.FormatRefs.TextFormatIds.Count,
            TotalTableFormat = context.FormatRefs.TableFormatIds.Count,
            TotalDrawingFormat = context.FormatRefs.DrawingFormatIds.Count,
            TotalStorageTargets = context.StorageDirectories.Count,
            ExistingStorageDirectories = context.ExistingStorageDirectories
        };
    }

    public async Task<DokumenHistoryPurgeResult> PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        var context = await BuildContextAsync(cancellationToken);
        var dokumenIds = context.DokumenIds;
        var strategy = _db.Database.CreateExecutionStrategy();

        var dbResult = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var dokumenQueueIds = _db.Antrians
                .Where(a => a.AntrianTipe == "dokumen")
                .Select(a => a.AntrianId);

            var dokumenKesalahanIds = _db.Kesalahans
                .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen)
                .Select(k => k.KesalahanId);

            var dokumenSectionIds = _db.DokumenSections
                .Where(s => s.DsecRefTipe == "dokumen")
                .Select(s => s.DsecId);

            var dokumenPartIds = _db.DokumenParts
                .Where(p => dokumenSectionIds.Contains(p.DsecId))
                .Select(p => p.DpartId);

            var dokumenElementIds = _db.DokumenElemens
                .Where(e => e.DpartId.HasValue && dokumenPartIds.Contains(e.DpartId.Value))
                .Select(e => e.DelemenId);

            var deletedKesalahanDetail = await _db.KesalahanDetails
                .Where(detail => dokumenKesalahanIds.Contains(detail.KesalahanId))
                .ExecuteDeleteAsync(cancellationToken);

            var deletedKesalahan = await _db.Kesalahans
                .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedAdobeLog = await _db.AdobeApiLogs
                .Where(log => log.AntrianId.HasValue && dokumenQueueIds.Contains(log.AntrianId.Value))
                .ExecuteDeleteAsync(cancellationToken);

            var deletedLlmLog = await _db.LlmApiLogs
                .Where(log => log.AntrianId.HasValue && dokumenQueueIds.Contains(log.AntrianId.Value))
                .ExecuteDeleteAsync(cancellationToken);

            var deletedVisual = await _db.DokumenElemenVisuals
                .Where(visual => ((visual.DevRefTipe == "dokumen" &&
                                   visual.DevRefId.HasValue &&
                                   dokumenIds.Contains(visual.DevRefId.Value)) ||
                                  (visual.DokumenElemenId.HasValue && dokumenElementIds.Contains(visual.DokumenElemenId.Value))))
                .ExecuteDeleteAsync(cancellationToken);

            var deletedNote = await _db.DokumenNotes
                .Where(note => note.DnoteRefTipe == "dokumen")
                .ExecuteDeleteAsync(cancellationToken);

            var deletedParagraphFormat = await DeleteByChunkAsync(
                context.FormatRefs.ParagraphFormatIds,
                chunk => _db.DokumenFormatParagrafs.Where(item => chunk.Contains(item.DfpId)).ExecuteDeleteAsync(cancellationToken));

            var deletedTextFormat = await DeleteByChunkAsync(
                context.FormatRefs.TextFormatIds,
                chunk => _db.DokumenFormatTexts.Where(item => chunk.Contains(item.DftxId)).ExecuteDeleteAsync(cancellationToken));

            var deletedTableFormat = await DeleteByChunkAsync(
                context.FormatRefs.TableFormatIds,
                chunk => _db.DokumenFormatTables.Where(item => chunk.Contains(item.DftId)).ExecuteDeleteAsync(cancellationToken));

            var deletedDrawingFormat = await DeleteByChunkAsync(
                context.FormatRefs.DrawingFormatIds,
                chunk => _db.DokumenFormatDrawings.Where(item => chunk.Contains(item.DfdrId)).ExecuteDeleteAsync(cancellationToken));

            var deletedElemen = await _db.DokumenElemens
                .Where(element => element.DpartId.HasValue && dokumenPartIds.Contains(element.DpartId.Value))
                .ExecuteDeleteAsync(cancellationToken);

            var deletedPart = await _db.DokumenParts
                .Where(part => dokumenSectionIds.Contains(part.DsecId))
                .ExecuteDeleteAsync(cancellationToken);

            var deletedSection = await _db.DokumenSections
                .Where(section => section.DsecRefTipe == "dokumen")
                .ExecuteDeleteAsync(cancellationToken);

            var deletedAntrian = await _db.Antrians
                .Where(a => a.AntrianTipe == "dokumen")
                .ExecuteDeleteAsync(cancellationToken);

            var deletedDokumen = await _db.Dokumens
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new DokumenHistoryPurgeResult
            {
                DeletedDokumen = deletedDokumen,
                DeletedAntrian = deletedAntrian,
                DeletedSection = deletedSection,
                DeletedPart = deletedPart,
                DeletedElemen = deletedElemen,
                DeletedVisual = deletedVisual,
                DeletedNote = deletedNote,
                DeletedKesalahan = deletedKesalahan,
                DeletedKesalahanDetail = deletedKesalahanDetail,
                DeletedAdobeLog = deletedAdobeLog,
                DeletedLlmLog = deletedLlmLog,
                DeletedParagraphFormat = deletedParagraphFormat,
                DeletedTextFormat = deletedTextFormat,
                DeletedTableFormat = deletedTableFormat,
                DeletedDrawingFormat = deletedDrawingFormat,
                StorageTargets = context.StorageDirectories.Count
            };
        });

        var failedStorageDirectories = new List<string>();
        var deletedStorageDirectories = 0;
        foreach (var directory in context.StorageDirectories)
        {
            try
            {
                if (!Directory.Exists(directory))
                    continue;

                Directory.Delete(directory, recursive: true);
                deletedStorageDirectories++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete storage directory {Directory}", directory);
                failedStorageDirectories.Add(directory);
            }
        }

        return new DokumenHistoryPurgeResult
        {
            DeletedDokumen = dbResult.DeletedDokumen,
            DeletedAntrian = dbResult.DeletedAntrian,
            DeletedSection = dbResult.DeletedSection,
            DeletedPart = dbResult.DeletedPart,
            DeletedElemen = dbResult.DeletedElemen,
            DeletedVisual = dbResult.DeletedVisual,
            DeletedNote = dbResult.DeletedNote,
            DeletedKesalahan = dbResult.DeletedKesalahan,
            DeletedKesalahanDetail = dbResult.DeletedKesalahanDetail,
            DeletedAdobeLog = dbResult.DeletedAdobeLog,
            DeletedLlmLog = dbResult.DeletedLlmLog,
            DeletedParagraphFormat = dbResult.DeletedParagraphFormat,
            DeletedTextFormat = dbResult.DeletedTextFormat,
            DeletedTableFormat = dbResult.DeletedTableFormat,
            DeletedDrawingFormat = dbResult.DeletedDrawingFormat,
            StorageTargets = dbResult.StorageTargets,
            DeletedStorageDirectories = deletedStorageDirectories,
            FailedStorageDirectories = failedStorageDirectories
        };
    }

    private async Task<PurgeContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        var dokumens = await _db.Dokumens
            .AsNoTracking()
            .Select(d => new DokumenStorageRow(d.DokumenId, d.MhsNrp))
            .ToListAsync(cancellationToken);
        var dokumenIds = dokumens
            .Select(d => (uint)d.DokumenId)
            .ToArray();

        var queueRows = await _db.Antrians
            .AsNoTracking()
            .Where(a => a.AntrianTipe == "dokumen")
            .Select(a => new QueueStatusRow(
                a.AntrianId,
                a.AntrianExtractionStatus,
                a.AntrianLabelingStatus,
                a.AntrianValidationStatus))
            .ToListAsync(cancellationToken);

        var sectionIds = await _db.DokumenSections
            .AsNoTracking()
            .Where(section => section.DsecRefTipe == "dokumen")
            .Select(section => section.DsecId)
            .ToListAsync(cancellationToken);

        var partIds = sectionIds.Count == 0
            ? new List<uint>()
            : await _db.DokumenParts
                .AsNoTracking()
                .Where(part => sectionIds.Contains(part.DsecId))
                .Select(part => part.DpartId)
                .ToListAsync(cancellationToken);

        var elements = partIds.Count == 0
            ? new List<ElementJsonRow>()
            : await _db.DokumenElemens
                .AsNoTracking()
                .Where(element => element.DpartId.HasValue && partIds.Contains(element.DpartId.Value))
                .Select(element => new ElementJsonRow(element.DelemenId, element.DelemenJsonTree))
                .ToListAsync(cancellationToken);

        var formatRefs = new JsonFormatReferenceSet();
        foreach (var element in elements)
            CollectFormatIds(element.Json, formatRefs);
        var elementIds = elements
            .Select(element => element.DelemenId)
            .ToArray();

        var notes = await _db.DokumenNotes
            .AsNoTracking()
            .Where(note => note.DnoteRefTipe == "dokumen")
            .Select(note => new NoteJsonRow(note.DnoteId, note.DnoteJsonTree))
            .ToListAsync(cancellationToken);

        foreach (var note in notes)
            CollectFormatIds(note.Json, formatRefs);

        var dokumenQueueIds = _db.Antrians
            .AsNoTracking()
            .Where(a => a.AntrianTipe == "dokumen")
            .Select(a => a.AntrianId);

        var dokumenKesalahanIds = _db.Kesalahans
            .AsNoTracking()
            .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen)
            .Select(k => k.KesalahanId);

        var visualCount = await _db.DokumenElemenVisuals
            .AsNoTracking()
            .CountAsync(
                visual => ((visual.DevRefTipe == "dokumen" &&
                            visual.DevRefId.HasValue &&
                            dokumenIds.Contains(visual.DevRefId.Value)) ||
                           (visual.DokumenElemenId.HasValue && elementIds.Contains(visual.DokumenElemenId.Value))),
                cancellationToken);

        var storageDirectories = BuildStorageDirectories(dokumens);

        return new PurgeContext
        {
            DokumenCount = dokumens.Count,
            DokumenIds = dokumenIds,
            AntrianCount = queueRows.Count,
            ActiveQueueCount = queueRows.Count(IsQueueActive),
            SectionCount = sectionIds.Count,
            PartCount = partIds.Count,
            ElementCount = elements.Count,
            VisualCount = visualCount,
            NoteCount = notes.Count,
            KesalahanCount = await _db.Kesalahans.AsNoTracking().CountAsync(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen, cancellationToken),
            KesalahanDetailCount = await _db.KesalahanDetails.AsNoTracking().CountAsync(detail => dokumenKesalahanIds.Contains(detail.KesalahanId), cancellationToken),
            AdobeLogCount = await _db.AdobeApiLogs.AsNoTracking().CountAsync(log => log.AntrianId.HasValue && dokumenQueueIds.Contains(log.AntrianId.Value), cancellationToken),
            LlmLogCount = await _db.LlmApiLogs.AsNoTracking().CountAsync(log => log.AntrianId.HasValue && dokumenQueueIds.Contains(log.AntrianId.Value), cancellationToken),
            FormatRefs = formatRefs,
            StorageDirectories = storageDirectories,
            ExistingStorageDirectories = storageDirectories.Count(Directory.Exists)
        };
    }

    private static List<string> BuildStorageDirectories(IEnumerable<DokumenStorageRow> dokumens)
    {
        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dokumen in dokumens)
        {
            if (string.IsNullOrWhiteSpace(dokumen.MhsNrp))
                continue;

            var candidate = Path.GetFullPath(Path.Combine(
                fullStoragePath,
                "dokumen",
                dokumen.MhsNrp.Trim(),
                dokumen.DokumenId.ToString()));

            if (!candidate.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
                continue;

            directories.Add(candidate);
        }

        return directories.ToList();
    }

    private static bool IsQueueActive(QueueStatusRow queue)
    {
        return ActiveQueueStates.Contains(queue.ExtractionStatus ?? string.Empty) ||
               ActiveQueueStates.Contains(queue.LabelingStatus ?? string.Empty) ||
               ActiveQueueStates.Contains(queue.ValidationStatus ?? string.Empty);
    }

    private static async Task<int> DeleteByChunkAsync<T>(
        IEnumerable<T> ids,
        Func<T[], Task<int>> deleteChunkAsync)
        where T : notnull
    {
        var total = 0;
        foreach (var chunk in ids.Distinct().Chunk(500))
            total += await deleteChunkAsync(chunk);

        return total;
    }

    private static void CollectFormatIds(string? json, JsonFormatReferenceSet refs)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var document = JsonDocument.Parse(json);
            CollectFormatIds(document.RootElement, refs);
        }
        catch (JsonException)
        {
            // Ignore malformed historical rows.
        }
    }

    private static void CollectFormatIds(JsonElement element, JsonFormatReferenceSet refs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "dfp_id":
                            AddUInt(property.Value, refs.ParagraphFormatIds);
                            break;
                        case "dftx_id":
                        case "result_dftx_id":
                            AddUInt(property.Value, refs.TextFormatIds);
                            break;
                        case "dft_id":
                            AddUInt(property.Value, refs.TableFormatIds);
                            break;
                        case "dfdr_id":
                            AddULong(property.Value, refs.DrawingFormatIds);
                            break;
                    }

                    CollectFormatIds(property.Value, refs);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectFormatIds(item, refs);
                break;
        }
    }

    private static void AddUInt(JsonElement element, ISet<uint> target)
    {
        if (TryReadUInt(element, out var value))
            target.Add(value);
    }

    private static void AddULong(JsonElement element, ISet<ulong> target)
    {
        if (TryReadULong(element, out var value))
            target.Add(value);
    }

    private static bool TryReadUInt(JsonElement element, out uint value)
    {
        value = 0;

        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetUInt32(out value);

        if (element.ValueKind == JsonValueKind.String)
            return uint.TryParse(element.GetString(), out value);

        return false;
    }

    private static bool TryReadULong(JsonElement element, out ulong value)
    {
        value = 0;

        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetUInt64(out value);

        if (element.ValueKind == JsonValueKind.String)
            return ulong.TryParse(element.GetString(), out value);

        return false;
    }

    private sealed class PurgeContext
    {
        public int DokumenCount { get; init; }
        public required uint[] DokumenIds { get; init; }
        public int AntrianCount { get; init; }
        public int ActiveQueueCount { get; init; }
        public int SectionCount { get; init; }
        public int PartCount { get; init; }
        public int ElementCount { get; init; }
        public int VisualCount { get; init; }
        public int NoteCount { get; init; }
        public int KesalahanCount { get; init; }
        public int KesalahanDetailCount { get; init; }
        public int AdobeLogCount { get; init; }
        public int LlmLogCount { get; init; }
        public required JsonFormatReferenceSet FormatRefs { get; init; }
        public required List<string> StorageDirectories { get; init; }
        public int ExistingStorageDirectories { get; init; }
    }

    private sealed class JsonFormatReferenceSet
    {
        public HashSet<uint> ParagraphFormatIds { get; } = new();
        public HashSet<uint> TextFormatIds { get; } = new();
        public HashSet<uint> TableFormatIds { get; } = new();
        public HashSet<ulong> DrawingFormatIds { get; } = new();
    }

    private sealed record DokumenStorageRow(int DokumenId, string? MhsNrp);
    private sealed record QueueStatusRow(uint AntrianId, string? ExtractionStatus, string? LabelingStatus, string? ValidationStatus);
    private sealed record ElementJsonRow(ulong DelemenId, string? Json);
    private sealed record NoteJsonRow(uint DnoteId, string? Json);
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IExtractionArtifactCleanupService
{
    Task ResetAsync(string refTipe, uint refId, CancellationToken cancellationToken = default);
}

public sealed class ExtractionArtifactCleanupService : IExtractionArtifactCleanupService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<ExtractionArtifactCleanupService> _logger;

    public ExtractionArtifactCleanupService(
        KorektorBukuDbContext db,
        ILogger<ExtractionArtifactCleanupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ResetAsync(string refTipe, uint refId, CancellationToken cancellationToken = default)
    {
        var normalizedRefTipe = string.IsNullOrWhiteSpace(refTipe)
            ? "dokumen"
            : refTipe.Trim().ToLowerInvariant();

        var sectionIds = await _db.DokumenSections
            .Where(section => section.DsecRefTipe == normalizedRefTipe && section.DsecRefId == refId)
            .Select(section => section.DsecId)
            .ToListAsync(cancellationToken);

        var partIds = sectionIds.Count == 0
            ? new List<uint>()
            : await _db.DokumenParts
                .Where(part => sectionIds.Contains(part.DsecId))
                .Select(part => part.DpartId)
                .ToListAsync(cancellationToken);

        var elementRows = partIds.Count == 0
            ? new List<ElementCleanupRow>()
            : await _db.DokumenElemens
                .Where(element => element.DpartId.HasValue && partIds.Contains(element.DpartId.Value))
                .Select(element => new ElementCleanupRow
                {
                    DelemenId = element.DelemenId,
                    Json = element.DelemenJsonTree
                })
                .ToListAsync(cancellationToken);

        var noteRows = await _db.DokumenNotes
            .Where(note => note.DnoteRefTipe == normalizedRefTipe && note.DnoteRefId == refId)
            .Select(note => note.DnoteJsonTree)
            .ToListAsync(cancellationToken);

        var formatRefs = new JsonFormatReferenceSet();
        foreach (var row in elementRows)
            CollectFormatIds(row.Json, formatRefs);

        foreach (var noteJson in noteRows)
            CollectFormatIds(noteJson, formatRefs);

        var elementIds = elementRows
            .Select(row => row.DelemenId)
            .ToList();

        var sections = sectionIds.Count == 0
            ? new List<DokumenSection>()
            : await _db.DokumenSections
                .Where(section => sectionIds.Contains(section.DsecId))
                .ToListAsync(cancellationToken);

        var parts = partIds.Count == 0
            ? new List<DokumenPart>()
            : await _db.DokumenParts
                .Where(part => partIds.Contains(part.DpartId))
                .ToListAsync(cancellationToken);

        var elements = elementIds.Count == 0
            ? new List<DokumenElemen>()
            : await _db.DokumenElemens
                .Where(element => elementIds.Contains(element.DelemenId))
                .ToListAsync(cancellationToken);

        var visuals = await _db.DokumenElemenVisuals
            .Where(visual => visual.DevRefTipe == normalizedRefTipe && visual.DevRefId == refId)
            .ToListAsync(cancellationToken);

        var notes = await _db.DokumenNotes
            .Where(note => note.DnoteRefTipe == normalizedRefTipe && note.DnoteRefId == refId)
            .ToListAsync(cancellationToken);

        var paragraphFormats = formatRefs.ParagraphFormatIds.Count == 0
            ? new List<DokumenFormatParagraf>()
            : await _db.DokumenFormatParagrafs
                .Where(item => formatRefs.ParagraphFormatIds.Contains(item.DfpId))
                .ToListAsync(cancellationToken);

        var textFormats = formatRefs.TextFormatIds.Count == 0
            ? new List<DokumenFormatText>()
            : await _db.DokumenFormatTexts
                .Where(item => formatRefs.TextFormatIds.Contains(item.DftxId))
                .ToListAsync(cancellationToken);

        var tableFormats = formatRefs.TableFormatIds.Count == 0
            ? new List<DokumenFormatTable>()
            : await _db.DokumenFormatTables
                .Where(item => formatRefs.TableFormatIds.Contains(item.DftId))
                .ToListAsync(cancellationToken);

        var drawingFormats = formatRefs.DrawingFormatIds.Count == 0
            ? new List<DokumenFormatDrawing>()
            : await _db.DokumenFormatDrawings
                .Where(item => formatRefs.DrawingFormatIds.Contains(item.DfdrId))
                .ToListAsync(cancellationToken);

        if (visuals.Count > 0)
            _db.DokumenElemenVisuals.RemoveRange(visuals);

        if (notes.Count > 0)
            _db.DokumenNotes.RemoveRange(notes);

        if (paragraphFormats.Count > 0)
            _db.DokumenFormatParagrafs.RemoveRange(paragraphFormats);

        if (textFormats.Count > 0)
            _db.DokumenFormatTexts.RemoveRange(textFormats);

        if (tableFormats.Count > 0)
            _db.DokumenFormatTables.RemoveRange(tableFormats);

        if (drawingFormats.Count > 0)
            _db.DokumenFormatDrawings.RemoveRange(drawingFormats);

        await _db.SaveChangesAsync(cancellationToken);

        if (elements.Count > 0)
        {
            _db.DokumenElemens.RemoveRange(elements);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (parts.Count > 0)
        {
            _db.DokumenParts.RemoveRange(parts);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (sections.Count > 0)
        {
            _db.DokumenSections.RemoveRange(sections);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (sectionIds.Count > 0 || visuals.Count > 0 || notes.Count > 0)
        {
            _logger.LogInformation(
                "Reset extraction artifacts for {RefTipe} {RefId}: sections={SectionCount}, parts={PartCount}, elements={ElementCount}, notes={NoteCount}, visuals={VisualCount}",
                normalizedRefTipe,
                refId,
                sections.Count,
                parts.Count,
                elements.Count,
                notes.Count,
                visuals.Count);
        }
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

    private sealed class ElementCleanupRow
    {
        public ulong DelemenId { get; init; }

        public string? Json { get; init; }
    }

    private sealed class JsonFormatReferenceSet
    {
        public HashSet<uint> ParagraphFormatIds { get; } = new();

        public HashSet<uint> TextFormatIds { get; } = new();

        public HashSet<uint> TableFormatIds { get; } = new();

        public HashSet<ulong> DrawingFormatIds { get; } = new();
    }
}

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

        var noteRows = normalizedRefTipe == "dokumen"
            ? await _db.DokumenNotes
                .Where(note => note.DokumenId == refId)
                .Select(note => note.DnoteJsonTree)
                .ToListAsync(cancellationToken)
            : new List<string?>();

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

        var notes = normalizedRefTipe == "dokumen"
            ? await _db.DokumenNotes
                .Where(note => note.DokumenId == refId)
                .ToListAsync(cancellationToken)
            : new List<DokumenNote>();

        var media = normalizedRefTipe == "dokumen"
            ? await _db.DokumenMedias
                .Where(item => item.DokumenId == (int)refId)
                .ToListAsync(cancellationToken)
            : new List<DokumenMedia>();

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

        var tableRowFormats = formatRefs.TableRowFormatIds.Count == 0
            ? new List<DokumenFormatTableRow>()
            : await _db.DokumenFormatTableRows
                .Where(item => formatRefs.TableRowFormatIds.Contains(item.DftrId))
                .ToListAsync(cancellationToken);

        var tableCellFormats = formatRefs.TableCellFormatIds.Count == 0
            ? new List<DokumenFormatTableCell>()
            : await _db.DokumenFormatTableCells
                .Where(item => formatRefs.TableCellFormatIds.Contains(item.DftcId))
                .ToListAsync(cancellationToken);

        var drawingFormats = formatRefs.DrawingFormatIds.Count == 0
            ? new List<DokumenFormatDrawing>()
            : await _db.DokumenFormatDrawings
                .Where(item => formatRefs.DrawingFormatIds.Contains(item.DfdrId))
                .ToListAsync(cancellationToken);

        var fieldFormats = formatRefs.FieldFormatIds.Count == 0
            ? new List<DokumenFormatField>()
            : await _db.DokumenFormatFields
                .Where(item => formatRefs.FieldFormatIds.Contains(item.DffdId))
                .ToListAsync(cancellationToken);

        if (visuals.Count > 0)
            _db.DokumenElemenVisuals.RemoveRange(visuals);

        if (notes.Count > 0)
            _db.DokumenNotes.RemoveRange(notes);

        if (media.Count > 0)
            _db.DokumenMedias.RemoveRange(media);

        if (paragraphFormats.Count > 0)
            _db.DokumenFormatParagrafs.RemoveRange(paragraphFormats);

        if (textFormats.Count > 0)
            _db.DokumenFormatTexts.RemoveRange(textFormats);

        if (tableFormats.Count > 0)
            _db.DokumenFormatTables.RemoveRange(tableFormats);

        if (tableRowFormats.Count > 0)
            _db.DokumenFormatTableRows.RemoveRange(tableRowFormats);

        if (tableCellFormats.Count > 0)
            _db.DokumenFormatTableCells.RemoveRange(tableCellFormats);

        if (drawingFormats.Count > 0)
            _db.DokumenFormatDrawings.RemoveRange(drawingFormats);

        if (fieldFormats.Count > 0)
            _db.DokumenFormatFields.RemoveRange(fieldFormats);

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

        if (sectionIds.Count > 0 || visuals.Count > 0 || notes.Count > 0 || media.Count > 0)
        {
            _logger.LogInformation(
                "Reset extraction artifacts for {RefTipe} {RefId}: sections={SectionCount}, parts={PartCount}, elements={ElementCount}, notes={NoteCount}, visuals={VisualCount}, media={MediaCount}",
                normalizedRefTipe,
                refId,
                sections.Count,
                parts.Count,
                elements.Count,
                notes.Count,
                visuals.Count,
                media.Count);
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
                        case "dftr_id":
                            AddUInt(property.Value, refs.TableRowFormatIds);
                            break;
                        case "dftc_id":
                            AddUInt(property.Value, refs.TableCellFormatIds);
                            break;
                        case "dfdr_id":
                            AddULong(property.Value, refs.DrawingFormatIds);
                            break;
                        case "dffd_id":
                            AddULong(property.Value, refs.FieldFormatIds);
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

        public HashSet<uint> TableRowFormatIds { get; } = new();

        public HashSet<uint> TableCellFormatIds { get; } = new();

        public HashSet<ulong> DrawingFormatIds { get; } = new();

        public HashSet<ulong> FieldFormatIds { get; } = new();
    }
}

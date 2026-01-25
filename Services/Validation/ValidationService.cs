using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

#region DTOs for Aturan JSON Values

public class RuleValue<T>
{
    [JsonPropertyName("value")]
    public T? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }
}

public class DecimalRuleValue
{
    [JsonPropertyName("value")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }
}

public class FlexibleDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                raw = raw.Trim().Replace(',', '.');
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }

        throw new JsonException("Invalid decimal value.");
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

// Paper Size Rule
public class PaperSectionRule
{
    [JsonPropertyName("section")]
    public SectionRules? Section { get; set; }
}

public class SectionRules
{
    [JsonPropertyName("awal")]
    public RuleValue<List<PaperSpec>>? Awal { get; set; }

    [JsonPropertyName("isi")]
    public RuleValue<List<PaperSpec>>? Isi { get; set; }

    [JsonPropertyName("akhir")]
    public RuleValue<List<PaperSpec>>? Akhir { get; set; }

    [JsonPropertyName("lampiran")]
    public RuleValue<List<PaperSpec>>? Lampiran { get; set; }
}

public class PaperSpec
{
    [JsonPropertyName("size")]
    public string? Size { get; set; } // "A4", "A3"

    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; } // "PORTRAIT", "LANDSCAPE"
}

// Margin Rule
public class MarginRule
{
    [JsonPropertyName("paper")]
    public PaperMargins? Paper { get; set; }
}

public class PaperMargins
{
    [JsonPropertyName("a4_portrait")]
    public RuleValue<MarginSpec>? A4Portrait { get; set; }

    [JsonPropertyName("a4_landscape")]
    public RuleValue<MarginSpec>? A4Landscape { get; set; }

    [JsonPropertyName("a3_landscape")]
    public RuleValue<MarginSpec>? A3Landscape { get; set; }
}

public class MarginSpec
{
    [JsonPropertyName("top")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Top { get; set; }

    [JsonPropertyName("bottom")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Bottom { get; set; }

    [JsonPropertyName("left")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Left { get; set; }

    [JsonPropertyName("right")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Right { get; set; }
}

// Header Footer Rule
public class HeaderFooterRule
{
    [JsonPropertyName("header_from_top")]
    public DecimalRuleValue? HeaderFromTop { get; set; }

    [JsonPropertyName("footer_from_bottom")]
    public DecimalRuleValue? FooterFromBottom { get; set; }
}

// Gutter Rule (for binding margin)
public class GutterRule
{
    [JsonPropertyName("gutter")]
    public decimal Gutter { get; set; } // in cm

    [JsonPropertyName("position")]
    public string? Position { get; set; } // "left" or "top"
}

// Column Rule
public class ColumnRule
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}

// Page Numbering Rule
public class PageNumberingRule
{
    [JsonPropertyName("section")]
    public PageNumberingSectionRules? Section { get; set; }
}

public class PageNumberingSectionRules
{
    [JsonPropertyName("awal")]
    public PageNumberingSpec? Awal { get; set; }

    [JsonPropertyName("isi")]
    public PageNumberingSpec? Isi { get; set; }

    [JsonPropertyName("akhir")]
    public PageNumberingSpec? Akhir { get; set; }

    [JsonPropertyName("lampiran")]
    public PageNumberingSpec? Lampiran { get; set; }
}

public class PageNumberingSpec
{
    [JsonPropertyName("format")]
    public string? Format { get; set; } // "decimal", "lowerRoman", "upperRoman", "lowerLetter", "upperLetter"

    [JsonPropertyName("start")]
    public int? Start { get; set; } // Starting page number (null means continue)
}

// Chapter Title Rule (judul_bab)
public class ChapterTitleRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public TitleNumberingRule? Numbering { get; set; }

    [JsonPropertyName("struktur_konten")]
    public ChapterContentStructureRule? StrukturKonten { get; set; }
}

public class TitleFontRule
{
    [JsonPropertyName("font_name")]
    public RuleValue<string>? FontName { get; set; }

    [JsonPropertyName("font_size")]
    public DecimalRuleValue? FontSize { get; set; }

    [JsonPropertyName("font_style")]
    public TitleFontStyleRule? FontStyle { get; set; }
}

public class TitleFontStyleRule
{
    [JsonPropertyName("bold")]
    public RuleValue<bool>? Bold { get; set; }

    [JsonPropertyName("italic")]
    public RuleValue<bool>? Italic { get; set; }

    [JsonPropertyName("underline")]
    public RuleValue<bool>? Underline { get; set; }
}

public class TitleParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public RuleValue<string>? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class TitleParagraphSpacingRule
{
    [JsonPropertyName("line_spacing")]
    public DecimalRuleValue? LineSpacing { get; set; }

    [JsonPropertyName("before")]
    public DecimalRuleValue? Before { get; set; }

    [JsonPropertyName("after")]
    public DecimalRuleValue? After { get; set; }
}

public class TitleNumberingRule
{
    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }

    [JsonPropertyName("case")]
    public RuleValue<string>? Case { get; set; }

    [JsonPropertyName("enter_after_number")]
    public RuleValue<bool>? EnterAfterNumber { get; set; }
}

public class ChapterContentStructureRule
{
    [JsonPropertyName("satu_baris_kosong_setelah")]
    public RuleValue<bool>? SatuBarisKosongSetelah { get; set; }

    [JsonPropertyName("min_satu_paragraf_sebelum_subbab")]
    public RuleValue<bool>? MinSatuParagrafSebelumSubbab { get; set; }
}

// Subchapter Title Rule (judul_subbab)
public class SubchapterTitleRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public SubchapterParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public TitleNumberingRule? Numbering { get; set; }

    [JsonPropertyName("struktur_konten")]
    public SubchapterContentStructureRule? StrukturKonten { get; set; }
}

public class SubchapterParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("hanging_min_cm")]
    public DecimalRuleValue? HangingMinCm { get; set; }

    [JsonPropertyName("hanging_max_cm")]
    public DecimalRuleValue? HangingMaxCm { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class SubchapterContentStructureRule
{
    [JsonPropertyName("minimal_satu_paragraf_setelah")]
    public RuleValue<bool>? MinimalSatuParagrafSetelah { get; set; }

    [JsonPropertyName("cegah_posisi_paling_bawah")]
    public RuleValue<bool>? CegahPosisiPalingBawah { get; set; }

    [JsonPropertyName("cegah_subbab_tunggal")]
    public RuleValue<bool>? CegahSubbabTunggal { get; set; }
}

// Paragraph Rule (paragraf)
public class ParagraphRule
{
    [JsonPropertyName("font")]
    public ParagraphFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public ParagraphFormatRule? Paragraph { get; set; }
}

public class ParagraphFontRule
{
    [JsonPropertyName("font_name")]
    public RuleValue<string>? FontName { get; set; }

    [JsonPropertyName("font_size")]
    public DecimalRuleValue? FontSize { get; set; }
}

public class ParagraphFormatRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("first_line_indent")]
    public DecimalRuleValue? FirstLineIndent { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

// List Item Rule (item_daftar)
public class ListItemRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public ListItemParagraphRule? Paragraph { get; set; }
}

public class ListItemParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public ListItemIndentationRule? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class ListItemIndentationRule
{
    [JsonPropertyName("left_indent")]
    public DecimalRuleValue? LeftIndent { get; set; }

    [JsonPropertyName("hanging")]
    public DecimalRuleValue? Hanging { get; set; }
}

// Image Rule (gambar)
public class ImageRule
{
    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("position")]
    public ImagePositionRule? Position { get; set; }
}

public class ImagePositionRule
{
    [JsonPropertyName("layout_option")]
    public RuleValue<string>? LayoutOption { get; set; }

    [JsonPropertyName("cegah_melebihi_margin")]
    public RuleValue<bool>? CegahMelebihiMargin { get; set; }

    [JsonPropertyName("cegah_memenuhi_halaman")]
    public RuleValue<bool>? CegahMemenuhiHalaman { get; set; }
}

// Caption Image Rule (caption_gambar)
public class CaptionImageRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public CaptionNumberingRule? Numbering { get; set; }

    [JsonPropertyName("position")]
    public RuleValue<string>? Position { get; set; }
}

public class CaptionNumberingRule
{
    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }

    [JsonPropertyName("case")]
    public RuleValue<string>? Case { get; set; }

    [JsonPropertyName("enter_after_numbering")]
    public RuleValue<bool>? EnterAfterNumbering { get; set; }
}

#endregion

#region Validation Result DTOs

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; set; } = new();
    public int TotalChecks { get; set; }
    public int PassedChecks { get; set; }
    public decimal Score => TotalChecks > 0 ? (decimal)PassedChecks / TotalChecks * 100 : 100;
}

public class ErrorLocation
{
    public int HalamanKe { get; set; }
    public ErrorBbox? Bbox { get; set; }
}

public class ErrorBbox
{
    public decimal X0 { get; set; }
    public decimal Y0 { get; set; }
    public decimal X1 { get; set; }
    public decimal Y1 { get; set; }
}

public class ValidationError
{
    public string Category { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public int? SectionIndex { get; set; }
    public List<ErrorLocation> Locations { get; set; } = new();
    public string? DiffType { get; set; }
    public string? Cause { get; set; }
    public bool? HasNumbering { get; set; }
    public string? StyleName { get; set; }
    public string? StyleId { get; set; }
    public string? Evidence { get; set; }
    public string? ToolRequirement { get; set; }
    public string? FeatureName { get; set; }
    public List<string>? AllowedActions { get; set; }
    public List<string>? DisallowedActions { get; set; }
    public string? ScopeHint { get; set; }
    public string? PageRange { get; set; }
    public string? PrevElementText { get; set; }
    public string? PrevElementLabel { get; set; }
    public string? NextElementText { get; set; }
    public string? NextElementLabel { get; set; }
    public decimal? PageMarginTopCm { get; set; }
    public decimal? PageMarginBottomCm { get; set; }
    public decimal? PageMarginLeftCm { get; set; }
    public decimal? PageMarginRightCm { get; set; }
    public ulong? DokumenElemenId { get; set; }
    public bool IsRequired { get; set; } = true;
}

#endregion

public interface IValidationService
{
    Task<ValidationResult> ValidateDokumenAsync(int dokumenId, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePageSettingsAsync(int dokumenId, CancellationToken cancellationToken = default);
}

public partial class ValidationService : IValidationService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<ValidationService> _logger;

    // Conversion constants
    private const decimal TwipsPerCm = 566.929m; // 1 cm = 566.929 twips
    private const decimal TwipsTolerance = 28.35m; // ~0.5mm tolerance
    private const decimal EmusPerCm = 360000m; // 1 cm = 360,000 EMUs

    // Standard paper sizes in twips (width x height for portrait)
    private static readonly Dictionary<string, (uint Width, uint Height)> PaperSizes = new()
    {
        { "A4", (11906, 16838) },  // 210mm x 297mm
        { "A3", (16838, 23811) },  // 297mm x 420mm
        { "LETTER", (12240, 15840) }, // 8.5" x 11"
        { "LEGAL", (12240, 20160) }  // 8.5" x 14"
    };

    public ValidationService(KorektorBukuDbContext db, ILogger<ValidationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateDokumenAsync(int dokumenId, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        // Validate page settings
        var pageResult = await ValidatePageSettingsAsync(dokumenId, cancellationToken);
        result.Errors.AddRange(pageResult.Errors);
        result.TotalChecks += pageResult.TotalChecks;
        result.PassedChecks += pageResult.PassedChecks;

        var classification = await ClassifyElementsAsync(dokumenId, cancellationToken);

        // Validate chapter title
        var titleResult = await ValidateChapterTitleAsync(dokumenId, classification.ChapterTitleIds, cancellationToken);
        result.Errors.AddRange(titleResult.Errors);
        result.TotalChecks += titleResult.TotalChecks;
        result.PassedChecks += titleResult.PassedChecks;

        // Validate subchapter title
        var subchapterResult = await ValidateSubchapterTitleAsync(dokumenId, classification.SubchapterIds, cancellationToken);
        result.Errors.AddRange(subchapterResult.Errors);
        result.TotalChecks += subchapterResult.TotalChecks;
        result.PassedChecks += subchapterResult.PassedChecks;

        // Validate paragraphs
        var paragraphResult = await ValidateParagraphAsync(
            dokumenId,
            classification.ParagraphIds,
            classification.ListItemIds,
            cancellationToken);
        result.Errors.AddRange(paragraphResult.Errors);
        result.TotalChecks += paragraphResult.TotalChecks;
        result.PassedChecks += paragraphResult.PassedChecks;

        // Validate list items
        var listItemResult = await ValidateListItemAsync(dokumenId, classification.ListItemIds, cancellationToken);
        result.Errors.AddRange(listItemResult.Errors);
        result.TotalChecks += listItemResult.TotalChecks;
        result.PassedChecks += listItemResult.PassedChecks;

        // Validate images
        var imageResult = await ValidateImageAsync(dokumenId, cancellationToken);
        result.Errors.AddRange(imageResult.Errors);
        result.TotalChecks += imageResult.TotalChecks;
        result.PassedChecks += imageResult.PassedChecks;

        // TODO: Add more validation categories here
        // - Table validation
        // etc.

        return result;
    }

    private sealed class ElementClassification
    {
        public HashSet<ulong> ChapterTitleIds { get; } = new();
        public HashSet<ulong> SubchapterIds { get; } = new();
        public HashSet<ulong> ListItemIds { get; } = new();
        public HashSet<ulong> ParagraphIds { get; } = new();
    }

    private async Task<ElementClassification> ClassifyElementsAsync(
        int dokumenId,
        CancellationToken cancellationToken)
    {
        var classification = new ElementClassification();

        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DokumenId == (uint)dokumenId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new BodyElementInfo { DelemenId = e.DelemenId, DelemenType = e.DelemenType, DelemenJsonTree = e.DelemenJsonTree })
            .ToListAsync(cancellationToken);

        if (bodyElements.Count == 0)
            return classification;

        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        foreach (var elem in bodyElements)
        {
            if (!labelMap.TryGetValue(elem.DelemenId, out var rawLabel))
                continue;

            var normalized = NormalizeLabel(rawLabel);
            if (normalized == "judul_bab")
            {
                classification.ChapterTitleIds.Add(elem.DelemenId);
                continue;
            }

            if (normalized == "judul_subbab")
            {
                classification.SubchapterIds.Add(elem.DelemenId);
                continue;
            }

            if (IsListLabel(normalized))
            {
                classification.ListItemIds.Add(elem.DelemenId);
                continue;
            }

            if (normalized == "paragraf")
            {
                classification.ParagraphIds.Add(elem.DelemenId);
            }
        }

        return classification;
    }

    private async Task PersistStructureLabelsAsync(
        IReadOnlyList<BodyElementInfo> bodyElements,
        ElementClassification classification,
        Dictionary<ulong, ElementContentInfo> contentById,
        Dictionary<uint, DokumenFormatParagraf> paragraphFormats,
        CancellationToken cancellationToken)
    {
        if (bodyElements.Count == 0)
            return;

        var labelById = BuildStructureLabelMap(bodyElements, classification, contentById, paragraphFormats);
        if (labelById.Count == 0)
            return;

        var (idColumn, labelColumn) = await ResolveVisualColumnsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn))
            return;

        if (!string.IsNullOrWhiteSpace(labelColumn) &&
            labelColumn.Equals("dev_label_struktural", StringComparison.OrdinalIgnoreCase))
            return;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS " +
                                       "WHERE TABLE_SCHEMA = DATABASE() " +
                                       "AND TABLE_NAME = 'dokumen_elemen_visual' " +
                                       "AND COLUMN_NAME = 'dev_label_struktural' " +
                                       "LIMIT 1";

                var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);
                if (exists == null || exists == DBNull.Value)
                    return;
            }

            const int batchSize = 200;
            foreach (var chunk in labelById.Chunk(batchSize))
            {
                using var cmd = connection.CreateCommand();
                var sb = new StringBuilder();
                sb.Append("UPDATE `dokumen_elemen_visual` ");
                sb.Append("SET `dev_label_struktural` = CASE `")
                  .Append(idColumn)
                  .Append("` ");

                var ids = new List<string>();
                var index = 0;
                foreach (var (id, label) in chunk)
                {
                    var paramName = "@label" + index;
                    sb.Append("WHEN ").Append(id).Append(" THEN ").Append(paramName).Append(' ');

                    var param = cmd.CreateParameter();
                    param.ParameterName = paramName;
                    param.Value = label ?? (object)DBNull.Value;
                    cmd.Parameters.Add(param);

                    ids.Add(id.ToString(CultureInfo.InvariantCulture));
                    index++;
                }

                sb.Append("ELSE `dev_label_struktural` END ");
                sb.Append("WHERE `").Append(idColumn).Append("` IN (")
                  .Append(string.Join(",", ids))
                  .Append(')');

                cmd.CommandText = sb.ToString();
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update dev_label_struktural");
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    private static Dictionary<ulong, string?> BuildStructureLabelMap(
        IReadOnlyList<BodyElementInfo> bodyElements,
        ElementClassification classification,
        Dictionary<ulong, ElementContentInfo> contentById,
        Dictionary<uint, DokumenFormatParagraf> paragraphFormats)
    {
        var labels = new Dictionary<ulong, string?>(bodyElements.Count);
        foreach (var elem in bodyElements)
        {
            string? label = null;

            if (classification.ChapterTitleIds.Contains(elem.DelemenId))
            {
                label = "judul_bab";
            }
            else if (classification.SubchapterIds.Contains(elem.DelemenId))
            {
                label = "judul_subbab";
            }
            else if (classification.ListItemIds.Contains(elem.DelemenId))
            {
                label = BuildListStructureLabel(elem, contentById, paragraphFormats);
            }
            else if (classification.ParagraphIds.Contains(elem.DelemenId))
            {
                label = "paragraf";
            }

            labels[elem.DelemenId] = label;
        }

        return labels;
    }

    private static string BuildListStructureLabel(
        BodyElementInfo element,
        Dictionary<ulong, ElementContentInfo> contentById,
        Dictionary<uint, DokumenFormatParagraf> paragraphFormats)
    {
        DokumenFormatParagraf? format = null;
        if (contentById.TryGetValue(element.DelemenId, out var content) &&
            content.ParagraphFormatId.HasValue)
        {
            paragraphFormats.TryGetValue(content.ParagraphFormatId.Value, out format);
        }

        var level = TryParseListItemLevel(element.DelemenType, format);
        if (level.HasValue)
            return $"list_level_{level.Value + 1}";

        return "list_item";
    }

    /// <summary>
    /// Creates a list of ErrorLocation from a page number and merged bbox string.
    /// This is the legacy overload - prefer using the Dictionary overload for multi-page support.
    /// </summary>
    protected static List<ErrorLocation> CreateLocations(int? pageNumber, string? mergedBbox)
    {
        var locations = new List<ErrorLocation>();
        
        if (!pageNumber.HasValue)
            return locations;

        var loc = new ErrorLocation { HalamanKe = pageNumber.Value };

        if (!string.IsNullOrWhiteSpace(mergedBbox))
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(mergedBbox);
                var root = doc.RootElement;
                
                loc.Bbox = new ErrorBbox
                {
                    X0 = root.TryGetProperty("x0", out var x0) ? x0.GetDecimal() : 0,
                    Y0 = root.TryGetProperty("y0", out var y0) ? y0.GetDecimal() : 0,
                    X1 = root.TryGetProperty("x1", out var x1) ? x1.GetDecimal() : 0,
                    Y1 = root.TryGetProperty("y1", out var y1) ? y1.GetDecimal() : 0
                };
            }
            catch
            {
                // Ignore parsing errors, add location without bbox
            }
        }

        locations.Add(loc);
        return locations;
    }

    /// <summary>
    /// Creates a list of ErrorLocation from a dictionary of page numbers to bounding boxes.
    /// Supports elements that span multiple pages.
    /// </summary>
    /// <param name="pageBboxMap">Dictionary where key is page number and value is the merged bbox for that page</param>
    protected static List<ErrorLocation> CreateLocations(Dictionary<int, ErrorBbox> pageBboxMap)
    {
        var locations = new List<ErrorLocation>();
        
        if (pageBboxMap == null || pageBboxMap.Count == 0)
            return locations;

        foreach (var (pageNumber, bbox) in pageBboxMap.OrderBy(kv => kv.Key))
        {
            locations.Add(new ErrorLocation
            {
                HalamanKe = pageNumber,
                Bbox = bbox
            });
        }

        return locations;
    }

    /// <summary>
    /// Creates a list of ErrorLocation from page numbers and optional per-page bounding boxes.
    /// </summary>
    protected static List<ErrorLocation> CreateLocations(
        IEnumerable<int> pageNumbers,
        Dictionary<int, ErrorBbox>? pageBboxMap)
    {
        var pageSet = new HashSet<int>();
        if (pageNumbers != null)
        {
            foreach (var page in pageNumbers)
                pageSet.Add(page);
        }

        if (pageBboxMap != null)
        {
            foreach (var page in pageBboxMap.Keys)
                pageSet.Add(page);
        }

        var pages = pageSet
            .OrderBy(p => p)
            .ToList();

        var locations = new List<ErrorLocation>(pages.Count);
        if (pages.Count == 0)
            return locations;

        foreach (var page in pages)
        {
            ErrorBbox? bbox = null;
            if (pageBboxMap != null)
                pageBboxMap.TryGetValue(page, out bbox);

            locations.Add(new ErrorLocation
            {
                HalamanKe = page,
                Bbox = bbox
            });
        }

        return locations;
    }

    private sealed class ElementNeighborContext
    {
        public string? PrevText { get; init; }
        public string? PrevLabel { get; init; }
        public string? NextText { get; init; }
        public string? NextLabel { get; init; }
        public decimal? MarginTopCm { get; init; }
        public decimal? MarginBottomCm { get; init; }
        public decimal? MarginLeftCm { get; init; }
        public decimal? MarginRightCm { get; init; }
    }

    private sealed class PageMarginSnapshot
    {
        public decimal? TopCm { get; init; }
        public decimal? BottomCm { get; init; }
        public decimal? LeftCm { get; init; }
        public decimal? RightCm { get; init; }
    }

    private static void ApplyContextToErrors(List<ValidationError> errors, int startIndex, ElementNeighborContext? context)
    {
        if (context == null)
            return;

        for (var i = startIndex; i < errors.Count; i++)
            ApplyContext(errors[i], context);
    }

    private static void ApplyElementIdToErrors(List<ValidationError> errors, int startIndex, ulong elementId)
    {
        for (var i = startIndex; i < errors.Count; i++)
        {
            if (!errors[i].DokumenElemenId.HasValue)
                errors[i].DokumenElemenId = elementId;
        }
    }

    private static void ApplyContext(ValidationError error, ElementNeighborContext context)
    {
        if (string.IsNullOrWhiteSpace(error.PrevElementText))
            error.PrevElementText = context.PrevText;
        if (string.IsNullOrWhiteSpace(error.PrevElementLabel))
            error.PrevElementLabel = context.PrevLabel;
        if (string.IsNullOrWhiteSpace(error.NextElementText))
            error.NextElementText = context.NextText;
        if (string.IsNullOrWhiteSpace(error.NextElementLabel))
            error.NextElementLabel = context.NextLabel;

        if (!error.PageMarginTopCm.HasValue)
            error.PageMarginTopCm = context.MarginTopCm;
        if (!error.PageMarginBottomCm.HasValue)
            error.PageMarginBottomCm = context.MarginBottomCm;
        if (!error.PageMarginLeftCm.HasValue)
            error.PageMarginLeftCm = context.MarginLeftCm;
        if (!error.PageMarginRightCm.HasValue)
            error.PageMarginRightCm = context.MarginRightCm;
    }

    private static Dictionary<ulong, ElementNeighborContext> BuildNeighborContexts(
        IReadOnlyList<ulong> orderedElementIds,
        Dictionary<ulong, string?> elementJsonById,
        Dictionary<ulong, string> labelMap,
        Dictionary<ulong, PageMarginSnapshot> marginsById)
    {
        var contexts = new Dictionary<ulong, ElementNeighborContext>(orderedElementIds.Count);
        var contentCache = new Dictionary<ulong, ElementContentInfo>();

        string? GetText(ulong id)
        {
            if (!elementJsonById.TryGetValue(id, out var json))
                return null;

            if (!contentCache.TryGetValue(id, out var content))
            {
                content = ParseElementContent(json);
                contentCache[id] = content;
            }

            return NormalizeContextText(content.PlainText);
        }

        for (var i = 0; i < orderedElementIds.Count; i++)
        {
            var id = orderedElementIds[i];
            var prevId = i > 0 ? orderedElementIds[i - 1] : (ulong?)null;
            var nextId = i + 1 < orderedElementIds.Count ? orderedElementIds[i + 1] : (ulong?)null;

            string? prevLabel = null;
            string? nextLabel = null;
            if (prevId.HasValue)
                labelMap.TryGetValue(prevId.Value, out prevLabel);
            if (nextId.HasValue)
                labelMap.TryGetValue(nextId.Value, out nextLabel);

            var margin = marginsById.TryGetValue(id, out var found) ? found : null;

            contexts[id] = new ElementNeighborContext
            {
                PrevText = prevId.HasValue ? GetText(prevId.Value) : null,
                PrevLabel = prevLabel,
                NextText = nextId.HasValue ? GetText(nextId.Value) : null,
                NextLabel = nextLabel,
                MarginTopCm = margin?.TopCm,
                MarginBottomCm = margin?.BottomCm,
                MarginLeftCm = margin?.LeftCm,
                MarginRightCm = margin?.RightCm
            };
        }

        return contexts;
    }

    private static string? NormalizeContextText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        const int maxLength = 160;
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength] + "...";
    }

    private static string NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        return label.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    }

    private async Task<Dictionary<ulong, PageMarginSnapshot>> LoadPageMarginsAsync(
        IEnumerable<ulong> elementIds,
        CancellationToken cancellationToken)
    {
        var ids = elementIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<ulong, PageMarginSnapshot>();

        var margins = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where ids.Contains(e.DelemenId)
            select new
            {
                e.DelemenId,
                s.DsecMarginTopTwips,
                s.DsecMarginBottomTwips,
                s.DsecMarginLeftTwips,
                s.DsecMarginRightTwips
            }).ToListAsync(cancellationToken);

        return margins
            .GroupBy(m => m.DelemenId)
            .ToDictionary(
                g => g.Key,
                g => new PageMarginSnapshot
                {
                    TopCm = TwipsToCm(g.First().DsecMarginTopTwips),
                    BottomCm = TwipsToCm(g.First().DsecMarginBottomTwips),
                    LeftCm = TwipsToCm(g.First().DsecMarginLeftTwips),
                    RightCm = TwipsToCm(g.First().DsecMarginRightTwips)
                });
    }

    private static decimal? TwipsToCm(uint? twips)
    {
        if (!twips.HasValue)
            return null;

        return Math.Round(twips.Value / TwipsPerCm, 2);
    }

    private static string NormalizeAlignmentValue(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return string.Empty;

        var normalized = alignment.Trim().ToLowerInvariant();
        if (normalized == "both")
            return "justify";

        return normalized;
    }

    private static bool AreAlignmentsEquivalent(string? actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        return string.Equals(
            NormalizeAlignmentValue(actual),
            NormalizeAlignmentValue(expected),
            StringComparison.OrdinalIgnoreCase);
    }

}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IAturanImportService
{
    Task ImportFromArtifactsAsync(uint aturanId, CancellationToken cancellationToken = default);
}

public sealed class AturanImportService : IAturanImportService
{
    private static readonly Regex ChapterTitleRegex = new(@"^\s*BAB\s+[IVXLCDM0-9]+\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SubchapterTitleRegex = new(@"^\s*\d+(?:\s*\.\s*\d+)*\.?\s*", RegexOptions.Compiled);
    private static readonly Regex ImageCaptionRegex = new(@"^\s*Gambar\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableCaptionRegex = new(@"^\s*Tabel\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CodeCaptionRegex = new(@"^\s*(Algoritma|Segmen\s+Program)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly KorektorBukuDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AturanImportService> _logger;

    public AturanImportService(
        KorektorBukuDbContext db,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<AturanImportService> logger)
    {
        _db = db;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ImportFromArtifactsAsync(uint aturanId, CancellationToken cancellationToken = default)
    {
        var aturan = await _db.Aturans.FirstOrDefaultAsync(a => a.AturanId == aturanId, cancellationToken);
        if (aturan == null)
            throw new InvalidOperationException($"Aturan {aturanId} tidak ditemukan");

        var detailDrafts = AturanExportCatalog.CreateDefaultDetails(aturanId)
            .ToDictionary(
                detail => NormalizeLabel(detail.AturanDetailKey),
                detail => new DraftDetail(detail, ParseDraftJson(detail)),
                StringComparer.OrdinalIgnoreCase);

        var sections = await _db.DokumenSections
            .Where(section => section.DsecRefTipe == "aturan" && section.DsecRefId == aturanId)
            .OrderBy(section => section.DsecIndex)
            .ThenBy(section => section.DsecId)
            .ToListAsync(cancellationToken);

        var bodyRows = await (from element in _db.DokumenElemens
            join part in _db.DokumenParts on element.DpartId equals part.DpartId
            join section in _db.DokumenSections on part.DsecId equals section.DsecId
            where section.DsecRefTipe == "aturan" &&
                  section.DsecRefId == aturanId &&
                  part.DpartType == "body"
            orderby section.DsecIndex, element.DelemenSequence, element.DelemenId
            select new ElementRow
            {
                ElementId = element.DelemenId,
                DsecId = section.DsecId,
                PartType = part.DpartType,
                PartPosition = part.DpartPosition ?? "default",
                ElementType = element.DelemenType,
                Json = element.DelemenJsonTree
            })
            .ToListAsync(cancellationToken);

        var headerFooterRows = await (from element in _db.DokumenElemens
            join part in _db.DokumenParts on element.DpartId equals part.DpartId
            join section in _db.DokumenSections on part.DsecId equals section.DsecId
            where section.DsecRefTipe == "aturan" &&
                  section.DsecRefId == aturanId &&
                  (part.DpartType == "header" || part.DpartType == "footer")
            orderby section.DsecIndex, part.DpartType, part.DpartPosition, element.DelemenSequence, element.DelemenId
            select new ElementRow
            {
                ElementId = element.DelemenId,
                DsecId = section.DsecId,
                PartType = part.DpartType,
                PartPosition = part.DpartPosition ?? "default",
                ElementType = element.DelemenType,
                Json = element.DelemenJsonTree
            })
            .ToListAsync(cancellationToken);

        var allRows = bodyRows.Concat(headerFooterRows).ToList();
        var labelMap = await LoadVisualLabelsAsync(allRows.Select(row => row.ElementId), aturanId, cancellationToken);
        var samples = await BuildSamplesAsync(allRows, labelMap, cancellationToken);

        ApplyPageSettings(detailDrafts["page_settings"].Json, sections);
        ApplyPageNumbering(
            detailDrafts["nomor_halaman"].Json,
            sections,
            samples.Where(sample => !string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase)).ToList());
        ApplyTitleRule(detailDrafts["judul_bab"].Json, FilterSamples(samples, "judul_bab"), ChapterTitleRegex, includeHanging: false, useCenterFallback: true, normalizeNumberingIndent: false);
        ApplyTitleRule(detailDrafts["judul_subbab"].Json, FilterSamples(samples, "judul_subbab"), SubchapterTitleRegex, includeHanging: true, useCenterFallback: false, normalizeNumberingIndent: true);
        ApplyParagraphRule(detailDrafts["paragraf"].Json, FilterSamples(samples, "paragraf"), includeFirstLineIndent: true, includeHanging: false, normalizeNumberingIndent: true);
        ApplyParagraphRule(detailDrafts["item_daftar"].Json, FilterListItemSamples(samples), includeFirstLineIndent: false, includeHanging: true, normalizeNumberingIndent: false);
        ApplyImageRule(detailDrafts["gambar"].Json, samples, ImageCaptionRegex);
        ApplyTableRule(detailDrafts["tabel"].Json, samples, TableCaptionRegex);
        ApplyCodeRule(detailDrafts["kode"].Json, samples, CodeCaptionRegex);
        ApplyFormulaRule(detailDrafts["rumus"].Json, FilterSamples(samples, "rumus", "formula", "equation"));
        ApplyFootnoteRule(detailDrafts["footnote"].Json, FilterSamples(samples, "footnote"));

        var normalizedDetails = new List<AturanDetail>();
        foreach (var draft in detailDrafts.Values)
        {
            var rawJson = draft.Json.ToJsonString(JsonOptions);
            if (!AturanDetailJsonNormalizer.TryNormalize(rawJson, out var normalizedJson, out var errorMessage))
                throw new InvalidOperationException($"Gagal menormalkan draft aturan {draft.Detail.AturanDetailKey}: {errorMessage}");

            if (!AturanDetailShapeValidator.TryValidate(draft.Detail.AturanDetailKey, normalizedJson, out var shapeError))
                throw new InvalidOperationException($"Draft aturan {draft.Detail.AturanDetailKey} tidak sesuai shape: {shapeError}");

            draft.Detail.AturanDetailJsonValue = normalizedJson;
            draft.Detail.AturanDetailCatatan = null;
            normalizedDetails.Add(draft.Detail);
        }

        var existingDetails = await _db.AturanDetails
            .Where(detail => detail.AturanId == aturanId)
            .ToListAsync(cancellationToken);
        if (existingDetails.Count > 0)
            _db.AturanDetails.RemoveRange(existingDetails);

        _db.AturanDetails.AddRange(normalizedDetails);
        aturan.AturanStatus = AturanStatusValues.MenungguReview;
        aturan.AturanUpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);

        await SendReviewReadyEmailAsync(aturan, cancellationToken);
    }

    private static JsonObject ParseDraftJson(AturanDetail detail)
    {
        return JsonNode.Parse(detail.AturanDetailJsonValue ?? "{}") as JsonObject
            ?? new JsonObject();
    }

    private static List<ElementSample> FilterSamples(IEnumerable<ElementSample> samples, params string[] labels)
    {
        var set = labels.Select(NormalizeLabel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return samples
            .Where(sample => set.Contains(sample.Label))
            .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<ElementSample> FilterListItemSamples(IEnumerable<ElementSample> samples)
    {
        return samples
            .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
            .Where(sample =>
                string.Equals(sample.Label, "list_item", StringComparison.OrdinalIgnoreCase) ||
                sample.Label.StartsWith("list_level_", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<List<ElementSample>> BuildSamplesAsync(
        List<ElementRow> rows,
        IReadOnlyDictionary<ulong, string> labelMap,
        CancellationToken cancellationToken)
    {
        var parsedRows = rows
            .Select((row, index) =>
            {
                var content = ParseElementContent(row.Json);
                labelMap.TryGetValue(row.ElementId, out var rawLabel);
                var normalizedLabel = NormalizeLabel(rawLabel);
                var normalizedText = NormalizeWhitespace(content.PlainText);
                var tableAggregate = ExtractTableContentAggregate(row.Json, out var tableFormatId);

                return new
                {
                    Row = row,
                    OrderIndex = index,
                    Content = content,
                    NormalizedLabel = normalizedLabel,
                    NormalizedText = normalizedText,
                    TableAggregate = tableAggregate,
                    TableFormatId = tableFormatId
                };
            })
            .ToList();

        var paragraphFormatIds = parsedRows
            .Select(item => item.Content.ParagraphFormatId)
            .Concat(parsedRows.SelectMany(item => item.TableAggregate.ParagraphFormatIds.Select(id => (uint?)id)))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var textFormatIds = parsedRows
            .SelectMany(item => item.Content.TextFormatIds)
            .Concat(parsedRows.SelectMany(item => item.TableAggregate.TextFormatIds))
            .Distinct()
            .ToList();

        var tableFormatIds = parsedRows
            .Select(item => item.TableFormatId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = paragraphFormatIds.Count == 0
            ? new Dictionary<uint, DokumenFormatParagraf>()
            : await _db.DokumenFormatParagrafs
                .Where(format => paragraphFormatIds.Contains(format.DfpId))
                .ToDictionaryAsync(format => format.DfpId, cancellationToken);

        var textFormats = textFormatIds.Count == 0
            ? new Dictionary<uint, DokumenFormatText>()
            : await _db.DokumenFormatTexts
                .Where(format => textFormatIds.Contains(format.DftxId))
                .ToDictionaryAsync(format => format.DftxId, cancellationToken);

        var tableFormats = tableFormatIds.Count == 0
            ? new Dictionary<uint, DokumenFormatTable>()
            : await _db.DokumenFormatTables
                .Where(format => tableFormatIds.Contains(format.DftId))
                .ToDictionaryAsync(format => format.DftId, cancellationToken);

        return parsedRows
            .Select(item =>
            {
                paragraphFormats.TryGetValue(item.Content.ParagraphFormatId ?? 0, out var paragraphFormat);
                tableFormats.TryGetValue(item.TableFormatId ?? 0, out var tableFormat);

                var runFormats = item.Content.TextFormatIds
                    .Where(textFormats.ContainsKey)
                    .Select(id => textFormats[id])
                    .GroupBy(format => format.DftxId)
                    .Select(group => group.First())
                    .ToList();

                var tableRunFormats = item.TableAggregate.TextFormatIds
                    .Where(textFormats.ContainsKey)
                    .Select(id => textFormats[id])
                    .GroupBy(format => format.DftxId)
                    .Select(group => group.First())
                    .ToList();

                var tableParagraphFormats = item.TableAggregate.ParagraphFormatIds
                    .Where(paragraphFormats.ContainsKey)
                    .Select(id => paragraphFormats[id])
                    .GroupBy(format => format.DfpId)
                    .Select(group => group.First())
                    .ToList();

                return new ElementSample
                {
                    Row = item.Row,
                    OrderIndex = item.OrderIndex,
                    Label = item.NormalizedLabel,
                    NormalizedText = item.NormalizedText,
                    Content = item.Content,
                    ParagraphFormat = paragraphFormat,
                    TextFormats = runFormats,
                    TableFormat = tableFormat,
                    TableContent = item.TableAggregate,
                    TableTextFormats = tableRunFormats,
                    TableParagraphFormats = tableParagraphFormats
                };
            })
            .ToList();
    }

    private async Task<IReadOnlyDictionary<ulong, string>> LoadVisualLabelsAsync(
        IEnumerable<ulong> elementIds,
        uint aturanId,
        CancellationToken cancellationToken)
    {
        var ids = elementIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<ulong, string>();

        var rows = await _db.DokumenElemenVisuals
            .Where(visual => visual.DevRefTipe == "aturan" &&
                             visual.DevRefId == aturanId &&
                             visual.DokumenElemenId.HasValue &&
                             ids.Contains(visual.DokumenElemenId.Value))
            .Select(visual => new
            {
                visual.DokumenElemenId,
                Label = visual.DevLabelStruktural ?? visual.DevLabel
            })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<ulong, string>();
        foreach (var row in rows)
        {
            if (!row.DokumenElemenId.HasValue || string.IsNullOrWhiteSpace(row.Label))
                continue;

            var elementId = row.DokumenElemenId.Value;
            if (!map.TryGetValue(elementId, out var existing) ||
                GetLabelPriority(row.Label!) < GetLabelPriority(existing))
            {
                map[elementId] = row.Label!;
            }
        }

        return map;
    }

    private static void ApplyPageSettings(JsonObject root, List<DokumenSection> sections)
    {
        if (sections.Count == 0)
            return;

        var section = sections
            .OrderBy(item => item.DsecIndex ?? uint.MaxValue)
            .ThenBy(item => item.DsecId)
            .First();

        TrySetWrappedValue(root, InferPaperSize(section), "paper", "size");
        TrySetWrappedValue(root, (section.DsecOrientation ?? "portrait").ToUpperInvariant(), "paper", "orientation");
        TrySetWrappedValue(root, TwipsToCm(section.DsecMarginTopTwips), "margin", "top");
        TrySetWrappedValue(root, TwipsToCm(section.DsecMarginBottomTwips), "margin", "bottom");
        TrySetWrappedValue(root, TwipsToCm(section.DsecMarginLeftTwips), "margin", "left");
        TrySetWrappedValue(root, TwipsToCm(section.DsecMarginRightTwips), "margin", "right");
        TrySetWrappedValue(root, TwipsToCm(section.DsecHeaderMarginTwips), "header_footer", "header_from_top");
        TrySetWrappedValue(root, TwipsToCm(section.DsecFooterMarginTwips), "header_footer", "footer_from_bottom");
        TrySetWrappedValue(root, TwipsToCm(section.DsecGutterTwips), "gutter", "size");
        TrySetWrappedValue(root, NormalizeGutterPosition(section.DsecGutterPosition), "gutter", "position");
        TrySetWrappedValue(root, section.DsecColumnCount.HasValue ? (int)section.DsecColumnCount.Value : 1, "column");
    }

    private static void ApplyPageNumbering(JsonObject root, List<DokumenSection> sections, List<ElementSample> headerFooterSamples)
    {
        if (sections.Count > 0)
        {
            var section = sections
                .OrderBy(item => item.DsecIndex ?? uint.MaxValue)
                .ThenBy(item => item.DsecId)
                .First();

            TrySetWrappedValue(root, NormalizeWordPageNumberFormat(section.DsecPageNumFormat), "numbering", "number_format");
            TrySetWrappedValue(root, sections.Any(item => item.DsecHasTitlePage), "variation", "different_first_page", "enabled");
            TrySetWrappedValue(root, sections.Any(item => item.DsecDifferentOddEven), "variation", "different_odd_even", "enabled");
        }

        var pageFieldSamples = headerFooterSamples
            .Where(sample => sample.Content.HasPageField)
            .ToList();
        if (pageFieldSamples.Count == 0)
            return;

        ApplyDominantTextStyle(root, pageFieldSamples.SelectMany(sample => sample.TextFormats).ToList(), "font");

        var dominantParagraph = SelectDominantParagraphFormat(pageFieldSamples
            .Select(sample => sample.ParagraphFormat)
            .Where(format => format != null)!);
        if (dominantParagraph != null)
            ApplyParagraphFormat(root, dominantParagraph, includeFirstLineIndent: true, includeHanging: false, "paragraph");

        ApplyPageNumberSlot(root, pageFieldSamples, "default", "variation", "default");
        ApplyPageNumberSlot(root, pageFieldSamples, "first", "variation", "different_first_page", "first");
        ApplyPageNumberSlot(root, pageFieldSamples, "even", "variation", "different_odd_even", "even");
    }

    private static void ApplyPageNumberSlot(
        JsonObject root,
        IReadOnlyList<ElementSample> samples,
        string partPosition,
        params string[] prefix)
    {
        var slotSamples = samples
            .Where(sample => string.Equals(NormalizePartPosition(sample.Row.PartPosition), partPosition, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (slotSamples.Count == 0)
            return;

        var preferredSample = slotSamples
            .OrderBy(sample => string.Equals(sample.Row.PartType, "footer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .First();

        TrySetWrappedValue(root, NormalizePartType(preferredSample.Row.PartType), prefix.Concat(["position", "location"]).ToArray());
        TrySetWrappedValue(
            root,
            NormalizeAlignmentValue(preferredSample.ParagraphFormat?.DfpJc) switch
            {
                "left" => "left",
                "right" => "right",
                "center" => "center",
                _ => "center"
            },
            prefix.Concat(["position", "alignment"]).ToArray());
    }

    private static void ApplyTitleRule(
        JsonObject root,
        List<ElementSample> samples,
        Regex numberingRegex,
        bool includeHanging,
        bool useCenterFallback,
        bool normalizeNumberingIndent)
    {
        if (samples.Count == 0)
            return;

        ApplyDominantTextStyle(root, samples.SelectMany(sample => sample.TextFormats).ToList(), "font");

        var dominantParagraph = SelectDominantParagraphFormat(
            samples.Select(sample => sample.ParagraphFormat),
            normalizeNumberingIndent);
        if (dominantParagraph != null)
            ApplyParagraphFormat(
                root,
                dominantParagraph,
                includeFirstLineIndent: false,
                includeHanging: includeHanging,
                normalizeNumberingIndent,
                "paragraph");
        else if (useCenterFallback)
            TrySetWrappedValue(root, "center", "paragraph", "alignment");

        var caseCandidates = ExtractCaseCandidateTexts(samples, numberingRegex)
            .Select(InferCase)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var dominantCase = MostCommon(caseCandidates);
        if (!string.IsNullOrWhiteSpace(dominantCase))
            TrySetWrappedValue(root, dominantCase, "numbering", "case");

        var hasEnterAfterNumber = DetermineEnterAfterNumber(samples, numberingRegex);
        if (HasWrappedValue(root, "numbering", "enter_after_number"))
            TrySetWrappedValue(root, hasEnterAfterNumber, "numbering", "enter_after_number");
        if (HasWrappedValue(root, "numbering", "enter_after_numbering"))
            TrySetWrappedValue(root, hasEnterAfterNumber, "numbering", "enter_after_numbering");
    }

    private static void ApplyParagraphRule(
        JsonObject root,
        List<ElementSample> samples,
        bool includeFirstLineIndent,
        bool includeHanging,
        bool normalizeNumberingIndent)
    {
        if (samples.Count == 0)
            return;

        var preferredSamples = normalizeNumberingIndent
            ? SelectPreferredParagraphRuleSamples(samples)
            : samples;

        ApplyDominantTextStyle(root, preferredSamples.SelectMany(sample => sample.TextFormats).ToList(), "font");

        var dominantParagraph = SelectDominantParagraphFormat(
            preferredSamples.Select(sample => sample.ParagraphFormat),
            normalizeNumberingIndent);
        if (dominantParagraph != null)
            ApplyParagraphFormat(
                root,
                dominantParagraph,
                includeFirstLineIndent,
                includeHanging,
                normalizeNumberingIndent,
                "paragraph");
    }

    private static void ApplyImageRule(JsonObject root, List<ElementSample> samples, Regex captionRegex)
    {
        var imageSamples = samples
            .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
            .Where(sample =>
                string.Equals(sample.Label, "gambar", StringComparison.OrdinalIgnoreCase) ||
                sample.Content.DrawingFormatIds.Count > 0 ||
                string.Equals(sample.Row.ElementType, "image", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (imageSamples.Count > 0)
        {
            var dominantParagraph = SelectDominantParagraphFormat(imageSamples.Select(sample => sample.ParagraphFormat));
            if (dominantParagraph != null)
                ApplyParagraphFormat(EnsureObjectAtPath(root, "gambar"), dominantParagraph, includeFirstLineIndent: false, includeHanging: false, "paragraph");
            else
                TrySetWrappedValue(root, "center", "gambar", "paragraph", "alignment");
        }

        var captionSamples = FilterSamples(samples, "caption_gambar");
        if (captionSamples.Count == 0)
        {
            captionSamples = samples
                .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
                .Where(sample => IsParagraphLike(sample) && captionRegex.IsMatch(sample.NormalizedText))
                .ToList();
        }

        ApplyCaptionRule(EnsureObjectAtPath(root, "caption_gambar"), captionSamples, imageSamples, captionRegex, "after");
    }

    private static void ApplyTableRule(JsonObject root, List<ElementSample> samples, Regex captionRegex)
    {
        var tableSamples = samples
            .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
            .Where(sample =>
                string.Equals(sample.Label, "tabel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sample.Label, "table", StringComparison.OrdinalIgnoreCase) ||
                sample.TableFormat != null ||
                string.Equals(sample.Row.ElementType, "table", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (tableSamples.Count > 0)
        {
            var alignment = MostCommon(tableSamples
                .Select(sample => NormalizeAlignmentValue(sample.TableFormat?.DftJc))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList());
            if (!string.IsNullOrWhiteSpace(alignment))
                TrySetWrappedValue(root, alignment, "tabel", "position", "alignment");

            var indentValues = tableSamples
                .Where(sample => sample.TableFormat?.DftTblIndTwips.HasValue == true)
                .Select(sample => Math.Round(sample.TableFormat!.DftTblIndTwips!.Value / 1440.0m * 2.54m, 2))
                .ToList();
            if (indentValues.Count > 0)
                TrySetWrappedValue(root, MostCommon(indentValues), "tabel", "position", "indent_from_left");

            var tableTextFormats = tableSamples
                .SelectMany(sample => sample.TableTextFormats)
                .ToList();
            ApplyDominantTextStyle(EnsureObjectAtPath(root, "tabel", "konten_tabel"), tableTextFormats, "font");

            var tableParagraphFormat = SelectDominantParagraphFormat(tableSamples.SelectMany(sample => sample.TableParagraphFormats));
            if (tableParagraphFormat != null)
                ApplyParagraphFormat(EnsureObjectAtPath(root, "tabel", "konten_tabel"), tableParagraphFormat, includeFirstLineIndent: false, includeHanging: false, "paragraph");
        }

        var captionSamples = FilterSamples(samples, "caption_tabel");
        if (captionSamples.Count == 0)
        {
            captionSamples = samples
                .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
                .Where(sample => IsParagraphLike(sample) && captionRegex.IsMatch(sample.NormalizedText))
                .ToList();
        }

        ApplyCaptionRule(EnsureObjectAtPath(root, "caption_tabel"), captionSamples, tableSamples, captionRegex, "before");
    }

    private static void ApplyCodeRule(JsonObject root, List<ElementSample> samples, Regex captionRegex)
    {
        var codeSamples = samples
            .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
            .Where(sample => string.Equals(sample.Label, "kode", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (codeSamples.Count > 0)
        {
            var codeTextFormats = codeSamples
                .SelectMany(sample => sample.TextFormats.Concat(sample.TableTextFormats))
                .ToList();
            ApplyDominantTextStyle(EnsureObjectAtPath(root, "kode"), codeTextFormats, "font");

            var codeParagraph = SelectDominantParagraphFormat(codeSamples
                .Select(sample => sample.ParagraphFormat)
                .Concat(codeSamples.SelectMany(sample => sample.TableParagraphFormats)),
                normalizeNumberingIndent: true);
            if (codeParagraph != null)
                ApplyParagraphFormat(
                    EnsureObjectAtPath(root, "kode"),
                    codeParagraph,
                    includeFirstLineIndent: false,
                    includeHanging: true,
                    normalizeNumberingIndent: true,
                    "paragraph");
        }

        var titleSamples = FilterSamples(samples, "judul_kode");
        if (titleSamples.Count == 0)
        {
            titleSamples = samples
                .Where(sample => string.Equals(sample.Row.PartType, "body", StringComparison.OrdinalIgnoreCase))
                .Where(sample => IsParagraphLike(sample) && captionRegex.IsMatch(sample.NormalizedText))
                .ToList();
        }

        if (titleSamples.Count > 0)
            TrySetWrappedValue(root, true, "kode", "numbering", "use_numbering");

        ApplyCaptionRule(EnsureObjectAtPath(root, "judul_kode"), titleSamples, codeSamples, captionRegex, "before");
    }

    private static void ApplyFormulaRule(JsonObject root, List<ElementSample> samples)
    {
        if (samples.Count == 0)
            return;

        ApplyDominantTextStyle(root, samples.SelectMany(sample => sample.TextFormats).ToList(), "font");

        var paragraph = SelectDominantParagraphFormat(samples.Select(sample => sample.ParagraphFormat));
        if (paragraph != null)
        {
            ApplyParagraphFormat(root, paragraph, includeFirstLineIndent: true, includeHanging: false, "paragraph");
            TrySetWrappedValue(root, GetLeftIndentCm(paragraph), "position", "overall_indent_cm");
        }
    }

    private static void ApplyFootnoteRule(JsonObject root, List<ElementSample> samples)
    {
        if (samples.Count == 0)
            return;

        ApplyDominantTextStyle(EnsureObjectAtPath(root, "footnote_text"), samples.SelectMany(sample => sample.TextFormats).ToList(), "font");

        var paragraph = SelectDominantParagraphFormat(samples.Select(sample => sample.ParagraphFormat));
        if (paragraph != null)
            ApplyParagraphFormat(EnsureObjectAtPath(root, "footnote_text"), paragraph, includeFirstLineIndent: false, includeHanging: false, "paragraph");
    }

    private static void ApplyCaptionRule(
        JsonObject root,
        IReadOnlyList<ElementSample> captionSamples,
        IReadOnlyList<ElementSample> anchorSamples,
        Regex captionPrefixRegex,
        string defaultPosition)
    {
        if (captionSamples.Count == 0)
            return;

        ApplyDominantTextStyle(root, captionSamples.SelectMany(sample => sample.TextFormats).ToList(), "font");

        var paragraph = SelectDominantParagraphFormat(captionSamples.Select(sample => sample.ParagraphFormat));
        if (paragraph != null)
            ApplyParagraphFormat(root, paragraph, includeFirstLineIndent: false, includeHanging: false, "paragraph");

        var dominantCase = MostCommon(ExtractCaseCandidateTexts(captionSamples, captionPrefixRegex)
            .Select(InferCase)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList());
        if (!string.IsNullOrWhiteSpace(dominantCase))
            TrySetWrappedValue(root, dominantCase, "numbering", "case");

        var hasEnterAfterNumber = DetermineEnterAfterNumber(captionSamples, captionPrefixRegex);
        if (HasWrappedValue(root, "numbering", "enter_after_number"))
            TrySetWrappedValue(root, hasEnterAfterNumber, "numbering", "enter_after_number");
        if (HasWrappedValue(root, "numbering", "enter_after_numbering"))
            TrySetWrappedValue(root, hasEnterAfterNumber, "numbering", "enter_after_numbering");

        TrySetWrappedValue(root, DetermineCaptionPosition(captionSamples, anchorSamples, defaultPosition), "position");
    }

    private static string DetermineCaptionPosition(
        IReadOnlyList<ElementSample> captionSamples,
        IReadOnlyList<ElementSample> anchorSamples,
        string defaultPosition)
    {
        if (captionSamples.Count == 0 || anchorSamples.Count == 0)
            return defaultPosition;

        var beforeCount = 0;
        var afterCount = 0;

        foreach (var caption in captionSamples)
        {
            var nearestAnchor = anchorSamples
                .OrderBy(anchor => Math.Abs(anchor.OrderIndex - caption.OrderIndex))
                .ThenBy(anchor => anchor.OrderIndex)
                .FirstOrDefault();

            if (nearestAnchor == null)
                continue;

            if (caption.OrderIndex < nearestAnchor.OrderIndex)
                beforeCount++;
            else if (caption.OrderIndex > nearestAnchor.OrderIndex)
                afterCount++;
        }

        if (beforeCount == afterCount)
            return defaultPosition;

        return beforeCount > afterCount ? "before" : "after";
    }

    private static void ApplyDominantTextStyle(
        JsonObject root,
        IReadOnlyList<DokumenFormatText> formats,
        params string[] prefix)
    {
        if (formats.Count == 0)
            return;

        var target = EnsureObjectAtPath(root, prefix);
        var fontName = MostCommon(formats
            .Select(format => NormalizeWhitespace(format.DftxFontAscii ?? string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList());
        var fontSizes = formats
            .Where(format => format.DftxSizeHalfpt.HasValue)
            .Select(format => Math.Round(format.DftxSizeHalfpt!.Value / 2m, 2))
            .ToList();
        var bold = MostCommonBool(formats
            .Where(format => format.DftxBold.HasValue)
            .Select(format => format.DftxBold!.Value)
            .ToList());
        var italic = MostCommonBool(formats
            .Where(format => format.DftxItalic.HasValue)
            .Select(format => format.DftxItalic!.Value)
            .ToList());
        var underline = MostCommonBool(formats.Select(HasUnderline).ToList());

        if (!string.IsNullOrWhiteSpace(fontName))
            TrySetWrappedValue(target, fontName, "font_name");
        if (fontSizes.Count > 0)
            TrySetWrappedValue(target, MostCommon(fontSizes), "font_size");
        if (bold.HasValue)
            TrySetWrappedValue(target, bold.Value, "font_style", "bold");
        if (italic.HasValue)
            TrySetWrappedValue(target, italic.Value, "font_style", "italic");
        if (underline.HasValue)
            TrySetWrappedValue(target, underline.Value, "font_style", "underline");
    }

    private static void ApplyParagraphFormat(
        JsonObject root,
        DokumenFormatParagraf format,
        bool includeFirstLineIndent,
        bool includeHanging,
        params string[] prefix)
    {
        ApplyParagraphFormat(
            root,
            format,
            includeFirstLineIndent,
            includeHanging,
            normalizeNumberingIndent: false,
            prefix);
    }

    private static void ApplyParagraphFormat(
        JsonObject root,
        DokumenFormatParagraf format,
        bool includeFirstLineIndent,
        bool includeHanging,
        bool normalizeNumberingIndent,
        params string[] prefix)
    {
        var target = EnsureObjectAtPath(root, prefix);
        var alignment = NormalizeAlignmentValue(format.DfpJc);
        if (!string.IsNullOrWhiteSpace(alignment))
            TrySetWrappedValue(target, alignment, "alignment");

        TrySetWrappedValue(target, GetLeftIndentCm(format, normalizeNumberingIndent), "indentation", "left_indent");
        TrySetWrappedValue(target, GetRightIndentCm(format), "indentation", "right_indent");
        if (includeFirstLineIndent)
            TrySetWrappedValue(target, GetFirstLineIndentCm(format), "indentation", "first_line_indent");
        if (includeHanging)
            TrySetWrappedValue(target, GetHangingIndentCm(format), "indentation", "hanging");

        ApplyParagraphSpacing(target, format);
    }

    private static void ApplyParagraphSpacing(JsonObject root, DokumenFormatParagraf format)
    {
        var lineSpacing = GetLineSpacing(format);
        if (lineSpacing.HasValue)
            TrySetWrappedValue(root, lineSpacing.Value, "spacing", "line_spacing");

        var spacingBefore = TwipsToPoints(format.DfpSpacingBeforeTwips);
        if (spacingBefore.HasValue)
            TrySetWrappedValue(root, spacingBefore.Value, "spacing", "before");

        var spacingAfter = TwipsToPoints(format.DfpSpacingAfterTwips);
        if (spacingAfter.HasValue)
            TrySetWrappedValue(root, spacingAfter.Value, "spacing", "after");
    }

    private static DokumenFormatParagraf? SelectDominantParagraphFormat(
        IEnumerable<DokumenFormatParagraf?> formats,
        bool normalizeNumberingIndent = false)
    {
        return formats
            .Where(format => format != null)
            .GroupBy(format => BuildParagraphFormatKey(format!, normalizeNumberingIndent))
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .FirstOrDefault();
    }

    private static string BuildParagraphFormatKey(
        DokumenFormatParagraf format,
        bool normalizeNumberingIndent = false)
    {
        return string.Join("|",
            NormalizeAlignmentValue(format.DfpJc),
            GetLeftIndentCm(format, normalizeNumberingIndent).ToString(CultureInfo.InvariantCulture),
            GetRightIndentCm(format).ToString(CultureInfo.InvariantCulture),
            GetFirstLineIndentCm(format).ToString(CultureInfo.InvariantCulture),
            GetHangingIndentCm(format).ToString(CultureInfo.InvariantCulture),
            (GetLineSpacing(format) ?? 0m).ToString(CultureInfo.InvariantCulture),
            (TwipsToPoints(format.DfpSpacingBeforeTwips) ?? 0m).ToString(CultureInfo.InvariantCulture),
            (TwipsToPoints(format.DfpSpacingAfterTwips) ?? 0m).ToString(CultureInfo.InvariantCulture));
    }

    private static string BuildTableFormatKey(DokumenFormatTable format)
    {
        return string.Join("|",
            NormalizeAlignmentValue(format.DftJc),
            (format.DftTblIndTwips ?? 0).ToString(CultureInfo.InvariantCulture),
            NormalizeWhitespace(format.DftTblLayoutType ?? string.Empty),
            NormalizeWhitespace(format.DftTblWType ?? string.Empty));
    }

    private static bool TrySetWrappedValue(JsonObject root, object? value, params string[] path)
    {
        if (path.Length == 0)
            return false;

        var parent = EnsureObjectAtPath(root, path[..^1]);
        var key = path[^1];
        if (parent.TryGetPropertyValue(key, out var existingNode) &&
            existingNode is JsonObject existingObject &&
            existingObject.ContainsKey("value"))
        {
            existingObject["value"] = ToJsonNode(value);
            return true;
        }

        parent[key] = new JsonObject
        {
            ["value"] = ToJsonNode(value),
            ["is_editable"] = false,
            ["is_hard_constraint"] = false
        };
        return true;
    }

    private static bool HasWrappedValue(JsonObject root, params string[] path)
    {
        if (path.Length == 0)
            return false;

        JsonNode? current = root;
        foreach (var segment in path)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return false;
        }

        return current is JsonObject wrapper && wrapper.ContainsKey("value");
    }

    private static JsonObject EnsureObjectAtPath(JsonObject root, params string[] path)
    {
        JsonObject current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetPropertyValue(segment, out var nextNode) || nextNode is not JsonObject nextObject)
            {
                nextObject = new JsonObject();
                current[segment] = nextObject;
            }

            current = nextObject;
        }

        return current;
    }

    private static JsonNode? ToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            _ => JsonSerializer.SerializeToNode(value, JsonOptions)
        };
    }

    private static string? NormalizeWordPageNumberFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "arabic" or "arab" or "decimal" or "desimal" => "decimal",
            "lowerroman" or "roman_lower" or "lower_roman" => "lowerRoman",
            "upperroman" or "roman_upper" or "upper_roman" => "upperRoman",
            "lowerletter" or "lower_letter" or "lower_alpha" or "letter_lower" => "lowerLetter",
            "upperletter" or "upper_letter" or "upper_alpha" or "letter_upper" => "upperLetter",
            _ => normalized
        };
    }

    private static string InferPaperSize(DokumenSection section)
    {
        var widthCm = TwipsToCm(section.DsecPageWidthTwips);
        var heightCm = TwipsToCm(section.DsecPageHeightTwips);
        if (!widthCm.HasValue || !heightCm.HasValue)
            return "A4";

        var normalizedWidth = Math.Min(widthCm.Value, heightCm.Value);
        var normalizedHeight = Math.Max(widthCm.Value, heightCm.Value);

        var knownSizes = new[]
        {
            new { Name = "A4", Width = 21.0m, Height = 29.7m },
            new { Name = "Letter", Width = 21.59m, Height = 27.94m },
            new { Name = "Legal", Width = 21.59m, Height = 35.56m },
            new { Name = "A5", Width = 14.8m, Height = 21.0m }
        };

        return knownSizes
            .OrderBy(size => Math.Abs(size.Width - normalizedWidth) + Math.Abs(size.Height - normalizedHeight))
            .First()
            .Name;
    }

    private static string NormalizeGutterPosition(string? value)
    {
        var normalized = (value ?? "left").Trim().ToLowerInvariant();
        return normalized is "left" or "right" or "top" ? normalized : "left";
    }

    private static string NormalizePartPosition(string? value)
    {
        var normalized = (value ?? "default").Trim().ToLowerInvariant();
        return normalized is "default" or "first" or "even" or "odd" ? normalized : "default";
    }

    private static string NormalizePartType(string? value)
    {
        var normalized = (value ?? "footer").Trim().ToLowerInvariant();
        return normalized is "header" or "footer" ? normalized : "footer";
    }

    private static string InferCase(string? text)
    {
        var normalized = NormalizeWhitespace(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (normalized == normalized.ToUpperInvariant())
            return "UPPERCASE";
        if (normalized == normalized.ToLowerInvariant())
            return "LOWERCASE";
        if (IsTitleCaseText(normalized))
            return "Title Case";

        return "Sentence Case";
    }

    private static bool IsTitleCaseText(string text)
    {
        var words = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Any(char.IsLetter))
            .ToList();
        if (words.Count == 0)
            return false;

        return words.All(word =>
        {
            var firstLetterIndex = word.TakeWhile(ch => !char.IsLetter(ch)).Count();
            if (firstLetterIndex >= word.Length)
                return true;

            var actualWord = word[firstLetterIndex..];
            if (actualWord.Length == 1)
                return char.IsUpper(actualWord[0]);

            return char.IsUpper(actualWord[0]) &&
                   actualWord[1..].Where(char.IsLetter).All(char.IsLower);
        });
    }

    private static bool HasUnderline(DokumenFormatText format)
    {
        return !string.IsNullOrWhiteSpace(format.DftxUnderline) &&
               !string.Equals(format.DftxUnderline, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLabel(string? label)
    {
        return (label ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static int GetLabelPriority(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return int.MaxValue;

        var normalized = NormalizeLabel(label);
        if (normalized.StartsWith("list_level_", StringComparison.OrdinalIgnoreCase))
            return 5;

        return normalized switch
        {
            "judul_bab" => 0,
            "judul_subbab" => 1,
            "judul_kode" => 2,
            "caption_gambar" => 2,
            "caption_tabel" => 3,
            "paragraf" => 4,
            "list_item" => 5,
            "tabel" => 6,
            "gambar" => 7,
            "kode" => 8,
            "page_header" => 9,
            "page_footer" => 10,
            "formula" or "rumus" => 11,
            "footnote" => 13,
            _ => 20
        };
    }

    private static ElementContentInfo ParseElementContent(string? json)
    {
        var info = new ElementContentInfo();
        if (string.IsNullOrWhiteSpace(json))
            return info;

        try
        {
            using var doc = JsonDocument.Parse(json);
            ParseElementContent(doc.RootElement, info);
        }
        catch (JsonException)
        {
            // Ignore malformed JSON and keep defaults.
        }

        return info;
    }

    private static void ParseElementContent(JsonElement element, ElementContentInfo info)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (!info.ParagraphFormatId.HasValue &&
            element.TryGetProperty("dfp_id", out var dfpEl) &&
            dfpEl.TryGetUInt32(out var dfpId))
        {
            info.ParagraphFormatId = dfpId;
        }

        if (element.TryGetProperty("text", out var textEl) &&
            textEl.ValueKind == JsonValueKind.String &&
            !element.TryGetProperty("content", out var ignoredContent))
        {
            var text = textEl.GetString() ?? string.Empty;
            AppendTextRun(info, text, null);
            return;
        }

        if (!element.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in contentEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var type = item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString()
                : null;

            if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "field", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(type, "field", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("field_type", out var fieldTypeEl) &&
                    fieldTypeEl.ValueKind == JsonValueKind.String &&
                    string.Equals(fieldTypeEl.GetString(), "PAGE", StringComparison.OrdinalIgnoreCase))
                {
                    info.HasPageField = true;
                }

                var value = item.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.String
                    ? valueEl.GetString()
                    : item.TryGetProperty("text", out var altTextEl) && altTextEl.ValueKind == JsonValueKind.String
                        ? altTextEl.GetString()
                        : null;

                uint? runFormatId = null;
                if (string.Equals(type, "field", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("result_dftx_id", out var resultEl) &&
                    resultEl.TryGetUInt32(out var resultId))
                {
                    runFormatId = resultId;
                }
                else if (item.TryGetProperty("dftx_id", out var dftxEl) && dftxEl.TryGetUInt32(out var dftxId))
                {
                    runFormatId = dftxId;
                }

                AppendTextRun(info, value, runFormatId);
                continue;
            }

            if (string.Equals(type, "math", StringComparison.OrdinalIgnoreCase))
            {
                var mathText = item.TryGetProperty("text", out var mathEl) && mathEl.ValueKind == JsonValueKind.String
                    ? mathEl.GetString()
                    : null;
                AppendTextRun(info, mathText, null);
                continue;
            }

            if (item.TryGetProperty("dfdr_id", out var drawingEl) && drawingEl.TryGetUInt64(out var drawingId))
                info.DrawingFormatIds.Add(drawingId);

            if (item.TryGetProperty("content", out var nestedContent))
            {
                info.HasNonTextContent = true;
                ParseElementContent(item, info);
            }
            else
            {
                info.HasNonTextContent = true;
            }
        }
    }

    private static void AppendTextRun(ElementContentInfo info, string? text, uint? formatId)
    {
        if (string.IsNullOrEmpty(text))
            return;

        info.PlainText += text;
        if (formatId.HasValue)
            info.TextFormatIds.Add(formatId.Value);

        info.TextRuns.Add(new TextRunInfo
        {
            Text = text,
            TextFormatId = formatId
        });
    }

    private static TableContentAggregate ExtractTableContentAggregate(string? json, out uint? tableFormatId)
    {
        var aggregate = new TableContentAggregate();
        tableFormatId = null;

        if (string.IsNullOrWhiteSpace(json))
            return aggregate;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("dft_id", out var dftEl) && dftEl.TryGetUInt32(out var dftId))
                tableFormatId = dftId;

            CollectTableContent(root, aggregate);
        }
        catch (JsonException)
        {
            // Ignore malformed historical rows.
        }

        return aggregate;
    }

    private static void CollectTableContent(JsonElement tableObj, TableContentAggregate aggregate)
    {
        if (tableObj.ValueKind != JsonValueKind.Object)
            return;

        if (!tableObj.TryGetProperty("content", out var contentObj) || contentObj.ValueKind != JsonValueKind.Object)
            return;

        if (!contentObj.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var row in rowsEl.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("cells", out var cellsEl) ||
                cellsEl.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var cell in cellsEl.EnumerateArray())
            {
                if (cell.ValueKind != JsonValueKind.Object ||
                    !cell.TryGetProperty("content", out var cellContent) ||
                    cellContent.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                CollectCellContent(cellContent, aggregate);
            }
        }
    }

    private static void CollectCellContent(JsonElement cellContent, TableContentAggregate aggregate)
    {
        foreach (var item in cellContent.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            string? type = null;
            if (item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                type = typeEl.GetString();

            if (string.Equals(type, "table", StringComparison.OrdinalIgnoreCase))
            {
                if (item.TryGetProperty("content", out var nestedTable) && nestedTable.ValueKind == JsonValueKind.Object)
                {
                    using var nestedDoc = JsonDocument.Parse($@"{{""content"":{nestedTable.GetRawText()}}}");
                    CollectTableContent(nestedDoc.RootElement, aggregate);
                }
                continue;
            }

            if (item.TryGetProperty("content", out var nestedItemContent))
            {
                var info = ParseElementContent(item.GetRawText());
                AppendContentInfo(aggregate, info);
                continue;
            }

            var directInfo = ParseElementContent(item.GetRawText());
            AppendContentInfo(aggregate, directInfo);
        }
    }

    private static void AppendContentInfo(TableContentAggregate aggregate, ElementContentInfo info)
    {
        if (info.ParagraphFormatId.HasValue)
            aggregate.ParagraphFormatIds.Add(info.ParagraphFormatId.Value);

        aggregate.TextFormatIds.AddRange(info.TextFormatIds);
        aggregate.TextRuns.AddRange(info.TextRuns);
    }

    private static string NormalizeWhitespace(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return Regex.Replace(input.Trim(), "\\s+", " ");
    }

    private static string NormalizeAlignmentValue(string? alignment)
    {
        var normalized = NormalizeWhitespace(alignment ?? string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "both" or "justify" or "distributed" or "distribute" => "justify",
            "start" => "left",
            "end" => "right",
            "center" => "center",
            "left" => "left",
            "right" => "right",
            _ => normalized
        };
    }

    private static decimal? TwipsToCm(uint? twips)
    {
        return twips.HasValue ? Math.Round(twips.Value / 1440.0m * 2.54m, 2) : null;
    }

    private static decimal? TwipsToCm(long? twips)
    {
        return twips.HasValue ? Math.Round(twips.Value / 1440.0m * 2.54m, 2) : null;
    }

    private static decimal? TwipsToPoints(uint? twips)
    {
        return twips.HasValue ? Math.Round(twips.Value / 20m, 2) : null;
    }

    private static decimal GetLeftIndentCm(DokumenFormatParagraf format)
    {
        return GetLeftIndentCm(format, normalizeNumberingIndent: false);
    }

    private static decimal GetLeftIndentCm(
        DokumenFormatParagraf format,
        bool normalizeNumberingIndent)
    {
        if (normalizeNumberingIndent && HasNumberingDerivedIndent(format))
            return 0m;

        var leftTwips = format.DfpIndLeftTwips.HasValue && format.DfpIndLeftTwips.Value != 0
            ? format.DfpIndLeftTwips.Value
            : format.DfpIndStartTwips ?? 0;

        return Math.Round(leftTwips / 1440.0m * 2.54m, 2);
    }

    private static decimal GetRightIndentCm(DokumenFormatParagraf format)
    {
        var rightTwips = format.DfpIndRightTwips.HasValue && format.DfpIndRightTwips.Value != 0
            ? format.DfpIndRightTwips.Value
            : format.DfpIndEndTwips ?? 0;

        return Math.Round(rightTwips / 1440.0m * 2.54m, 2);
    }

    private static decimal GetFirstLineIndentCm(DokumenFormatParagraf format)
    {
        return Math.Round((format.DfpIndFirstLineTwips ?? 0) / 1440.0m * 2.54m, 2);
    }

    private static decimal GetHangingIndentCm(DokumenFormatParagraf format)
    {
        return Math.Round((format.DfpIndHangingTwips ?? 0) / 1440.0m * 2.54m, 2);
    }

    private static List<ElementSample> SelectPreferredParagraphRuleSamples(List<ElementSample> samples)
    {
        var preferred = samples
            .Where(sample => !HasNumberingDerivedIndent(sample.ParagraphFormat))
            .ToList();

        return preferred.Count > 0 ? preferred : samples;
    }

    private static bool HasNumberingDerivedIndent(DokumenFormatParagraf? format)
    {
        if (format == null)
            return false;

        var hasNumbering = format.DfpIsList || (format.DfpListNumId ?? 0) > 0;
        if (!hasNumbering)
            return false;

        var leftTwips = format.DfpIndLeftTwips.HasValue && format.DfpIndLeftTwips.Value != 0
            ? format.DfpIndLeftTwips.Value
            : format.DfpIndStartTwips ?? 0;

        return leftTwips > 0 || (format.DfpIndHangingTwips ?? 0) > 0;
    }

    private static decimal? GetLineSpacing(DokumenFormatParagraf format)
    {
        if (!format.DfpSpacingLineTwips.HasValue)
            return null;

        if (string.IsNullOrWhiteSpace(format.DfpSpacingLineRule) ||
            string.Equals(format.DfpSpacingLineRule, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(format.DfpSpacingLineTwips.Value / 240m, 2);
        }

        return null;
    }

    private static T? MostCommon<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            return default;

        return values
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static bool? MostCommonBool(IReadOnlyList<bool> values)
    {
        if (values.Count == 0)
            return null;

        return values.Count(value => value) >= values.Count(value => !value);
    }

    private static bool IsParagraphLike(ElementSample sample)
    {
        return string.Equals(sample.Row.ElementType, "paragraph", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(sample.NormalizedText);
    }

    private static bool HasLineBreakAfterNumber(string? text, Regex numberingRegex)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = numberingRegex.Match(text);
        if (!match.Success)
            return false;

        var tail = text[(match.Index + match.Length)..];
        return tail.Contains('\n') || tail.Contains('\r');
    }

    private static bool DetermineEnterAfterNumber(IReadOnlyList<ElementSample> samples, Regex numberingRegex)
    {
        if (samples.Count == 0)
            return false;

        if (samples.Any(sample => HasLineBreakAfterNumber(sample.Content.PlainText, numberingRegex)))
            return true;

        var orderedSamples = samples
            .OrderBy(sample => sample.OrderIndex)
            .ToList();

        for (var index = 0; index < orderedSamples.Count; index++)
        {
            if (!IsStandaloneNumberingSample(orderedSamples[index], numberingRegex))
                continue;

            var continuation = GetImmediateContinuationSample(orderedSamples, index);
            if (continuation == null)
                continue;

            if (HasAlphabeticContent(ExtractNumberingTail(continuation.Content.PlainText, numberingRegex)))
                return true;
        }

        return false;
    }

    private static List<string> ExtractCaseCandidateTexts(IReadOnlyList<ElementSample> samples, Regex numberingRegex)
    {
        if (samples.Count == 0)
            return [];

        var orderedSamples = samples
            .OrderBy(sample => sample.OrderIndex)
            .ToList();
        var caseCandidates = new List<string>();

        for (var index = 0; index < orderedSamples.Count; index++)
        {
            var tailText = ExtractNumberingTail(orderedSamples[index].Content.PlainText, numberingRegex);
            if (HasAlphabeticContent(tailText))
            {
                caseCandidates.Add(tailText);
                continue;
            }

            if (!IsStandaloneNumberingSample(orderedSamples[index], numberingRegex))
                continue;

            var continuation = GetImmediateContinuationSample(orderedSamples, index);
            if (continuation == null)
                continue;

            var continuationText = ExtractNumberingTail(continuation.Content.PlainText, numberingRegex);
            if (HasAlphabeticContent(continuationText))
                caseCandidates.Add(continuationText);
        }

        return caseCandidates;
    }

    private static ElementSample? GetImmediateContinuationSample(IReadOnlyList<ElementSample> orderedSamples, int index)
    {
        if (index + 1 >= orderedSamples.Count)
            return null;

        var current = orderedSamples[index];
        var next = orderedSamples[index + 1];
        if (next.OrderIndex - current.OrderIndex > 1)
            return null;

        if (next.Row.DsecId != current.Row.DsecId)
            return null;

        if (!string.Equals(next.Row.PartType, current.Row.PartType, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.Equals(
                NormalizePartPosition(next.Row.PartPosition),
                NormalizePartPosition(current.Row.PartPosition),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return next;
    }

    private static bool IsStandaloneNumberingSample(ElementSample sample, Regex numberingRegex)
    {
        if (string.IsNullOrWhiteSpace(sample.Content.PlainText))
            return false;

        var match = numberingRegex.Match(sample.Content.PlainText);
        if (!match.Success)
            return false;

        return !HasAlphabeticContent(ExtractNumberingTail(sample.Content.PlainText, numberingRegex));
    }

    private static string ExtractNumberingTail(string? text, Regex numberingRegex)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var rawText = text ?? string.Empty;
        var match = numberingRegex.Match(rawText);
        if (!match.Success)
            return NormalizeWhitespace(rawText);

        var tail = rawText[(match.Index + match.Length)..]
            .TrimStart(' ', '\t', '\r', '\n', ':', '-', '.', ')');
        return NormalizeWhitespace(tail);
    }

    private static bool HasAlphabeticContent(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.Any(char.IsLetter);
    }

    private async Task SendReviewReadyEmailAsync(Aturan aturan, CancellationToken cancellationToken)
    {
        try
        {
            var recipients = ParseRecipients(_configuration["Email:AdminRecipients"]);
            if (recipients.Count == 0)
            {
                _logger.LogInformation(
                    "AdminRecipients kosong, email review-ready untuk aturan {AturanId} tidak dikirim.",
                    aturan.AturanId);
                return;
            }

            var dashboardUrl = (_configuration["Email:DashboardUrl"] ?? "http://localhost:5173").TrimEnd('/');
            var reviewUrl = $"{dashboardUrl}/admin/template?templateId={aturan.AturanId}";
            var encodedVersi = WebUtility.HtmlEncode(aturan.AturanVersi);
            var encodedReviewUrl = WebUtility.HtmlEncode(reviewUrl);

            var subject = $"Template aturan siap direview: {aturan.AturanVersi}";
            var bodyHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; line-height: 1.6; color: #1f2937; }}
        .container {{ max-width: 640px; margin: 0 auto; padding: 24px; }}
        .panel {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 24px; }}
        .badge {{ display: inline-block; padding: 6px 12px; border-radius: 999px; background: #dbeafe; color: #1d4ed8; font-weight: 600; }}
        .button {{ display: inline-block; margin-top: 20px; background: #2563eb; color: #ffffff !important; text-decoration: none; padding: 12px 20px; border-radius: 8px; font-weight: 600; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='panel'>
            <p style='margin-top:0;'>Template aturan baru selesai diproses dan siap direview.</p>
            <p><strong>Versi:</strong> {encodedVersi}</p>
            <p><span class='badge'>menunggu_review</span></p>
            <a href='{encodedReviewUrl}' class='button'>Buka Review Aturan</a>
        </div>
    </div>
</body>
</html>";

            await _emailService.SendEmailAsync(recipients, subject, bodyHtml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal mengirim email review-ready untuk aturan {AturanId}", aturan.AturanId);
        }
    }

    private static List<(string Email, string Name)> ParseRecipients(string? rawRecipients)
    {
        if (string.IsNullOrWhiteSpace(rawRecipients))
            return [];

        return rawRecipients
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email =>
            {
                var normalizedEmail = email.Trim();
                var name = normalizedEmail.Contains('@')
                    ? normalizedEmail[..normalizedEmail.IndexOf('@')]
                    : "Admin";
                return (Email: normalizedEmail, Name: name);
            })
            .ToList();
    }

    private sealed class DraftDetail
    {
        public DraftDetail(AturanDetail detail, JsonObject json)
        {
            Detail = detail;
            Json = json;
        }

        public AturanDetail Detail { get; }
        public JsonObject Json { get; }
    }

    private sealed class ElementRow
    {
        public ulong ElementId { get; init; }
        public uint DsecId { get; init; }
        public string PartType { get; init; } = "body";
        public string PartPosition { get; init; } = "default";
        public string? ElementType { get; init; }
        public string? Json { get; init; }
    }

    private sealed class ElementSample
    {
        public ElementRow Row { get; init; } = null!;
        public int OrderIndex { get; init; }
        public string Label { get; init; } = string.Empty;
        public string NormalizedText { get; init; } = string.Empty;
        public ElementContentInfo Content { get; init; } = new();
        public DokumenFormatParagraf? ParagraphFormat { get; init; }
        public IReadOnlyList<DokumenFormatText> TextFormats { get; init; } = Array.Empty<DokumenFormatText>();
        public DokumenFormatTable? TableFormat { get; init; }
        public TableContentAggregate TableContent { get; init; } = new();
        public IReadOnlyList<DokumenFormatText> TableTextFormats { get; init; } = Array.Empty<DokumenFormatText>();
        public IReadOnlyList<DokumenFormatParagraf> TableParagraphFormats { get; init; } = Array.Empty<DokumenFormatParagraf>();
    }

    private sealed class ElementContentInfo
    {
        public uint? ParagraphFormatId { get; set; }
        public string PlainText { get; set; } = string.Empty;
        public List<uint> TextFormatIds { get; } = new();
        public List<TextRunInfo> TextRuns { get; } = new();
        public List<ulong> DrawingFormatIds { get; } = new();
        public bool HasNonTextContent { get; set; }
        public bool HasPageField { get; set; }
    }

    private sealed class TextRunInfo
    {
        public string Text { get; set; } = string.Empty;
        public uint? TextFormatId { get; set; }
    }

    private sealed class TableContentAggregate
    {
        public List<uint> ParagraphFormatIds { get; } = new();
        public List<uint> TextFormatIds { get; } = new();
        public List<TextRunInfo> TextRuns { get; } = new();
    }
}

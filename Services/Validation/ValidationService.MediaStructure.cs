using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private enum BlankParagraphGapSide
    {
        Before,
        After
    }

    private sealed class BlankParagraphFormatValidationTarget
    {
        public string Field { get; init; } = string.Empty;
        public string SubjectLabel { get; init; } = string.Empty;
        public string Evidence { get; init; } = string.Empty;
        public ElementNeighborContext? Context { get; init; }
        public List<ulong> BlankElementIds { get; init; } = new();
        public BlankParagraphGapSide GapSide { get; init; }
        public int BlockStartIndex { get; init; }
        public ulong BlockStartElementId { get; init; }
        public int BlockEndIndex { get; init; }
        public ulong BlockEndElementId { get; init; }
    }

    private sealed class MediaBlankStructureSettings
    {
        public int BlankParagraphsBefore { get; init; } = 1;
        public int BlankParagraphsAfter { get; init; } = 1;
        public bool IgnoreBlankParagraphBeforeAtPageTop { get; init; } = true;
    }

    private MediaBlankStructureSettings ResolveMediaBlankStructureSettings(MediaContentStructureRule? rule)
    {
        return new MediaBlankStructureSettings
        {
            BlankParagraphsBefore = ClampBlankParagraphCount(rule?.JumlahBarisKosongSebelum?.Value, defaultValue: 1),
            BlankParagraphsAfter = ClampBlankParagraphCount(rule?.JumlahBarisKosongSetelah?.Value, defaultValue: 1),
            IgnoreBlankParagraphBeforeAtPageTop = rule?.AbaikanJikaDiAwalHalaman?.Value ?? true
        };
    }

    private static int ClampBlankParagraphCount(decimal? value, int defaultValue)
    {
        if (!value.HasValue)
            return defaultValue;

        var rounded = (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
        return Math.Max(0, rounded);
    }

    private async Task<ParagraphRule?> LoadParagraphRuleAsync(
        uint aturanId,
        string warningContext,
        CancellationToken cancellationToken)
    {
        var details = await LoadCanonicalDetailsAsync(aturanId, cancellationToken, "paragraf");
        details.TryGetValue("paragraf", out var paragraphDetail);

        if (paragraphDetail == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ParagraphRule>(
                paragraphDetail.AturanDetailJsonValue ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan paragraf for {Context}", warningContext);
            return null;
        }
    }

    private async Task ValidateMediaBlankParagraphStructureAsync(
        ValidationResult result,
        string field,
        string elementLabel,
        string evidence,
        int startIndex,
        ulong startElementId,
        int endIndex,
        ulong endElementId,
        IReadOnlyCollection<ulong> locationElementIds,
        ulong primaryElementId,
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, ElementContentInfo> elementContentById,
        IReadOnlyDictionary<ulong, string?> elementJsonById,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById,
        IReadOnlyDictionary<ulong, ElementNeighborContext> contextById,
        ParagraphRule? paragraphRule,
        MediaContentStructureRule? structureRule,
        CancellationToken cancellationToken)
    {
        var settings = ResolveMediaBlankStructureSettings(structureRule);
        if (settings.BlankParagraphsBefore < 0 && settings.BlankParagraphsAfter < 0)
            return;

        var locationIds = locationElementIds
            .Where(id => id != 0)
            .Distinct()
            .ToList();
        var locations = await BuildElementLocationsAsync(locationIds, cancellationToken);
        var context = contextById.TryGetValue(primaryElementId, out var foundContext)
            ? foundContext
            : null;
        var formatTargets = new List<BlankParagraphFormatValidationTarget>();

        if (!(settings.IgnoreBlankParagraphBeforeAtPageTop &&
              IsElementAtTopOfPage(startIndex, startElementId, bodyElements, elementContentById, visualSummaryById)))
        {
            var blankBeforeIds = CollectBlankParagraphsBeforeElement(
                startIndex,
                startElementId,
                bodyElements,
                elementContentById,
                visualSummaryById);

            result.IncrementTotalChecks(structureRule?.JumlahBarisKosongSebelum?.IsHardConstraint == true);
            if (blankBeforeIds.Count == settings.BlankParagraphsBefore)
            {
                result.IncrementPassedChecks();

                if (paragraphRule != null && blankBeforeIds.Count > 0)
                {
                    formatTargets.Add(new BlankParagraphFormatValidationTarget
                    {
                        Field = field,
                        SubjectLabel = $"baris kosong sebelum {elementLabel}",
                        Evidence = evidence,
                        Context = context,
                        BlankElementIds = blankBeforeIds,
                        GapSide = BlankParagraphGapSide.Before,
                        BlockStartIndex = startIndex,
                        BlockStartElementId = startElementId,
                        BlockEndIndex = endIndex,
                        BlockEndElementId = endElementId
                    });
                }
            }
            else
            {
                AddMediaBlankParagraphStructureError(
                    result,
                    field,
                    $"Jumlah baris kosong sebelum {elementLabel} tidak sesuai",
                    settings.BlankParagraphsBefore,
                    blankBeforeIds.Count,
                    evidence,
                    locations,
                    primaryElementId,
                    context);
            }
        }

        if (!IsElementAtBottomOfPage(endIndex, endElementId, bodyElements, elementContentById, visualSummaryById))
        {
            var blankAfterIds = CollectBlankParagraphsAfterElement(
                endIndex,
                endElementId,
                bodyElements,
                elementContentById,
                visualSummaryById);

            result.IncrementTotalChecks(structureRule?.JumlahBarisKosongSetelah?.IsHardConstraint == true);
            if (blankAfterIds.Count == settings.BlankParagraphsAfter)
            {
                result.IncrementPassedChecks();

                if (paragraphRule != null && blankAfterIds.Count > 0)
                {
                    formatTargets.Add(new BlankParagraphFormatValidationTarget
                    {
                        Field = field,
                        SubjectLabel = $"baris kosong sesudah {elementLabel}",
                        Evidence = evidence,
                        Context = context,
                        BlankElementIds = blankAfterIds,
                        GapSide = BlankParagraphGapSide.After,
                        BlockStartIndex = startIndex,
                        BlockStartElementId = startElementId,
                        BlockEndIndex = endIndex,
                        BlockEndElementId = endElementId
                    });
                }
            }
            else
            {
                AddMediaBlankParagraphStructureError(
                    result,
                    field,
                    $"Jumlah baris kosong sesudah {elementLabel} tidak sesuai",
                    settings.BlankParagraphsAfter,
                    blankAfterIds.Count,
                    evidence,
                    locations,
                    primaryElementId,
                    context);
            }
        }

        if (paragraphRule == null || formatTargets.Count == 0)
            return;

        await ValidateBlankParagraphFormatTargetsAsync(
            result,
            formatTargets,
            bodyElements,
            elementContentById,
            elementJsonById,
            visualSummaryById,
            paragraphRule,
            cancellationToken);
    }

    private static void AddMediaBlankParagraphStructureError(
        ValidationResult result,
        string field,
        string message,
        int expectedCount,
        int actualCount,
        string evidence,
        List<ErrorLocation> locations,
        ulong elementId,
        ElementNeighborContext? context)
    {
        var error = new ValidationError
        {
            Category = "Isi Buku",
            Field = field,
            Message = message,
            Expected = $"Tepat {expectedCount} baris kosong",
            Actual = $"{actualCount} baris kosong",
            Evidence = evidence,
            Locations = locations,
            DokumenElemenId = elementId
        };

        if (context != null)
            ApplyContext(error, context);

        result.Errors.Add(error);
    }

    private async Task ValidateBlankParagraphFormatTargetsAsync(
        ValidationResult result,
        IReadOnlyList<BlankParagraphFormatValidationTarget> targets,
        IReadOnlyDictionary<ulong, ElementContentInfo> elementContentById,
        IReadOnlyDictionary<ulong, string?> elementJsonById,
        ParagraphRule paragraphRule,
        CancellationToken cancellationToken)
    {
        await ValidateBlankParagraphFormatTargetsAsync(
            result,
            targets,
            Array.Empty<BodyElementInfo>(),
            elementContentById,
            elementJsonById,
            new Dictionary<ulong, VisualElementSummary>(),
            paragraphRule,
            cancellationToken);
    }

    private async Task ValidateBlankParagraphFormatTargetsAsync(
        ValidationResult result,
        IReadOnlyList<BlankParagraphFormatValidationTarget> targets,
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, ElementContentInfo> elementContentById,
        IReadOnlyDictionary<ulong, string?> elementJsonById,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById,
        ParagraphRule paragraphRule,
        CancellationToken cancellationToken)
    {
        var blankElementIds = targets
            .SelectMany(target => target.BlankElementIds)
            .Distinct()
            .ToList();

        if (blankElementIds.Count == 0)
            return;

        var locationsByElementId = await BuildElementLocationsMapAsync(blankElementIds, cancellationToken);
        var boundaryElementIds = targets
            .SelectMany(target => new[] { target.BlockStartElementId, target.BlockEndElementId })
            .Where(id => id != 0)
            .Distinct()
            .ToList();
        var pageLayoutsByElementId = boundaryElementIds.Count > 0
            ? await LoadPageLayoutsAsync(boundaryElementIds, cancellationToken)
            : new Dictionary<ulong, PageLayoutSnapshot>();
        var paragraphFormatIds = blankElementIds
            .Select(id => elementContentById.TryGetValue(id, out var content) ? content.ParagraphFormatId : null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = paragraphFormatIds.Count > 0
            ? await _db.DokumenFormatParagrafs
                .Where(format => paragraphFormatIds.Contains(format.DfpId))
                .ToDictionaryAsync(format => format.DfpId, cancellationToken)
            : new Dictionary<uint, DokumenFormatParagraf>();

        var textFormatIds = blankElementIds
            .SelectMany(id =>
            {
                if (!elementJsonById.TryGetValue(id, out var json))
                    return Array.Empty<uint>();

                return ExtractTextFormatIdsIncludingEmptyRuns(json);
            })
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(format => textFormatIds.Contains(format.DftxId))
                .ToDictionaryAsync(format => format.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        foreach (var target in targets)
        {
            List<ErrorLocation>? gapLocations = null;
            foreach (var blankElementId in target.BlankElementIds)
            {
                if (!elementContentById.TryGetValue(blankElementId, out var content))
                    continue;

                DokumenFormatParagraf? paragraphFormat = null;
                if (content.ParagraphFormatId.HasValue)
                    paragraphFormats.TryGetValue(content.ParagraphFormatId.Value, out paragraphFormat);

                var blankTextFormats = elementJsonById.TryGetValue(blankElementId, out var elementJson)
                    ? ExtractTextFormatIdsIncludingEmptyRuns(elementJson)
                        .Select(id => textFormats.TryGetValue(id, out var format) ? format : null)
                        .Where(format => format != null)
                        .Cast<DokumenFormatText>()
                        .ToList()
                    : new List<DokumenFormatText>();

                var locations = GetLocationsForElement(blankElementId, locationsByElementId);
                if (locations.Count == 0)
                {
                    gapLocations ??= BuildGapLocationsForBlankParagraphTarget(
                        target,
                        bodyElements,
                        visualSummaryById,
                        pageLayoutsByElementId);
                    locations = CloneLocations(gapLocations);
                }

                ValidateBlankParagraphFontAgainstParagraphRule(
                    result,
                    target.Field,
                    target.SubjectLabel,
                    paragraphRule,
                    blankTextFormats,
                    target.Evidence,
                    locations,
                    blankElementId,
                    target.Context);

                ValidateBlankParagraphSpacingAgainstParagraphRule(
                    result,
                    target.Field,
                    target.SubjectLabel,
                    paragraphRule,
                    paragraphFormat,
                    target.Evidence,
                    locations,
                    blankElementId,
                    target.Context);
            }
        }
    }

    private static List<ErrorLocation> BuildGapLocationsForBlankParagraphTarget(
        BlankParagraphFormatValidationTarget target,
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById,
        IReadOnlyDictionary<ulong, PageLayoutSnapshot> pageLayoutsByElementId)
    {
        if (bodyElements.Count == 0)
            return [];

        if (target.GapSide == BlankParagraphGapSide.Before)
        {
            if (!TryFindPreviousVisiblePosition(
                    bodyElements,
                    target.BlockStartIndex - 1,
                    visualSummaryById,
                    out var previousPage,
                    out var previousBottom))
            {
                return [];
            }

            if (!TryGetFirstVisualPosition(
                    visualSummaryById,
                    target.BlockStartElementId,
                    out var blockPage,
                    out var blockTop))
            {
                return [];
            }

            if (previousPage != blockPage ||
                !pageLayoutsByElementId.TryGetValue(target.BlockStartElementId, out var startLayout) ||
                !TryBuildGapBbox(startLayout, previousBottom, blockTop, out var bbox))
            {
                return [];
            }

            return
            [
                new ErrorLocation
                {
                    HalamanKe = blockPage,
                    Bbox = bbox
                }
            ];
        }

        if (!TryGetLastVisualPosition(
                visualSummaryById,
                target.BlockEndElementId,
                out var endPage,
                out var blockBottom))
        {
            return [];
        }

        if (!TryFindNextVisiblePosition(
                bodyElements,
                target.BlockEndIndex + 1,
                visualSummaryById,
                out var nextPage,
                out var nextTop))
        {
            return [];
        }

        if (endPage != nextPage ||
            !pageLayoutsByElementId.TryGetValue(target.BlockEndElementId, out var endLayout) ||
            !TryBuildGapBbox(endLayout, blockBottom, nextTop, out var afterBbox))
        {
            return [];
        }

        return
        [
            new ErrorLocation
            {
                HalamanKe = endPage,
                Bbox = afterBbox
            }
        ];
    }

    private static bool TryBuildGapBbox(
        PageLayoutSnapshot layout,
        double gapTop,
        double gapBottom,
        out ErrorBbox? bbox)
    {
        bbox = null;

        if (!layout.WidthCm.HasValue || !layout.HeightCm.HasValue)
            return false;

        var pageWidthPt = CmToPoints(layout.WidthCm.Value);
        var pageHeightPt = CmToPoints(layout.HeightCm.Value);
        var textLeftPt = CmToPoints(Math.Max(0m, layout.MarginLeftCm ?? 0m));
        var textRightPt = pageWidthPt - CmToPoints(Math.Max(0m, layout.MarginRightCm ?? 0m));

        bbox = CreateBbox(
            textLeftPt,
            (decimal)gapTop,
            textRightPt,
            (decimal)gapBottom,
            pageWidthPt,
            pageHeightPt);

        return bbox != null;
    }

    private static bool TryFindPreviousVisiblePosition(
        IReadOnlyList<BodyElementInfo> bodyElements,
        int startIndex,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById,
        out int page,
        out double y1)
    {
        page = default;
        y1 = default;

        for (var cursor = Math.Min(startIndex, bodyElements.Count - 1); cursor >= 0; cursor--)
        {
            if (TryGetLastVisualPosition(visualSummaryById, bodyElements[cursor].DelemenId, out page, out y1))
                return true;
        }

        return false;
    }

    private static bool TryFindNextVisiblePosition(
        IReadOnlyList<BodyElementInfo> bodyElements,
        int startIndex,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById,
        out int page,
        out double y0)
    {
        page = default;
        y0 = default;

        for (var cursor = Math.Max(0, startIndex); cursor < bodyElements.Count; cursor++)
        {
            if (TryGetFirstVisualPosition(visualSummaryById, bodyElements[cursor].DelemenId, out page, out y0))
                return true;
        }

        return false;
    }

    private static List<ErrorLocation> CloneLocations(IReadOnlyList<ErrorLocation>? locations)
    {
        if (locations == null || locations.Count == 0)
            return new List<ErrorLocation>();

        return locations
            .Select(location => new ErrorLocation
            {
                HalamanKe = location.HalamanKe,
                Bbox = location.Bbox == null
                    ? null
                    : new ErrorBbox
                    {
                        X0 = location.Bbox.X0,
                        Y0 = location.Bbox.Y0,
                        X1 = location.Bbox.X1,
                        Y1 = location.Bbox.Y1
                    }
            })
            .ToList();
    }

    private void ValidateBlankParagraphFontAgainstParagraphRule(
        ValidationResult result,
        string field,
        string subjectLabel,
        ParagraphRule paragraphRule,
        IReadOnlyList<DokumenFormatText> textFormats,
        string evidence,
        List<ErrorLocation> locations,
        ulong blankElementId,
        ElementNeighborContext? context)
    {
        if (textFormats.Count == 0)
            return;

        var expectedFontName = paragraphRule.Font?.FontName?.Value;
        if (!string.IsNullOrWhiteSpace(expectedFontName))
        {
            result.IncrementTotalChecks(paragraphRule.Font?.FontName?.IsHardConstraint == true);
            var actuals = textFormats
                .Select(format => format.DftxFontAscii ?? "unknown")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (actuals.All(actual => string.Equals(actual, expectedFontName, StringComparison.OrdinalIgnoreCase)))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                AddBlankParagraphFormatError(
                    result,
                    field,
                    $"Font {subjectLabel} tidak sesuai dengan aturan paragraf",
                    expectedFontName,
                    string.Join(", ", actuals),
                    evidence,
                    locations,
                    blankElementId,
                    context);
            }
        }

        var expectedFontSize = paragraphRule.Font?.FontSize?.Value;
        if (expectedFontSize.HasValue)
        {
            result.IncrementTotalChecks(paragraphRule.Font?.FontSize?.IsHardConstraint == true);
            var expectedHalfPt = expectedFontSize.Value * 2m;
            var actuals = textFormats
                .Select(format => format.DftxSizeHalfpt.HasValue ? (decimal?)format.DftxSizeHalfpt.Value : null)
                .Where(actual => actual.HasValue)
                .Select(actual => actual!.Value)
                .Distinct()
                .ToList();

            if (actuals.Count > 0 && actuals.All(actual => Math.Abs(actual - expectedHalfPt) <= 0.5m))
            {
                result.IncrementPassedChecks();
            }
            else if (actuals.Count > 0)
            {
                AddBlankParagraphFormatError(
                    result,
                    field,
                    $"Ukuran font {subjectLabel} tidak sesuai dengan aturan paragraf",
                    expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                    string.Join(", ", actuals.Select(actual => (actual / 2m).ToString(CultureInfo.InvariantCulture) + " pt")),
                    evidence,
                    locations,
                    blankElementId,
                    context);
            }
        }
    }

    private void ValidateBlankParagraphSpacingAgainstParagraphRule(
        ValidationResult result,
        string field,
        string subjectLabel,
        ParagraphRule paragraphRule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations,
        ulong blankElementId,
        ElementNeighborContext? context)
    {
        if (format == null)
            return;

        var spacingRule = paragraphRule.Paragraph?.Spacing;
        if (spacingRule?.LineSpacing?.Value.HasValue == true)
        {
            result.IncrementTotalChecks(spacingRule.LineSpacing?.IsHardConstraint == true);
            var expected = spacingRule.LineSpacing.Value.Value;
            var actual = GetLineSpacing(format);

            if (actual.HasValue && Math.Abs(actual.Value - expected) <= 0.05m)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                AddBlankParagraphFormatError(
                    result,
                    field,
                    $"Line spacing {subjectLabel} tidak sesuai dengan aturan paragraf",
                    expected.ToString(CultureInfo.InvariantCulture),
                    actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                    evidence,
                    locations,
                    blankElementId,
                    context);
            }
        }

        if (spacingRule?.Before?.Value.HasValue == true)
        {
            result.IncrementTotalChecks(spacingRule.Before?.IsHardConstraint == true);
            var expected = spacingRule.Before.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingBeforeTwips);

            if (actual.HasValue && IsWithinTolerance(actual.Value, expected, 0.5m) && !format.DfpSpacingBeforeAutospacing)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                AddBlankParagraphFormatError(
                    result,
                    field,
                    $"Spacing before {subjectLabel} tidak sesuai dengan aturan paragraf",
                    expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    actual.HasValue
                        ? actual.Value.ToString(CultureInfo.InvariantCulture) + " pt"
                        : "unknown",
                    evidence,
                    locations,
                    blankElementId,
                    context);
            }
        }

        if (spacingRule?.After?.Value.HasValue == true)
        {
            result.IncrementTotalChecks(spacingRule.After?.IsHardConstraint == true);
            var expected = spacingRule.After.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingAfterTwips);

            if (actual.HasValue && IsWithinTolerance(actual.Value, expected, 0.5m) && !format.DfpSpacingAfterAutospacing)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                AddBlankParagraphFormatError(
                    result,
                    field,
                    $"Spacing after {subjectLabel} tidak sesuai dengan aturan paragraf",
                    expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    actual.HasValue
                        ? actual.Value.ToString(CultureInfo.InvariantCulture) + " pt"
                        : "unknown",
                    evidence,
                    locations,
                    blankElementId,
                    context);
            }
        }
    }

    private static void AddBlankParagraphFormatError(
        ValidationResult result,
        string field,
        string message,
        string expected,
        string actual,
        string evidence,
        List<ErrorLocation> locations,
        ulong blankElementId,
        ElementNeighborContext? context)
    {
        var error = new ValidationError
        {
            Category = "Isi Buku",
            Field = field,
            Message = message,
            Expected = expected,
            Actual = actual,
            Evidence = evidence,
            Locations = locations,
            DokumenElemenId = blankElementId
        };

        if (context != null)
            ApplyContext(error, context);

        result.Errors.Add(error);
    }

    private static IReadOnlyList<uint> ExtractTextFormatIdsIncludingEmptyRuns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<uint>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var ids = new HashSet<uint>();
            CollectTextFormatIds(doc.RootElement, ids);
            return ids.ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<uint>();
        }
    }

    private static void CollectTextFormatIds(JsonElement element, HashSet<uint> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("dftx_id", out var dftxEl) && dftxEl.TryGetUInt32(out var dftxId))
                    ids.Add(dftxId);

                if (element.TryGetProperty("result_dftx_id", out var resultEl) && resultEl.TryGetUInt32(out var resultId))
                    ids.Add(resultId);

                foreach (var property in element.EnumerateObject())
                    CollectTextFormatIds(property.Value, ids);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectTextFormatIds(item, ids);
                break;
        }
    }

    private static bool IsElementAtTopOfPage(
        int anchorIndex,
        ulong anchorElementId,
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, ElementContentInfo> elementContentById,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById)
    {
        if (!TryGetFirstVisualPosition(visualSummaryById, anchorElementId, out var anchorPage, out var anchorTop))
            return false;

        const double visualTolerance = 0.5d;
        for (var cursor = anchorIndex - 1; cursor >= 0; cursor--)
        {
            var previousElementId = bodyElements[cursor].DelemenId;
            if (!visualSummaryById.TryGetValue(previousElementId, out var previousVisual))
            {
                if (elementContentById.TryGetValue(previousElementId, out var previousContent) &&
                    !IsEmptyElement(previousContent))
                {
                    return false;
                }

                continue;
            }

            if (previousVisual.Bounds.Any(bounds =>
                    bounds.Page == anchorPage &&
                    bounds.Y0 < anchorTop + visualTolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsElementAtBottomOfPage(
        int anchorIndex,
        ulong anchorElementId,
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, ElementContentInfo> elementContentById,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById)
    {
        if (!TryGetLastVisualPosition(visualSummaryById, anchorElementId, out var anchorPage, out var anchorBottom))
            return false;

        const double visualTolerance = 0.5d;
        for (var cursor = anchorIndex + 1; cursor < bodyElements.Count; cursor++)
        {
            var nextElementId = bodyElements[cursor].DelemenId;
            if (!visualSummaryById.TryGetValue(nextElementId, out var nextVisual))
            {
                if (elementContentById.TryGetValue(nextElementId, out var nextContent) &&
                    !IsEmptyElement(nextContent))
                {
                    return false;
                }

                continue;
            }

            if (nextVisual.Bounds.Count > 0 &&
                nextVisual.Bounds.All(bounds => bounds.Page != anchorPage) &&
                nextVisual.Bounds.Any(bounds => bounds.Page > anchorPage))
            {
                return true;
            }

            if (nextVisual.Bounds.Any(bounds =>
                    bounds.Page == anchorPage &&
                    bounds.Y0 > anchorBottom - visualTolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static List<ulong> CollectBlankParagraphsBeforeElement(
        int anchorIndex,
        ulong anchorElementId,
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, ElementContentInfo> elementContentById,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById)
    {
        if (!TryGetFirstVisualPosition(visualSummaryById, anchorElementId, out var anchorPage, out var anchorTop))
            return [];

        const double visualTolerance = 0.5d;
        var blankElementIds = new List<ulong>();
        var currentTop = anchorTop;

        for (var cursor = anchorIndex - 1; cursor >= 0; cursor--)
        {
            var candidateId = bodyElements[cursor].DelemenId;
            if (!elementContentById.TryGetValue(candidateId, out var candidateContent) ||
                !IsEmptyElement(candidateContent))
            {
                break;
            }

            if (!TryGetVisualBoundsOnPage(visualSummaryById, candidateId, anchorPage, out var candidateTop, out var candidateBottom))
            {
                if (!HasInvisibleBlankParagraph(visualSummaryById, candidateId))
                    break;

                blankElementIds.Insert(0, candidateId);
                continue;
            }

            if (candidateBottom > currentTop + visualTolerance)
                break;

            blankElementIds.Insert(0, candidateId);
            currentTop = candidateTop;
        }

        return PreferVisibleBlankParagraphs(blankElementIds, anchorPage, visualSummaryById);
    }

    private static List<ulong> CollectBlankParagraphsAfterElement(
        int anchorIndex,
        ulong anchorElementId,
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, ElementContentInfo> elementContentById,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById)
    {
        if (!TryGetLastVisualPosition(visualSummaryById, anchorElementId, out var anchorPage, out var anchorBottom))
            return [];

        const double visualTolerance = 0.5d;
        var blankElementIds = new List<ulong>();
        var currentBottom = anchorBottom;

        for (var cursor = anchorIndex + 1; cursor < bodyElements.Count; cursor++)
        {
            var candidateId = bodyElements[cursor].DelemenId;
            if (!elementContentById.TryGetValue(candidateId, out var candidateContent) ||
                !IsEmptyElement(candidateContent))
            {
                break;
            }

            if (!TryGetVisualBoundsOnPage(visualSummaryById, candidateId, anchorPage, out var candidateTop, out var candidateBottom))
            {
                if (!HasInvisibleBlankParagraph(visualSummaryById, candidateId))
                    break;

                blankElementIds.Add(candidateId);
                continue;
            }

            if (candidateTop < currentBottom - visualTolerance)
                break;

            blankElementIds.Add(candidateId);
            currentBottom = candidateBottom;
        }

        var preferredBlankElementIds = PreferVisibleBlankParagraphs(blankElementIds, anchorPage, visualSummaryById);
        if (preferredBlankElementIds.Count > 1 &&
            preferredBlankElementIds.All(id => !TryGetVisualBoundsOnPage(visualSummaryById, id, anchorPage, out var _, out var _)) &&
            TryFindNextVisiblePosition(bodyElements, anchorIndex + 1, visualSummaryById, out var nextPage, out var _) &&
            nextPage != anchorPage)
        {
            return [preferredBlankElementIds[0]];
        }

        return preferredBlankElementIds;
    }

    private static List<ulong> PreferVisibleBlankParagraphs(
        IReadOnlyList<ulong> blankElementIds,
        int anchorPage,
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById)
    {
        if (blankElementIds.Count <= 1)
            return blankElementIds.ToList();

        var visibleIds = blankElementIds
            .Where(id => TryGetVisualBoundsOnPage(visualSummaryById, id, anchorPage, out var _, out var _))
            .ToList();

        return visibleIds.Count > 0 ? visibleIds : blankElementIds.ToList();
    }

    private static bool HasInvisibleBlankParagraph(
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById,
        ulong elementId)
    {
        return !visualSummaryById.TryGetValue(elementId, out var summary) ||
               summary.Bounds.Count == 0;
    }

    private static bool TryGetLastVisualPosition(
        IReadOnlyDictionary<ulong, VisualElementSummary> visualSummaryById,
        ulong elementId,
        out int page,
        out double y1)
    {
        page = default;
        y1 = default;

        if (!visualSummaryById.TryGetValue(elementId, out var summary) ||
            summary.Bounds.Count == 0)
        {
            return false;
        }

        var lastBounds = summary.Bounds[^1];
        page = lastBounds.Page;
        y1 = lastBounds.Y1;
        return true;
    }
}

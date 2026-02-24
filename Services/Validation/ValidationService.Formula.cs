using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private sealed class FormulaElementInfo
    {
        public ulong ElementId { get; init; }
        public int OrderIndex { get; init; }
        public string NormalizedLabel { get; init; } = string.Empty;
        public ElementContentInfo Content { get; init; } = new();
        public string Evidence { get; init; } = "Rumus";
    }

    private sealed class FormulaTabStopInfo
    {
        public string Alignment { get; init; } = string.Empty;
        public decimal? PositionCm { get; init; }
        public string LeaderStyle { get; init; } = string.Empty;
    }

    private async Task<ValidationResult> ValidateFormulaAsync(int dokumenId, CancellationToken cancellationToken)
    {
        var result = new ValidationResult();

        var dokumen = await _db.Dokumens.FindAsync(new object[] { dokumenId }, cancellationToken);
        if (dokumen == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Dokumen",
                Field = "dokumen_id",
                Message = "Dokumen tidak ditemukan"
            });
            return result;
        }

        var aturan = await _db.Aturans
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (aturan == null)
            return result;

        var formulaDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "rumus")
            .FirstOrDefaultAsync(cancellationToken);

        if (formulaDetail == null)
            return result;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        FormulaRule? formulaRule = null;

        try
        {
            var rawJson = formulaDetail.AturanDetailJsonValue ?? "{}";
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("rumus", out var rumusElement))
            {
                formulaRule = JsonSerializer.Deserialize<FormulaRule>(rumusElement.GetRawText(), jsonOptions);
            }
            else
            {
                formulaRule = JsonSerializer.Deserialize<FormulaRule>(rawJson, jsonOptions);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan rumus");
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "rumus",
                Message = "Format aturan rumus tidak valid"
            });
            return result;
        }

        if (formulaRule == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "rumus",
                Message = "Aturan rumus tidak valid"
            });
            return result;
        }

        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DsecRefTipe == "dokumen" && s.DsecRefId == (uint)dokumenId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new BodyElementInfo
            {
                DelemenId = e.DelemenId,
                DelemenType = e.DelemenType,
                DelemenJsonTree = e.DelemenJsonTree
            }).ToListAsync(cancellationToken);

        if (bodyElements.Count == 0)
            return result;

        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var orderedElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => (string?)e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);
        var pageNumbersById = await LoadPageNumbersAsync(orderedElementIds, cancellationToken);
        var pageLayoutsById = await LoadPageLayoutsAsync(orderedElementIds, cancellationToken);

        var formulaElements = new List<FormulaElementInfo>();
        for (var index = 0; index < bodyElements.Count; index++)
        {
            var element = bodyElements[index];
            var normalizedLabel = labelMap.TryGetValue(element.DelemenId, out var rawLabel)
                ? NormalizeLabel(rawLabel)
                : string.Empty;

            if (!IsFormulaElementCandidate(normalizedLabel))
                continue;

            var content = ParseElementContent(element.DelemenJsonTree);
            var evidence = BuildFormulaEvidence(content);

            formulaElements.Add(new FormulaElementInfo
            {
                ElementId = element.DelemenId,
                OrderIndex = index,
                NormalizedLabel = normalizedLabel,
                Content = content,
                Evidence = evidence
            });
        }

        if (formulaElements.Count == 0)
            return result;

        var formulaIdSet = formulaElements
            .Select(e => e.ElementId)
            .ToHashSet();

        var pagesWithNonFormulaParagraph = BuildPagesWithNonFormulaParagraph(
            bodyElements,
            labelMap,
            pageNumbersById,
            formulaIdSet);

        var paragraphFormatIds = formulaElements
            .Where(e => e.Content.ParagraphFormatId.HasValue)
            .Select(e => e.Content.ParagraphFormatId!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = paragraphFormatIds.Count > 0
            ? await _db.DokumenFormatParagrafs
                .Where(p => paragraphFormatIds.Contains(p.DfpId))
                .ToDictionaryAsync(p => p.DfpId, cancellationToken)
            : new Dictionary<uint, DokumenFormatParagraf>();

        var textFormatIds = formulaElements
            .SelectMany(e => e.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        foreach (var formulaElement in formulaElements.OrderBy(e => e.OrderIndex))
        {
            var errorStart = result.Errors.Count;

            DokumenFormatParagraf? paragraphFormat = null;
            if (formulaElement.Content.ParagraphFormatId.HasValue)
                paragraphFormats.TryGetValue(formulaElement.Content.ParagraphFormatId.Value, out paragraphFormat);

            var elementTextFormats = formulaElement.Content.TextFormatIds
                .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                .Where(tf => tf != null)
                .ToList();

            var locations = await BuildElementLocationsAsync(formulaElement.ElementId, cancellationToken);
            pageLayoutsById.TryGetValue(formulaElement.ElementId, out var formulaPageLayout);

            ValidateFormulaFont(
                result,
                formulaRule,
                elementTextFormats!,
                formulaElement.Content.TextRuns,
                formulaElement.Evidence,
                locations);

            ValidateFormulaParagraphFormat(
                result,
                formulaRule,
                paragraphFormat,
                formulaElement.Evidence,
                locations,
                formulaElement.Content.PlainText,
                formulaPageLayout);

            ValidateFormulaTabs(
                result,
                formulaRule,
                paragraphFormat,
                formulaElement.Evidence,
                locations);

            ValidateFormulaNumbering(
                result,
                formulaRule,
                formulaElement.Content,
                formulaElement.Evidence,
                locations);

            ValidateFormulaPageStructure(
                result,
                formulaRule,
                formulaElement,
                pageNumbersById,
                pagesWithNonFormulaParagraph,
                locations);

            if (neighborContexts.TryGetValue(formulaElement.ElementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, formulaElement.ElementId);
        }

        return result;
    }

    private static bool IsFormulaElementCandidate(string normalizedLabel)
    {
        return IsFormulaLabel(normalizedLabel);
    }

    private static bool IsFormulaLabel(string normalizedLabel)
    {
        return normalizedLabel == "rumus" ||
               normalizedLabel == "formula" ||
               normalizedLabel == "equation";
    }

    private static string BuildFormulaEvidence(ElementContentInfo content)
    {
        var text = NormalizeWhitespace(content.PlainText);
        if (string.IsNullOrWhiteSpace(text))
            return "Rumus";

        return text.Length > 100 ? text[..100] + "..." : text;
    }

    private static HashSet<int> BuildPagesWithNonFormulaParagraph(
        IReadOnlyList<BodyElementInfo> bodyElements,
        Dictionary<ulong, string> labelMap,
        Dictionary<ulong, int> pageNumbersById,
        HashSet<ulong> formulaElementIds)
    {
        var pages = new HashSet<int>();
        if (bodyElements.Count == 0 || pageNumbersById.Count == 0)
            return pages;

        foreach (var element in bodyElements)
        {
            if (formulaElementIds.Contains(element.DelemenId))
                continue;

            if (!pageNumbersById.TryGetValue(element.DelemenId, out var page) || page <= 0)
                continue;

            var normalizedLabel = labelMap.TryGetValue(element.DelemenId, out var rawLabel)
                ? NormalizeLabel(rawLabel)
                : string.Empty;

            if (normalizedLabel != "paragraf")
                continue;

            var content = ParseElementContent(element.DelemenJsonTree);
            if (!string.IsNullOrWhiteSpace(NormalizeWhitespace(content.PlainText)))
                pages.Add(page);
        }

        return pages;
    }

    private void ValidateFormulaFont(
        ValidationResult result,
        FormulaRule rule,
        List<DokumenFormatText> textFormats,
        IReadOnlyList<TextRunInfo> textRuns,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (textFormats.Count == 0)
            return;

        var expectedFontName = rule.Font?.FontName?.Value;
        var expectedFontSize = rule.Font?.FontSize?.Value;
        if (string.IsNullOrWhiteSpace(expectedFontName) && !expectedFontSize.HasValue)
            return;

        var textFormatById = BuildTextFormatMap(textFormats);
        var runs = FilterFormulaFontRuns(GetMeaningfulRuns(textRuns));
        if (runs.Count == 0)
            return;

        if (!string.IsNullOrWhiteSpace(expectedFontName))
        {
            result.IncrementTotalChecks();
            var mismatches = CollectRunMismatches(
                runs,
                textFormatById,
                tf => !string.Equals(tf.DftxFontAscii ?? "unknown", expectedFontName, StringComparison.OrdinalIgnoreCase),
                tf => tf.DftxFontAscii ?? "unknown");

            if (mismatches.Count == 0)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "Font rumus tidak sesuai",
                    Expected = expectedFontName,
                    Actual = BuildMismatchSummary(mismatches),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (expectedFontSize.HasValue)
        {
            result.IncrementTotalChecks();
            var expectedHalfPt = expectedFontSize.Value * 2m;
            var mismatches = CollectRunMismatches(
                runs,
                textFormatById,
                tf => !tf.DftxSizeHalfpt.HasValue || Math.Abs(tf.DftxSizeHalfpt.Value - expectedHalfPt) > 0.5m,
                tf => tf.DftxSizeHalfpt.HasValue
                    ? (tf.DftxSizeHalfpt.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt"
                    : "unknown");

            if (mismatches.Count == 0)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "Ukuran font rumus tidak sesuai",
                    Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = BuildMismatchSummary(mismatches),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private static List<TextRunInfo> FilterFormulaFontRuns(IEnumerable<TextRunInfo> runs)
    {
        var filtered = new List<TextRunInfo>();
        foreach (var run in runs)
        {
            var normalized = NormalizeWhitespace(run.Text);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (Regex.IsMatch(normalized, @"^[()\[\]{}\d\.\-,:;+\s]+$"))
                continue;

            filtered.Add(run);
        }

        return filtered;
    }

    private void ValidateFormulaParagraphFormat(
        ValidationResult result,
        FormulaRule rule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations,
        string formulaText,
        PageLayoutSnapshot? pageLayout)
    {
        if (format == null)
            return;

        var expectedAlignments = new List<(string? Expected, string Message)>
        {
            (rule.Paragraph?.Alignment?.Value, "Alignment rumus tidak sesuai"),
            (rule.Position?.ParagraphAlignment?.Value, "Paragraph alignment rumus tidak sesuai"),
            (rule.Position?.EquationAlignment?.Value, "Equation alignment rumus tidak sesuai")
        };

        var checkedAlignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (expectedAlignment, message) in expectedAlignments)
        {
            if (string.IsNullOrWhiteSpace(expectedAlignment))
                continue;

            var normalizedExpected = NormalizeAlignmentValue(expectedAlignment);
            if (!checkedAlignments.Add(normalizedExpected))
                continue;

            result.IncrementTotalChecks();
            var actual = format.DfpJc ?? "unknown";
            var alignmentContext = CreateAlignmentContext(formulaText, locations, pageLayout);
            if (AreAlignmentsEquivalent(actual, expectedAlignment, alignmentContext))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = message,
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var indentationRule = rule.Paragraph?.Indentation;
        var expectedFirstLineIndent = indentationRule?.FirstLineIndent?.Value ??
                                      indentationRule?.FirstLineIndentCm?.Value;
        var expectedLeftIndent = indentationRule?.LeftIndent?.Value ??
                                 indentationRule?.LeftIndentCm?.Value ??
                                 indentationRule?.LeftCm?.Value;
        var expectedOverallIndent = rule.Position?.OverallIndentCm?.Value;

        if (expectedFirstLineIndent.HasValue)
        {
            result.IncrementTotalChecks();
            var firstLineTwips = format.DfpIndFirstLineTwips ?? 0;
            var firstLineCm = firstLineTwips / 1440.0m * 2.54m;

            if (Math.Abs(firstLineCm - expectedFirstLineIndent.Value) <= 0.05m)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "First line indent rumus tidak sesuai",
                    Expected = expectedFirstLineIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = firstLineCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var leftTwips = format.DfpIndLeftTwips.HasValue && format.DfpIndLeftTwips.Value != 0
            ? format.DfpIndLeftTwips.Value
            : format.DfpIndStartTwips ?? 0;
        var leftCm = leftTwips / 1440.0m * 2.54m;

        if (expectedLeftIndent.HasValue)
        {
            result.IncrementTotalChecks();
            if (Math.Abs(leftCm - expectedLeftIndent.Value) <= 0.05m)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "Left indent rumus tidak sesuai",
                    Expected = expectedLeftIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = leftCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (expectedOverallIndent.HasValue)
        {
            result.IncrementTotalChecks();
            if (Math.Abs(leftCm - expectedOverallIndent.Value) <= 0.05m)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "Overall indent rumus tidak sesuai",
                    Expected = expectedOverallIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = leftCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        result.IncrementTotalChecks();
        var rightCm = GetRightIndentCm(format);
        if (Math.Abs(rightCm) <= 0.05m)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "rumus",
                Message = "Right indent rumus harus 0",
                Expected = "0 cm",
                Actual = rightCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                Evidence = evidence,
                Locations = locations
            });
        }

        var spacingRule = rule.Paragraph?.Spacing;
        if (spacingRule?.LineSpacing?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.LineSpacing.Value.Value;
            var actual = GetLineSpacing(format);

            if (actual.HasValue && Math.Abs(actual.Value - expected) <= 0.05m)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "Line spacing rumus tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (spacingRule?.Before?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.Before.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingBeforeTwips);

            if (actual.HasValue &&
                IsWithinTolerance(actual.Value, expected, 0.5m) &&
                !format.DfpSpacingBeforeAutospacing)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "Spacing before rumus tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (spacingRule?.After?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.After.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingAfterTwips);

            if (actual.HasValue &&
                IsWithinTolerance(actual.Value, expected, 0.5m) &&
                !format.DfpSpacingAfterAutospacing)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = "Spacing after rumus tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateFormulaTabs(
        ValidationResult result,
        FormulaRule rule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (format == null || rule.Tabs == null)
            return;

        var tabStops = ParseFormulaTabStops(format.DfpTabsJson);

        ValidateFormulaTab(
            result,
            "left",
            tabStops,
            rule.Tabs.LeftTab,
            evidence,
            locations);

        ValidateFormulaTab(
            result,
            "right",
            tabStops,
            rule.Tabs.RightTab,
            evidence,
            locations);
    }

    private void ValidateFormulaTab(
        ValidationResult result,
        string tabName,
        List<FormulaTabStopInfo> tabStops,
        FormulaTabRule? tabRule,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (tabRule == null)
            return;

        var expectedAlignment = NormalizeTabAlignment(tabRule.Alignment?.Value);
        var expectedLeader = string.IsNullOrWhiteSpace(tabRule.LeaderStyle?.Value)
            ? string.Empty
            : NormalizeTabLeader(tabRule.LeaderStyle?.Value);
        var expectedPositionCm = tabRule.PositionCm?.Value;
        var expectedDistanceCm = tabRule.DistanceFromEquationCm?.Value;
        var dependsOnEquationLength = tabRule.DependsOnEquationLength?.Value ?? false;

        if (!expectedPositionCm.HasValue && expectedDistanceCm.HasValue && !dependsOnEquationLength)
            expectedPositionCm = expectedDistanceCm;

        var requiresValidation =
            !string.IsNullOrWhiteSpace(expectedAlignment) ||
            !string.IsNullOrWhiteSpace(expectedLeader) ||
            expectedPositionCm.HasValue ||
            expectedDistanceCm.HasValue;

        if (!requiresValidation)
            return;

        var preferRight = tabName.Equals("right", StringComparison.OrdinalIgnoreCase);
        var actualTab = FindFormulaTabStop(tabStops, expectedAlignment, preferRight);

        result.IncrementTotalChecks();
        if (actualTab == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "rumus",
                Message = $"Tab {tabName} rumus tidak ditemukan",
                Expected = $"Tab {tabName}",
                Actual = "Tidak ada tab",
                Evidence = evidence,
                Locations = locations
            });
            return;
        }
        result.IncrementPassedChecks();

        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks();
            if (string.Equals(actualTab.Alignment, expectedAlignment, StringComparison.OrdinalIgnoreCase))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = $"Alignment tab {tabName} rumus tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actualTab.Alignment,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedLeader))
        {
            result.IncrementTotalChecks();
            if (string.Equals(actualTab.LeaderStyle, expectedLeader, StringComparison.OrdinalIgnoreCase))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = $"Leader style tab {tabName} rumus tidak sesuai",
                    Expected = expectedLeader,
                    Actual = actualTab.LeaderStyle,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (expectedPositionCm.HasValue)
        {
            result.IncrementTotalChecks();
            if (actualTab.PositionCm.HasValue && Math.Abs(actualTab.PositionCm.Value - expectedPositionCm.Value) <= 0.15m)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = $"Posisi tab {tabName} rumus tidak sesuai",
                    Expected = expectedPositionCm.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = actualTab.PositionCm?.ToString("F2", CultureInfo.InvariantCulture) + " cm" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
        else if (expectedDistanceCm.HasValue && dependsOnEquationLength)
        {
            result.IncrementTotalChecks();
            if (actualTab.PositionCm.HasValue && actualTab.PositionCm.Value + 0.15m >= expectedDistanceCm.Value)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "rumus",
                    Message = $"Jarak minimum tab {tabName} rumus tidak sesuai",
                    Expected = $">= {expectedDistanceCm.Value.ToString(CultureInfo.InvariantCulture)} cm",
                    Actual = actualTab.PositionCm?.ToString("F2", CultureInfo.InvariantCulture) + " cm" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private static List<FormulaTabStopInfo> ParseFormulaTabStops(string? tabsJson)
    {
        var tabStops = new List<FormulaTabStopInfo>();
        if (string.IsNullOrWhiteSpace(tabsJson))
            return tabStops;

        try
        {
            using var doc = JsonDocument.Parse(tabsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return tabStops;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                string? alignment = null;
                if (item.TryGetProperty("val", out var valEl) && valEl.ValueKind == JsonValueKind.String)
                    alignment = valEl.GetString();

                string? leader = null;
                if (item.TryGetProperty("leader", out var leaderEl) && leaderEl.ValueKind == JsonValueKind.String)
                    leader = leaderEl.GetString();

                decimal? positionCm = null;
                if (item.TryGetProperty("pos", out var posEl) && TryParseJsonDecimal(posEl, out var posTwips))
                    positionCm = posTwips / 1440m * 2.54m;

                tabStops.Add(new FormulaTabStopInfo
                {
                    Alignment = NormalizeTabAlignment(alignment),
                    LeaderStyle = NormalizeTabLeader(leader),
                    PositionCm = positionCm
                });
            }
        }
        catch (JsonException)
        {
            // Ignore malformed tab JSON.
        }

        return tabStops;
    }

    private static bool TryParseJsonDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDecimal(out value);

        if (element.ValueKind != JsonValueKind.String)
            return false;

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim().Replace(',', '.');
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static FormulaTabStopInfo? FindFormulaTabStop(
        List<FormulaTabStopInfo> tabStops,
        string expectedAlignment,
        bool preferRight)
    {
        var candidates = tabStops
            .Where(t => !string.Equals(t.Alignment, "clear", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            var filtered = candidates
                .Where(t => string.Equals(t.Alignment, expectedAlignment, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count > 0)
                candidates = filtered;
        }

        if (candidates.Count == 0)
            return null;

        if (preferRight)
            return candidates.OrderByDescending(t => t.PositionCm ?? decimal.MinValue).First();

        return candidates.OrderBy(t => t.PositionCm ?? decimal.MaxValue).First();
    }

    private static string NormalizeTabAlignment(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return string.Empty;

        var normalized = alignment.Trim().ToLowerInvariant();
        return normalized switch
        {
            "start" => "left",
            "end" => "right",
            "num" => "right",
            "number" => "right",
            "decimal" => "right",
            _ => normalized
        };
    }

    private static string NormalizeTabLeader(string? leaderStyle)
    {
        if (string.IsNullOrWhiteSpace(leaderStyle))
            return "none";

        var normalized = leaderStyle.Trim().ToLowerInvariant();
        return normalized switch
        {
            "dots" => "dot",
            "none" => "none",
            "nil" => "none",
            _ => normalized
        };
    }

    private void ValidateFormulaNumbering(
        ValidationResult result,
        FormulaRule rule,
        ElementContentInfo content,
        string evidence,
        List<ErrorLocation> locations)
    {
        var expectedFormat = rule.Numbering?.NumberFormat?.Value;
        if (string.IsNullOrWhiteSpace(expectedFormat))
            return;

        result.IncrementTotalChecks();

        var numberToken = ExtractFormulaNumberToken(content.PlainText);
        if (string.IsNullOrWhiteSpace(numberToken))
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "rumus",
                Message = "Nomor rumus tidak ditemukan",
                Expected = expectedFormat,
                Actual = "Tidak ada nomor",
                Evidence = evidence,
                Locations = locations
            });
            return;
        }

        if (MatchesFormulaNumberFormat(numberToken, expectedFormat))
        {
            result.IncrementPassedChecks();
            return;
        }

        result.Errors.Add(new ValidationError
        {
            Category = "Isi Buku",
            Field = "rumus",
            Message = "Format nomor rumus tidak sesuai",
            Expected = expectedFormat,
            Actual = numberToken,
            Evidence = evidence,
            Locations = locations
        });
    }

    private static string? ExtractFormulaNumberToken(string? plainText)
    {
        var normalized = NormalizeWhitespace(plainText ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var tailMatch = Regex.Match(normalized, @"(\(\s*\d+(?:\.\d+)*\s*\)|\d+(?:\.\d+)+)\s*$");
        if (tailMatch.Success && tailMatch.Groups.Count > 1)
            return NormalizeWhitespace(tailMatch.Groups[1].Value);

        var parenthesizedMatches = Regex.Matches(normalized, @"\(\s*\d+(?:\.\d+)*\s*\)");
        if (parenthesizedMatches.Count > 0)
            return NormalizeWhitespace(parenthesizedMatches[^1].Value);

        return null;
    }

    private static bool MatchesFormulaNumberFormat(string actualNumber, string template)
    {
        var normalizedActual = NormalizeWhitespace(actualNumber);
        var normalizedTemplate = NormalizeWhitespace(template);
        if (string.IsNullOrWhiteSpace(normalizedActual) || string.IsNullOrWhiteSpace(normalizedTemplate))
            return false;

        var pattern = Regex.Escape(normalizedTemplate);
        pattern = Regex.Replace(pattern, @"\\\[[^\]]+\\\]", _ => @"\d+");
        pattern = pattern.Replace(@"\ ", @"\s*");
        pattern = "^" + pattern + "$";

        return Regex.IsMatch(normalizedActual, pattern, RegexOptions.IgnoreCase);
    }

    private void ValidateFormulaPageStructure(
        ValidationResult result,
        FormulaRule rule,
        FormulaElementInfo formulaElement,
        Dictionary<ulong, int> pageNumbersById,
        HashSet<int> pagesWithNonFormulaParagraph,
        List<ErrorLocation> locations)
    {
        var requiresParagraphOnPage =
            rule.Position?.CegahMemenuhiHalaman?.Value == true ||
            rule.StrukturHalaman?.CegahMemenuhiHalaman?.Value == true ||
            rule.StrukturHalaman?.MinimalSatuParagrafDiHalaman?.Value == true;

        if (!requiresParagraphOnPage)
            return;

        if (!pageNumbersById.TryGetValue(formulaElement.ElementId, out var pageNumber) || pageNumber <= 0)
            return;

        result.IncrementTotalChecks();
        if (pagesWithNonFormulaParagraph.Contains(pageNumber))
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "rumus",
                Message = "Halaman rumus tidak boleh hanya berisi rumus",
                Expected = "Ada minimal satu paragraf non-rumus pada halaman yang sama",
                Actual = "Tidak ada paragraf non-rumus",
                Evidence = formulaElement.Evidence,
                Locations = locations
            });
        }
    }
}


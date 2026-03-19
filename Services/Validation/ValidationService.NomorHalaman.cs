using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private sealed class PageNumberParagraphInfo
    {
        public uint SectionId { get; init; }
        public string PartType { get; init; } = "footer";
        public string PartPosition { get; init; } = "default";
        public ulong ElementId { get; init; }
        public ElementContentInfo Content { get; init; } = new();
        public DokumenFormatParagraf? ParagraphFormat { get; init; }
        public IReadOnlyList<DokumenFormatText> TextFormats { get; init; } = Array.Empty<DokumenFormatText>();

        public bool IsMeaningful =>
            Content.HasPageField ||
            Content.HasNonTextContent ||
            !string.IsNullOrWhiteSpace(NormalizeWhitespace(Content.PlainText));
    }

    private sealed record ExpectedPageNumberSlot(
        string Key,
        string Label,
        string PartPosition,
        string Location,
        string Alignment);

    private sealed class PageNumberElementRow
    {
        public uint DsecId { get; init; }
        public string PartType { get; init; } = "footer";
        public string PartPosition { get; init; } = "default";
        public ulong ElementId { get; init; }
        public string? DelemenJsonTree { get; init; }
    }

    private static readonly IReadOnlyDictionary<string, string> LegacyWordPageNumberFormats =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arabic"] = "decimal",
            ["arab"] = "decimal",
            ["decimal"] = "decimal",
            ["desimal"] = "decimal",
            ["lowerroman"] = "lowerRoman",
            ["roman_lower"] = "lowerRoman",
            ["lower_roman"] = "lowerRoman",
            ["upperroman"] = "upperRoman",
            ["roman_upper"] = "upperRoman",
            ["upper_roman"] = "upperRoman",
            ["lowerletter"] = "lowerLetter",
            ["lower_letter"] = "lowerLetter",
            ["lower_alpha"] = "lowerLetter",
            ["letter_lower"] = "lowerLetter",
            ["upperletter"] = "upperLetter",
            ["upper_letter"] = "upperLetter",
            ["upper_alpha"] = "upperLetter",
            ["letter_upper"] = "upperLetter"
        };

    private static NomorHalamanRule BuildEffectiveNomorHalamanRule(NomorHalamanRule? rule)
    {
        var effectiveRule = rule ?? new NomorHalamanRule();

        effectiveRule.Numbering ??= new TitleNumberingRule();
        effectiveRule.Numbering.NumberFormat ??= new RuleValue<string>
        {
            Value = "decimal",
            IsEditable = false,
            IsHardConstraint = false
        };
        effectiveRule.Numbering.NumberFormat.Value = NormalizeWordPageNumberFormat(effectiveRule.Numbering.NumberFormat.Value) ?? "decimal";

        effectiveRule.Font ??= new TitleFontRule();
        effectiveRule.Font.FontName ??= new RuleValue<string> { Value = "Times New Roman", IsEditable = true, IsHardConstraint = false };
        effectiveRule.Font.FontSize ??= new DecimalRuleValue { Value = 12m, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Font.FontStyle ??= new TitleFontStyleRule();
        effectiveRule.Font.FontStyle.Bold ??= new RuleValue<bool> { Value = false, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Font.FontStyle.Italic ??= new RuleValue<bool> { Value = false, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Font.FontStyle.Underline ??= new RuleValue<bool> { Value = false, IsEditable = true, IsHardConstraint = false };

        effectiveRule.Paragraph ??= new TitleParagraphRule();
        effectiveRule.Paragraph.Indentation ??= new TitleParagraphIndentationRule();
        effectiveRule.Paragraph.Indentation.LeftIndent ??= new DecimalRuleValue { Value = 0m, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Paragraph.Indentation.RightIndent ??= new DecimalRuleValue { Value = 0m, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Paragraph.Indentation.FirstLineIndent ??= new DecimalRuleValue { Value = 0m, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Paragraph.Spacing ??= new TitleParagraphSpacingRule();
        effectiveRule.Paragraph.Spacing.LineSpacing ??= new DecimalRuleValue { Value = 1m, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Paragraph.Spacing.Before ??= new DecimalRuleValue { Value = 0m, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Paragraph.Spacing.After ??= new DecimalRuleValue { Value = 0m, IsEditable = true, IsHardConstraint = false };

        effectiveRule.StrukturKonten ??= new NomorHalamanContentStructureRule();
        effectiveRule.StrukturKonten.CegahBarisTambahan ??= new RuleValue<bool> { Value = true, IsEditable = true, IsHardConstraint = false };

        effectiveRule.Variation ??= new NomorHalamanVariationRule();
        effectiveRule.Variation.Default ??= new NomorHalamanSlotRule();
        effectiveRule.Variation.Default.Position ??= new NomorHalamanPositionRule();
        effectiveRule.Variation.Default.Position.Location ??= new RuleValue<string> { Value = "footer", IsEditable = true, IsHardConstraint = false };
        effectiveRule.Variation.Default.Position.Alignment ??= new RuleValue<string> { Value = "center", IsEditable = true, IsHardConstraint = false };

        effectiveRule.Variation.DifferentFirstPage ??= new NomorHalamanFirstPageVariationRule();
        effectiveRule.Variation.DifferentFirstPage.Enabled ??= new RuleValue<bool> { Value = true, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Variation.DifferentFirstPage.First ??= new NomorHalamanSlotRule();
        effectiveRule.Variation.DifferentFirstPage.First.Position ??= new NomorHalamanPositionRule();
        effectiveRule.Variation.DifferentFirstPage.First.Position.Location ??= new RuleValue<string> { Value = "header", IsEditable = true, IsHardConstraint = false };
        effectiveRule.Variation.DifferentFirstPage.First.Position.Alignment ??= new RuleValue<string> { Value = "right", IsEditable = true, IsHardConstraint = false };

        effectiveRule.Variation.DifferentOddEven ??= new NomorHalamanOddEvenVariationRule();
        effectiveRule.Variation.DifferentOddEven.Enabled ??= new RuleValue<bool> { Value = false, IsEditable = true, IsHardConstraint = false };
        effectiveRule.Variation.DifferentOddEven.Even ??= new NomorHalamanSlotRule();
        effectiveRule.Variation.DifferentOddEven.Even.Position ??= new NomorHalamanPositionRule();
        effectiveRule.Variation.DifferentOddEven.Even.Position.Location ??= new RuleValue<string> { Value = "footer", IsEditable = true, IsHardConstraint = false };
        effectiveRule.Variation.DifferentOddEven.Even.Position.Alignment ??= new RuleValue<string> { Value = "left", IsEditable = true, IsHardConstraint = false };

        return effectiveRule;
    }

    private async Task<Dictionary<uint, IReadOnlyList<PageNumberParagraphInfo>>> LoadPageNumberParagraphsBySectionAsync(
        IEnumerable<uint> sectionIds,
        CancellationToken cancellationToken)
    {
        var ids = sectionIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<uint, IReadOnlyList<PageNumberParagraphInfo>>();

        var paragraphRows = await (from part in _db.DokumenParts
            join element in _db.DokumenElemens on part.DpartId equals element.DpartId
            where ids.Contains(part.DsecId)
                  && (part.DpartType == "header" || part.DpartType == "footer")
                  && element.DelemenType == "paragraph"
            orderby part.DsecId, part.DpartPosition, part.DpartType, element.DelemenSequence, element.DelemenId
            select new PageNumberElementRow
            {
                DsecId = part.DsecId,
                PartType = part.DpartType,
                PartPosition = part.DpartPosition ?? "default",
                ElementId = element.DelemenId,
                DelemenJsonTree = element.DelemenJsonTree
            }).ToListAsync(cancellationToken);

        if (paragraphRows.Count == 0)
            return new Dictionary<uint, IReadOnlyList<PageNumberParagraphInfo>>();

        var parsedRows = paragraphRows
            .Select(row => new
            {
                row.DsecId,
                row.PartType,
                row.PartPosition,
                row.ElementId,
                Content = ParseElementContent(row.DelemenJsonTree)
            })
            .ToList();

        var paragraphFormatIds = parsedRows
            .Select(row => row.Content.ParagraphFormatId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = paragraphFormatIds.Count == 0
            ? new Dictionary<uint, DokumenFormatParagraf>()
            : await _db.DokumenFormatParagrafs
                .Where(format => paragraphFormatIds.Contains(format.DfpId))
                .ToDictionaryAsync(format => format.DfpId, cancellationToken);

        var textFormatIds = parsedRows
            .SelectMany(row => row.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count == 0
            ? new Dictionary<uint, DokumenFormatText>()
            : await _db.DokumenFormatTexts
                .Where(format => textFormatIds.Contains(format.DftxId))
                .ToDictionaryAsync(format => format.DftxId, cancellationToken);

        return parsedRows
            .GroupBy(row => row.DsecId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PageNumberParagraphInfo>)group
                    .Select(row =>
                    {
                        paragraphFormats.TryGetValue(row.Content.ParagraphFormatId ?? 0, out var paragraphFormat);
                        var relevantTextFormats = row.Content.TextFormatIds
                            .Where(textFormats.ContainsKey)
                            .Select(id => textFormats[id])
                            .GroupBy(format => format.DftxId)
                            .Select(grouping => grouping.First())
                            .ToList();

                        return new PageNumberParagraphInfo
                        {
                            SectionId = row.DsecId,
                            PartType = NormalizePageNumberPartType(row.PartType),
                            PartPosition = NormalizePageNumberPartPosition(row.PartPosition),
                            ElementId = row.ElementId,
                            Content = row.Content,
                            ParagraphFormat = paragraphFormat,
                            TextFormats = relevantTextFormats
                        };
                    })
                    .ToList());
    }

    private async Task ValidateNomorHalamanRuleAsync(
        ValidationResult result,
        DokumenSection section,
        NomorHalamanRule rule,
        IReadOnlyList<PageNumberParagraphInfo> sectionParagraphs,
        int sectionNumber,
        Dictionary<uint, int> sectionPageMap,
        CancellationToken cancellationToken)
    {
        var expectedNumberFormat = NormalizeWordPageNumberFormat(rule.Numbering?.NumberFormat?.Value) ?? "decimal";
        result.IncrementTotalChecks();
        var actualFormat = NormalizeWordPageNumberFormat(section.DsecPageNumFormat) ?? "decimal";
        if (string.Equals(actualFormat, expectedNumberFormat, StringComparison.OrdinalIgnoreCase))
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Nomor Halaman",
                Field = "page_number_format",
                Message = $"Format nomor halaman section {sectionNumber} (bagian isi) tidak sesuai",
                Expected = expectedNumberFormat,
                Actual = actualFormat,
                SectionIndex = sectionNumber,
                Locations = BuildPageSettingsLocations("page_number_format", section, sectionPageMap)
            });
        }

        var expectedDifferentFirstPage = rule.Variation?.DifferentFirstPage?.Enabled?.Value ?? false;
        result.IncrementTotalChecks();
        if (section.DsecHasTitlePage == expectedDifferentFirstPage)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Nomor Halaman",
                Field = "different_first_page",
                Message = $"Pengaturan different first page section {sectionNumber} (bagian isi) tidak sesuai",
                Expected = expectedDifferentFirstPage ? "true" : "false",
                Actual = section.DsecHasTitlePage ? "true" : "false",
                SectionIndex = sectionNumber,
                Locations = BuildPageSettingsLocations("page_numbering", section, sectionPageMap)
            });
        }

        var expectedDifferentOddEven = rule.Variation?.DifferentOddEven?.Enabled?.Value ?? false;
        result.IncrementTotalChecks();
        if (section.DsecDifferentOddEven == expectedDifferentOddEven)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Nomor Halaman",
                Field = "different_odd_even",
                Message = $"Pengaturan nomor halaman ganjil-genap section {sectionNumber} (bagian isi) tidak sesuai",
                Expected = expectedDifferentOddEven ? "true" : "false",
                Actual = section.DsecDifferentOddEven ? "true" : "false",
                SectionIndex = sectionNumber,
                Locations = BuildPageSettingsLocations("different_odd_even", section, sectionPageMap)
            });
        }

        foreach (var slot in BuildExpectedPageNumberSlots(rule))
        {
            await ValidatePageNumberSlotAsync(
                result,
                section,
                rule,
                slot,
                sectionParagraphs,
                sectionNumber,
                sectionPageMap,
                cancellationToken);
        }
    }

    private async Task ValidatePageNumberSlotAsync(
        ValidationResult result,
        DokumenSection section,
        NomorHalamanRule rule,
        ExpectedPageNumberSlot slot,
        IReadOnlyList<PageNumberParagraphInfo> sectionParagraphs,
        int sectionNumber,
        Dictionary<uint, int> sectionPageMap,
        CancellationToken cancellationToken)
    {
        var samePositionParagraphs = sectionParagraphs
            .Where(paragraph => string.Equals(paragraph.PartPosition, slot.PartPosition, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var slotParagraphs = samePositionParagraphs
            .Where(paragraph => string.Equals(paragraph.PartType, slot.Location, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pageParagraphsInSlot = slotParagraphs
            .Where(paragraph => paragraph.Content.HasPageField)
            .ToList();

        var pageParagraphsInPosition = samePositionParagraphs
            .Where(paragraph => paragraph.Content.HasPageField)
            .ToList();

        result.IncrementTotalChecks();
        if (pageParagraphsInSlot.Count == 1)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            var actualLocation = pageParagraphsInPosition.Count > 0
                ? pageParagraphsInPosition
                    .Select(paragraph => paragraph.PartType)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .First()
                : "tidak ditemukan";

            var locations = pageParagraphsInPosition.Count > 0
                ? await BuildElementLocationsAsync(pageParagraphsInPosition.Select(paragraph => paragraph.ElementId), cancellationToken)
                : BuildPageSettingsLocations("page_numbering", section, sectionPageMap);

            result.Errors.Add(new ValidationError
            {
                Category = "Nomor Halaman",
                Field = "page_number_location",
                Message = $"Posisi nomor halaman slot {slot.Label} section {sectionNumber} (bagian isi) tidak sesuai",
                Expected = slot.Location,
                Actual = actualLocation,
                SectionIndex = sectionNumber,
                Locations = locations
            });
        }

        if (pageParagraphsInSlot.Count == 0)
            return;

        var activeParagraphLocations = await BuildElementLocationsAsync(
            pageParagraphsInSlot.Select(paragraph => paragraph.ElementId),
            cancellationToken);

        var expectedAlignment = NormalizeAlignmentValue(slot.Alignment);
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks();
            var alignments = pageParagraphsInSlot
                .Select(paragraph => NormalizeAlignmentValue(paragraph.ParagraphFormat?.DfpJc))
                .Where(alignment => !string.IsNullOrWhiteSpace(alignment))
                .ToList();

            if (alignments.Count > 0 &&
                alignments.All(alignment => AreAlignmentsEquivalent(alignment, expectedAlignment)))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Nomor Halaman",
                    Field = "page_number_alignment",
                    Message = $"Alignment nomor halaman slot {slot.Label} section {sectionNumber} (bagian isi) tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = alignments.Count == 0 ? "unknown" : string.Join(", ", alignments.Distinct(StringComparer.OrdinalIgnoreCase)),
                    SectionIndex = sectionNumber,
                    Locations = activeParagraphLocations
                });
            }
        }

        ValidatePageNumberFontRules(result, pageParagraphsInSlot, rule.Font, slot.Label, sectionNumber, activeParagraphLocations);
        ValidatePageNumberParagraphRules(result, pageParagraphsInSlot, rule.Paragraph, slot.Label, sectionNumber, activeParagraphLocations);

        var expectedNoExtraParagraphs = rule.StrukturKonten?.CegahBarisTambahan?.Value ?? false;
        if (expectedNoExtraParagraphs)
        {
            result.IncrementTotalChecks();
            var meaningfulParagraphCount = slotParagraphs.Count(paragraph => paragraph.IsMeaningful);
            if (pageParagraphsInSlot.Count == 1 && meaningfulParagraphCount == 1)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                var structureLocations = slotParagraphs.Count > 0
                    ? await BuildElementLocationsAsync(slotParagraphs.Select(paragraph => paragraph.ElementId), cancellationToken)
                    : activeParagraphLocations;

                result.Errors.Add(new ValidationError
                {
                    Category = "Nomor Halaman",
                    Field = "page_number_structure",
                    Message = $"Slot nomor halaman {slot.Label} section {sectionNumber} (bagian isi) tidak boleh memiliki baris tambahan",
                    Expected = "1 paragraf nomor halaman",
                    Actual = $"{meaningfulParagraphCount} paragraf bermakna / {pageParagraphsInSlot.Count} field PAGE",
                    SectionIndex = sectionNumber,
                    Locations = structureLocations
                });
            }
        }
    }

    private void ValidatePageNumberFontRules(
        ValidationResult result,
        IReadOnlyList<PageNumberParagraphInfo> paragraphs,
        TitleFontRule? fontRule,
        string slotLabel,
        int sectionNumber,
        IReadOnlyList<ErrorLocation> locations)
    {
        if (fontRule == null || paragraphs.Count == 0)
            return;

        var textFormats = paragraphs
            .SelectMany(paragraph => paragraph.TextFormats)
            .GroupBy(format => format.DftxId)
            .ToDictionary(group => group.Key, group => group.First());

        var runs = GetMeaningfulRuns(paragraphs.SelectMany(paragraph => paragraph.Content.TextRuns));

        ValidatePageNumberTextRule(
            result,
            "page_number_font_name",
            $"Font nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            fontRule.FontName?.Value,
            runs,
            textFormats,
            (format, expected) => !string.Equals(format.DftxFontAscii ?? "unknown", expected, StringComparison.OrdinalIgnoreCase),
            format => format.DftxFontAscii ?? "unknown",
            textFormats.Values.Select(format => format.DftxFontAscii ?? "unknown"),
            locations);

        var expectedFontSize = fontRule.FontSize?.Value;
        if (expectedFontSize.HasValue)
        {
            ValidatePageNumberTextRule(
                result,
                "page_number_font_size",
                $"Ukuran font nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
                expectedFontSize.Value.ToString(CultureInfo.InvariantCulture),
                runs,
                textFormats,
                (format, expected) =>
                {
                    var expectedHalfPoint = decimal.Parse(expected, CultureInfo.InvariantCulture) * 2m;
                    return !format.DftxSizeHalfpt.HasValue || Math.Abs(format.DftxSizeHalfpt.Value - expectedHalfPoint) > 0.5m;
                },
                format => format.DftxSizeHalfpt.HasValue
                    ? (format.DftxSizeHalfpt.Value / 2m).ToString(CultureInfo.InvariantCulture)
                    : "unknown",
                textFormats.Values.Select(format => format.DftxSizeHalfpt.HasValue
                    ? (format.DftxSizeHalfpt.Value / 2m).ToString(CultureInfo.InvariantCulture)
                    : "unknown"),
                locations);
        }

        ValidatePageNumberTextRule(
            result,
            "page_number_bold",
            $"Bold nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            fontRule.FontStyle?.Bold?.Value is bool expectedBold ? expectedBold.ToString().ToLowerInvariant() : null,
            runs,
            textFormats,
            (format, expected) => !format.DftxBold.HasValue || format.DftxBold.Value.ToString().ToLowerInvariant() != expected,
            format => format.DftxBold.HasValue ? format.DftxBold.Value.ToString().ToLowerInvariant() : "unknown",
            textFormats.Values.Select(format => format.DftxBold.HasValue ? format.DftxBold.Value.ToString().ToLowerInvariant() : "unknown"),
            locations);

        ValidatePageNumberTextRule(
            result,
            "page_number_italic",
            $"Italic nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            fontRule.FontStyle?.Italic?.Value is bool expectedItalic ? expectedItalic.ToString().ToLowerInvariant() : null,
            runs,
            textFormats,
            (format, expected) => !format.DftxItalic.HasValue || format.DftxItalic.Value.ToString().ToLowerInvariant() != expected,
            format => format.DftxItalic.HasValue ? format.DftxItalic.Value.ToString().ToLowerInvariant() : "unknown",
            textFormats.Values.Select(format => format.DftxItalic.HasValue ? format.DftxItalic.Value.ToString().ToLowerInvariant() : "unknown"),
            locations);

        ValidatePageNumberTextRule(
            result,
            "page_number_underline",
            $"Underline nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            fontRule.FontStyle?.Underline?.Value is bool expectedUnderline ? expectedUnderline.ToString().ToLowerInvariant() : null,
            runs,
            textFormats,
            (format, expected) =>
            {
                var hasUnderline = !string.IsNullOrWhiteSpace(format.DftxUnderline) &&
                    !string.Equals(format.DftxUnderline, "none", StringComparison.OrdinalIgnoreCase);
                return hasUnderline.ToString().ToLowerInvariant() != expected;
            },
            format =>
            {
                var hasUnderline = !string.IsNullOrWhiteSpace(format.DftxUnderline) &&
                    !string.Equals(format.DftxUnderline, "none", StringComparison.OrdinalIgnoreCase);
                return hasUnderline.ToString().ToLowerInvariant();
            },
            textFormats.Values.Select(format =>
            {
                var hasUnderline = !string.IsNullOrWhiteSpace(format.DftxUnderline) &&
                    !string.Equals(format.DftxUnderline, "none", StringComparison.OrdinalIgnoreCase);
                return hasUnderline.ToString().ToLowerInvariant();
            }),
            locations);
    }

    private void ValidatePageNumberTextRule(
        ValidationResult result,
        string field,
        string message,
        string? expected,
        IReadOnlyList<TextRunInfo> runs,
        IReadOnlyDictionary<uint, DokumenFormatText> textFormats,
        Func<DokumenFormatText, string, bool> isMismatch,
        Func<DokumenFormatText, string> actualFormatter,
        IEnumerable<string> fallbackValues,
        IReadOnlyList<ErrorLocation> locations)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return;

        result.IncrementTotalChecks();
        if (textFormats.Count == 0)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Nomor Halaman",
                Field = field,
                Message = message,
                Expected = expected,
                Actual = "unknown",
                Locations = locations.ToList()
            });
            return;
        }

        if (runs.Count > 0)
        {
            var mismatches = CollectRunMismatches(
                runs,
                new Dictionary<uint, DokumenFormatText>(textFormats),
                format => isMismatch(format, expected),
                actualFormatter);

            if (mismatches.Count == 0)
            {
                result.IncrementPassedChecks();
                return;
            }

            result.Errors.Add(new ValidationError
            {
                Category = "Nomor Halaman",
                Field = field,
                Message = message,
                Expected = expected,
                Actual = BuildMismatchSummary(mismatches),
                Locations = locations.ToList()
            });
            return;
        }

        var actualValues = fallbackValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (actualValues.Count > 0 && actualValues.All(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)))
        {
            result.IncrementPassedChecks();
            return;
        }

        result.Errors.Add(new ValidationError
        {
            Category = "Nomor Halaman",
            Field = field,
            Message = message,
            Expected = expected,
            Actual = actualValues.Count == 0 ? "unknown" : string.Join(", ", actualValues),
            Locations = locations.ToList()
        });
    }

    private void ValidatePageNumberParagraphRules(
        ValidationResult result,
        IReadOnlyList<PageNumberParagraphInfo> paragraphs,
        TitleParagraphRule? paragraphRule,
        string slotLabel,
        int sectionNumber,
        IReadOnlyList<ErrorLocation> locations)
    {
        if (paragraphRule == null || paragraphs.Count == 0)
            return;

        var paragraphFormats = paragraphs
            .Select(paragraph => paragraph.ParagraphFormat)
            .Where(format => format != null)
            .Select(format => format!)
            .ToList();

        if (paragraphFormats.Count == 0)
            return;

        ValidatePageNumberIndentationComponent(
            result,
            "page_number_left_indent",
            $"Left indent nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            paragraphFormats,
            paragraphRule.Indentation?.LeftIndent?.Value,
            GetLeftIndentCm,
            locations);

        ValidatePageNumberIndentationComponent(
            result,
            "page_number_right_indent",
            $"Right indent nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            paragraphFormats,
            paragraphRule.Indentation?.RightIndent?.Value,
            GetRightIndentCm,
            locations);

        ValidatePageNumberIndentationComponent(
            result,
            "page_number_first_line_indent",
            $"First line indent nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            paragraphFormats,
            paragraphRule.Indentation?.FirstLineIndent?.Value,
            GetFirstLineIndentCm,
            locations);

        ValidatePageNumberSpacingComponent(
            result,
            "page_number_line_spacing",
            $"Line spacing nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            paragraphFormats,
            paragraphRule.Spacing?.LineSpacing?.Value,
            format => GetLineSpacing(format),
            locations,
            string.Empty);

        ValidatePageNumberSpacingComponent(
            result,
            "page_number_spacing_before",
            $"Spacing before nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            paragraphFormats,
            paragraphRule.Spacing?.Before?.Value,
            format => TwipsToPoints(format.DfpSpacingBeforeTwips),
            locations,
            " pt");

        ValidatePageNumberSpacingComponent(
            result,
            "page_number_spacing_after",
            $"Spacing after nomor halaman slot {slotLabel} section {sectionNumber} (bagian isi) tidak sesuai",
            paragraphFormats,
            paragraphRule.Spacing?.After?.Value,
            format => TwipsToPoints(format.DfpSpacingAfterTwips),
            locations,
            " pt");
    }

    private void ValidatePageNumberIndentationComponent(
        ValidationResult result,
        string field,
        string message,
        IReadOnlyList<DokumenFormatParagraf> paragraphFormats,
        decimal? expected,
        Func<DokumenFormatParagraf, decimal> selector,
        IReadOnlyList<ErrorLocation> locations)
    {
        if (!expected.HasValue || paragraphFormats.Count == 0)
            return;

        result.IncrementTotalChecks();
        var actualValues = paragraphFormats.Select(selector).ToList();
        if (actualValues.All(actual => IsWithinTolerance(actual, expected.Value, 0.05m)))
        {
            result.IncrementPassedChecks();
            return;
        }

        result.Errors.Add(new ValidationError
        {
            Category = "Nomor Halaman",
            Field = field,
            Message = message,
            Expected = expected.Value.ToString(CultureInfo.InvariantCulture) + " cm",
            Actual = string.Join(", ", actualValues.Select(value => value.ToString("F2", CultureInfo.InvariantCulture) + " cm").Distinct()),
            Locations = locations.ToList()
        });
    }

    private void ValidatePageNumberSpacingComponent(
        ValidationResult result,
        string field,
        string message,
        IReadOnlyList<DokumenFormatParagraf> paragraphFormats,
        decimal? expected,
        Func<DokumenFormatParagraf, decimal?> selector,
        IReadOnlyList<ErrorLocation> locations,
        string unitSuffix)
    {
        if (!expected.HasValue || paragraphFormats.Count == 0)
            return;

        result.IncrementTotalChecks();
        var actualValues = paragraphFormats.Select(selector).ToList();
        if (actualValues.All(actual => actual.HasValue && IsWithinTolerance(actual.Value, expected.Value, 0.05m)))
        {
            result.IncrementPassedChecks();
            return;
        }

        result.Errors.Add(new ValidationError
        {
            Category = "Nomor Halaman",
            Field = field,
            Message = message,
            Expected = expected.Value.ToString(CultureInfo.InvariantCulture) + unitSuffix,
            Actual = string.Join(", ", actualValues.Select(value => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + unitSuffix : "unknown").Distinct()),
            Locations = locations.ToList()
        });
    }

    private static IReadOnlyList<ExpectedPageNumberSlot> BuildExpectedPageNumberSlots(NomorHalamanRule rule)
    {
        var slots = new List<ExpectedPageNumberSlot>();
        var oddEvenEnabled = rule.Variation?.DifferentOddEven?.Enabled?.Value ?? false;
        var defaultLabel = oddEvenEnabled ? "Odd" : "Default";

        slots.Add(new ExpectedPageNumberSlot(
            Key: "default",
            Label: defaultLabel,
            PartPosition: "default",
            Location: NormalizePageNumberPartType(rule.Variation?.Default?.Position?.Location?.Value),
            Alignment: NormalizeAlignmentValue(rule.Variation?.Default?.Position?.Alignment?.Value)));

        if (rule.Variation?.DifferentFirstPage?.Enabled?.Value ?? false)
        {
            slots.Insert(0, new ExpectedPageNumberSlot(
                Key: "first",
                Label: "First",
                PartPosition: "first",
                Location: NormalizePageNumberPartType(rule.Variation?.DifferentFirstPage?.First?.Position?.Location?.Value),
                Alignment: NormalizeAlignmentValue(rule.Variation?.DifferentFirstPage?.First?.Position?.Alignment?.Value)));
        }

        if (oddEvenEnabled)
        {
            slots.Add(new ExpectedPageNumberSlot(
                Key: "even",
                Label: "Even",
                PartPosition: "even",
                Location: NormalizePageNumberPartType(rule.Variation?.DifferentOddEven?.Even?.Position?.Location?.Value),
                Alignment: NormalizeAlignmentValue(rule.Variation?.DifferentOddEven?.Even?.Position?.Alignment?.Value)));
        }

        return slots;
    }

    private static string? NormalizeWordPageNumberFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return LegacyWordPageNumberFormats.TryGetValue(normalized, out var mapped)
            ? mapped
            : normalized;
    }

    private static string NormalizePageNumberPartType(string? value)
    {
        var normalized = (value ?? "footer").Trim().ToLowerInvariant();
        return normalized is "header" or "footer" ? normalized : "footer";
    }

    private static string NormalizePageNumberPartPosition(string? value)
    {
        var normalized = (value ?? "default").Trim().ToLowerInvariant();
        return normalized is "default" or "first" or "even" ? normalized : "default";
    }
}

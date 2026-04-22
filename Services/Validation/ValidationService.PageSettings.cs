using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Controllers;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private const decimal PageEndBlankLineHeightPoints = 18m;

    private sealed class NomorHalamanSectionRule
    {
        public bool? Continue { get; init; }
        public bool? DifferentFirstPage { get; init; }
        public bool? DifferentOddEven { get; init; }
        public bool? FirstPageIsEmpty { get; init; }
        public string? NumberFormat { get; init; }
    }

    private sealed class PageEndVisualRow
    {
        public int Page { get; init; }
        public uint? SectionId { get; init; }
        public string? PartType { get; init; }
        public string? Label { get; init; }
        public string? Text { get; init; }
        public string? ElementType { get; init; }
        public string? ElementJsonTree { get; init; }
        public decimal? X0 { get; init; }
        public decimal? Y0 { get; init; }
        public decimal? X1 { get; init; }
        public decimal? Y1 { get; init; }
    }

    public async Task<ValidationResult> ValidatePageSettingsAsync(int dokumenId, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        var target = await ResolveValidationTargetAsync(dokumenId, cancellationToken);
        if (!target.Exists)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Dokumen",
                Field = "dokumen_id",
                Message = "Dokumen tidak ditemukan"
            });
            return result;
        }

        // Get active aturan with details
        var aturan = await _db.Aturans
            .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (aturan == null)
            return result;

        var aturanDetails = await LoadCanonicalDetailsAsync(
            aturan.AturanId,
            cancellationToken,
            "page_settings",
            "nomor_halaman");

        PageSettingsRule? pageSettingsRule = null;
        NomorHalamanRule? nomorHalamanRule = null;

        foreach (var detail in aturanDetails.Values)
        {
            try
            {
                var key = (detail.AturanDetailKey ?? string.Empty).Trim().ToLowerInvariant();

                if (key == "nomor_halaman")
                {
                    nomorHalamanRule = JsonSerializer.Deserialize<NomorHalamanRule>(
                        detail.AturanDetailJsonValue ?? "{}",
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    continue;
                }

                if (key == "page_settings")
                {
                    pageSettingsRule = JsonSerializer.Deserialize<PageSettingsRule>(detail.AturanDetailJsonValue ?? "{}");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse aturan detail: {Key}", detail.AturanDetailKey);
            }
        }

        var effectivePageSettings = BuildEffectivePageSettings(pageSettingsRule);
        var effectiveColumnRule = effectivePageSettings.Column?.Value is > 0
            ? effectivePageSettings.Column
            : new RuleValue<int> { Value = 1 };
        var effectiveNomorHalamanRule = BuildEffectiveNomorHalamanRule(nomorHalamanRule);

        var (sectionRefType, sectionRefId) = ResolveSectionRefForValidation(dokumenId);

        // Get all sections for current validation target
        var sections = await _db.DokumenSections
            .Where(s => s.DsecRefTipe == sectionRefType && s.DsecRefId == sectionRefId)
            .OrderBy(s => s.DsecIndex)
            .ToListAsync(cancellationToken);

        if (sections.Count == 0)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "section",
                Message = "Tidak ada section yang ditemukan dalam dokumen"
            });
            return result;
        }

        var sectionPageMap = await LoadSectionPageMapAsync(sections, cancellationToken);
        var firstPhysicalPageSectionId = ResolveFirstPhysicalPageSectionId(sections, sectionPageMap);
        var renderedPageNumberStartsBySection = BuildRenderedPageNumberStartsBySection(sections, sectionPageMap);
        var pageNumberParagraphsBySection = await LoadPageNumberParagraphsBySectionAsync(
            sections,
            cancellationToken);

        _logger.LogInformation("Validating {Count} sections for ref {RefType}:{RefId}, logical ID: {DokumenId}, treated as bagian isi",
            sections.Count, sectionRefType, sectionRefId, dokumenId);

        // Validate each section
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var sectionType = DetermineSectionType();

            _logger.LogInformation("Section {Index}: Type={SectionType}, PageFormat={PageFormat}, " +
                "Size={Width}x{Height} twips, Orientation={Orientation}, " +
                "Margins: T={Top}, B={Bottom}, L={Left}, R={Right} twips, " +
                "Header={Header}, Footer={Footer} twips",
                i + 1, sectionType, section.DsecPageNumFormat,
                section.DsecPageWidthTwips, section.DsecPageHeightTwips, section.DsecOrientation,
                section.DsecMarginTopTwips, section.DsecMarginBottomTwips,
                section.DsecMarginLeftTwips, section.DsecMarginRightTwips,
                section.DsecHeaderMarginTwips, section.DsecFooterMarginTwips);

            ValidatePaperSize(result, section, effectivePageSettings.Paper, i + 1, sectionType, sectionPageMap);
            ValidateMargins(result, section, effectivePageSettings.Margin, i + 1, sectionType, sectionPageMap);
            ValidateHeaderFooter(result, section, effectivePageSettings.HeaderFooter!, i + 1, sectionType, sectionPageMap);
            ValidateGutter(result, section, effectivePageSettings.Gutter!, i + 1, sectionType, sectionPageMap);
            ValidateColumnCount(result, section, effectiveColumnRule, i + 1, sectionType, sectionPageMap);

            await ValidateNomorHalamanRuleAsync(
                result,
                section,
                effectiveNomorHalamanRule,
                pageNumberParagraphsBySection.TryGetValue(section.DsecId, out var sectionParagraphs)
                    ? sectionParagraphs
                    : Array.Empty<PageNumberParagraphInfo>(),
                sections,
                renderedPageNumberStartsBySection,
                section.DsecId == firstPhysicalPageSectionId,
                CanUseManualFirstPageFallback(section, sections, sectionPageMap),
                i + 1,
                sectionPageMap,
                cancellationToken);
        }

        await ValidatePageEndBlankLinesAsync(
            result,
            sections,
            effectivePageSettings.AkhirHalaman,
            sectionPageMap,
            sectionRefType,
            sectionRefId,
            cancellationToken);

        await ValidateEmptyPagesAsync(
            result,
            sections,
            effectivePageSettings.AkhirHalaman,
            sectionPageMap,
            sectionRefType,
            sectionRefId,
            cancellationToken);

        return result;
    }

    private static string DetermineSectionType()
    {
        return "isi";
    }

    private static PageSettingsRule BuildEffectivePageSettings(PageSettingsRule? rule)
    {
        var effectiveRule = rule ?? new PageSettingsRule();

        effectiveRule.Paper ??= new PagePaperRule();
        effectiveRule.Paper.Size ??= new RuleValue<string> { Value = "A4" };
        effectiveRule.Paper.Orientation ??= new RuleValue<string> { Value = "PORTRAIT" };

        effectiveRule.Margin ??= new MarginRule();
        effectiveRule.Margin.Top ??= new DecimalRuleValue { Value = 4m };
        effectiveRule.Margin.Bottom ??= new DecimalRuleValue { Value = 3m };
        effectiveRule.Margin.Left ??= new DecimalRuleValue { Value = 4m };
        effectiveRule.Margin.Right ??= new DecimalRuleValue { Value = 3m };

        effectiveRule.HeaderFooter ??= new HeaderFooterRule();
        effectiveRule.HeaderFooter.HeaderFromTop ??= new DecimalRuleValue { Value = 2.5m };
        effectiveRule.HeaderFooter.FooterFromBottom ??= new DecimalRuleValue { Value = 1.5m };

        effectiveRule.Gutter ??= new GutterRule();
        effectiveRule.Gutter.Size ??= new DecimalRuleValue { Value = 0m };
        effectiveRule.Gutter.Position ??= new RuleValue<string> { Value = "left" };

        effectiveRule.Column ??= new RuleValue<int> { Value = 1 };
        effectiveRule.AkhirHalaman ??= new PageEndRule();
        effectiveRule.AkhirHalaman.MaxBarisKosong ??= new DecimalRuleValue { Value = 3m };
        effectiveRule.AkhirHalaman.CegahHalamanKosong ??= new RuleValue<bool> { Value = true };
        return effectiveRule;
    }

    private void ValidatePaperSize(
        ValidationResult result,
        DokumenSection section,
        PagePaperRule? expectedPaper,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        if (expectedPaper?.Size?.Value == null && expectedPaper?.Orientation?.Value == null)
            return;

        var expected = new PaperSpec
        {
            Size = expectedPaper?.Size?.Value,
            Orientation = expectedPaper?.Orientation?.Value
        };
        result.IncrementTotalChecks();

        var actualSize = DetectPaperSize(section.DsecPageWidthTwips, section.DsecPageHeightTwips);
        var actualOrientation = section.DsecOrientation?.ToUpper() ?? "PORTRAIT";
        var actualDimensions = FormatPageDimensionsCm(section.DsecPageWidthTwips, section.DsecPageHeightTwips);
        var actualDescriptor = string.IsNullOrWhiteSpace(actualDimensions)
            ? $"{actualSize} {actualOrientation}"
            : $"{actualSize} {actualOrientation} ({actualDimensions})";

        var expectedSize = expected.Size?.Trim().ToUpperInvariant();
        var expectedOrientation = (expected.Orientation ?? "PORTRAIT").Trim().ToUpperInvariant();
        var sizeMismatch = expectedSize != actualSize;
        var orientationMismatch = expectedOrientation != actualOrientation;
        var isValid = !sizeMismatch && !orientationMismatch;

        if (isValid)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "paper",
                Message = $"Ukuran kertas section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = FormatPaperSpec(expected),
                Actual = actualDescriptor,
                IsHardConstraint =
                    (sizeMismatch && expectedPaper?.Size?.IsHardConstraint == true) ||
                    (orientationMismatch && expectedPaper?.Orientation?.IsHardConstraint == true),
                SectionIndex = sectionNumber,
                Locations = BuildPageSettingsLocations("paper", section, sectionPageMap)
            });
        }
    }

    private static string? FormatPaperSpec(PaperSpec spec)
    {
        if (spec == null || string.IsNullOrWhiteSpace(spec.Size))
            return null;

        var size = spec.Size!.Trim().ToUpperInvariant();
        var orientation = (spec.Orientation ?? "PORTRAIT").Trim().ToUpperInvariant();

        if (PaperSizes.TryGetValue(size, out var dimensions))
        {
            var width = dimensions.Width;
            var height = dimensions.Height;
            if (orientation == "LANDSCAPE")
            {
                (width, height) = (height, width);
            }

            var dim = FormatPageDimensionsCm(width, height);
            if (!string.IsNullOrWhiteSpace(dim))
                return $"{size} {orientation} ({dim})";
        }

        return $"{size} {orientation}";
    }

    private static string? FormatPageDimensionsCm(uint? widthTwips, uint? heightTwips)
    {
        if (!widthTwips.HasValue || !heightTwips.HasValue)
            return null;

        var widthCm = Math.Round(widthTwips.Value / TwipsPerCm, 2);
        var heightCm = Math.Round(heightTwips.Value / TwipsPerCm, 2);
        var widthText = widthCm.ToString(CultureInfo.InvariantCulture);
        var heightText = heightCm.ToString(CultureInfo.InvariantCulture);
        return $"{widthText} x {heightText} cm";
    }

    private void ValidateMargins(
        ValidationResult result,
        DokumenSection section,
        MarginRule? margins,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        if (margins == null)
            return;

        // Validate each margin
        ValidateSingleMargin(result, "top", section, section.DsecMarginTopTwips, margins.Top?.Value, margins.Top?.IsHardConstraint == true, sectionNumber, sectionType, sectionPageMap);
        ValidateSingleMargin(result, "bottom", section, section.DsecMarginBottomTwips, margins.Bottom?.Value, margins.Bottom?.IsHardConstraint == true, sectionNumber, sectionType, sectionPageMap);
        ValidateSingleMargin(result, "left", section, section.DsecMarginLeftTwips, margins.Left?.Value, margins.Left?.IsHardConstraint == true, sectionNumber, sectionType, sectionPageMap);
        ValidateSingleMargin(result, "right", section, section.DsecMarginRightTwips, margins.Right?.Value, margins.Right?.IsHardConstraint == true, sectionNumber, sectionType, sectionPageMap);
    }

    private void ValidateSingleMargin(
        ValidationResult result,
        string marginName,
        DokumenSection section,
        long? actualTwips,
        decimal? expectedCm,
        bool isHardConstraint,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        if (!expectedCm.HasValue)
            return;

        result.IncrementTotalChecks(isHardConstraint);

        var expectedTwips = expectedCm.Value * TwipsPerCm;
        var actualCm = actualTwips.HasValue ? actualTwips.Value / TwipsPerCm : 0;

        if (actualTwips.HasValue && Math.Abs(actualTwips.Value - expectedTwips) <= TwipsTolerance * 2)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = $"margin_{marginName}",
                Message = $"Margin {marginName} section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = $"{expectedCm.Value} cm",
                Actual = $"{actualCm:F2} cm",
                SectionIndex = sectionNumber,
                Locations = BuildPageSettingsLocations($"margin_{marginName}", section, sectionPageMap, expectedCm)
            });
        }
    }

    private void ValidateSingleMargin(
        ValidationResult result,
        string marginName,
        DokumenSection section,
        uint? actualTwips,
        decimal? expectedCm,
        bool isHardConstraint,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        ValidateSingleMargin(
            result,
            marginName,
            section,
            actualTwips.HasValue ? (long?)actualTwips.Value : null,
            expectedCm,
            isHardConstraint,
            sectionNumber,
            sectionType,
            sectionPageMap);
    }

    private void ValidateHeaderFooter(
        ValidationResult result,
        DokumenSection section,
        HeaderFooterRule rule,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        // Validate header distance from top
        var expectedHeaderCm = rule.HeaderFromTop?.Value;
        if (expectedHeaderCm.HasValue)
        {
            result.IncrementTotalChecks(rule.HeaderFromTop?.IsHardConstraint == true);
            var expectedHeaderTwips = expectedHeaderCm.Value * TwipsPerCm;
            var actualHeaderCm = section.DsecHeaderMarginTwips.HasValue
                ? section.DsecHeaderMarginTwips.Value / TwipsPerCm
                : 0;

            if (section.DsecHeaderMarginTwips.HasValue &&
                Math.Abs(section.DsecHeaderMarginTwips.Value - expectedHeaderTwips) <= TwipsTolerance * 2)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "header_from_top",
                    Message = $"Jarak header dari atas section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = $"{expectedHeaderCm.Value} cm",
                    Actual = $"{actualHeaderCm:F2} cm",
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("header_from_top", section, sectionPageMap, expectedHeaderCm)
                });
            }
        }

        // Validate footer distance from bottom
        var expectedFooterCm = rule.FooterFromBottom?.Value;
        if (expectedFooterCm.HasValue)
        {
            result.IncrementTotalChecks(rule.FooterFromBottom?.IsHardConstraint == true);
            var expectedFooterTwips = expectedFooterCm.Value * TwipsPerCm;
            var actualFooterCm = section.DsecFooterMarginTwips.HasValue
                ? section.DsecFooterMarginTwips.Value / TwipsPerCm
                : 0;

            if (section.DsecFooterMarginTwips.HasValue &&
                Math.Abs(section.DsecFooterMarginTwips.Value - expectedFooterTwips) <= TwipsTolerance * 2)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "footer_from_bottom",
                    Message = $"Jarak footer dari bawah section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = $"{expectedFooterCm.Value} cm",
                    Actual = $"{actualFooterCm:F2} cm",
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("footer_from_bottom", section, sectionPageMap, expectedFooterCm)
                });
            }
        }
    }

    private void ValidateDifferentOddEven(
        ValidationResult result,
        DokumenSection section,
        bool? expectedDifferentOddEvenValue,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        var expectedDifferentOddEven = expectedDifferentOddEvenValue ?? false;
        result.IncrementTotalChecks();

        if (section.DsecDifferentOddEven == expectedDifferentOddEven)
        {
            result.IncrementPassedChecks();
            return;
        }

        result.Errors.Add(new ValidationError
        {
                Category = "Pengaturan Halaman",
                Field = "different_odd_even",
                Message = $"Pengaturan nomor halaman ganjil-genap section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = expectedDifferentOddEven ? "1" : "0",
                Actual = section.DsecDifferentOddEven ? "1" : "0",
                SectionIndex = sectionNumber,
            Locations = BuildPageSettingsLocations("different_odd_even", section, sectionPageMap)
        });
    }

    private void ValidateGutter(
        ValidationResult result,
        DokumenSection section,
        GutterRule rule,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        // Validate gutter size
        result.IncrementTotalChecks(rule.Size?.IsHardConstraint == true);
        var expectedGutterCm = rule.Size?.Value ?? 0m;
        var expectedGutterTwips = expectedGutterCm * TwipsPerCm;
        var actualGutterCm = section.DsecGutterTwips.HasValue 
            ? section.DsecGutterTwips.Value / TwipsPerCm 
            : 0;

        if (Math.Abs((section.DsecGutterTwips ?? 0) - expectedGutterTwips) <= TwipsTolerance * 2)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "gutter",
                Message = $"Gutter section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = $"{expectedGutterCm} cm",
                Actual = $"{actualGutterCm:F2} cm",
                SectionIndex = sectionNumber,
                Locations = BuildPageSettingsLocations("gutter", section, sectionPageMap)
            });
        }

        if (expectedGutterCm <= 0)
        {
            return;
        }

        // Validate gutter position if specified
        var expectedPositionValue = rule.Position?.Value;
        if (!string.IsNullOrEmpty(expectedPositionValue))
        {
            result.IncrementTotalChecks(rule.Position?.IsHardConstraint == true);
            var actualPosition = section.DsecGutterPosition?.ToLower() ?? "left";
            var expectedPosition = expectedPositionValue.ToLowerInvariant();

            if (actualPosition == expectedPosition)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "gutter_position",
                    Message = $"Posisi gutter section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = expectedPosition,
                    Actual = actualPosition,
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("gutter_position", section, sectionPageMap)
                });
            }
        }
    }

    private void ValidateColumnCount(
        ValidationResult result,
        DokumenSection section,
        RuleValue<int> rule,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        result.IncrementTotalChecks(rule.IsHardConstraint);
        var actualColumns = (int)(section.DsecColumnCount ?? 1);
        var expectedColumns = rule.Value <= 0 ? 1 : rule.Value;

        if (actualColumns == expectedColumns)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "column_count",
                Message = $"Jumlah kolom section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = $"{expectedColumns} kolom",
                Actual = $"{actualColumns} kolom",
                SectionIndex = sectionNumber,
                Locations = BuildPageSettingsLocations("column_count", section, sectionPageMap)
            });
        }
    }

    private async Task ValidatePageEndBlankLinesAsync(
        ValidationResult result,
        IReadOnlyList<DokumenSection> sections,
        PageEndRule? rule,
        Dictionary<uint, int> sectionPageMap,
        string sectionRefType,
        uint sectionRefId,
        CancellationToken cancellationToken)
    {
        var expectedMaxBlankLines = Math.Max(0m, rule?.MaxBarisKosong?.Value ?? 3m);
        var preventEmptyPages = rule?.CegahHalamanKosong?.Value == true;
        var visualRows = await LoadPageEndVisualRowsAsync(sectionRefType, sectionRefId, cancellationToken);
        var previewLastPage = await ResolveValidationTargetLastPageAsync(sectionRefType, sectionRefId, cancellationToken);
        var visualLastPage = visualRows.Count > 0 ? visualRows.Max(row => row.Page) : 0;
        var lastPage = Math.Max(visualLastPage, previewLastPage ?? 0);
        if (lastPage <= 1)
            return;

        var sectionNumbersById = sections
            .Select((section, index) => new { section.DsecId, Number = index + 1 })
            .ToDictionary(item => item.DsecId, item => item.Number);
        var pageGroups = visualRows
            .Where(row => row.Page > 0)
            .GroupBy(row => row.Page)
            .OrderBy(group => group.Key)
            .ToList();

        foreach (var pageGroup in pageGroups.Where(group => group.Key < lastPage))
        {
            var section = ResolveSectionForPage(sections, sectionPageMap, pageGroup.Key, pageGroup.Select(row => row.SectionId));
            if (section == null)
                continue;

            if (!TryBuildTextAreaBoundsPoints(
                    section,
                    out var textAreaLeft,
                    out var textAreaTop,
                    out var textAreaRight,
                    out var textAreaBottom,
                    out var pageWidth,
                    out var pageHeight))
            {
                continue;
            }

            var contentBottomBoundary = ResolvePageEndBottomBoundary(pageGroup, textAreaBottom, pageHeight);
            if (contentBottomBoundary <= textAreaTop)
                contentBottomBoundary = textAreaBottom;

            result.IncrementTotalChecks(rule?.MaxBarisKosong?.IsHardConstraint == true);

            var contentRows = pageGroup
                .Where(row => HasMeaningfulBodyContent(row) && row.Y1.HasValue)
                .OrderByDescending(row => row.Y1)
                .ThenByDescending(row => row.X1)
                .ToList();

            if (preventEmptyPages && contentRows.Count == 0)
            {
                result.IncrementPassedChecks();
                continue;
            }

            ErrorBbox? bbox;
            int detectedBlankLines;

            if (contentRows.Count == 0)
            {
                detectedBlankLines = EstimatePageEndBlankLines(Math.Max(0m, contentBottomBoundary - textAreaTop));
                bbox = CreateBbox(
                    textAreaLeft,
                    textAreaTop,
                    textAreaRight,
                    contentBottomBoundary,
                    pageWidth,
                    pageHeight);
            }
            else
            {
                var lastContentRow = contentRows[0];
                var lastElementBottom = ClampToRange(lastContentRow.Y1!.Value, 0m, pageHeight);
                detectedBlankLines = EstimatePageEndBlankLines(Math.Max(0m, contentBottomBoundary - lastElementBottom));
                bbox = CreateBbox(
                    textAreaLeft,
                    lastElementBottom,
                    textAreaRight,
                    contentBottomBoundary,
                    pageWidth,
                    pageHeight);
            }

            if (detectedBlankLines <= expectedMaxBlankLines)
            {
                result.IncrementPassedChecks();
                continue;
            }

            sectionNumbersById.TryGetValue(section.DsecId, out var sectionNumber);
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "max_baris_kosong_akhir_halaman",
                Message = $"Sisa ruang kosong di akhir halaman {pageGroup.Key} melebihi batas, maksimal {FormatBlankLineCount(expectedMaxBlankLines)} baris, terdeteksi sekitar {detectedBlankLines} baris",
                Expected = $"Maksimal {FormatBlankLineCount(expectedMaxBlankLines)} baris kosong",
                Actual = $"Sekitar {detectedBlankLines} baris kosong",
                SectionIndex = sectionNumber > 0 ? sectionNumber : null,
                Locations =
                [
                    new ErrorLocation
                    {
                        HalamanKe = pageGroup.Key,
                        Bbox = bbox
                    }
                ]
            });
        }
    }

    private static decimal ResolvePageEndBottomBoundary(
        IGrouping<int, PageEndVisualRow> pageGroup,
        decimal textAreaBottom,
        decimal pageHeight)
    {
        var boundary = textAreaBottom;

        var footnoteTop = pageGroup
            .Where(IsFootnotePageEndVisual)
            .Select(row => row.Y0)
            .Where(y => y.HasValue)
            .Select(y => ClampToRange(y!.Value, 0m, pageHeight))
            .DefaultIfEmpty(boundary)
            .Min();

        return Math.Min(boundary, footnoteTop);
    }

    private async Task<List<PageEndVisualRow>> LoadPageEndVisualRowsAsync(
        string sectionRefType,
        uint sectionRefId,
        CancellationToken cancellationToken)
    {
        return await (
            from visual in _db.DokumenElemenVisuals
            where visual.DevPage.HasValue &&
                  visual.DevRefId == sectionRefId &&
                  (visual.DevRefTipe == sectionRefType ||
                   visual.DevRefTipe == null ||
                   visual.DevRefTipe == string.Empty)
            join element in _db.DokumenElemens on visual.DokumenElemenId equals (ulong?)element.DelemenId into elementJoin
            from element in elementJoin.DefaultIfEmpty()
            join part in _db.DokumenParts on element.DpartId equals (uint?)part.DpartId into partJoin
            from part in partJoin.DefaultIfEmpty()
            select new PageEndVisualRow
            {
                Page = (int)visual.DevPage!.Value,
                SectionId = part != null ? part.DsecId : null,
                PartType = part != null ? part.DpartType : null,
                Label = visual.DevLabelStruktural ?? visual.DevLabel,
                Text = visual.DevText,
                ElementType = element != null ? element.DelemenType : null,
                ElementJsonTree = element != null ? element.DelemenJsonTree : null,
                X0 = visual.DevBboxX0.HasValue ? (decimal?)visual.DevBboxX0.Value : null,
                Y0 = visual.DevBboxY0.HasValue ? (decimal?)visual.DevBboxY0.Value : null,
                X1 = visual.DevBboxX1.HasValue ? (decimal?)visual.DevBboxX1.Value : null,
                Y1 = visual.DevBboxY1.HasValue ? (decimal?)visual.DevBboxY1.Value : null
            }).ToListAsync(cancellationToken);
    }

    private async Task ValidateEmptyPagesAsync(
        ValidationResult result,
        IReadOnlyList<DokumenSection> sections,
        PageEndRule? rule,
        Dictionary<uint, int> sectionPageMap,
        string sectionRefType,
        uint sectionRefId,
        CancellationToken cancellationToken)
    {
        var preventEmptyPages = rule?.CegahHalamanKosong?.Value ?? false;
        if (!preventEmptyPages)
            return;

        var visualRows = await LoadPageEndVisualRowsAsync(sectionRefType, sectionRefId, cancellationToken);
        var previewLastPage = await ResolveValidationTargetLastPageAsync(sectionRefType, sectionRefId, cancellationToken);
        var visualLastPage = visualRows.Count > 0 ? visualRows.Max(row => row.Page) : 0;
        var lastPage = Math.Max(visualLastPage, previewLastPage ?? 0);
        if (lastPage <= 0)
            return;

        var sectionNumbersById = sections
            .Select((section, index) => new { section.DsecId, Number = index + 1 })
            .ToDictionary(item => item.DsecId, item => item.Number);

        for (var page = 1; page <= lastPage; page++)
        {
            var pageRows = visualRows
                .Where(row => row.Page == page)
                .ToList();
            var section = ResolveSectionForPage(sections, sectionPageMap, page, pageRows.Select(row => row.SectionId));
            if (section == null || !TryGetPageBoundsPoints(section, out var pageWidth, out var pageHeight))
                continue;

            result.IncrementTotalChecks(rule?.CegahHalamanKosong?.IsHardConstraint == true);

            if (pageRows.Any(HasMeaningfulBodyContent))
            {
                result.IncrementPassedChecks();
                continue;
            }

            sectionNumbersById.TryGetValue(section.DsecId, out var sectionNumber);
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "cegah_halaman_kosong",
                Message = $"Halaman {page} kosong, tidak ada isi body selain whitespace",
                Expected = "Setiap halaman harus memiliki isi body",
                Actual = "Halaman hanya berisi whitespace atau header/footer",
                SectionIndex = sectionNumber > 0 ? sectionNumber : null,
                Locations =
                [
                    new ErrorLocation
                    {
                        HalamanKe = page,
                        Bbox = CreateBbox(0m, 0m, pageWidth, pageHeight, pageWidth, pageHeight)
                    }
                ]
            });
        }
    }

    private async Task<int?> ResolveValidationTargetLastPageAsync(
        string sectionRefType,
        uint sectionRefId,
        CancellationToken cancellationToken)
    {
        var pages = await LoadValidationTargetPreviewPagesAsync(sectionRefType, sectionRefId, cancellationToken);
        return pages.Count > 0 ? pages.Max() : null;
    }

    private async Task<List<int>> LoadValidationTargetPreviewPagesAsync(
        string sectionRefType,
        uint sectionRefId,
        CancellationToken cancellationToken)
    {
        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);

        if (string.Equals(sectionRefType, "dokumen", StringComparison.OrdinalIgnoreCase))
        {
            var dokumen = await _db.Dokumens
                .AsNoTracking()
                .Where(item => item.DokumenId == (int)sectionRefId)
                .Select(item => new
                {
                    item.DokumenId,
                    item.MhsNrp,
                    item.DokumenImagesPath
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (dokumen == null)
                return [];

            var candidateDirs = BuildValidationDokumenImageDirectories(
                storagePath,
                fullStoragePath,
                dokumen.DokumenImagesPath,
                dokumen.MhsNrp,
                dokumen.DokumenId);
            return PreviewImageHelper.EnumerateAvailablePages(candidateDirs);
        }

        if (string.Equals(sectionRefType, "bab", StringComparison.OrdinalIgnoreCase))
        {
            var bab = await _db.Babs
                .AsNoTracking()
                .Where(item => item.BabId == sectionRefId)
                .Join(
                    _db.Bukus.AsNoTracking(),
                    item => item.BukuId,
                    buku => (uint)buku.BukuId,
                    (item, buku) => new
                    {
                        item.BabImagesPath,
                        item.BukuId,
                        item.BabOrder,
                        buku.MhsNrp
                    })
                .FirstOrDefaultAsync(cancellationToken);

            if (bab == null)
                return [];

            var candidateDirs = BuildValidationBabImageDirectories(
                storagePath,
                fullStoragePath,
                bab.BabImagesPath,
                bab.MhsNrp,
                bab.BukuId,
                bab.BabOrder);
            return PreviewImageHelper.EnumerateAvailablePages(candidateDirs);
        }

        return [];
    }

    private static IReadOnlyList<string> BuildValidationDokumenImageDirectories(
        string storagePath,
        string fullStoragePath,
        string? configuredImagesPath,
        string nrp,
        int dokumenId)
    {
        var candidateDirs = new List<string?>();
        var configuredImagesDir = PreviewImageHelper.ResolveConfiguredImagesDirectory(
            storagePath,
            fullStoragePath,
            configuredImagesPath);
        if (!string.IsNullOrWhiteSpace(configuredImagesDir))
            candidateDirs.Add(configuredImagesDir);

        candidateDirs.Add(Path.Combine(storagePath, "dokumen", nrp, dokumenId.ToString(), "images"));
        return PreviewImageHelper.BuildSafeCandidateDirectories(fullStoragePath, candidateDirs);
    }

    private static IReadOnlyList<string> BuildValidationBabImageDirectories(
        string storagePath,
        string fullStoragePath,
        string? configuredImagesPath,
        string nrp,
        uint bukuId,
        byte? babOrder)
    {
        var candidateDirs = new List<string?>();
        var configuredImagesDir = PreviewImageHelper.ResolveConfiguredImagesDirectory(
            storagePath,
            fullStoragePath,
            configuredImagesPath);
        if (!string.IsNullOrWhiteSpace(configuredImagesDir))
            candidateDirs.Add(configuredImagesDir);

        var baseImagesDir = Path.Combine(storagePath, "buku", nrp, bukuId.ToString(), "images");
        if (babOrder.HasValue)
            candidateDirs.Add(Path.Combine(baseImagesDir, babOrder.Value.ToString()));

        candidateDirs.Add(baseImagesDir);
        return PreviewImageHelper.BuildSafeCandidateDirectories(fullStoragePath, candidateDirs);
    }

    private static bool IsExcludedPageEndVisual(PageEndVisualRow row)
    {
        var partType = (row.PartType ?? string.Empty).Trim().ToLowerInvariant();
        if (partType is "header" or "footer")
            return true;

        var normalizedLabel = NormalizeLabel(row.Label);
        return normalizedLabel is "page_header" or "page_footer" or "header" or "footer";
    }

    private static bool IsFootnotePageEndVisual(PageEndVisualRow row)
    {
        var normalizedLabel = NormalizeLabel(row.Label);
        return normalizedLabel.StartsWith("footnote", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMeaningfulBodyContent(PageEndVisualRow row)
    {
        var partType = (row.PartType ?? string.Empty).Trim().ToLowerInvariant();
        if (partType != "body" || IsExcludedPageEndVisual(row))
            return false;

        if (!IsParagraphElement(row.ElementType))
            return true;

        var content = ParseElementContent(row.ElementJsonTree);
        return !IsEmptyElement(content);
    }

    private static bool HasVisiblePageBodyContent(PageEndVisualRow row)
    {
        if (IsExcludedPageEndVisual(row))
            return false;

        if (!string.IsNullOrWhiteSpace(row.Text))
            return true;

        var normalizedLabel = NormalizeLabel(row.Label);
        return normalizedLabel is
            "gambar" or
            "caption_gambar" or
            "tabel" or
            "caption_tabel" or
            "kode" or
            "judul_kode" or
            "rumus" or
            "formula";
    }

    private DokumenSection? ResolveSectionForPage(
        IReadOnlyList<DokumenSection> sections,
        Dictionary<uint, int> sectionPageMap,
        int page,
        IEnumerable<uint?> candidateSectionIds)
    {
        foreach (var sectionId in candidateSectionIds
                     .Where(id => id.HasValue)
                     .Select(id => id!.Value)
                     .Distinct())
        {
            var explicitSection = sections.FirstOrDefault(section => section.DsecId == sectionId);
            if (explicitSection != null)
                return explicitSection;
        }

        var mappedSection = sections
            .Where(section => sectionPageMap.TryGetValue(section.DsecId, out var startPage) && startPage <= page)
            .OrderByDescending(section => sectionPageMap[section.DsecId])
            .ThenByDescending(section => section.DsecIndex)
            .FirstOrDefault();
        if (mappedSection != null)
            return mappedSection;

        return sections
            .OrderBy(section => section.DsecIndex)
            .FirstOrDefault();
    }

    private bool TryBuildTextAreaBoundsPoints(
        DokumenSection section,
        out decimal left,
        out decimal top,
        out decimal right,
        out decimal bottom,
        out decimal pageWidth,
        out decimal pageHeight)
    {
        left = 0m;
        top = 0m;
        right = 0m;
        bottom = 0m;
        pageWidth = 0m;
        pageHeight = 0m;

        if (!TryGetPageBoundsPoints(section, out pageWidth, out pageHeight))
            return false;

        var leftMargin = TwipsToPoints(section.DsecMarginLeftTwips) ?? 0m;
        var rightMargin = TwipsToPoints(section.DsecMarginRightTwips) ?? 0m;
        var topMargin = TwipsToPoints(section.DsecMarginTopTwips) ?? 0m;
        var bottomMargin = TwipsToPoints(section.DsecMarginBottomTwips) ?? 0m;

        left = ClampToRange(leftMargin, 0m, pageWidth);
        right = ClampToRange(pageWidth - rightMargin, 0m, pageWidth);
        top = ClampToRange(topMargin, 0m, pageHeight);
        bottom = ClampToRange(pageHeight - bottomMargin, 0m, pageHeight);

        return right > left && bottom > top;
    }

    private static int EstimatePageEndBlankLines(decimal remainingPoints)
    {
        if (remainingPoints <= 0m)
            return 0;

        return (int)Math.Floor((remainingPoints + 0.5m) / PageEndBlankLineHeightPoints);
    }

    private static string FormatBlankLineCount(decimal value)
    {
        if (decimal.Truncate(value) == value)
            return value.ToString("0", CultureInfo.InvariantCulture);

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private PageNumberingSpec? GetExpectedPageNumbering(PageNumberingSectionRules rules, string sectionType)
    {
        return sectionType.ToLower() switch
        {
            "awal" => rules.Awal,
            "isi" => rules.Isi,
            "akhir" => rules.Akhir,
            "lampiran" => rules.Lampiran,
            _ => rules.Isi
        };
    }

    private void ValidatePageNumbering(
        ValidationResult result,
        DokumenSection section,
        PageNumberingSpec? expected,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        if (expected == null)
            return;

        // Validate format
        if (!string.IsNullOrEmpty(expected.Format))
        {
            result.IncrementTotalChecks();
            var actualFormat = section.DsecPageNumFormat?.ToLower() ?? "decimal";
            var expectedFormat = expected.Format.ToLower();

            if (actualFormat == expectedFormat)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "page_number_format",
                    Message = $"Format nomor halaman section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = expectedFormat,
                    Actual = actualFormat,
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("page_number_format", section, sectionPageMap)
                });
            }
        }

        // Validate start number if specified
        if (expected.Start.HasValue)
        {
            result.IncrementTotalChecks();
            var actualStart = (int)(section.DsecPageNumStart ?? 0);

            if (actualStart == expected.Start.Value || (expected.Start.Value == 1 && actualStart == 0))
            {
                // 0 is treated as "continue from previous" which for first section means 1
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "page_number_start",
                    Message = $"Nomor halaman awal section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = $"{expected.Start.Value}",
                    Actual = actualStart == 0 ? "lanjutan" : $"{actualStart}",
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("page_number_start", section, sectionPageMap)
                });
            }
        }
    }

    private void ValidateNomorHalamanRule(
        ValidationResult result,
        DokumenSection section,
        NomorHalamanSectionRule rule,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        if (!string.IsNullOrWhiteSpace(rule.NumberFormat))
        {
            result.IncrementTotalChecks();
            var expectedFormat = rule.NumberFormat!.Trim().ToLowerInvariant();
            var actualFormat = (section.DsecPageNumFormat ?? "decimal").Trim().ToLowerInvariant();

            if (actualFormat == expectedFormat)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "page_number_format",
                    Message = $"Format nomor halaman section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = expectedFormat,
                    Actual = actualFormat,
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("page_number_format", section, sectionPageMap)
                });
            }
        }

        if (rule.Continue.HasValue)
        {
            result.IncrementTotalChecks();
            var expectedContinue = rule.Continue.Value;
            var hasExplicitRestart = section.DsecPageNumStart.HasValue && section.DsecPageNumStart.Value > 0;
            var actualContinue = !hasExplicitRestart;

            if (actualContinue == expectedContinue)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "page_number_continue",
                    Message = $"Pengaturan lanjutan nomor halaman section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = expectedContinue ? "continue" : "restart",
                    Actual = actualContinue ? "continue" : "restart",
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("page_number_start", section, sectionPageMap)
                });
            }
        }

        if (rule.DifferentFirstPage.HasValue)
        {
            result.IncrementTotalChecks();
            var expectedDifferentFirstPage = rule.DifferentFirstPage.Value;
            var actualDifferentFirstPage = section.DsecHasTitlePage;

            if (actualDifferentFirstPage == expectedDifferentFirstPage)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "different_first_page",
                    Message = $"Pengaturan different first page section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                    Expected = expectedDifferentFirstPage ? "true" : "false",
                    Actual = actualDifferentFirstPage ? "true" : "false",
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("page_numbering", section, sectionPageMap)
                });
            }
        }

        // With current extraction data we cannot validate first-page emptiness directly.
        // We use title-page flag as best-effort proxy; manual numbering does not satisfy this rule.
        if (rule.FirstPageIsEmpty == true)
        {
            result.IncrementTotalChecks();
            if (section.DsecHasTitlePage)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Pengaturan Halaman",
                    Field = "first_page_is_empty",
                    Message = $"Halaman pertama section {sectionNumber} (bagian {sectionType}) seharusnya dipisahkan untuk nomor halaman",
                    Expected = "true",
                    Actual = "false",
                    SectionIndex = sectionNumber,
                    Locations = BuildPageSettingsLocations("page_numbering", section, sectionPageMap)
                });
            }
        }
    }

    private static NomorHalamanSectionRule ParseNomorHalamanSectionRule(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new NomorHalamanSectionRule();

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            return new NomorHalamanSectionRule
            {
                Continue = ReadFlexibleBoolProperty(root, "continue"),
                DifferentFirstPage = ReadFlexibleBoolProperty(root, "different_first_page"),
                DifferentOddEven = ReadFlexibleBoolProperty(root, "different_odd_even"),
                FirstPageIsEmpty = ReadNestedFlexibleBool(root, "first_page", "is_empty"),
                NumberFormat = NormalizeNomorHalamanFormat(ExtractNomorHalamanFormat(root))
            };
        }
        catch (JsonException)
        {
            return new NomorHalamanSectionRule();
        }
    }

    private static string? ExtractNomorHalamanFormat(JsonElement root)
    {
        // 1) Root-level number_format.type
        if (TryGetPropertyIgnoreCase(root, "number_format", out var numberFormat))
        {
            var type = TryGetNumberFormatType(numberFormat);
            if (!string.IsNullOrWhiteSpace(type))
                return type;
        }

        // 2) first_page.number_format.type
        if (TryGetPropertyIgnoreCase(root, "first_page", out var firstPage) &&
            TryGetPropertyIgnoreCase(firstPage, "number_format", out var firstPageNumberFormat))
        {
            var type = TryGetNumberFormatType(firstPageNumberFormat);
            if (!string.IsNullOrWhiteSpace(type))
                return type;
        }

        // 3) default_page.number_format.type
        if (TryGetPropertyIgnoreCase(root, "default_page", out var defaultPage) &&
            TryGetPropertyIgnoreCase(defaultPage, "number_format", out var defaultPageNumberFormat))
        {
            var type = TryGetNumberFormatType(defaultPageNumberFormat);
            if (!string.IsNullOrWhiteSpace(type))
                return type;
        }

        return null;
    }

    private static string? TryGetNumberFormatType(JsonElement numberFormat)
    {
        if (numberFormat.ValueKind == JsonValueKind.Object &&
            TryGetPropertyIgnoreCase(numberFormat, "type", out var typeElement))
        {
            return ReadFlexibleString(typeElement);
        }

        return ReadFlexibleString(numberFormat);
    }

    private static bool? ReadNestedFlexibleBool(JsonElement root, string parentName, string childName)
    {
        if (!TryGetPropertyIgnoreCase(root, parentName, out var parent))
            return null;

        return ReadFlexibleBoolProperty(parent, childName);
    }

    private static bool? ReadFlexibleBoolProperty(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            return null;

        return ReadFlexibleBool(value);
    }

    private static bool? ReadFlexibleBool(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (value.TryGetInt32(out var number))
                    return number != 0;
                break;
            case JsonValueKind.String:
            {
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                if (bool.TryParse(raw, out var boolValue))
                    return boolValue;

                if (int.TryParse(raw, out var intValue))
                    return intValue != 0;
                break;
            }
            case JsonValueKind.Object:
                if (TryGetPropertyIgnoreCase(value, "value", out var nested))
                    return ReadFlexibleBool(nested);
                break;
        }

        return null;
    }

    private static string? ReadFlexibleString(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
            {
                var raw = value.GetString();
                return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            }
            case JsonValueKind.Number:
                return value.GetRawText();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return value.GetBoolean().ToString().ToLowerInvariant();
            case JsonValueKind.Object:
                if (TryGetPropertyIgnoreCase(value, "value", out var nested))
                    return ReadFlexibleString(nested);
                break;
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? NormalizeNomorHalamanFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "arab" => "decimal",
            "arabic" => "decimal",
            "desimal" => "decimal",
            "decimal" => "decimal",
            "roman" => "lowerroman",
            "romawi" => "lowerroman",
            "lower_roman" => "lowerroman",
            "upper_roman" => "upperroman",
            "lower_letter" => "lowerletter",
            "upper_letter" => "upperletter",
            "none" => null,
            _ => normalized
        };
    }

    private async Task<Dictionary<uint, int>> LoadSectionPageMapAsync(
        IReadOnlyList<DokumenSection> sections,
        CancellationToken cancellationToken)
    {
        var sectionPageMap = new Dictionary<uint, int>();
        if (sections == null || sections.Count == 0)
            return sectionPageMap;

        var sectionIds = sections
            .Select(s => s.DsecId)
            .Distinct()
            .ToList();

        var bodyParts = await _db.DokumenParts
            .Where(p => sectionIds.Contains(p.DsecId) && p.DpartType == "body")
            .Select(p => new { p.DpartId, p.DsecId })
            .ToListAsync(cancellationToken);

        if (bodyParts.Count == 0)
            return sectionPageMap;

        var partIds = bodyParts
            .Select(p => p.DpartId)
            .Distinct()
            .ToList();

        var elements = await _db.DokumenElemens
            .Where(e => e.DpartId.HasValue && partIds.Contains(e.DpartId.Value))
            .Select(e => new
            {
                DpartId = e.DpartId!.Value,
                e.DelemenId,
                e.DelemenType,
                e.DelemenSequence
            })
            .ToListAsync(cancellationToken);

        if (elements.Count == 0)
            return sectionPageMap;

        var candidatesByPart = elements
            .GroupBy(e => e.DpartId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(e => string.Equals(e.DelemenType, "paragraph", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(e => e.DelemenSequence ?? uint.MaxValue)
                    .ThenBy(e => e.DelemenId)
                    .Select(e => e.DelemenId)
                    .ToList());

        var candidateIds = candidatesByPart
            .Values
            .SelectMany(ids => ids)
            .Distinct()
            .ToList();

        var pageByElement = await LoadPageNumbersAsync(candidateIds, cancellationToken);

        foreach (var bodyPart in bodyParts.OrderBy(p => p.DpartId))
        {
            if (sectionPageMap.ContainsKey(bodyPart.DsecId))
                continue;

            if (!candidatesByPart.TryGetValue(bodyPart.DpartId, out var candidates) || candidates.Count == 0)
                continue;

            foreach (var candidateId in candidates)
            {
                if (pageByElement.TryGetValue(candidateId, out var page) && page > 0)
                {
                    sectionPageMap[bodyPart.DsecId] = page;
                    break;
                }
            }
        }

        // Fallback: single-section documents should still point to page 1
        // when no anchor page could be resolved from visual data.
        if (sections.Count == 1)
        {
            var onlySectionId = sections[0].DsecId;
            if (!sectionPageMap.ContainsKey(onlySectionId))
                sectionPageMap[onlySectionId] = 1;
        }

        return sectionPageMap;
    }

    private List<ErrorLocation> BuildPageSettingsLocations(
        string field,
        DokumenSection section,
        Dictionary<uint, int> sectionPageMap,
        decimal? expectedDistanceCm = null)
    {
        if (!sectionPageMap.TryGetValue(section.DsecId, out var page) || page <= 0)
            return new List<ErrorLocation>();

        var bbox = BuildPageSettingsBbox(field, section, expectedDistanceCm);
        return new List<ErrorLocation>
        {
            new()
            {
                HalamanKe = page,
                Bbox = bbox
            }
        };
    }

    private ErrorBbox? BuildPageSettingsBbox(string field, DokumenSection section, decimal? expectedDistanceCm)
    {
        if (!TryGetPageBoundsPoints(section, out var pageWidth, out var pageHeight))
            return null;

        var normalizedField = (field ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedField.StartsWith("margin_", StringComparison.Ordinal))
        {
            var side = normalizedField["margin_".Length..];
            return BuildMarginAreaBbox(side, section, pageWidth, pageHeight, expectedDistanceCm);
        }

        if (normalizedField == "header_from_top")
            return BuildHeaderAreaBbox(section, pageWidth, pageHeight, expectedDistanceCm);

        if (normalizedField == "footer_from_bottom")
            return BuildFooterAreaBbox(section, pageWidth, pageHeight, expectedDistanceCm);

        return CreateBbox(0m, 0m, pageWidth, pageHeight, pageWidth, pageHeight);
    }

    private ErrorBbox? BuildMarginAreaBbox(
        string side,
        DokumenSection section,
        decimal pageWidth,
        decimal pageHeight,
        decimal? expectedDistanceCm)
    {
        switch ((side ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "top":
            {
                var top = ResolveDistancePoints(section.DsecMarginTopTwips, expectedDistanceCm);
                return CreateBbox(0m, 0m, pageWidth, top, pageWidth, pageHeight);
            }
            case "bottom":
            {
                var bottom = ResolveDistancePoints(section.DsecMarginBottomTwips, expectedDistanceCm);
                return CreateBbox(0m, pageHeight - bottom, pageWidth, pageHeight, pageWidth, pageHeight);
            }
            case "left":
            {
                var left = ResolveDistancePoints(section.DsecMarginLeftTwips, expectedDistanceCm);
                return CreateBbox(0m, 0m, left, pageHeight, pageWidth, pageHeight);
            }
            case "right":
            {
                var right = ResolveDistancePoints(section.DsecMarginRightTwips, expectedDistanceCm);
                return CreateBbox(pageWidth - right, 0m, pageWidth, pageHeight, pageWidth, pageHeight);
            }
            default:
                return CreateBbox(0m, 0m, pageWidth, pageHeight, pageWidth, pageHeight);
        }
    }

    private ErrorBbox? BuildHeaderAreaBbox(
        DokumenSection section,
        decimal pageWidth,
        decimal pageHeight,
        decimal? expectedDistanceCm)
    {
        var headerDistance = ResolveDistancePoints(section.DsecHeaderMarginTwips, expectedDistanceCm);
        return CreateBbox(0m, 0m, pageWidth, headerDistance, pageWidth, pageHeight);
    }

    private ErrorBbox? BuildFooterAreaBbox(
        DokumenSection section,
        decimal pageWidth,
        decimal pageHeight,
        decimal? expectedDistanceCm)
    {
        var footerDistance = ResolveDistancePoints(section.DsecFooterMarginTwips, expectedDistanceCm);
        return CreateBbox(0m, pageHeight - footerDistance, pageWidth, pageHeight, pageWidth, pageHeight);
    }

    private bool TryGetPageBoundsPoints(DokumenSection section, out decimal widthPoints, out decimal heightPoints)
    {
        var widthFromSection = TwipsToPoints(section.DsecPageWidthTwips);
        var heightFromSection = TwipsToPoints(section.DsecPageHeightTwips);
        if (widthFromSection.HasValue && heightFromSection.HasValue &&
            widthFromSection.Value > 0 && heightFromSection.Value > 0)
        {
            widthPoints = widthFromSection.Value;
            heightPoints = heightFromSection.Value;
            return true;
        }

        var detectedSize = DetectPaperSize(section.DsecPageWidthTwips, section.DsecPageHeightTwips);
        if (!PaperSizes.TryGetValue(detectedSize, out var dimensions))
            dimensions = PaperSizes["A4"];

        var orientation = (section.DsecOrientation ?? "PORTRAIT").Trim().ToUpperInvariant();
        var widthTwips = dimensions.Width;
        var heightTwips = dimensions.Height;
        if (orientation == "LANDSCAPE")
            (widthTwips, heightTwips) = (heightTwips, widthTwips);

        widthPoints = widthTwips / 20m;
        heightPoints = heightTwips / 20m;
        return widthPoints > 0m && heightPoints > 0m;
    }

    private static decimal ResolveDistancePoints(uint? twips, decimal? expectedDistanceCm)
    {
        var actual = TwipsToPoints(twips) ?? 0m;
        if (actual > 0m)
            return actual;

        if (expectedDistanceCm.HasValue && expectedDistanceCm.Value > 0m)
            return expectedDistanceCm.Value * TwipsPerCm / 20m;

        return 0m;
    }

    private static decimal ResolveDistancePoints(long? twips, decimal? expectedDistanceCm)
    {
        var actual = TwipsToPoints(twips) ?? 0m;
        if (actual > 0m)
            return actual;

        if (expectedDistanceCm.HasValue && expectedDistanceCm.Value > 0m)
            return expectedDistanceCm.Value * TwipsPerCm / 20m;

        return 0m;
    }

    private static ErrorBbox? CreateBbox(
        decimal x0,
        decimal y0,
        decimal x1,
        decimal y1,
        decimal pageWidth,
        decimal pageHeight)
    {
        var left = ClampToRange(Math.Min(x0, x1), 0m, pageWidth);
        var right = ClampToRange(Math.Max(x0, x1), 0m, pageWidth);
        var top = ClampToRange(Math.Min(y0, y1), 0m, pageHeight);
        var bottom = ClampToRange(Math.Max(y0, y1), 0m, pageHeight);

        if (right <= left || bottom <= top)
            return null;

        return new ErrorBbox
        {
            X0 = left,
            Y0 = top,
            X1 = right,
            Y1 = bottom
        };
    }

    private static decimal ClampToRange(decimal value, decimal min, decimal max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }

    private string DetectPaperSize(uint? widthTwips, uint? heightTwips)
    {
        if (!widthTwips.HasValue || !heightTwips.HasValue)
            return "UNKNOWN";

        var w = widthTwips.Value;
        var h = heightTwips.Value;

        foreach (var (name, (expectedW, expectedH)) in PaperSizes)
        {
            // Allow some tolerance (~5mm = ~283 twips)
            var diffW = Math.Abs((long)w - expectedW);
            var diffH = Math.Abs((long)h - expectedH);
            if (diffW < 300 && diffH < 300)
                return name;
        }

        return "CUSTOM";
    }
}


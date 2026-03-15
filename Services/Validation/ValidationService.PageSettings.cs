using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private sealed class NomorHalamanSectionRule
    {
        public bool? Continue { get; init; }
        public bool? DifferentFirstPage { get; init; }
        public bool? FirstPageIsEmpty { get; init; }
        public string? NumberFormat { get; init; }
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
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (aturan == null)
            return result;

        // Get aturan details for page settings + nomor halaman
        var aturanDetails = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Pengaturan Halaman" || d.AturanDetailKategori == "Nomor Halaman")
            .ToListAsync(cancellationToken);

        // Parse rules
        PaperSectionRule? paperRule = null;
        MarginRule? marginRule = null;
        HeaderFooterRule? headerFooterRule = null;
        GutterRule? gutterRule = null;
        ColumnRule? columnRule = null;
        PageNumberingRule? pageNumberingRule = null;
        var nomorHalamanRules = new Dictionary<string, NomorHalamanSectionRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in aturanDetails)
        {
            try
            {
                var kategori = (detail.AturanDetailKategori ?? string.Empty).Trim();
                var key = (detail.AturanDetailKey ?? string.Empty).Trim().ToLowerInvariant();

                if (kategori.Equals("Nomor Halaman", StringComparison.OrdinalIgnoreCase))
                {
                    var parsedRule = ParseNomorHalamanSectionRule(detail.AturanDetailJsonValue);
                    switch (key)
                    {
                        case "nomor_halaman_awal":
                            nomorHalamanRules["awal"] = parsedRule;
                            break;
                        case "nomor_halaman_isi":
                            nomorHalamanRules["isi"] = parsedRule;
                            break;
                        case "nomor_halaman_akhir":
                            nomorHalamanRules["akhir"] = parsedRule;
                            break;
                        case "nomor_halaman_lampiran":
                            nomorHalamanRules["lampiran"] = parsedRule;
                            break;
                    }

                    continue;
                }

                switch (key)
                {
                    case "paper":
                        paperRule = JsonSerializer.Deserialize<PaperSectionRule>(detail.AturanDetailJsonValue ?? "{}");
                        break;
                    case "margin":
                        marginRule = JsonSerializer.Deserialize<MarginRule>(detail.AturanDetailJsonValue ?? "{}");
                        break;
                    case "header_footer":
                        headerFooterRule = JsonSerializer.Deserialize<HeaderFooterRule>(detail.AturanDetailJsonValue ?? "{}");
                        break;
                    case "gutter":
                        gutterRule = JsonSerializer.Deserialize<GutterRule>(detail.AturanDetailJsonValue ?? "{}");
                        break;
                    case "column":
                        columnRule = JsonSerializer.Deserialize<ColumnRule>(detail.AturanDetailJsonValue ?? "{}");
                        break;
                    case "page_numbering":
                        pageNumberingRule = JsonSerializer.Deserialize<PageNumberingRule>(detail.AturanDetailJsonValue ?? "{}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse aturan detail: {Key}", detail.AturanDetailKey);
            }
        }

        // Default rules when not provided in DB
        gutterRule ??= new GutterRule
        {
            Gutter = new DecimalRuleValue { Value = 0m },
            Position = new RuleValue<string> { Value = "left" }
        };

        var effectiveColumnRule = columnRule?.Count?.Value is > 0
            ? columnRule
            : new ColumnRule { Count = new RuleValue<int> { Value = 1 } };

        var dokumenTipe = target.DokumenTipe;
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

        _logger.LogInformation("Validating {Count} sections for ref {RefType}:{RefId}, logical ID: {DokumenId}, tipe: {DokumenTipe}",
            sections.Count, sectionRefType, sectionRefId, dokumenId, dokumenTipe);

        // Validate each section
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var sectionType = DetermineSectionType(section, i, sections.Count, dokumenTipe);

            _logger.LogInformation("Section {Index}: Type={SectionType}, PageFormat={PageFormat}, " +
                "Size={Width}x{Height} twips, Orientation={Orientation}, " +
                "Margins: T={Top}, B={Bottom}, L={Left}, R={Right} twips, " +
                "Header={Header}, Footer={Footer} twips",
                i + 1, sectionType, section.DsecPageNumFormat,
                section.DsecPageWidthTwips, section.DsecPageHeightTwips, section.DsecOrientation,
                section.DsecMarginTopTwips, section.DsecMarginBottomTwips,
                section.DsecMarginLeftTwips, section.DsecMarginRightTwips,
                section.DsecHeaderMarginTwips, section.DsecFooterMarginTwips);

            // Validate paper size
            if (paperRule?.Section != null)
            {
                var allowedPapers = GetAllowedPapersForSection(paperRule.Section, sectionType);
                ValidatePaperSize(result, section, allowedPapers, i + 1, sectionType, sectionPageMap);
            }

            // Validate margins
            if (marginRule?.Paper != null)
            {
                ValidateMargins(result, section, marginRule.Paper, i + 1, sectionType, sectionPageMap);
            }

            // Validate header/footer distances
            if (headerFooterRule != null)
            {
                ValidateHeaderFooter(result, section, headerFooterRule, i + 1, sectionType, sectionPageMap);
            }

            // Validate odd/even headers (must be off)
            ValidateDifferentOddEven(result, section, headerFooterRule, i + 1, sectionType, sectionPageMap);

            // Validate gutter
            if (gutterRule != null)
            {
                ValidateGutter(result, section, gutterRule, i + 1, sectionType, sectionPageMap);
            }

            // Validate column count (must be 1)
            ValidateColumnCount(result, section, effectiveColumnRule, i + 1, sectionType, sectionPageMap);

            // Validate page numbering
            if (nomorHalamanRules.TryGetValue(sectionType, out var nomorHalamanRule))
            {
                ValidateNomorHalamanRule(result, section, nomorHalamanRule, i + 1, sectionType, sectionPageMap);
            }
            else if (pageNumberingRule?.Section != null)
            {
                var expectedNumbering = GetExpectedPageNumbering(pageNumberingRule.Section, sectionType);
                ValidatePageNumbering(result, section, expectedNumbering, i + 1, sectionType, sectionPageMap);
            }
        }

        return result;
    }

    private string DetermineSectionType(DokumenSection section, int sectionIndex, int totalSections, string? dokumenTipe)
    {
        // Strong signal: dokumen_tipe is already semantic (awal/isi/akhir/lampiran).
        var normalizedDokumenTipe = (dokumenTipe ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedDokumenTipe is "awal" or "isi" or "akhir" or "lampiran")
            return normalizedDokumenTipe;

        // Determine section type based on page numbering format and position
        // This is more accurate than just using position

        var pageNumFormat = section.DsecPageNumFormat?.ToLower();

        // Roman numerals typically indicate front matter (awal)
        if (pageNumFormat == "lowerroman" || pageNumFormat == "upperroman")
        {
            return "awal";
        }

        // Check if this section restarts page numbering from 1 with decimal
        // This typically indicates the start of main content (isi)
        if (pageNumFormat == "decimal" && section.DsecPageNumStart == 1 && sectionIndex > 0)
        {
            // This is likely the start of "isi" section
            return "isi";
        }

        // If only one section, check dokumen_tipe to determine
        if (totalSections == 1)
        {
            // Single section documents are typically "isi"
            return "isi";
        }

        // First section without roman numerals could still be "awal" or "isi"
        if (sectionIndex == 0)
        {
            // If first section has roman numerals or no page numbers, it's "awal"
            if (string.IsNullOrEmpty(pageNumFormat) || pageNumFormat == "lowerroman" || pageNumFormat == "upperroman")
                return "awal";
            return "isi";
        }

        // Last section could be "akhir" or "lampiran"
        // Without more content analysis, we default to "isi"
        // Lampiran detection would require content analysis (looking for "LAMPIRAN" text)

        // Middle sections are "isi" by default
        return "isi";
    }

    private List<PaperSpec> GetAllowedPapersForSection(SectionRules rules, string sectionType)
    {
        return sectionType.ToLower() switch
        {
            "awal" => rules.Awal?.Value ?? new List<PaperSpec>(),
            "isi" => rules.Isi?.Value ?? new List<PaperSpec>(),
            "akhir" => rules.Akhir?.Value ?? new List<PaperSpec>(),
            "lampiran" => rules.Lampiran?.Value ?? new List<PaperSpec>(),
            _ => rules.Isi?.Value ?? new List<PaperSpec>()
        };
    }

    private void ValidatePaperSize(
        ValidationResult result,
        DokumenSection section,
        List<PaperSpec> allowedPapers,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        result.IncrementTotalChecks();

        if (allowedPapers.Count == 0)
        {
            result.IncrementPassedChecks(); // No restriction for this section type
            return;
        }

        var actualSize = DetectPaperSize(section.DsecPageWidthTwips, section.DsecPageHeightTwips);
        var actualOrientation = section.DsecOrientation?.ToUpper() ?? "PORTRAIT";
        var actualDimensions = FormatPageDimensionsCm(section.DsecPageWidthTwips, section.DsecPageHeightTwips);
        var actualDescriptor = string.IsNullOrWhiteSpace(actualDimensions)
            ? $"{actualSize} {actualOrientation}"
            : $"{actualSize} {actualOrientation} ({actualDimensions})";

        var isValid = allowedPapers.Any(p =>
            p.Size?.ToUpper() == actualSize &&
            p.Orientation?.ToUpper() == actualOrientation);

        if (isValid)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            var allowedStr = string.Join(", ", allowedPapers.Select(FormatPaperSpec).Where(s => !string.IsNullOrWhiteSpace(s)));
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "paper",
                Message = $"Ukuran kertas section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = allowedStr,
                Actual = actualDescriptor,
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
        PaperMargins margins,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        var paperSize = DetectPaperSize(section.DsecPageWidthTwips, section.DsecPageHeightTwips);
        var orientation = section.DsecOrientation?.ToUpper() ?? "PORTRAIT";

        MarginSpec? expectedMargins = (paperSize, orientation) switch
        {
            ("A4", "PORTRAIT") => margins.A4Portrait?.Value,
            ("A4", "LANDSCAPE") => margins.A4Landscape?.Value,
            ("A3", "LANDSCAPE") => margins.A3Landscape?.Value,
            _ => margins.A4Portrait?.Value // Default
        };

        if (expectedMargins == null)
            return;

        // Validate each margin
        ValidateSingleMargin(result, "top", section, section.DsecMarginTopTwips, expectedMargins.Top, sectionNumber, sectionType, sectionPageMap);
        ValidateSingleMargin(result, "bottom", section, section.DsecMarginBottomTwips, expectedMargins.Bottom, sectionNumber, sectionType, sectionPageMap);
        ValidateSingleMargin(result, "left", section, section.DsecMarginLeftTwips, expectedMargins.Left, sectionNumber, sectionType, sectionPageMap);
        ValidateSingleMargin(result, "right", section, section.DsecMarginRightTwips, expectedMargins.Right, sectionNumber, sectionType, sectionPageMap);
    }

    private void ValidateSingleMargin(
        ValidationResult result,
        string marginName,
        DokumenSection section,
        uint? actualTwips,
        decimal? expectedCm,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        if (!expectedCm.HasValue)
            return;

        result.IncrementTotalChecks();

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
            result.IncrementTotalChecks();
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
            result.IncrementTotalChecks();
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
        HeaderFooterRule? rule,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        var expectedDifferentOddEven = rule?.DifferentOddEven?.Value ?? false;
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
            Message = $"Pengaturan header/footer ganjil-genap section {sectionNumber} (bagian {sectionType}) tidak sesuai",
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
        result.IncrementTotalChecks();
        var expectedGutterCm = rule.Gutter?.Value ?? 0m;
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

        // Validate gutter position if specified
        var expectedPositionValue = rule.Position?.Value;
        if (!string.IsNullOrEmpty(expectedPositionValue))
        {
            result.IncrementTotalChecks();
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
        ColumnRule rule,
        int sectionNumber,
        string sectionType,
        Dictionary<uint, int> sectionPageMap)
    {
        result.IncrementTotalChecks();
        var actualColumns = (int)(section.DsecColumnCount ?? 1);
        var expectedColumns = rule.Count?.Value ?? 1;

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
            var canUseManualFallback = CanUseManualPageNumberFallback(section);

            if (actualContinue == expectedContinue)
            {
                result.IncrementPassedChecks();
            }
            else if (!expectedContinue && canUseManualFallback)
            {
                // Manual numbering in header/footer does not always persist restart metadata.
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
            var canUseManualFallback = CanUseManualPageNumberFallback(section);

            if (actualDifferentFirstPage == expectedDifferentFirstPage)
            {
                result.IncrementPassedChecks();
            }
            else if (expectedDifferentFirstPage && canUseManualFallback)
            {
                // Some documents emulate "different first page" manually while visual output remains correct.
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
        // We use title-page flag as best-effort proxy.
        if (rule.FirstPageIsEmpty == true)
        {
            result.IncrementTotalChecks();
            if (section.DsecHasTitlePage || CanUseManualPageNumberFallback(section))
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

    private static bool CanUseManualPageNumberFallback(DokumenSection section)
    {
        // Manual page numbers can be visually correct without explicit restart metadata.
        return !section.DsecPageNumStart.HasValue || section.DsecPageNumStart.Value == 0;
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


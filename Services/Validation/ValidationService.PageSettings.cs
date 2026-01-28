using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    public async Task<ValidationResult> ValidatePageSettingsAsync(int dokumenId, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        // Get dokumen info
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

        // Get active aturan with details
        var aturan = await _db.Aturans
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (aturan == null)
            return result;

        // Get aturan details for page settings
        var aturanDetails = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Pengaturan Halaman")
            .ToListAsync(cancellationToken);

        // Parse rules
        PaperSectionRule? paperRule = null;
        MarginRule? marginRule = null;
        HeaderFooterRule? headerFooterRule = null;
        GutterRule? gutterRule = null;
        ColumnRule? columnRule = null;
        PageNumberingRule? pageNumberingRule = null;

        foreach (var detail in aturanDetails)
        {
            try
            {
                switch (detail.AturanDetailKey?.ToLower())
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

        var effectiveColumnRule = new ColumnRule { Count = 1 };
        if (columnRule?.Count == 1)
            effectiveColumnRule = columnRule;

        // Get all sections for this dokumen
        var sections = await _db.DokumenSections
            .Where(s => s.DokumenId == (uint)dokumenId)
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

        _logger.LogInformation("Validating {Count} sections for dokumen ID: {DokumenId}, tipe: {DokumenTipe}",
            sections.Count, dokumenId, dokumen.DokumenTipe);

        // Validate each section
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var sectionType = DetermineSectionType(section, i, sections.Count, dokumen.DokumenTipe);

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
                ValidatePaperSize(result, section, allowedPapers, i + 1, sectionType);
            }

            // Validate margins
            if (marginRule?.Paper != null)
            {
                ValidateMargins(result, section, marginRule.Paper, i + 1, sectionType);
            }

            // Validate header/footer distances
            if (headerFooterRule != null)
            {
                ValidateHeaderFooter(result, section, headerFooterRule, i + 1, sectionType);
            }

            // Validate odd/even headers (must be off)
            ValidateDifferentOddEven(result, section, i + 1, sectionType);

            // Validate gutter
            if (gutterRule != null)
            {
                ValidateGutter(result, section, gutterRule, i + 1, sectionType);
            }

            // Validate column count (must be 1)
            ValidateColumnCount(result, section, effectiveColumnRule, i + 1, sectionType);

            // Validate page numbering
            if (pageNumberingRule?.Section != null)
            {
                var expectedNumbering = GetExpectedPageNumbering(pageNumberingRule.Section, sectionType);
                ValidatePageNumbering(result, section, expectedNumbering, i + 1, sectionType);
            }
        }

        return result;
    }

    private string DetermineSectionType(DokumenSection section, int sectionIndex, int totalSections, string? dokumenTipe)
    {
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

    private void ValidatePaperSize(ValidationResult result, DokumenSection section, List<PaperSpec> allowedPapers, int sectionNumber, string sectionType)
    {
        result.TotalChecks++;

        if (allowedPapers.Count == 0)
        {
            result.PassedChecks++; // No restriction for this section type
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
            result.PassedChecks++;
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
                SectionIndex = sectionNumber
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

    private void ValidateMargins(ValidationResult result, DokumenSection section, PaperMargins margins, int sectionNumber, string sectionType)
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
        ValidateSingleMargin(result, "top", section.DsecMarginTopTwips, expectedMargins.Top, sectionNumber, sectionType);
        ValidateSingleMargin(result, "bottom", section.DsecMarginBottomTwips, expectedMargins.Bottom, sectionNumber, sectionType);
        ValidateSingleMargin(result, "left", section.DsecMarginLeftTwips, expectedMargins.Left, sectionNumber, sectionType);
        ValidateSingleMargin(result, "right", section.DsecMarginRightTwips, expectedMargins.Right, sectionNumber, sectionType);
    }

    private void ValidateSingleMargin(ValidationResult result, string marginName, uint? actualTwips, decimal? expectedCm, int sectionNumber, string sectionType)
    {
        if (!expectedCm.HasValue)
            return;

        result.TotalChecks++;

        var expectedTwips = expectedCm.Value * TwipsPerCm;
        var actualCm = actualTwips.HasValue ? actualTwips.Value / TwipsPerCm : 0;

        if (actualTwips.HasValue && Math.Abs(actualTwips.Value - expectedTwips) <= TwipsTolerance * 2)
        {
            result.PassedChecks++;
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
                SectionIndex = sectionNumber
            });
        }
    }

    private void ValidateHeaderFooter(ValidationResult result, DokumenSection section, HeaderFooterRule rule, int sectionNumber, string sectionType)
    {
        // Validate header distance from top
        var expectedHeaderCm = rule.HeaderFromTop?.Value;
        if (expectedHeaderCm.HasValue)
        {
            result.TotalChecks++;
            var expectedHeaderTwips = expectedHeaderCm.Value * TwipsPerCm;
            var actualHeaderCm = section.DsecHeaderMarginTwips.HasValue
                ? section.DsecHeaderMarginTwips.Value / TwipsPerCm
                : 0;

            if (section.DsecHeaderMarginTwips.HasValue &&
                Math.Abs(section.DsecHeaderMarginTwips.Value - expectedHeaderTwips) <= TwipsTolerance * 2)
            {
                result.PassedChecks++;
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
                    SectionIndex = sectionNumber
                });
            }
        }

        // Validate footer distance from bottom
        var expectedFooterCm = rule.FooterFromBottom?.Value;
        if (expectedFooterCm.HasValue)
        {
            result.TotalChecks++;
            var expectedFooterTwips = expectedFooterCm.Value * TwipsPerCm;
            var actualFooterCm = section.DsecFooterMarginTwips.HasValue
                ? section.DsecFooterMarginTwips.Value / TwipsPerCm
                : 0;

            if (section.DsecFooterMarginTwips.HasValue &&
                Math.Abs(section.DsecFooterMarginTwips.Value - expectedFooterTwips) <= TwipsTolerance * 2)
            {
                result.PassedChecks++;
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
                    SectionIndex = sectionNumber
                });
            }
        }
    }

    private void ValidateDifferentOddEven(ValidationResult result, DokumenSection section, int sectionNumber, string sectionType)
    {
        result.TotalChecks++;

        if (!section.DsecDifferentOddEven)
        {
            result.PassedChecks++;
            return;
        }

        result.Errors.Add(new ValidationError
        {
            Category = "Pengaturan Halaman",
            Field = "different_odd_even",
            Message = $"Pengaturan header/footer ganjil-genap section {sectionNumber} (bagian {sectionType}) tidak diperbolehkan",
            Expected = "0",
            Actual = "1",
            SectionIndex = sectionNumber
        });
    }

    private void ValidateGutter(ValidationResult result, DokumenSection section, GutterRule rule, int sectionNumber, string sectionType)
    {
        // Validate gutter size
        result.TotalChecks++;
        var expectedGutterTwips = rule.Gutter * TwipsPerCm;
        var actualGutterCm = section.DsecGutterTwips.HasValue 
            ? section.DsecGutterTwips.Value / TwipsPerCm 
            : 0;

        if (Math.Abs((section.DsecGutterTwips ?? 0) - expectedGutterTwips) <= TwipsTolerance * 2)
        {
            result.PassedChecks++;
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "gutter",
                Message = $"Gutter section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = $"{rule.Gutter} cm",
                Actual = $"{actualGutterCm:F2} cm",
                SectionIndex = sectionNumber
            });
        }

        // Validate gutter position if specified
        if (!string.IsNullOrEmpty(rule.Position))
        {
            result.TotalChecks++;
            var actualPosition = section.DsecGutterPosition?.ToLower() ?? "left";
            var expectedPosition = rule.Position.ToLower();

            if (actualPosition == expectedPosition)
            {
                result.PassedChecks++;
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
                    SectionIndex = sectionNumber
                });
            }
        }
    }

    private void ValidateColumnCount(ValidationResult result, DokumenSection section, ColumnRule rule, int sectionNumber, string sectionType)
    {
        result.TotalChecks++;
        var actualColumns = (int)(section.DsecColumnCount ?? 1);

        if (actualColumns == rule.Count)
        {
            result.PassedChecks++;
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Pengaturan Halaman",
                Field = "column_count",
                Message = $"Jumlah kolom section {sectionNumber} (bagian {sectionType}) tidak sesuai",
                Expected = $"{rule.Count} kolom",
                Actual = $"{actualColumns} kolom",
                SectionIndex = sectionNumber
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

    private void ValidatePageNumbering(ValidationResult result, DokumenSection section, PageNumberingSpec? expected, int sectionNumber, string sectionType)
    {
        if (expected == null)
            return;

        // Validate format
        if (!string.IsNullOrEmpty(expected.Format))
        {
            result.TotalChecks++;
            var actualFormat = section.DsecPageNumFormat?.ToLower() ?? "decimal";
            var expectedFormat = expected.Format.ToLower();

            if (actualFormat == expectedFormat)
            {
                result.PassedChecks++;
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
                    SectionIndex = sectionNumber
                });
            }
        }

        // Validate start number if specified
        if (expected.Start.HasValue)
        {
            result.TotalChecks++;
            var actualStart = (int)(section.DsecPageNumStart ?? 0);

            if (actualStart == expected.Start.Value || (expected.Start.Value == 1 && actualStart == 0))
            {
                // 0 is treated as "continue from previous" which for first section means 1
                result.PassedChecks++;
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
                    SectionIndex = sectionNumber
                });
            }
        }
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

using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// ValidationService partial class for Subchapter Title (Judul Subbab) validation
/// </summary>
public partial class ValidationService
{
    // Regex pattern for subchapter numbering: X.X, X.X., X.X.X, X.X.X., etc. (with optional trailing dot)
    private static readonly Regex SubchapterNumberPattern = new(@"^\d+(\.\d+)+\.?", RegexOptions.Compiled);

    private async Task<ValidationResult> ValidateSubchapterTitleAsync(
        int dokumenId,
        HashSet<ulong>? subchapterIds,
        CancellationToken cancellationToken)
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
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Aturan",
                Field = "aturan",
                Message = "Tidak ada aturan yang aktif"
            });
            return result;
        }

        var subbabDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "judul_subbab")
            .FirstOrDefaultAsync(cancellationToken);

        if (subbabDetail == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_subbab",
                Message = "Aturan judul subbab tidak ditemukan"
            });
            return result;
        }

        SubchapterTitleRule? rule = null;
        try
        {
            rule = JsonSerializer.Deserialize<SubchapterTitleRule>(
                subbabDetail.AturanDetailJsonValue ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan judul_subbab");
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_subbab",
                Message = "Format aturan judul subbab tidak valid"
            });
            return result;
        }

        if (rule == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_subbab",
                Message = "Aturan judul subbab tidak valid"
            });
            return result;
        }

        // Get all body elements
        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DokumenId == (uint)dokumenId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new { e.DelemenId, e.DelemenType, e.DelemenJsonTree })
            .ToListAsync(cancellationToken);

        if (bodyElements.Count == 0)
        {
            return result; // No elements to validate
        }

        // Load visual labels
        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var orderedElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);

        // Find section_header elements
        var sectionHeaderIds = labelMap
            .Where(kv => kv.Value.Equals("section_header", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToHashSet();

        // Build element lookup
        var elementsById = bodyElements.ToDictionary(e => e.DelemenId);

        // Find subchapter titles (section headers starting with X.X pattern)
        var subchapterElements = new List<(ulong Id, ElementContentInfo Content)>();
        if (subchapterIds != null)
        {
            foreach (var elem in bodyElements)
            {
                if (!subchapterIds.Contains(elem.DelemenId))
                    continue;

                var content = ParseElementContent(elem.DelemenJsonTree);
                subchapterElements.Add((elem.DelemenId, content));
            }
        }
        else
        {
            foreach (var elem in bodyElements)
            {
                if (!sectionHeaderIds.Contains(elem.DelemenId))
                    continue;

                var content = ParseElementContent(elem.DelemenJsonTree);
                var plainText = content.PlainText?.Trim() ?? string.Empty;

                // Check if it matches subchapter pattern (X.X, X.X.X, etc.)
                if (SubchapterNumberPattern.IsMatch(plainText))
                {
                    subchapterElements.Add((elem.DelemenId, content));
                }
            }
        }

        if (subchapterElements.Count == 0)
        {
            // No subchapters found, not an error
            return result;
        }

        // --- Validate sequence completeness (based on struktur_konten rule) ---
        var preventSingleSubchapter = rule?.StrukturKonten?.CegahSubbabTunggal?.Value ?? true;
        ValidateSubchapterSequence(result, subchapterElements, preventSingleSubchapter, neighborContexts);

        // --- Validate paragraph after subchapter (based on struktur_konten rule) ---
        var requireParagraphAfter = rule?.StrukturKonten?.MinimalSatuParagrafSetelah?.Value ?? true;
        var preventBottomPosition = rule?.StrukturKonten?.CegahPosisiPalingBawah?.Value ?? true;
        
        var bodyElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        if (requireParagraphAfter || preventBottomPosition)
        {
            await ValidateParagraphAfterSubchapterAsync(
                result,
                bodyElementIds,
                subchapterElements,
                labelMap,
                neighborContexts,
                requireParagraphAfter,
                preventBottomPosition,
                cancellationToken);
        }

        // Collect all paragraph format IDs for batch loading
        var paragraphIds = subchapterElements
            .Where(e => e.Content.ParagraphFormatId.HasValue)
            .Select(e => e.Content.ParagraphFormatId!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = await _db.DokumenFormatParagrafs
            .Where(p => paragraphIds.Contains(p.DfpId))
            .ToDictionaryAsync(p => p.DfpId, cancellationToken);

        // Collect all text format IDs for batch loading
        var textFormatIds = subchapterElements
            .SelectMany(e => e.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        // Validate each subchapter
        foreach (var (elementId, content) in subchapterElements)
        {
            var plainText = content.PlainText?.Trim() ?? string.Empty;
            var match = SubchapterNumberPattern.Match(plainText);
            var subchapterNumber = match.Success ? match.Value : string.Empty;
            var subchapterTitle = match.Success ? plainText[match.Length..].Trim() : plainText;
            var errorStart = result.Errors.Count;

            // Get paragraph format
            DokumenFormatParagraf? paragraphFormat = null;
            if (content.ParagraphFormatId.HasValue)
            {
                paragraphFormats.TryGetValue(content.ParagraphFormatId.Value, out paragraphFormat);
            }

            // Get text formats for this element
            var elementTextFormats = content.TextFormatIds
                .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                .Where(tf => tf != null)
                .ToList();

            // Load page info for error reporting
            var pageNumbers = await LoadPageNumbersAsync(new[] { elementId }, cancellationToken);
            var pageBboxMap = await LoadPageBboxMapAsync(new[] { elementId }, cancellationToken);

            // Create locations for error reporting
            var locations = CreateLocations(pageNumbers.Values, pageBboxMap);

            // --- Font Validations ---
            ValidateSubchapterFont(result, rule, elementTextFormats!, content.TextRuns, plainText, locations);

            // --- Paragraph Validations ---
            ValidateSubchapterParagraph(result, rule, paragraphFormat, plainText, locations);

            // --- Numbering Validation ---
            ValidateSubchapterNumbering(result, rule, subchapterNumber, subchapterTitle, plainText, locations);

            if (neighborContexts.TryGetValue(elementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, elementId);
        }

        return result;
    }

    private void ValidateSubchapterFont(
        ValidationResult result,
        SubchapterTitleRule rule,
        List<DokumenFormatText> textFormats,
        IReadOnlyList<TextRunInfo> textRuns,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (textFormats.Count == 0)
            return;

        var textFormatById = BuildTextFormatMap(textFormats);
        var runs = GetMeaningfulRuns(textRuns);

        // Font Name
        var expectedFontName = rule?.Font?.FontName?.Value;
        if (!string.IsNullOrWhiteSpace(expectedFontName))
        {
            result.TotalChecks++;
            if (runs.Count > 0)
            {
                var mismatches = CollectRunMismatches(
                    runs,
                    textFormatById,
                    tf => !string.Equals(tf.DftxFontAscii ?? "unknown", expectedFontName, StringComparison.OrdinalIgnoreCase),
                    tf => tf.DftxFontAscii ?? "unknown");

                if (mismatches.Count == 0)
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = "Font judul subbab tidak sesuai",
                        Expected = expectedFontName,
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = textFormats.Select(tf => tf.DftxFontAscii ?? "unknown").Distinct().ToList();
                if (actuals.All(a => string.Equals(a, expectedFontName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = "Font judul subbab tidak sesuai",
                        Expected = expectedFontName,
                        Actual = string.Join(", ", actuals),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        // Font Size
        var expectedFontSize = rule?.Font?.FontSize?.Value;
        if (expectedFontSize.HasValue)
        {
            result.TotalChecks++;
            var expectedHalfPt = expectedFontSize.Value * 2m;
            if (runs.Count > 0)
            {
                var mismatches = CollectRunMismatches(
                    runs,
                    textFormatById,
                    tf => !tf.DftxSizeHalfpt.HasValue || Math.Abs(tf.DftxSizeHalfpt.Value - expectedHalfPt) > 0.5m,
                    tf => tf.DftxSizeHalfpt.HasValue
                        ? (tf.DftxSizeHalfpt.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt"
                        : "unknown");

                if (mismatches.Count == 0)
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = "Ukuran font judul subbab tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = textFormats
                    .Select(tf => tf.DftxSizeHalfpt.HasValue ? (decimal?)tf.DftxSizeHalfpt.Value : null)
                    .ToList();

                if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expectedHalfPt) <= 0.5m))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = "Ukuran font judul subbab tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt" : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        // Bold
        var expectedBold = rule?.Font?.FontStyle?.Bold?.Value;
        if (expectedBold.HasValue)
        {
            result.TotalChecks++;
            if (runs.Count > 0)
            {
                var mismatches = CollectRunMismatches(
                    runs,
                    textFormatById,
                    tf => !tf.DftxBold.HasValue || tf.DftxBold.Value != expectedBold.Value,
                    tf => tf.DftxBold.HasValue ? (tf.DftxBold.Value ? "Bold" : "Tidak Bold") : "unknown");

                if (mismatches.Count == 0)
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = expectedBold.Value ? "Judul subbab harus bold" : "Judul subbab tidak boleh bold",
                        Expected = expectedBold.Value ? "Bold" : "Tidak Bold",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = textFormats.Select(tf => tf.DftxBold).Distinct().ToList();
                if (actuals.All(a => a.HasValue && a.Value == expectedBold.Value))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = expectedBold.Value ? "Judul subbab harus bold" : "Judul subbab tidak boleh bold",
                        Expected = expectedBold.Value ? "Bold" : "Tidak Bold",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value ? "Bold" : "Tidak Bold") : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        // Italic
        var expectedItalic = rule?.Font?.FontStyle?.Italic?.Value;
        if (expectedItalic.HasValue)
        {
            result.TotalChecks++;
            if (runs.Count > 0)
            {
                var mismatches = CollectRunMismatches(
                    runs,
                    textFormatById,
                    tf => !tf.DftxItalic.HasValue || tf.DftxItalic.Value != expectedItalic.Value,
                    tf => tf.DftxItalic.HasValue ? (tf.DftxItalic.Value ? "Italic" : "Tidak Italic") : "unknown");

                if (mismatches.Count == 0)
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = expectedItalic.Value ? "Judul subbab harus italic" : "Judul subbab tidak boleh italic",
                        Expected = expectedItalic.Value ? "Italic" : "Tidak Italic",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = textFormats.Select(tf => tf.DftxItalic).Distinct().ToList();
                if (actuals.All(a => a.HasValue && a.Value == expectedItalic.Value))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = expectedItalic.Value ? "Judul subbab harus italic" : "Judul subbab tidak boleh italic",
                        Expected = expectedItalic.Value ? "Italic" : "Tidak Italic",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value ? "Italic" : "Tidak Italic") : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        // Underline
        var expectedUnderline = rule?.Font?.FontStyle?.Underline?.Value;
        if (expectedUnderline.HasValue)
        {
            result.TotalChecks++;
            if (runs.Count > 0)
            {
                var mismatches = CollectRunMismatches(
                    runs,
                    textFormatById,
                    tf =>
                    {
                        var hasUnderline = !string.IsNullOrWhiteSpace(tf.DftxUnderline) &&
                            !tf.DftxUnderline.Equals("none", StringComparison.OrdinalIgnoreCase);
                        return hasUnderline != expectedUnderline.Value;
                    },
                    tf =>
                    {
                        var hasUnderline = !string.IsNullOrWhiteSpace(tf.DftxUnderline) &&
                            !tf.DftxUnderline.Equals("none", StringComparison.OrdinalIgnoreCase);
                        return hasUnderline ? "Underline" : "Tidak Underline";
                    });

                if (mismatches.Count == 0)
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = expectedUnderline.Value ? "Judul subbab harus underline" : "Judul subbab tidak boleh underline",
                        Expected = expectedUnderline.Value ? "Underline" : "Tidak Underline",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = textFormats.Select(tf => tf.DftxUnderline).Distinct().ToList();
                bool matches = expectedUnderline.Value
                    ? actuals.All(a => !string.IsNullOrWhiteSpace(a) && !a.Equals("none", StringComparison.OrdinalIgnoreCase))
                    : actuals.All(a => string.IsNullOrWhiteSpace(a) || a.Equals("none", StringComparison.OrdinalIgnoreCase));

                if (matches)
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = expectedUnderline.Value ? "Judul subbab harus underline" : "Judul subbab tidak boleh underline",
                        Expected = expectedUnderline.Value ? "Underline" : "Tidak Underline",
                        Actual = string.Join(", ", actuals.Select(a => string.IsNullOrWhiteSpace(a) || a.Equals("none", StringComparison.OrdinalIgnoreCase) ? "Tidak Underline" : "Underline")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }
    }

    private void ValidateSubchapterParagraph(
        ValidationResult result,
        SubchapterTitleRule rule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (format == null)
            return;

        // Alignment
        var expectedAlignment = rule?.Paragraph?.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.TotalChecks++;
            var actual = format.DfpJc ?? "unknown";
            if (AreAlignmentsEquivalent(actual, expectedAlignment))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_subbab",
                    Message = "Alignment judul subbab tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        // Hanging Indent Range Validation
        var hangingMinCm = rule?.Paragraph?.HangingMinCm?.Value;
        var hangingMaxCm = rule?.Paragraph?.HangingMaxCm?.Value;
        if (hangingMinCm.HasValue || hangingMaxCm.HasValue)
        {
            result.TotalChecks++;

            // Get hanging indent in twips and convert to cm
            var hangingTwips = format.DfpIndHangingTwips ?? 0;
            var hangingCm = hangingTwips / 1440.0m * 2.54m; // twips to inches to cm

            var isList = format.DfpIsList ||
                         !string.IsNullOrWhiteSpace(format.DfpNumprJson) ||
                         (format.DfpListNumId ?? 0) > 0;

            var minOk = !hangingMinCm.HasValue || hangingCm >= hangingMinCm.Value - 0.05m; // 0.5mm tolerance
            var maxOk = !hangingMaxCm.HasValue || hangingCm <= hangingMaxCm.Value + 0.05m;
            var hasHanging = hangingTwips > 0;

            if (isList && minOk && maxOk && hasHanging)
            {
                result.PassedChecks++;
            }
            else
            {
                var expectedRange = $"{hangingMinCm?.ToString(CultureInfo.InvariantCulture) ?? "0"} - {hangingMaxCm?.ToString(CultureInfo.InvariantCulture) ?? "∞"} cm";
                if (!isList)
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = "Judul subbab harus menggunakan Numbering (Multilevel List) agar hanging indent jelas",
                        Expected = $"Numbering (Multilevel List) + hanging {expectedRange}",
                        Actual = $"Tidak menggunakan numbering; hanging {hangingCm:F2} cm",
                        Evidence = evidence,
                        Locations = locations
                    });
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = "Hanging indent judul subbab tidak sesuai",
                        Expected = expectedRange,
                        Actual = $"{hangingCm:F2} cm",
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        // Line Spacing
        var spacingRule = rule?.Paragraph?.Spacing;
        if (spacingRule?.LineSpacing?.Value.HasValue == true)
        {
            result.TotalChecks++;
            var expected = spacingRule.LineSpacing.Value.Value;
            var actual = GetLineSpacing(format);

            if (actual.HasValue && Math.Abs(actual.Value - expected) <= 0.05m)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_subbab",
                    Message = "Line spacing judul subbab tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        // Spacing Before
        if (spacingRule?.Before?.Value.HasValue == true)
        {
            result.TotalChecks++;
            var expected = spacingRule.Before.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingBeforeTwips);

            if (actual.HasValue && IsWithinTolerance(actual.Value, expected, 0.5m) && !format.DfpSpacingBeforeAutospacing)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_subbab",
                    Message = "Spacing before judul subbab tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        // Spacing After
        if (spacingRule?.After?.Value.HasValue == true)
        {
            result.TotalChecks++;
            var expected = spacingRule.After.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingAfterTwips);

            if (actual.HasValue && IsWithinTolerance(actual.Value, expected, 0.5m) && !format.DfpSpacingAfterAutospacing)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_subbab",
                    Message = "Spacing after judul subbab tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateSubchapterNumbering(
        ValidationResult result,
        SubchapterTitleRule rule,
        string subchapterNumber,
        string subchapterTitle,
        string evidence,
        List<ErrorLocation> locations)
    {
        // Title Case validation
        var expectedCase = rule?.Numbering?.Case?.Value;
        if (!string.IsNullOrWhiteSpace(expectedCase) && !string.IsNullOrWhiteSpace(subchapterTitle))
        {
            result.TotalChecks++;
            var isTitleCase = IsTitleCase(subchapterTitle);

            if (expectedCase.Equals("Title Case", StringComparison.OrdinalIgnoreCase) && isTitleCase)
            {
                result.PassedChecks++;
            }
            else if (expectedCase.Equals("UPPERCASE", StringComparison.OrdinalIgnoreCase) && subchapterTitle == subchapterTitle.ToUpperInvariant())
            {
                result.PassedChecks++;
            }
            else if (expectedCase.Equals("lowercase", StringComparison.OrdinalIgnoreCase) && subchapterTitle == subchapterTitle.ToLowerInvariant())
            {
                result.PassedChecks++;
            }
            else if (!expectedCase.Equals("Title Case", StringComparison.OrdinalIgnoreCase) &&
                     !expectedCase.Equals("UPPERCASE", StringComparison.OrdinalIgnoreCase) &&
                     !expectedCase.Equals("lowercase", StringComparison.OrdinalIgnoreCase))
            {
                // Unknown case type, pass
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_subbab",
                    Message = $"Judul subbab harus {expectedCase}",
                    Expected = expectedCase,
                    Actual = subchapterTitle,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private static bool IsTitleCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var minorWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dan", "atau", "yang", "di", "ke", "dari", "untuk", "dengan", "pada", "dalam",
            "and", "or", "the", "a", "an", "in", "on", "at", "to", "for", "of", "with"
        };

        for (int i = 0; i < words.Length; i++)
        {
            var word = TrimEdgePunctuation(words[i]);
            if (string.IsNullOrWhiteSpace(word))
                continue;

            var firstLetter = FindFirstLetter(word);
            if (!firstLetter.HasValue)
                continue;

            if (IsAllUppercaseAlpha(word))
                continue;

            var isMinor = i > 0 && minorWords.Contains(word);
            if (!isMinor)
            {
                if (!char.IsUpper(firstLetter.Value))
                    return false;
            }
        }

        return true;
    }

    private static string TrimEdgePunctuation(string word)
    {
        if (string.IsNullOrEmpty(word))
            return string.Empty;

        var start = 0;
        var end = word.Length - 1;

        while (start <= end && !char.IsLetterOrDigit(word[start]))
            start++;
        while (end >= start && !char.IsLetterOrDigit(word[end]))
            end--;

        if (start > end)
            return string.Empty;

        return word.Substring(start, end - start + 1);
    }

    private static char? FindFirstLetter(string word)
    {
        foreach (var ch in word)
        {
            if (char.IsLetter(ch))
                return ch;
        }

        return null;
    }

    private static bool IsAllUppercaseAlpha(string word)
    {
        var hasLetter = false;
        foreach (var ch in word)
        {
            if (!char.IsLetter(ch))
                continue;

            hasLetter = true;
            if (!char.IsUpper(ch))
                return false;
        }

        return hasLetter;
    }

    private async Task ValidateParagraphAfterSubchapterAsync(
        ValidationResult result,
        List<ulong> bodyElementIds,
        List<(ulong Id, ElementContentInfo Content)> subchapterElements,
        Dictionary<ulong, string> labelMap,
        Dictionary<ulong, ElementNeighborContext> contextById,
        bool validateParagraphAfter,
        bool validateBottomPosition,
        CancellationToken cancellationToken)
    {
        // Build index mapping for body elements
        var elementIndexMap = new Dictionary<ulong, int>();
        for (int i = 0; i < bodyElementIds.Count; i++)
        {
            elementIndexMap[bodyElementIds[i]] = i;
        }

        foreach (var (subchapterId, content) in subchapterElements)
        {
            if (!elementIndexMap.TryGetValue(subchapterId, out var subchapterIndex))
                continue;

            var plainText = content.PlainText?.Trim() ?? string.Empty;
            var context = contextById.TryGetValue(subchapterId, out var found) ? found : null;

            // Load page info for error reporting
            var pageNumbers = await LoadPageNumbersAsync(new[] { subchapterId }, cancellationToken);
            var pageBboxMap = await LoadPageBboxMapAsync(new[] { subchapterId }, cancellationToken);

            // Create locations for error reporting
            var locations = CreateLocations(pageNumbers.Values, pageBboxMap);

            // --- Validate paragraph after subchapter (based on struktur_konten rule) ---
            if (validateParagraphAfter)
            {
                result.TotalChecks++;

                // Check if next element exists and is a paragraph (text label)
                var nextIndex = subchapterIndex + 1;
                if (nextIndex < bodyElementIds.Count)
                {
                    var nextElementId = bodyElementIds[nextIndex];

                    if (labelMap.TryGetValue(nextElementId, out var nextLabel) &&
                        nextLabel.Equals("text", StringComparison.OrdinalIgnoreCase))
                    {
                        result.PassedChecks++;
                    }
                    else
                    {
                        // Next element is not a paragraph
                        var error = new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "judul_subbab",
                            Message = "Harus ada minimal 1 paragraf setelah judul subbab",
                            Expected = "Paragraf (text)",
                            Actual = nextLabel ?? "unknown",
                            Evidence = plainText,
                            Locations = locations,
                            DokumenElemenId = subchapterId
                        };
                        if (context != null)
                            ApplyContext(error, context);
                        result.Errors.Add(error);
                    }
                }
                else
                {
                    // No next element after subchapter
                    var error = new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_subbab",
                        Message = "Harus ada minimal 1 paragraf setelah judul subbab",
                        Expected = "Paragraf (text)",
                        Actual = "Tidak ada elemen setelah subbab",
                        Evidence = plainText,
                        Locations = locations,
                        DokumenElemenId = subchapterId
                    };
                    if (context != null)
                        ApplyContext(error, context);
                    result.Errors.Add(error);
                }
            }

            // --- Check if subchapter is at bottom of page (based on struktur_konten rule) ---
            if (validateBottomPosition)
            {
                await ValidateSubchapterPositionAsync(result, subchapterId, plainText, locations, context, cancellationToken);
            }
        }
    }

    private async Task ValidateSubchapterPositionAsync(
        ValidationResult result,
        ulong subchapterId,
        string evidence,
        List<ErrorLocation> locations,
        ElementNeighborContext? context,
        CancellationToken cancellationToken)
    {
        // Get page + Y1 position from dokumen_elemen_visual using raw SQL
        var (idColumn, labelColumn) = await ResolveVisualColumnsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn))
            return;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        int? page = null;
        double? y1 = null;
        var hasElementBelow = false;

        try
        {
            var sql = $"SELECT `dev_page`, `dev_bbox_y1` FROM `dokumen_elemen_visual` WHERE `{idColumn}` = @id LIMIT 1";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var idParam = cmd.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.Value = subchapterId;
            cmd.Parameters.Add(idParam);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                page = reader["dev_page"] != DBNull.Value ? Convert.ToInt32(reader["dev_page"]) : null;
                y1 = reader["dev_bbox_y1"] != DBNull.Value ? Convert.ToDouble(reader["dev_bbox_y1"]) : null;
            }
            if (!page.HasValue || !y1.HasValue)
                return;

            var labelExpr = !string.IsNullOrWhiteSpace(labelColumn)
                ? $"LOWER(REPLACE(`{labelColumn}`, '-', '_'))"
                : null;
            var labelFilter = labelExpr != null
                ? $"AND ({labelExpr} IS NULL OR {labelExpr} NOT IN ('footnote', 'page_footer', 'footer'))"
                : string.Empty;

            var belowSql = $"SELECT 1 FROM `dokumen_elemen_visual` " +
                           $"WHERE `dev_page` = @page " +
                           $"AND `dev_bbox_y0` IS NOT NULL " +
                           $"AND `dev_bbox_y0` > @y1 " +
                           $"AND `{idColumn}` <> @id " +
                           $"{labelFilter} " +
                           "LIMIT 1";

            using var belowCmd = connection.CreateCommand();
            belowCmd.CommandText = belowSql;
            var pageParam = belowCmd.CreateParameter();
            pageParam.ParameterName = "@page";
            pageParam.Value = page.Value;
            belowCmd.Parameters.Add(pageParam);
            var y1Param = belowCmd.CreateParameter();
            y1Param.ParameterName = "@y1";
            y1Param.Value = y1.Value;
            belowCmd.Parameters.Add(y1Param);
            var idParam2 = belowCmd.CreateParameter();
            idParam2.ParameterName = "@id";
            idParam2.Value = subchapterId;
            belowCmd.Parameters.Add(idParam2);

            using var belowReader = await belowCmd.ExecuteReaderAsync(cancellationToken);
            hasElementBelow = await belowReader.ReadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check elements below subchapter from dokumen_elemen_visual");
            return;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }

        result.TotalChecks++;
        if (hasElementBelow)
        {
            result.PassedChecks++;
        }
        else
        {
            var error = new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_subbab",
                Message = "Judul subbab berada di bawah halaman, pindahkan ke halaman berikutnya",
                Expected = "Judul subbab tidak di paling bawah halaman",
                Actual = "Judul subbab terlalu dekat dengan batas bawah halaman",
                Evidence = evidence,
                Locations = locations,
                DokumenElemenId = subchapterId
            };
            if (context != null)
                ApplyContext(error, context);
            result.Errors.Add(error);
        }
    }

    private void ValidateSubchapterSequence(
        ValidationResult result,
        List<(ulong Id, ElementContentInfo Content)> subchapterElements,
        bool preventSingleSubchapter,
        Dictionary<ulong, ElementNeighborContext> contextById)
    {
        // Extract all subchapter numbers
        var numbersWithEvidence = new List<(ulong Id, int[] Parts, string Number, string Evidence)>();

        foreach (var (id, content) in subchapterElements)
        {
            var plainText = content.PlainText?.Trim() ?? string.Empty;
            var match = SubchapterNumberPattern.Match(plainText);
            if (!match.Success)
                continue;

            var numberStr = match.Value.TrimEnd('.');
            var parts = numberStr.Split('.').Select(int.Parse).ToArray();

            if (parts.Length >= 2) // Must have at least X.X format
            {
                numbersWithEvidence.Add((id, parts, numberStr, plainText));
            }
        }

        if (numbersWithEvidence.Count == 0)
            return;

        // Group by chapter (first number)
        var byChapter = numbersWithEvidence
            .GroupBy(n => n.Parts[0])
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var chapterGroup in byChapter)
        {
            var chapterNum = chapterGroup.Key;
            var subchapters = chapterGroup.OrderBy(n => string.Join(".", n.Parts)).ToList();

            // Build a set of all existing numbers for quick lookup
            var existingNumbers = new HashSet<string>(subchapters.Select(s => string.Join(".", s.Parts)));

            result.TotalChecks++;
            var hasSequenceError = false;

            foreach (var (id, parts, number, evidence) in subchapters)
            {
                var context = contextById.TryGetValue(id, out var found) ? found : null;

                // Check 1: Parent must exist (e.g., if 1.2.1 exists, 1.2 must exist)
                if (parts.Length > 2)
                {
                    var parentParts = parts.Take(parts.Length - 1).ToArray();
                    var parentNumber = string.Join(".", parentParts);

                    if (!existingNumbers.Contains(parentNumber))
                    {
                        hasSequenceError = true;
                        var error = new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "judul_subbab",
                            Message = $"Subbab {number} tidak memiliki parent {parentNumber}",
                            Expected = parentNumber,
                            Actual = number,
                            Evidence = evidence,
                            DokumenElemenId = id
                        };
                        if (context != null)
                            ApplyContext(error, context);
                        result.Errors.Add(error);
                    }
                }

                // Check 2: Previous sibling should exist (e.g., if 1.3 exists, 1.2 must exist)
                var lastPart = parts[^1];
                if (lastPart > 1)
                {
                    var prevParts = parts.ToArray();
                    prevParts[^1] = lastPart - 1;
                    var prevNumber = string.Join(".", prevParts);

                    if (!existingNumbers.Contains(prevNumber))
                    {
                        hasSequenceError = true;
                        var error = new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "judul_subbab",
                            Message = $"Subbab {prevNumber} tidak ditemukan sebelum {number}",
                            Expected = prevNumber,
                            Actual = $"Loncat ke {number}",
                            Evidence = evidence,
                            DokumenElemenId = id
                        };
                        if (context != null)
                            ApplyContext(error, context);
                        result.Errors.Add(error);
                    }
                }
            }

            // Check 3: No single subchapter (if 1.1 exists, 1.2 must also exist)
            if (preventSingleSubchapter)
            {
                // Group subchapters by their parent path
                var subchaptersByParent = subchapters
                    .GroupBy(s => string.Join(".", s.Parts.Take(s.Parts.Length - 1)))
                    .ToList();

                foreach (var parentGroup in subchaptersByParent)
                {
                    var siblingCount = parentGroup.Count();
                    if (siblingCount == 1)
                    {
                        var singleSubchapter = parentGroup.First();
                        hasSequenceError = true;
                        var context = contextById.TryGetValue(singleSubchapter.Id, out var found) ? found : null;
                        
                        // Suggest what should exist
                        var nextNumber = singleSubchapter.Parts.ToArray();
                        nextNumber[^1] = nextNumber[^1] + 1;
                        var nextNumberStr = string.Join(".", nextNumber);

                        var error = new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "judul_subbab",
                            Message = $"Subbab {singleSubchapter.Number} tidak boleh berdiri sendiri, harus ada minimal {nextNumberStr}",
                            Expected = $"Minimal 2 subbab pada level yang sama",
                            Actual = $"Hanya {singleSubchapter.Number} yang ditemukan",
                            Evidence = singleSubchapter.Evidence,
                            DokumenElemenId = singleSubchapter.Id
                        };
                        if (context != null)
                            ApplyContext(error, context);
                        result.Errors.Add(error);
                    }
                }
            }

            if (!hasSequenceError)
            {
                result.PassedChecks++;
            }
        }
    }
}




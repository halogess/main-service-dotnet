using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// ValidationService partial class for Paragraph (paragraf) validation
/// </summary>
public partial class ValidationService
{
    private async Task<ValidationResult> ValidateParagraphAsync(int dokumenId, CancellationToken cancellationToken)
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

        var paragrafDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "paragraf")
            .FirstOrDefaultAsync(cancellationToken);

        if (paragrafDetail == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = "Aturan paragraf tidak ditemukan"
            });
            return result;
        }

        ParagraphRule? rule = null;
        try
        {
            rule = JsonSerializer.Deserialize<ParagraphRule>(
                paragrafDetail.AturanDetailJsonValue ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan paragraf");
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = "Format aturan paragraf tidak valid"
            });
            return result;
        }

        if (rule == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = "Aturan paragraf tidak valid"
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
            return result;
        }

        // Load visual labels - filter for "text" label (paragraphs)
        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var orderedElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);

        var textElementIds = labelMap
            .Where(kv => kv.Value.Equals("text", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToHashSet();

        // Find paragraph elements
        var paragraphElements = new List<(ulong Id, ElementContentInfo Content)>();
        foreach (var elem in bodyElements)
        {
            if (!textElementIds.Contains(elem.DelemenId))
                continue;

            var content = ParseElementContent(elem.DelemenJsonTree);
            var plainText = content.PlainText?.Trim() ?? string.Empty;

            // Only validate non-empty paragraphs
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                paragraphElements.Add((elem.DelemenId, content));
            }
        }

        if (paragraphElements.Count == 0)
        {
            return result;
        }

        // Collect all paragraph format IDs for batch loading
        var paragraphIds = paragraphElements
            .Where(e => e.Content.ParagraphFormatId.HasValue)
            .Select(e => e.Content.ParagraphFormatId!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = await _db.DokumenFormatParagrafs
            .Where(p => paragraphIds.Contains(p.DfpId))
            .ToDictionaryAsync(p => p.DfpId, cancellationToken);

        // Collect all text format IDs for batch loading
        var textFormatIds = paragraphElements
            .SelectMany(e => e.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        // Validate each paragraph
        foreach (var (elementId, content) in paragraphElements)
        {
            var plainText = content.PlainText?.Trim() ?? string.Empty;
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

            // Load page number for error reporting
            var pageNumbers = await LoadPageNumbersAsync(new[] { elementId }, cancellationToken);
            var pageNumber = pageNumbers.Values.FirstOrDefault();

            // Load bbox for error reporting
            var mergedBbox = await LoadMergedBboxAsync(new[] { elementId }, cancellationToken);

            // Create locations for error reporting
            var locations = CreateLocations(pageNumber, mergedBbox);

            // Truncate evidence for display
            var evidence = plainText.Length > 100 ? plainText[..100] + "..." : plainText;

            // --- Font Validations ---
            ValidateParagraphFont(result, rule, elementTextFormats!, evidence, locations);

            // --- Paragraph Format Validations ---
            ValidateParagraphFormat(result, rule, paragraphFormat, evidence, locations);

            // --- Sentence Count Suggestion (non-required) ---
            ValidateParagraphSentenceCount(result, plainText, evidence, locations);

            if (neighborContexts.TryGetValue(elementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);
        }

        return result;
    }

    private void ValidateParagraphFont(
        ValidationResult result,
        ParagraphRule rule,
        List<DokumenFormatText> textFormats,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (textFormats.Count == 0)
            return;

        // Font Name
        var expectedFontName = rule?.Font?.FontName?.Value;
        if (!string.IsNullOrWhiteSpace(expectedFontName))
        {
            result.TotalChecks++;
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
                    Field = "paragraf",
                    Message = "Font paragraf tidak sesuai",
                    Expected = expectedFontName,
                    Actual = string.Join(", ", actuals),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        // Font Size
        var expectedFontSize = rule?.Font?.FontSize?.Value;
        if (expectedFontSize.HasValue)
        {
            result.TotalChecks++;
            var expectedHalfPt = expectedFontSize.Value * 2m;
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
                    Field = "paragraf",
                    Message = "Ukuran font paragraf tidak sesuai",
                    Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt" : "unknown")),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateParagraphFormat(
        ValidationResult result,
        ParagraphRule rule,
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
            // Map "justify" to "both" for Word XML compatibility
            var expectedMapped = expectedAlignment.Equals("justify", StringComparison.OrdinalIgnoreCase) ? "both" : expectedAlignment;

            if (string.Equals(actual, expectedMapped, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actual, expectedAlignment, StringComparison.OrdinalIgnoreCase))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "paragraf",
                    Message = "Alignment paragraf tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        // First Line Indent
        var expectedFirstLineIndent = rule?.Paragraph?.FirstLineIndent?.Value;
        if (expectedFirstLineIndent.HasValue)
        {
            result.TotalChecks++;

            // Get first line indent in twips and convert to cm
            var firstLineTwips = format.DfpIndFirstLineTwips ?? 0;
            var firstLineCm = firstLineTwips / 1440.0m * 2.54m;

            // Allow 0.5mm tolerance
            if (Math.Abs(firstLineCm - expectedFirstLineIndent.Value) <= 0.05m)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "paragraf",
                    Message = "First line indent paragraf tidak sesuai",
                    Expected = expectedFirstLineIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = firstLineCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
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
                    Field = "paragraf",
                    Message = "Line spacing paragraf tidak sesuai",
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
                    Field = "paragraf",
                    Message = "Spacing before paragraf tidak sesuai",
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
                    Field = "paragraf",
                    Message = "Spacing after paragraf tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private static void ValidateParagraphSentenceCount(
        ValidationResult result,
        string plainText,
        string evidence,
        List<ErrorLocation> locations)
    {
        // Count sentences (split by . ! ?)
        var sentencePattern = new Regex(@"[.!?]+\s*", RegexOptions.Compiled);
        var sentences = sentencePattern.Split(plainText)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Count();

        // Ideal: 3-6 sentences per paragraph
        const int minSentences = 3;
        const int maxSentences = 6;

        result.TotalChecks++;
        if (sentences >= minSentences && sentences <= maxSentences)
        {
            result.PassedChecks++;
        }
        else
        {
            string message;
            if (sentences < minSentences)
            {
                message = $"Paragraf terlalu pendek ({sentences} kalimat), idealnya {minSentences}-{maxSentences} kalimat";
            }
            else
            {
                message = $"Paragraf terlalu panjang ({sentences} kalimat), idealnya {minSentences}-{maxSentences} kalimat";
            }

            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = message,
                Expected = $"{minSentences}-{maxSentences} kalimat",
                Actual = $"{sentences} kalimat",
                Evidence = evidence,
                Locations = locations,
                IsRequired = false // Non-required, just a suggestion
            });
        }
    }
}



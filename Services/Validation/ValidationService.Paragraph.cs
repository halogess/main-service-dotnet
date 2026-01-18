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

        ListItemRule? listRule = null;
        var listItemDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "item_daftar")
            .FirstOrDefaultAsync(cancellationToken);

        if (listItemDetail != null)
        {
            try
            {
                listRule = JsonSerializer.Deserialize<ListItemRule>(
                    listItemDetail.AturanDetailJsonValue ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse aturan item_daftar");
            }
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

        // Load visual labels (used to exclude headers)
        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var elementTypeById = bodyElements.ToDictionary(e => e.DelemenId, e => e.DelemenType);
        var orderedElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);

        var contentCache = new Dictionary<ulong, ElementContentInfo>();
        ElementContentInfo GetContent(ulong elementId, string? json)
        {
            if (!contentCache.TryGetValue(elementId, out var content))
            {
                content = ParseElementContent(json);
                contentCache[elementId] = content;
            }
            return content;
        }

        // Find paragraph elements in OpenXML order
        var paragraphElements = new List<(ulong Id, ElementContentInfo Content)>();
        foreach (var elem in bodyElements)
        {
            if (!IsParagraphElement(elem.DelemenType))
                continue;

            if (!labelMap.TryGetValue(elem.DelemenId, out var label) ||
                !label.Equals("text", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = GetContent(elem.DelemenId, elem.DelemenJsonTree);
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

        var paragraphIdSet = new HashSet<ulong>(paragraphElements.Select(e => e.Id));
        var specialListFormatByParagraphId = new Dictionary<ulong, uint>();
        var listItemFormatIds = new HashSet<uint>();
        var listItemTextFormatIds = new HashSet<uint>();
        var listBlocks = new List<(List<ulong> ListItemIds, List<ulong> ParagraphsAfter, uint? LastFormatId)>();

        for (var i = 0; i < bodyElements.Count; i++)
        {
            if (!IsListItemElement(bodyElements[i].DelemenType))
                continue;

            var listItemIds = new List<ulong>();
            var listEnd = i;
            while (listEnd < bodyElements.Count &&
                   IsListItemElement(bodyElements[listEnd].DelemenType))
            {
                var listElem = bodyElements[listEnd];
                listItemIds.Add(listElem.DelemenId);
                var listContent = GetContent(listElem.DelemenId, listElem.DelemenJsonTree);
                if (listContent.ParagraphFormatId.HasValue)
                    listItemFormatIds.Add(listContent.ParagraphFormatId.Value);
                foreach (var textId in listContent.TextFormatIds)
                    listItemTextFormatIds.Add(textId);
                listEnd++;
            }

            var lastListElement = bodyElements[listEnd - 1];
            var lastListContent = GetContent(lastListElement.DelemenId, lastListElement.DelemenJsonTree);
            var listFormatId = lastListContent.ParagraphFormatId;

            var paragraphsAfter = new List<ulong>();
            for (var j = listEnd; j < bodyElements.Count; j++)
            {
                var nextElem = bodyElements[j];
                if (!IsParagraphElement(nextElem.DelemenType))
                    break;

                if (!labelMap.TryGetValue(nextElem.DelemenId, out var nextLabel) ||
                    !nextLabel.Equals("text", StringComparison.OrdinalIgnoreCase))
                    break;

                if (paragraphIdSet.Contains(nextElem.DelemenId))
                    paragraphsAfter.Add(nextElem.DelemenId);
            }

            listBlocks.Add((listItemIds, paragraphsAfter, listFormatId));
            if (paragraphsAfter.Count < 2 && listFormatId.HasValue)
            {
                foreach (var paragraphId in paragraphsAfter)
                    specialListFormatByParagraphId[paragraphId] = listFormatId.Value;
            }

            i = listEnd - 1;
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

        var listFormats = listItemFormatIds.Count > 0
            ? await _db.DokumenFormatParagrafs
                .Where(p => listItemFormatIds.Contains(p.DfpId))
                .ToDictionaryAsync(p => p.DfpId, cancellationToken)
            : new Dictionary<uint, DokumenFormatParagraf>();

        var listTextFormats = listRule != null && listItemTextFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => listItemTextFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        var listIndentByParagraphId = new Dictionary<ulong, decimal>();
        foreach (var pair in specialListFormatByParagraphId)
        {
            if (!listFormats.TryGetValue(pair.Value, out var listFormat))
                continue;

            var hangingTwips = listFormat.DfpIndHangingTwips ?? 0;
            var hangingCm = hangingTwips / 1440.0m * 2.54m;
            listIndentByParagraphId[pair.Key] = hangingCm;
        }

        var listInvalidParagraphIds = new HashSet<ulong>();
        if (listRule != null && listBlocks.Count > 0)
        {
            var emptyLocations = new List<ErrorLocation>();
            foreach (var block in listBlocks)
            {
                if (block.ParagraphsAfter.Count == 0)
                    continue;

                var isValid = true;
                foreach (var listItemId in block.ListItemIds)
                {
                    var content = GetContent(listItemId, elementJsonById.TryGetValue(listItemId, out var json) ? json : null);
                    var plainText = content.PlainText?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(plainText))
                        continue;

                    var tempResult = new ValidationResult();
                    DokumenFormatParagraf? format = null;
                    if (content.ParagraphFormatId.HasValue)
                        listFormats.TryGetValue(content.ParagraphFormatId.Value, out format);

                    var elementTextFormats = content.TextFormatIds
                        .Select(id => listTextFormats.TryGetValue(id, out var tf) ? tf : null)
                        .Where(tf => tf != null)
                        .ToList();

                    elementTypeById.TryGetValue(listItemId, out var elementType);
                    var level = TryParseListItemLevel(elementType, format);
                    var evidence = plainText.Length > 100 ? plainText[..100] + "..." : plainText;

                    ValidateListItemFont(tempResult, listRule, elementTextFormats!, content.TextRuns, evidence, emptyLocations);
                    ValidateListItemParagraph(tempResult, listRule, format, level, evidence, emptyLocations);

                    if (tempResult.Errors.Count > 0)
                    {
                        isValid = false;
                        break;
                    }
                }

                if (!isValid)
                {
                    foreach (var paragraphId in block.ParagraphsAfter)
                        listInvalidParagraphIds.Add(paragraphId);
                }
            }
        }

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

            // Load page info for error reporting
            var pageNumbers = await LoadPageNumbersAsync(new[] { elementId }, cancellationToken);
            var pageBboxMap = await LoadPageBboxMapAsync(new[] { elementId }, cancellationToken);

            // Create locations for error reporting
            var locations = CreateLocations(pageNumbers.Values, pageBboxMap);

            // Truncate evidence for display
            var evidence = plainText.Length > 100 ? plainText[..100] + "..." : plainText;

            // --- Font Validations ---
            ValidateParagraphFont(result, rule, elementTextFormats!, content.TextRuns, evidence, locations);

            // --- Paragraph Format Validations ---
            var listIndentOverride = listIndentByParagraphId.TryGetValue(elementId, out var listIndentCm)
                ? listIndentCm
                : (decimal?)null;
            ValidateParagraphFormat(result, rule, paragraphFormat, evidence, locations, listIndentOverride);

            if (listInvalidParagraphIds.Contains(elementId))
            {
                result.TotalChecks++;
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "paragraf",
                    Message = "Paragraf setelah list dianggap tidak valid karena list di atas tidak sesuai aturan",
                    Expected = "List di atas valid",
                    Actual = "List di atas tidak sesuai aturan",
                    Evidence = evidence,
                    Locations = locations
                });
            }

            // --- Sentence Count Suggestion (non-required) ---
            ValidateParagraphSentenceCount(result, plainText, evidence, locations);

            if (neighborContexts.TryGetValue(elementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, elementId);
        }

        return result;
    }

    private void ValidateParagraphFont(
        ValidationResult result,
        ParagraphRule rule,
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
                        Field = "paragraf",
                        Message = "Font paragraf tidak sesuai",
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
                        Field = "paragraf",
                        Message = "Font paragraf tidak sesuai",
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
                        Field = "paragraf",
                        Message = "Ukuran font paragraf tidak sesuai",
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
    }

    private void ValidateParagraphFormat(
        ValidationResult result,
        ParagraphRule rule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations,
        decimal? listIndentOverrideCm)
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
                    Field = "paragraf",
                    Message = "Alignment paragraf tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (listIndentOverrideCm.HasValue)
        {
            result.TotalChecks++;
            var leftTwips = format.DfpIndLeftTwips ?? format.DfpIndStartTwips ?? 0;
            var leftCm = leftTwips / 1440.0m * 2.54m;

            if (Math.Abs(leftCm - listIndentOverrideCm.Value) <= 0.05m)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "paragraf",
                    Message = "Left indent paragraf setelah list tidak sesuai",
                    Expected = listIndentOverrideCm.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = leftCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }

            result.TotalChecks++;
            var hangingTwips = format.DfpIndHangingTwips ?? 0;
            var hangingCm = hangingTwips / 1440.0m * 2.54m;

            if (Math.Abs(hangingCm) <= 0.05m)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "paragraf",
                    Message = "Hanging indent paragraf setelah list tidak sesuai",
                    Expected = "0 cm",
                    Actual = hangingCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
        else
        {
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



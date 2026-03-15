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
    private sealed class ParagraphListBlockContext
    {
        public int StartIndex { get; init; }
        public int ListEndIndex { get; init; }
        public int AfterParagraphEndIndex { get; init; }
        public List<ulong> ListItemIds { get; init; } = new();
        public List<ulong> ParagraphsAfter { get; init; } = new();
        public uint? LastFormatId { get; init; }
        public ulong LastElementId { get; init; }
        public string? LastElementType { get; init; }
        public string? LastLabel { get; init; }
        public int SetId { get; set; }
    }

    private async Task<ValidationResult> ValidateParagraphAsync(
        int dokumenId,
        HashSet<ulong>? paragraphIds,
        HashSet<ulong>? listItemIds,
        CancellationToken cancellationToken)
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

        var aturan = await _db.Aturans
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (aturan == null)
        {
            return result;
        }

        var paragrafDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "paragraf")
            .FirstOrDefaultAsync(cancellationToken);

        if (paragrafDetail == null)
        {
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

        var (sectionRefType, sectionRefId) = ResolveSectionRefForValidation(dokumenId);
        // Get all body elements
        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DsecRefTipe == sectionRefType && s.DsecRefId == sectionRefId && p.DpartType == "body"
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
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => (string?)e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);
        var pageLayoutsById = await LoadPageLayoutsAsync(orderedElementIds, cancellationToken);

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

        string GetNormalizedLabel(ulong elementId)
        {
            return labelMap.TryGetValue(elementId, out var label)
                ? NormalizeLabel(label)
                : string.Empty;
        }

        bool IsListCandidate(ulong elementId, string? elementType)
        {
            if (listItemIds != null)
                return listItemIds.Contains(elementId);

            var label = GetNormalizedLabel(elementId);
            if (!string.IsNullOrEmpty(label))
                return IsListLabel(label);

            return false;
        }

        // Find paragraph elements in OpenXML order
        var paragraphElements = new List<(ulong Id, ElementContentInfo Content)>();
        foreach (var elem in bodyElements)
        {
            if (!IsParagraphElement(elem.DelemenType))
                continue;

            if (paragraphIds != null)
            {
                if (!paragraphIds.Contains(elem.DelemenId))
                    continue;
            }
            else
            {
                if (!labelMap.TryGetValue(elem.DelemenId, out var label))
                    continue;

                var normalizedLabel = NormalizeLabel(label);
                if (normalizedLabel != "paragraf")
                    continue;
            }

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
        var listItemFormatIds = new HashSet<uint>();
        var listItemTextFormatIds = new HashSet<uint>();
        var listBlocks = new List<ParagraphListBlockContext>();

        for (var i = 0; i < bodyElements.Count; i++)
        {
            if (!IsListCandidate(bodyElements[i].DelemenId, bodyElements[i].DelemenType))
                continue;

            var listBlockItemIds = new List<ulong>();
            var listEnd = i;
            while (listEnd < bodyElements.Count &&
                   IsListCandidate(bodyElements[listEnd].DelemenId, bodyElements[listEnd].DelemenType))
            {
                var listElem = bodyElements[listEnd];
                listBlockItemIds.Add(listElem.DelemenId);
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
            var lastListLabel = labelMap.TryGetValue(lastListElement.DelemenId, out var rawLabel)
                ? NormalizeLabel(rawLabel)
                : null;

            var paragraphsAfter = new List<ulong>();
            var afterParagraphEndIndex = listEnd - 1;
            for (var j = listEnd; j < bodyElements.Count; j++)
            {
                var nextElem = bodyElements[j];
                if (!IsParagraphElement(nextElem.DelemenType))
                    break;

                if (!labelMap.TryGetValue(nextElem.DelemenId, out var nextLabel))
                {
                    var unlabeledContent = GetContent(nextElem.DelemenId, nextElem.DelemenJsonTree);
                    if (!string.IsNullOrWhiteSpace(unlabeledContent.PlainText))
                        break;

                    // Ignore unlabeled blank paragraphs between list item and its explanation.
                    afterParagraphEndIndex = j;
                    continue;
                }

                var normalizedLabel = NormalizeLabel(nextLabel);
                if (normalizedLabel != "paragraf")
                    break;

                // Keep the structural end index on the last contiguous paragraph element,
                // even if this paragraph is skipped from paragraph-only validation.
                afterParagraphEndIndex = j;

                if (paragraphIdSet.Contains(nextElem.DelemenId))
                    paragraphsAfter.Add(nextElem.DelemenId);
            }
            listBlocks.Add(new ParagraphListBlockContext
            {
                StartIndex = i,
                ListEndIndex = listEnd - 1,
                AfterParagraphEndIndex = afterParagraphEndIndex,
                ListItemIds = listBlockItemIds,
                ParagraphsAfter = paragraphsAfter,
                LastFormatId = listFormatId,
                LastElementId = lastListElement.DelemenId,
                LastElementType = lastListElement.DelemenType,
                LastLabel = lastListLabel
            });

            i = listEnd - 1;
        }

        var singleParagraphExplanationBySetId = new Dictionary<int, bool>();
        if (listBlocks.Count > 0)
        {
            var currentSetId = 0;
            for (var i = 0; i < listBlocks.Count; i++)
            {
                if (i > 0 && listBlocks[i].StartIndex > listBlocks[i - 1].AfterParagraphEndIndex + 1)
                    currentSetId++;

                listBlocks[i].SetId = currentSetId;
            }

            singleParagraphExplanationBySetId = listBlocks
                .GroupBy(b => b.SetId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var totalItems = g.Count();
                        var withExplanation = g.Count(b => b.ParagraphsAfter.Count > 0);
                        return totalItems > 1 && withExplanation * 2 > totalItems;
                    });
        }

        // Collect all paragraph format IDs for batch loading
        var paragraphFormatIds = paragraphElements
            .Where(e => e.Content.ParagraphFormatId.HasValue)
            .Select(e => e.Content.ParagraphFormatId!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = await _db.DokumenFormatParagrafs
            .Where(p => paragraphFormatIds.Contains(p.DfpId))
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
        var listFirstLineIndentByParagraphId = new Dictionary<ulong, decimal>();
        var listHangingIndentByParagraphId = new Dictionary<ulong, decimal>();
        if (listBlocks.Count > 0)
        {
            var expectedListHanging = listRule?.Paragraph?.Indentation?.Hanging?.Value;
            var expectedListLeft = listRule?.Paragraph?.Indentation?.LeftIndent?.Value;

            foreach (var block in listBlocks)
            {
                if (block.ParagraphsAfter.Count == 0)
                    continue;

                var hasSingleParagraphAfter = block.ParagraphsAfter.Count == 1;
                if (hasSingleParagraphAfter &&
                    (!singleParagraphExplanationBySetId.TryGetValue(block.SetId, out var treatAsExplanation) || !treatAsExplanation))
                {
                    continue;
                }

                DokumenFormatParagraf? listFormat = null;
                if (block.LastFormatId.HasValue)
                    listFormats.TryGetValue(block.LastFormatId.Value, out listFormat);

                var level = TryParseListItemLevel(block.LastElementType, listFormat, block.LastLabel).GetValueOrDefault(0);
                decimal? expectedTextStartCm = null;
                if (expectedListLeft.HasValue || expectedListHanging.HasValue)
                {
                    var baseLeft = expectedListLeft ?? 0m;
                    var hanging = expectedListHanging ?? 0m;
                    expectedTextStartCm = baseLeft + (level + 1) * hanging;
                }
                else if (listFormat != null)
                {
                    var leftTwips = listFormat.DfpIndLeftTwips.HasValue && listFormat.DfpIndLeftTwips.Value != 0
                        ? listFormat.DfpIndLeftTwips.Value
                        : listFormat.DfpIndStartTwips ?? 0;
                    expectedTextStartCm = leftTwips / 1440.0m * 2.54m;
                }

                if (expectedTextStartCm.HasValue)
                {
                    foreach (var paragraphId in block.ParagraphsAfter)
                        listIndentByParagraphId[paragraphId] = expectedTextStartCm.Value;
                }

                if (block.ParagraphsAfter.Count < 2)
                {
                    foreach (var paragraphId in block.ParagraphsAfter)
                    {
                        listFirstLineIndentByParagraphId[paragraphId] = 0m;
                        listHangingIndentByParagraphId[paragraphId] = 0m;
                    }
                }
            }
        }

        var skipSentenceCountParagraphIds = new HashSet<ulong>();
        foreach (var block in listBlocks)
        {
            if (block.ParagraphsAfter.Count == 1 &&
                singleParagraphExplanationBySetId.TryGetValue(block.SetId, out var treatAsExplanation) &&
                treatAsExplanation)
            {
                foreach (var paragraphId in block.ParagraphsAfter)
                    skipSentenceCountParagraphIds.Add(paragraphId);
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
            var listFirstLineOverride = listFirstLineIndentByParagraphId.TryGetValue(elementId, out var listFirstLineCm)
                ? listFirstLineCm
                : (decimal?)null;
            var listHangingOverride = listHangingIndentByParagraphId.TryGetValue(elementId, out var listHangingCm)
                ? listHangingCm
                : (decimal?)null;
            pageLayoutsById.TryGetValue(elementId, out var pageLayout);
            ValidateParagraphFormat(
                result,
                rule,
                paragraphFormat,
                evidence,
                locations,
                listIndentOverride,
                listFirstLineOverride,
                listHangingOverride,
                plainText,
                pageLayout);

            // --- Sentence Count Suggestion (non-required) ---
            if (!skipSentenceCountParagraphIds.Contains(elementId))
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
            result.IncrementTotalChecks();
            if (runs.Count > 0)
            {
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
                    result.IncrementPassedChecks();
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
            result.IncrementTotalChecks();
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
                    result.IncrementPassedChecks();
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
                    result.IncrementPassedChecks();
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
        decimal? leftIndentOverrideCm,
        decimal? firstLineIndentOverrideCm,
        decimal? hangingIndentOverrideCm,
        string paragraphText,
        PageLayoutSnapshot? pageLayout)
    {
        if (format == null)
            return;

        // Alignment
        var expectedAlignment = rule?.Paragraph?.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks();
            var actual = format.DfpJc ?? "unknown";
            var alignmentContext = CreateAlignmentContext(paragraphText, locations, pageLayout);
            if (AreAlignmentsEquivalent(actual, expectedAlignment, alignmentContext))
            {
                result.IncrementPassedChecks();
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

        result.IncrementTotalChecks();
        var expectedLeftIndent = leftIndentOverrideCm ?? 0m;
        var leftTwips = format.DfpIndLeftTwips.HasValue && format.DfpIndLeftTwips.Value != 0
            ? format.DfpIndLeftTwips.Value
            : format.DfpIndStartTwips ?? 0;
        var leftCm = leftTwips / 1440.0m * 2.54m;

        if (Math.Abs(leftCm - expectedLeftIndent) <= 0.05m)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            var message = leftIndentOverrideCm.HasValue
                ? "Left indent paragraf setelah list tidak sesuai"
                : "Left indent paragraf harus 0";
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = message,
                Expected = expectedLeftIndent.ToString(CultureInfo.InvariantCulture) + " cm",
                Actual = leftCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                Evidence = evidence,
                Locations = locations
            });
        }

        var expectedFirstLineIndent = firstLineIndentOverrideCm ?? rule?.Paragraph?.FirstLineIndent?.Value;
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
                var message = firstLineIndentOverrideCm.HasValue
                    ? "First line indent paragraf setelah list tidak sesuai"
                    : "First line indent paragraf tidak sesuai";
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "paragraf",
                    Message = message,
                    Expected = expectedFirstLineIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = firstLineCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (hangingIndentOverrideCm.HasValue)
        {
            result.IncrementTotalChecks();
            var hangingTwips = format.DfpIndHangingTwips ?? 0;
            var hangingCm = hangingTwips / 1440.0m * 2.54m;

            if (Math.Abs(hangingCm - hangingIndentOverrideCm.Value) <= 0.05m)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "paragraf",
                    Message = "Hanging indent paragraf setelah list tidak sesuai",
                    Expected = hangingIndentOverrideCm.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = hangingCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
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
                Field = "paragraf",
                Message = "Right indent paragraf harus 0",
                Expected = "0 cm",
                Actual = rightCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                Evidence = evidence,
                Locations = locations
            });
        }

        // Line Spacing
        var spacingRule = rule?.Paragraph?.Spacing;
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
            result.IncrementTotalChecks();
            var expected = spacingRule.Before.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingBeforeTwips);

            if (actual.HasValue && IsWithinTolerance(actual.Value, expected, 0.5m) && !format.DfpSpacingBeforeAutospacing)
            {
                result.IncrementPassedChecks();
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
            result.IncrementTotalChecks();
            var expected = spacingRule.After.Value.Value;
            var actual = TwipsToPoints(format.DfpSpacingAfterTwips);

            if (actual.HasValue && IsWithinTolerance(actual.Value, expected, 0.5m) && !format.DfpSpacingAfterAutospacing)
            {
                result.IncrementPassedChecks();
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

        // Ideal: minimum sentences per paragraph
        const int minSentences = 3;

        result.IncrementTotalChecks();
        if (sentences >= minSentences)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = $"Paragraf terlalu pendek ({sentences} kalimat), idealnya minimal {minSentences} kalimat",
                Expected = $"Minimal {minSentences} kalimat",
                Actual = $"{sentences} kalimat",
                Evidence = evidence,
                Locations = locations,
                IsRequired = false // Non-required, just a suggestion
            });
        }
    }
}




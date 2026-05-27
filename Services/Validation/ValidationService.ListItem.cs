using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// ValidationService partial class for List Item (item_daftar) validation
/// </summary>
public partial class ValidationService
{
    private async Task<ValidationResult> ValidateListItemAsync(
        int dokumenId,
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
            .Where(a => a.AturanStatus == AturanStatusValues.Aktif)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (aturan == null)
        {
            return result;
        }

        var listItemDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "item_daftar")
            .FirstOrDefaultAsync(cancellationToken);

        if (listItemDetail == null)
        {
            return result;
        }

        ListItemRule? rule = null;
        try
        {
            rule = JsonSerializer.Deserialize<ListItemRule>(
                listItemDetail.AturanDetailJsonValue ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan item_daftar");
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "item_daftar",
                Message = "Format aturan item daftar tidak valid"
            });
            return result;
        }

        if (rule == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "item_daftar",
                Message = "Aturan item daftar tidak valid"
            });
            return result;
        }

        var (sectionRefType, sectionRefId) = ResolveSectionRefForValidation(dokumenId);
        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DsecRefTipe == sectionRefType && s.DsecRefId == sectionRefId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new BodyElementInfo
            {
                DelemenId = e.DelemenId,
                DelemenType = e.DelemenType,
                DelemenJsonTree = e.DelemenJsonTree
            })
            .ToListAsync(cancellationToken);

        if (bodyElements.Count == 0)
        {
            return result;
        }

        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var orderedElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => (string?)e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);
        var pageLayoutsById = await LoadPageLayoutsAsync(orderedElementIds, cancellationToken);

        var listItemElements = new List<(ulong Id, string? Type, string? Label, ElementContentInfo Content)>();
        foreach (var elem in bodyElements)
        {
            var content = ParseElementContent(elem.DelemenJsonTree);
            var plainText = content.PlainText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(plainText))
                continue;

            var normalizedLabel = labelMap.TryGetValue(elem.DelemenId, out var rawLabel)
                ? NormalizeLabel(rawLabel)
                : string.Empty;

            var isListCandidate = listItemIds != null
                ? listItemIds.Contains(elem.DelemenId)
                : !string.IsNullOrEmpty(normalizedLabel) && IsListLabel(normalizedLabel);

            if (!isListCandidate)
                continue;

            listItemElements.Add((elem.DelemenId, elem.DelemenType, normalizedLabel, content));
        }

        if (listItemElements.Count == 0)
        {
            return result;
        }

        var paragraphIds = listItemElements
            .Where(e => e.Content.ParagraphFormatId.HasValue)
            .Select(e => e.Content.ParagraphFormatId!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = await _db.DokumenFormatParagrafs
            .Where(p => paragraphIds.Contains(p.DfpId))
            .ToDictionaryAsync(p => p.DfpId, cancellationToken);

        var textFormatIds = listItemElements
            .SelectMany(e => e.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        var listItemErrorStart = result.Errors.Count;
        var listLevelByElementId = new Dictionary<ulong, string>(listItemElements.Count);
        var subchapterIndexByElementId = BuildSubchapterIndexMap(bodyElements, labelMap);

        foreach (var (elementId, elementType, elementLabel, content) in listItemElements)
        {
            var rawText = content.PlainText ?? string.Empty;
            var plainText = rawText.Trim();
            var evidence = plainText.Length > 100 ? plainText[..100] + "..." : plainText;
            var errorStart = result.Errors.Count;

            DokumenFormatParagraf? paragraphFormat = null;
            if (content.ParagraphFormatId.HasValue)
            {
                paragraphFormats.TryGetValue(content.ParagraphFormatId.Value, out paragraphFormat);
            }

            var elementTextFormats = content.TextFormatIds
                .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                .Where(tf => tf != null)
                .ToList();

            var pageNumbers = await LoadPageNumbersAsync(new[] { elementId }, cancellationToken);
            var pageBboxMap = await LoadPageBboxMapAsync(new[] { elementId }, cancellationToken);
            var locations = CreateLocations(pageNumbers.Values, pageBboxMap);
            pageLayoutsById.TryGetValue(elementId, out var pageLayout);

            var level = TryParseListItemLevel(elementType, paragraphFormat, elementLabel);
            var subchapterIndex = subchapterIndexByElementId.TryGetValue(elementId, out var subchapter)
                ? subchapter
                : 0;
            var mergeSet = level.HasValue
                ? $"subbab_{subchapterIndex}_list_level_{level.Value + 1}"
                : $"subbab_{subchapterIndex}_list_level_unknown";
            listLevelByElementId[elementId] = mergeSet;

            ValidateListItemFont(result, rule, elementTextFormats!, content.TextRuns, evidence, locations);
            ValidateListItemParagraph(result, rule, paragraphFormat, level, evidence, locations, plainText, rawText, pageLayout);

            if (neighborContexts.TryGetValue(elementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, elementId);
        }

        MergeIdenticalListItemErrorsByLevel(result.Errors, listItemErrorStart, listLevelByElementId);

        return result;
    }

    private static Dictionary<ulong, int> BuildSubchapterIndexMap(
        IReadOnlyList<BodyElementInfo> bodyElements,
        IReadOnlyDictionary<ulong, string> labelMap)
    {
        var map = new Dictionary<ulong, int>();
        var subchapterIndex = 0;

        foreach (var elem in bodyElements)
        {
            var elementId = elem.DelemenId;
            if (labelMap.TryGetValue(elementId, out var rawLabel))
            {
                var normalized = NormalizeLabel(rawLabel);
                if (normalized == "judul_subbab")
                    subchapterIndex++;
            }

            map[elementId] = subchapterIndex;
        }

        return map;
    }

    private readonly record struct ListItemErrorMergeKey(
        string SetKey,
        int PageNumber,
        string Category,
        string Field,
        string Message,
        string Expected,
        string Actual,
        string DiffType,
        string Cause,
        bool? HasNumbering,
        string StyleName,
        string StyleId,
        string ToolRequirement,
        string FeatureName,
        string ScopeHint,
        string PageRange,
        string AllowedActions,
        string DisallowedActions);

    private static void MergeIdenticalListItemErrorsByLevel(
        IList<ValidationError> errors,
        int startIndex,
        IReadOnlyDictionary<ulong, string> listLevelByElementId)
    {
        if (errors.Count == 0 || startIndex >= errors.Count || listLevelByElementId.Count == 0)
            return;

        var merged = new List<ValidationError>();
        var grouped = new Dictionary<ListItemErrorMergeKey, int>();

        for (var i = startIndex; i < errors.Count; i++)
        {
            var error = errors[i];
            var mergeKey = TryBuildListItemErrorMergeKey(error, listLevelByElementId);
            if (!mergeKey.HasValue)
            {
                merged.Add(error);
                continue;
            }

            if (!grouped.TryGetValue(mergeKey.Value, out var mergedIndex))
            {
                grouped[mergeKey.Value] = merged.Count;
                merged.Add(error);
                continue;
            }

            var target = merged[mergedIndex];
            target.Locations = AppendErrorLocations(target.Locations, error.Locations);
            target.AddValidationCheckKeys(error.ValidationCheckKeys);
            target.IsHardConstraint = target.IsHardConstraint || error.IsHardConstraint;

            if (!target.DokumenElemenId.HasValue && error.DokumenElemenId.HasValue)
                target.DokumenElemenId = error.DokumenElemenId;
        }

        ReplaceErrorTail(errors, startIndex, merged);
    }

    private static ListItemErrorMergeKey? TryBuildListItemErrorMergeKey(
        ValidationError error,
        IReadOnlyDictionary<ulong, string> listLevelByElementId)
    {
        if (!string.Equals(error.Field, "item_daftar", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!error.DokumenElemenId.HasValue ||
            !listLevelByElementId.TryGetValue(error.DokumenElemenId.Value, out var setKey))
            return null;

        var pageNumber = TryGetSingleLocationPage(error.Locations);
        if (!pageNumber.HasValue)
            return null;

        return new ListItemErrorMergeKey(
            setKey,
            pageNumber.Value,
            error.Category ?? string.Empty,
            error.Field ?? string.Empty,
            error.Message ?? string.Empty,
            error.Expected ?? string.Empty,
            error.Actual ?? string.Empty,
            error.DiffType ?? string.Empty,
            error.Cause ?? string.Empty,
            error.HasNumbering,
            error.StyleName ?? string.Empty,
            error.StyleId ?? string.Empty,
            error.ToolRequirement ?? string.Empty,
            error.FeatureName ?? string.Empty,
            error.ScopeHint ?? string.Empty,
            error.PageRange ?? string.Empty,
            BuildActionToken(error.AllowedActions),
            BuildActionToken(error.DisallowedActions));
    }

    private static List<ErrorLocation> AppendErrorLocations(
        IReadOnlyList<ErrorLocation> first,
        IReadOnlyList<ErrorLocation> second)
    {
        var combined = new List<ErrorLocation>();
        AddLocations(combined, first);
        AddLocations(combined, second);
        return combined;
    }

    private static void AddLocations(
        List<ErrorLocation> target,
        IReadOnlyList<ErrorLocation> source)
    {
        if (source == null || source.Count == 0)
            return;

        foreach (var loc in source)
        {
            if (loc == null)
                continue;

            target.Add(new ErrorLocation
            {
                HalamanKe = loc.HalamanKe,
                Bbox = loc.Bbox == null
                    ? null
                    : new ErrorBbox
                    {
                        X0 = loc.Bbox.X0,
                        Y0 = loc.Bbox.Y0,
                        X1 = loc.Bbox.X1,
                        Y1 = loc.Bbox.Y1
                    }
            });
        }
    }

    private void ValidateListItemFont(
        ValidationResult result,
        ListItemRule rule,
        List<DokumenFormatText> textFormats,
        IReadOnlyList<TextRunInfo> textRuns,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (textFormats.Count == 0)
            return;

        var textFormatById = BuildTextFormatMap(textFormats);
        var runs = GetMeaningfulRuns(textRuns);

        var expectedFontName = rule?.Font?.FontName?.Value;
        if (!string.IsNullOrWhiteSpace(expectedFontName))
        {
            result.IncrementTotalChecks(rule.Font?.FontName?.IsHardConstraint == true);
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
                        Field = "item_daftar",
                        Message = "Font item daftar tidak sesuai",
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
                        Field = "item_daftar",
                        Message = "Font item daftar tidak sesuai",
                        Expected = expectedFontName,
                        Actual = string.Join(", ", actuals),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedFontSize = rule?.Font?.FontSize?.Value;
        if (expectedFontSize.HasValue)
        {
            result.IncrementTotalChecks(rule.Font?.FontSize?.IsHardConstraint == true);
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
                        Field = "item_daftar",
                        Message = "Ukuran font item daftar tidak sesuai",
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
                        Field = "item_daftar",
                        Message = "Ukuran font item daftar tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt" : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

    }

    private void ValidateListItemParagraph(
        ValidationResult result,
        ListItemRule rule,
        DokumenFormatParagraf? format,
        int? level,
        string evidence,
        List<ErrorLocation> locations,
        string paragraphText,
        string rawParagraphText,
        PageLayoutSnapshot? pageLayout)
    {
        if (format == null)
            return;

        var expectedAlignment = rule?.Paragraph?.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks(rule.Paragraph?.Alignment?.IsHardConstraint == true);
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
                    Field = "item_daftar",
                    Message = "Alignment item daftar tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var expectedLeftIndent = rule?.Paragraph?.Indentation?.LeftIndent?.Value;
        var expectedHanging = rule?.Paragraph?.Indentation?.Hanging?.Value;
        var levelValue = level.GetValueOrDefault(0);

        var leftTwips = format.DfpIndLeftTwips.HasValue && format.DfpIndLeftTwips.Value != 0
            ? format.DfpIndLeftTwips.Value
            : format.DfpIndStartTwips ?? 0;
        var leftCm = leftTwips / 1440.0m * 2.54m;
        var firstLineCm = (format.DfpIndFirstLineTwips ?? 0) / 1440.0m * 2.54m;
        var hangingTwips = format.DfpIndHangingTwips ?? 0;
        var hangingCm = hangingTwips / 1440.0m * 2.54m;
        var normalizedLeftCm = Math.Max(0m, leftCm - hangingCm);

        decimal? expectedNormalizedLeftCm = null;
        if (expectedLeftIndent.HasValue)
        {
            expectedNormalizedLeftCm = expectedLeftIndent.Value;
            if (expectedHanging.HasValue && levelValue > 0)
                expectedNormalizedLeftCm += levelValue * expectedHanging.Value;
        }

        var hasParagraphLikeIndentShape = expectedNormalizedLeftCm.HasValue &&
                                          expectedHanging.HasValue &&
                                          Math.Abs(hangingCm) <= 0.05m &&
                                          Math.Abs(firstLineCm) <= 0.05m &&
                                          Math.Abs(leftCm - (expectedNormalizedLeftCm.Value + expectedHanging.Value)) <= 0.05m;

        if (expectedHanging.HasValue)
        {
            result.IncrementTotalChecks(rule.Paragraph?.Indentation?.Hanging?.IsHardConstraint == true);
            if (Math.Abs(hangingCm - expectedHanging.Value) <= 0.05m || hasParagraphLikeIndentShape)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "item_daftar",
                    Message = "Hanging indent item daftar tidak sesuai",
                    Expected = expectedHanging.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = hangingCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (expectedLeftIndent.HasValue)
        {
            result.IncrementTotalChecks(rule.Paragraph?.Indentation?.LeftIndent?.IsHardConstraint == true);
            var expectedLeftCm = expectedNormalizedLeftCm ?? expectedLeftIndent.Value;

            if (Math.Abs(normalizedLeftCm - expectedLeftCm) <= 0.05m || hasParagraphLikeIndentShape)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                var levelSuffix = levelValue > 0 ? $" (level {levelValue})" : string.Empty;
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "item_daftar",
                    Message = "Left indent item daftar tidak sesuai" + levelSuffix,
                    Expected = expectedLeftCm.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = normalizedLeftCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        // For list items, special indentation is represented by hanging indent only.
        // A non-zero first line indent would conflict with that model and must stay 0.
        var expectedFirstLineIndent = 0m;
        result.IncrementTotalChecks(false);
        var firstLineObservation = ObserveFirstLineIndent(format, rawParagraphText);
        if (Math.Abs(firstLineObservation.ActualCm - expectedFirstLineIndent) <= 0.05m &&
            !firstLineObservation.HasLeadingManualIndent)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            var message = "First line indent item daftar harus 0";
            if (firstLineObservation.HasLeadingManualIndent)
                message += " karena diawali spasi/tab";
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "item_daftar",
                Message = message,
                Expected = expectedFirstLineIndent.ToString(CultureInfo.InvariantCulture) + " cm",
                Actual = firstLineObservation.DisplayActual,
                Evidence = evidence,
                Locations = locations
            });
        }

        var expectedRightIndent = rule?.Paragraph?.Indentation?.RightIndent?.Value ?? 0m;
        result.IncrementTotalChecks(rule.Paragraph?.Indentation?.RightIndent?.IsHardConstraint == true);
        var rightCm = GetRightIndentCm(format);
        if (Math.Abs(rightCm - expectedRightIndent) <= 0.05m)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "item_daftar",
                Message = "Right indent item daftar tidak sesuai",
                Expected = expectedRightIndent.ToString(CultureInfo.InvariantCulture) + " cm",
                Actual = rightCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                Evidence = evidence,
                Locations = locations
            });
        }

        var spacingRule = rule?.Paragraph?.Spacing;
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
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "item_daftar",
                    Message = "Line spacing item daftar tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
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
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "item_daftar",
                    Message = "Spacing before item daftar tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
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
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "item_daftar",
                    Message = "Spacing after item daftar tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }
}


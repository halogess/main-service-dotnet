using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private sealed class CodeElementInfo
    {
        public ulong ElementId { get; init; }
        public int OrderIndex { get; init; }
        public ElementContentInfo Content { get; init; } = new();
    }

    private sealed class CodeBlockInfo
    {
        public int StartIndex { get; init; }
        public int EndIndex { get; set; }
        public List<ulong> ElementIds { get; init; } = new();
        public string Evidence { get; init; } = "Kode";
    }

    private async Task<ValidationResult> ValidateCodeAsync(int dokumenId, CancellationToken cancellationToken)
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

        var codeDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "kode")
            .FirstOrDefaultAsync(cancellationToken);

        if (codeDetail == null)
            return result;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        CodeRule? codeRule = null;
        CodeTitleRule? titleRule = null;

        try
        {
            var rawJson = codeDetail.AturanDetailJsonValue ?? "{}";
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("kode", out var kodeElement))
                {
                    codeRule = JsonSerializer.Deserialize<CodeRule>(kodeElement.GetRawText(), jsonOptions);
                }
                else
                {
                    codeRule = JsonSerializer.Deserialize<CodeRule>(rawJson, jsonOptions);
                }

                if (doc.RootElement.TryGetProperty("judul_kode", out var judulElement))
                {
                    titleRule = JsonSerializer.Deserialize<CodeTitleRule>(judulElement.GetRawText(), jsonOptions);
                }
            }
            else
            {
                codeRule = JsonSerializer.Deserialize<CodeRule>(rawJson, jsonOptions);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan kode");
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "kode",
                Message = "Format aturan kode tidak valid"
            });
            return result;
        }

        if (codeRule == null && titleRule == null)
            return result;

        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DokumenId == (uint)dokumenId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new BodyElementInfo { DelemenId = e.DelemenId, DelemenType = e.DelemenType, DelemenJsonTree = e.DelemenJsonTree })
            .ToListAsync(cancellationToken);

        if (bodyElements.Count == 0)
            return result;

        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var orderedElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => (string?)e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);

        var captionPrefixes = BuildCaptionPrefixes(titleRule?.Numbering);
        var codeElements = new List<CodeElementInfo>();
        var captionCandidates = new List<CaptionInfo>();

        for (var index = 0; index < bodyElements.Count; index++)
        {
            var elem = bodyElements[index];
            if (!IsParagraphElement(elem.DelemenType))
                continue;

            var normalizedLabel = labelMap.TryGetValue(elem.DelemenId, out var rawLabel)
                ? NormalizeLabel(rawLabel)
                : string.Empty;

            var content = ParseElementContent(elem.DelemenJsonTree);
            var normalizedText = NormalizeWhitespace(content.PlainText);

            if (IsCodeLabel(normalizedLabel))
            {
                if (string.IsNullOrWhiteSpace(normalizedText) && !content.HasNonTextContent)
                    continue;

                codeElements.Add(new CodeElementInfo
                {
                    ElementId = elem.DelemenId,
                    OrderIndex = index,
                    Content = content
                });
            }

            if (titleRule != null)
            {
                var isCaptionLabel = IsCodeCaptionLabel(normalizedLabel);
                var isCaptionPrefix = !string.IsNullOrWhiteSpace(normalizedText) &&
                    captionPrefixes.Count > 0 &&
                    StartsWithAnyPrefix(normalizedText, captionPrefixes);

                if (isCaptionLabel || isCaptionPrefix)
                {
                    if (string.IsNullOrWhiteSpace(normalizedText))
                        continue;

                    captionCandidates.Add(new CaptionInfo
                    {
                        ElementId = elem.DelemenId,
                        OrderIndex = index,
                        Content = content,
                        NormalizedText = normalizedText
                    });
                }
            }
        }

        if (codeElements.Count == 0)
            return result;

        var paragraphFormatIds = codeElements
            .Where(e => e.Content.ParagraphFormatId.HasValue)
            .Select(e => e.Content.ParagraphFormatId!.Value)
            .Concat(captionCandidates
                .Where(c => c.Content.ParagraphFormatId.HasValue)
                .Select(c => c.Content.ParagraphFormatId!.Value))
            .Distinct()
            .ToList();

        var paragraphFormats = paragraphFormatIds.Count > 0
            ? await _db.DokumenFormatParagrafs
                .Where(p => paragraphFormatIds.Contains(p.DfpId))
                .ToDictionaryAsync(p => p.DfpId, cancellationToken)
            : new Dictionary<uint, DokumenFormatParagraf>();

        var textFormatIds = codeElements
            .SelectMany(e => e.Content.TextFormatIds)
            .Concat(captionCandidates.SelectMany(c => c.Content.TextFormatIds))
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        foreach (var codeElement in codeElements)
        {
            var errorStart = result.Errors.Count;
            var content = codeElement.Content;
            var plainText = NormalizeWhitespace(content.PlainText);
            var evidence = plainText.Length > 100 ? plainText[..100] + "..." : plainText;

            DokumenFormatParagraf? paragraphFormat = null;
            if (content.ParagraphFormatId.HasValue)
                paragraphFormats.TryGetValue(content.ParagraphFormatId.Value, out paragraphFormat);

            var elementTextFormats = content.TextFormatIds
                .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                .Where(tf => tf != null)
                .ToList();

            var locations = await BuildElementLocationsAsync(codeElement.ElementId, cancellationToken);

            if (codeRule?.Font != null)
                ValidateCodeFont(result, codeRule.Font, elementTextFormats!, content.TextRuns, evidence, locations);

            if (codeRule?.Paragraph != null)
                ValidateCodeParagraphFormat(result, codeRule.Paragraph, paragraphFormat, evidence, locations);

            if (codeRule?.Numbering != null)
                ValidateCodeNumbering(result, codeRule.Numbering, content, paragraphFormat, evidence, locations);

            if (codeRule?.CegahGambarKode?.Value == true)
            {
                result.TotalChecks++;
                if (!content.HasNonTextContent)
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "kode",
                        Message = "Kode tidak boleh berisi gambar atau objek non-teks",
                        Expected = "Tidak ada gambar/objek",
                        Actual = "Objek non-teks terdeteksi",
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }

            if (neighborContexts.TryGetValue(codeElement.ElementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, codeElement.ElementId);
        }

        if (titleRule == null)
            return result;

        var codeBlocks = BuildCodeBlocks(codeElements);
        var usedCaptionIds = new HashSet<ulong>();

        for (var i = 0; i < codeBlocks.Count; i++)
        {
            var block = codeBlocks[i];
            var blockLocations = await BuildElementLocationsAsync(block.ElementIds, cancellationToken);

            var nextBlockStart = i + 1 < codeBlocks.Count ? codeBlocks[i + 1].StartIndex : int.MaxValue;
            var prevBlockEnd = i > 0 ? codeBlocks[i - 1].EndIndex : -1;

            CaptionInfo? captionAfter = captionCandidates.FirstOrDefault(c =>
                !usedCaptionIds.Contains(c.ElementId) &&
                c.OrderIndex > block.EndIndex &&
                c.OrderIndex < nextBlockStart);

            CaptionInfo? captionBefore = captionCandidates.LastOrDefault(c =>
                !usedCaptionIds.Contains(c.ElementId) &&
                c.OrderIndex < block.StartIndex &&
                c.OrderIndex > prevBlockEnd);

            CaptionInfo? selectedCaption = null;
            var positionRule = titleRule.Position?.Value?.Trim();
            var normalizedPosition = string.IsNullOrWhiteSpace(positionRule)
                ? null
                : positionRule.ToLowerInvariant();

            if (normalizedPosition == "after")
            {
                if (captionAfter != null)
                {
                    selectedCaption = captionAfter;
                }
                else if (captionBefore != null)
                {
                    result.TotalChecks++;
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_kode",
                        Message = "Posisi judul kode harus setelah kode",
                        Expected = "after",
                        Actual = "before",
                        Evidence = block.Evidence,
                        Locations = blockLocations
                    });
                }
                else
                {
                    result.TotalChecks++;
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_kode",
                        Message = "Judul kode tidak ditemukan",
                        Expected = "Judul setelah kode",
                        Actual = "Tidak ada judul",
                        Evidence = block.Evidence,
                        Locations = blockLocations
                    });
                }
            }
            else if (normalizedPosition == "before")
            {
                if (captionBefore != null)
                {
                    selectedCaption = captionBefore;
                }
                else if (captionAfter != null)
                {
                    result.TotalChecks++;
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_kode",
                        Message = "Posisi judul kode harus sebelum kode",
                        Expected = "before",
                        Actual = "after",
                        Evidence = block.Evidence,
                        Locations = blockLocations
                    });
                }
                else
                {
                    result.TotalChecks++;
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_kode",
                        Message = "Judul kode tidak ditemukan",
                        Expected = "Judul sebelum kode",
                        Actual = "Tidak ada judul",
                        Evidence = block.Evidence,
                        Locations = blockLocations
                    });
                }
            }
            else
            {
                selectedCaption = captionAfter ?? captionBefore;
            }

            if (selectedCaption != null)
            {
                usedCaptionIds.Add(selectedCaption.ElementId);
                var captionLocations = await BuildElementLocationsAsync(selectedCaption.ElementId, cancellationToken);
                var captionErrorStart = result.Errors.Count;

                var captionTextFormats = selectedCaption.Content.TextFormatIds
                    .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                    .Where(tf => tf != null)
                    .ToList();

                ValidateCaptionFont(result, titleRule.Font, selectedCaption, captionTextFormats!, "judul_kode", "judul kode", captionLocations);

                if (selectedCaption.Content.ParagraphFormatId.HasValue &&
                    paragraphFormats.TryGetValue(selectedCaption.Content.ParagraphFormatId.Value, out var captionFormat))
                {
                    ValidateCaptionParagraphFormat(result, titleRule.Paragraph, captionFormat, "judul_kode", "judul kode", selectedCaption.NormalizedText, captionLocations);
                }

                ValidateCaptionNumbering(result, titleRule.Numbering, selectedCaption, "judul_kode", "judul kode", captionLocations);

                if (neighborContexts.TryGetValue(selectedCaption.ElementId, out var captionContext))
                    ApplyContextToErrors(result.Errors, captionErrorStart, captionContext);

                ApplyElementIdToErrors(result.Errors, captionErrorStart, selectedCaption.ElementId);
            }
        }

        return result;
    }

    private static bool IsCodeLabel(string normalizedLabel)
    {
        return normalizedLabel == "kode" || normalizedLabel == "code";
    }

    private static bool IsCodeCaptionLabel(string normalizedLabel)
    {
        return normalizedLabel == "judul_kode" || normalizedLabel == "caption_kode";
    }

    private static List<CodeBlockInfo> BuildCodeBlocks(List<CodeElementInfo> codeElements)
    {
        var ordered = codeElements.OrderBy(c => c.OrderIndex).ToList();
        var blocks = new List<CodeBlockInfo>();

        var i = 0;
        while (i < ordered.Count)
        {
            var start = ordered[i];
            var block = new CodeBlockInfo
            {
                StartIndex = start.OrderIndex,
                EndIndex = start.OrderIndex,
                ElementIds = new List<ulong> { start.ElementId },
                Evidence = BuildCodeEvidence(start.Content)
            };

            var j = i + 1;
            while (j < ordered.Count && ordered[j].OrderIndex == ordered[j - 1].OrderIndex + 1)
            {
                block.ElementIds.Add(ordered[j].ElementId);
                block.EndIndex = ordered[j].OrderIndex;
                j++;
            }

            blocks.Add(block);
            i = j;
        }

        return blocks;
    }

    private static string BuildCodeEvidence(ElementContentInfo content)
    {
        var text = NormalizeWhitespace(content.PlainText);
        if (string.IsNullOrWhiteSpace(text))
            return "Kode";

        return text.Length > 80 ? text[..80] + "..." : text;
    }

    private void ValidateCodeFont(
        ValidationResult result,
        TitleFontRule rule,
        List<DokumenFormatText> textFormats,
        IReadOnlyList<TextRunInfo> textRuns,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (textFormats.Count == 0)
            return;

        var textFormatById = BuildTextFormatMap(textFormats);
        var runs = GetMeaningfulRuns(textRuns);

        var expectedFontName = rule.FontName?.Value;
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
                        Field = "kode",
                        Message = "Font kode tidak sesuai",
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
                        Field = "kode",
                        Message = "Font kode tidak sesuai",
                        Expected = expectedFontName,
                        Actual = string.Join(", ", actuals),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedFontSize = rule.FontSize?.Value;
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
                        Field = "kode",
                        Message = "Ukuran font kode tidak sesuai",
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
                        Field = "kode",
                        Message = "Ukuran font kode tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt" : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedBold = rule.FontStyle?.Bold?.Value;
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
                        Field = "kode",
                        Message = "Bold kode tidak sesuai",
                        Expected = expectedBold.Value.ToString(),
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
                        Field = "kode",
                        Message = "Bold kode tidak sesuai",
                        Expected = expectedBold.Value.ToString(),
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? a.Value.ToString() : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedItalic = rule.FontStyle?.Italic?.Value;
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
                        Field = "kode",
                        Message = "Italic kode tidak sesuai",
                        Expected = expectedItalic.Value.ToString(),
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
                        Field = "kode",
                        Message = "Italic kode tidak sesuai",
                        Expected = expectedItalic.Value.ToString(),
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? a.Value.ToString() : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedUnderline = rule.FontStyle?.Underline?.Value;
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
                        Field = "kode",
                        Message = "Underline kode tidak sesuai",
                        Expected = expectedUnderline.Value ? "true" : "false",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = textFormats.Select(tf => tf.DftxUnderline).Distinct().ToList();
                var matches = expectedUnderline.Value
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
                        Field = "kode",
                        Message = "Underline kode tidak sesuai",
                        Expected = expectedUnderline.Value ? "true" : "false",
                        Actual = string.Join(", ", actuals.Select(a => a ?? "none")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }
    }

    private void ValidateCodeParagraphFormat(
        ValidationResult result,
        CodeParagraphRule rule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (format == null)
            return;

        var expectedAlignment = rule.Alignment?.Value;
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
                    Field = "kode",
                    Message = "Alignment kode tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var expectedLeftIndent = rule.Indentation?.LeftIndent?.Value;
        var expectedHanging = rule.Indentation?.Hanging?.Value;

        var hangingTwips = format.DfpIndHangingTwips ?? 0;
        var hangingCm = hangingTwips / 1440.0m * 2.54m;

        if (expectedHanging.HasValue)
        {
            result.TotalChecks++;
            if (Math.Abs(hangingCm - expectedHanging.Value) <= 0.05m)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "kode",
                    Message = "Hanging indent kode tidak sesuai",
                    Expected = expectedHanging.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = hangingCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (expectedLeftIndent.HasValue)
        {
            result.TotalChecks++;
            var leftTwips = format.DfpIndLeftTwips.HasValue && format.DfpIndLeftTwips.Value != 0
                ? format.DfpIndLeftTwips.Value
                : format.DfpIndStartTwips ?? 0;
            var leftCm = leftTwips / 1440.0m * 2.54m;
            var alignedLeftCm = expectedHanging.HasValue ? leftCm - hangingCm : leftCm;

            if (Math.Abs(alignedLeftCm - expectedLeftIndent.Value) <= 0.05m)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "kode",
                    Message = "Left indent kode tidak sesuai",
                    Expected = expectedLeftIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = alignedLeftCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var spacingRule = rule.Spacing;
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
                    Field = "kode",
                    Message = "Line spacing kode tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

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
                    Field = "kode",
                    Message = "Spacing before kode tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

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
                    Field = "kode",
                    Message = "Spacing after kode tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateCodeNumbering(
        ValidationResult result,
        CodeNumberingRule rule,
        ElementContentInfo content,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations)
    {
        var expectedUse = rule.UseNumbering?.Value;
        var hasNumbering = format != null && (format.DfpIsList || (format.DfpListNumId ?? 0) > 0);

        if (expectedUse.HasValue)
        {
            result.TotalChecks++;
            if (expectedUse.Value == hasNumbering)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "kode",
                    Message = expectedUse.Value ? "Kode harus menggunakan numbering" : "Kode tidak boleh menggunakan numbering",
                    Expected = expectedUse.Value ? "Numbering aktif" : "Tanpa numbering",
                    Actual = hasNumbering ? "Numbering aktif" : "Tanpa numbering",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var expectedFormat = rule.NumberFormat?.Value;
        if (string.IsNullOrWhiteSpace(expectedFormat) || !hasNumbering)
            return;

        if (!TryExtractNumberingLabel(content, out var label))
            return;

        if (!MatchesCodeNumberFormat(label, expectedFormat))
        {
            result.TotalChecks++;
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "kode",
                Message = "Format numbering kode tidak sesuai",
                Expected = expectedFormat,
                Actual = label,
                Evidence = evidence,
                Locations = locations
            });
        }
        else
        {
            result.TotalChecks++;
            result.PassedChecks++;
        }
    }

    private static bool TryExtractNumberingLabel(ElementContentInfo content, out string label)
    {
        label = string.Empty;
        var run = content.TextRuns.FirstOrDefault(r => !string.IsNullOrWhiteSpace(NormalizeWhitespace(r.Text)));
        if (run == null)
            return false;

        var normalized = NormalizeWhitespace(run.Text);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var match = Regex.Match(normalized, @"^(\\d+)([.)]?)");
        if (!match.Success)
            return false;

        label = match.Value.Trim();
        return true;
    }

    private static bool MatchesCodeNumberFormat(string label, string expectedFormat)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (expectedFormat.Contains("%1", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(label, @"^\\d+([.)]?)$");

        return MatchesNumberFormat(label, expectedFormat, null);
    }
}

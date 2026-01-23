using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private sealed class ImageItemInfo
    {
        public string? RelationshipId { get; init; }
        public ulong? DrawingFormatId { get; init; }
    }

    private sealed class ImageBlockInfo
    {
        public ulong ElementId { get; init; }
        public int OrderIndex { get; init; }
        public uint? ParagraphFormatId { get; init; }
        public List<ImageItemInfo> ImageItems { get; } = new();
        public string Evidence { get; init; } = string.Empty;
    }

    private sealed class CaptionInfo
    {
        public ulong ElementId { get; init; }
        public int OrderIndex { get; init; }
        public ElementContentInfo Content { get; init; } = new();
        public string NormalizedText { get; init; } = string.Empty;
    }

    private sealed class PageLayoutSnapshot
    {
        public decimal? WidthCm { get; init; }
        public decimal? HeightCm { get; init; }
        public decimal? MarginLeftCm { get; init; }
        public decimal? MarginRightCm { get; init; }
        public decimal? MarginTopCm { get; init; }
        public decimal? MarginBottomCm { get; init; }
    }

    private async Task<ValidationResult> ValidateImageAsync(
        int dokumenId,
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

        var gambarDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "gambar")
            .FirstOrDefaultAsync(cancellationToken);

        var captionDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "caption_gambar")
            .FirstOrDefaultAsync(cancellationToken);

        ImageRule? imageRule = null;
        if (gambarDetail != null)
        {
            try
            {
                imageRule = JsonSerializer.Deserialize<ImageRule>(
                    gambarDetail.AturanDetailJsonValue ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse aturan gambar");
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "gambar",
                    Message = "Format aturan gambar tidak valid"
                });
            }
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "gambar",
                Message = "Aturan gambar tidak ditemukan"
            });
        }

        CaptionImageRule? captionRule = null;
        if (captionDetail != null)
        {
            try
            {
                captionRule = JsonSerializer.Deserialize<CaptionImageRule>(
                    captionDetail.AturanDetailJsonValue ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse aturan caption_gambar");
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "caption_gambar",
                    Message = "Format aturan caption gambar tidak valid"
                });
            }
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "caption_gambar",
                Message = "Aturan caption gambar tidak ditemukan"
            });
        }

        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DokumenId == (uint)dokumenId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new { e.DelemenId, e.DelemenType, e.DelemenJsonTree })
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

        var imageBlocks = new List<ImageBlockInfo>();
        var captionCandidates = new List<CaptionInfo>();

        for (var index = 0; index < bodyElements.Count; index++)
        {
            var elem = bodyElements[index];
            var content = GetContent(elem.DelemenId, elem.DelemenJsonTree);
            var normalizedText = NormalizeWhitespace(content.PlainText);

            var imageItems = ExtractImageItems(elem.DelemenJsonTree);
            if (imageItems.Count > 0 && string.IsNullOrWhiteSpace(normalizedText))
            {
                var imageBlock = new ImageBlockInfo
                {
                    ElementId = elem.DelemenId,
                    OrderIndex = index,
                    ParagraphFormatId = content.ParagraphFormatId,
                    Evidence = BuildImageEvidence(imageItems)
                };
                imageBlock.ImageItems.AddRange(imageItems);
                imageBlocks.Add(imageBlock);
            }

            if (IsParagraphElement(elem.DelemenType) &&
                !string.IsNullOrWhiteSpace(normalizedText) &&
                IsCaptionCandidate(normalizedText))
            {
                captionCandidates.Add(new CaptionInfo
                {
                    ElementId = elem.DelemenId,
                    OrderIndex = index,
                    Content = content,
                    NormalizedText = normalizedText
                });
            }
        }

        if (imageBlocks.Count == 0)
            return result;

        var paragraphFormatIds = imageBlocks
            .Select(b => b.ParagraphFormatId)
            .Concat(captionCandidates.Select(c => c.Content.ParagraphFormatId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = paragraphFormatIds.Count > 0
            ? await _db.DokumenFormatParagrafs
                .Where(p => paragraphFormatIds.Contains(p.DfpId))
                .ToDictionaryAsync(p => p.DfpId, cancellationToken)
            : new Dictionary<uint, DokumenFormatParagraf>();

        var drawingFormatIds = imageBlocks
            .SelectMany(b => b.ImageItems)
            .Select(i => i.DrawingFormatId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var drawingFormats = drawingFormatIds.Count > 0
            ? await _db.DokumenFormatDrawings
                .Where(d => drawingFormatIds.Contains(d.DfdrId))
                .ToDictionaryAsync(d => d.DfdrId, cancellationToken)
            : new Dictionary<ulong, DokumenFormatDrawing>();

        var captionTextFormatIds = captionCandidates
            .SelectMany(c => c.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var captionTextFormats = captionTextFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => captionTextFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        var usedCaptionIds = new HashSet<ulong>();

        for (var i = 0; i < imageBlocks.Count; i++)
        {
            var block = imageBlocks[i];
            var errorStart = result.Errors.Count;

            paragraphFormats.TryGetValue(block.ParagraphFormatId ?? 0, out var paragraphFormat);
            var locations = await BuildElementLocationsAsync(block.ElementId, cancellationToken);

            if (imageRule != null)
            {
                ValidateImageParagraphFormat(result, imageRule, paragraphFormat, block.Evidence, locations);
                ValidateImagePosition(result, imageRule, block, drawingFormats, pageLayoutsById, locations);
            }

            var nextImageIndex = i + 1 < imageBlocks.Count ? imageBlocks[i + 1].OrderIndex : int.MaxValue;
            var prevImageIndex = i > 0 ? imageBlocks[i - 1].OrderIndex : -1;

            CaptionInfo? captionAfter = captionCandidates.FirstOrDefault(c =>
                !usedCaptionIds.Contains(c.ElementId) &&
                c.OrderIndex > block.OrderIndex &&
                c.OrderIndex < nextImageIndex);

            if (captionAfter != null)
            {
                usedCaptionIds.Add(captionAfter.ElementId);
                if (captionRule != null)
                {
                    paragraphFormats.TryGetValue(captionAfter.Content.ParagraphFormatId ?? 0, out var captionFormat);
                    var captionLocations = await BuildElementLocationsAsync(captionAfter.ElementId, cancellationToken);
                    var captionErrorStart = result.Errors.Count;

                    ValidateCaptionFont(result, captionRule, captionAfter, captionTextFormats, captionLocations);
                    ValidateCaptionParagraphFormat(result, captionRule, captionFormat, captionAfter.NormalizedText, captionLocations);
                    ValidateCaptionNumbering(result, captionRule, captionAfter, captionLocations);

                    if (neighborContexts.TryGetValue(captionAfter.ElementId, out var captionContext))
                        ApplyContextToErrors(result.Errors, captionErrorStart, captionContext);

                    ApplyElementIdToErrors(result.Errors, captionErrorStart, captionAfter.ElementId);
                }
            }
            else if (captionRule?.Position?.Value?.Equals("after", StringComparison.OrdinalIgnoreCase) == true)
            {
                var captionBefore = captionCandidates.LastOrDefault(c =>
                    !usedCaptionIds.Contains(c.ElementId) &&
                    c.OrderIndex < block.OrderIndex &&
                    c.OrderIndex > prevImageIndex);

                if (captionBefore != null)
                {
                    result.TotalChecks++;
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "caption_gambar",
                        Message = "Posisi caption gambar harus setelah gambar",
                        Expected = "after",
                        Actual = "before",
                        Evidence = block.Evidence,
                        Locations = locations
                    });
                }
                else
                {
                    result.TotalChecks++;
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "caption_gambar",
                        Message = "Caption gambar tidak ditemukan",
                        Expected = "Caption setelah gambar",
                        Actual = "Tidak ada caption",
                        Evidence = block.Evidence,
                        Locations = locations
                    });
                }
            }

            if (neighborContexts.TryGetValue(block.ElementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, block.ElementId);
        }

        return result;
    }

    private async Task<List<ErrorLocation>> BuildElementLocationsAsync(
        ulong elementId,
        CancellationToken cancellationToken)
    {
        var pageNumbers = await LoadPageNumbersAsync(new[] { elementId }, cancellationToken);
        var pageBboxMap = await LoadPageBboxMapAsync(new[] { elementId }, cancellationToken);
        return CreateLocations(pageNumbers.Values, pageBboxMap);
    }

    private void ValidateImageParagraphFormat(
        ValidationResult result,
        ImageRule rule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (format == null || rule.Paragraph == null)
            return;

        var expectedAlignment = rule.Paragraph.Alignment?.Value;
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
                    Field = "gambar",
                    Message = "Alignment gambar tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var indentationValue = rule.Paragraph.Indentation?.Value;
        if (!string.IsNullOrWhiteSpace(indentationValue) &&
            indentationValue.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            result.TotalChecks++;
            if (!HasIndentation(format))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "gambar",
                    Message = "Indentasi paragraf gambar harus none (left, right, special harus 0)",
                    Expected = "Left: 0, Right: 0, Special: 0",
                    Actual = GetIndentationDetails(new List<DokumenFormatParagraf?> { format }),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var spacingRule = rule.Paragraph.Spacing;
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
                    Field = "gambar",
                    Message = "Line spacing paragraf gambar tidak sesuai",
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
                    Field = "gambar",
                    Message = "Spacing before paragraf gambar tidak sesuai",
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
                    Field = "gambar",
                    Message = "Spacing after paragraf gambar tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateImagePosition(
        ValidationResult result,
        ImageRule rule,
        ImageBlockInfo block,
        Dictionary<ulong, DokumenFormatDrawing> drawingFormats,
        Dictionary<ulong, PageLayoutSnapshot> pageLayouts,
        List<ErrorLocation> locations)
    {
        if (rule.Position == null)
            return;

        var layoutOption = rule.Position.LayoutOption?.Value;
        if (!string.IsNullOrWhiteSpace(layoutOption) &&
            layoutOption.Equals("inline_with_text", StringComparison.OrdinalIgnoreCase))
        {
            result.TotalChecks++;
            var nonInline = block.ImageItems
                .Select(i => i.DrawingFormatId)
                .Where(id => id.HasValue && drawingFormats.TryGetValue(id.Value, out var format) && !format.DfdrIsInline)
                .ToList();

            if (nonInline.Count == 0)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "gambar",
                    Message = "Layout gambar harus inline with text",
                    Expected = "inline_with_text",
                    Actual = "floating",
                    Evidence = block.Evidence,
                    Locations = locations
                });
            }
        }

        if (rule.Position.CegahMelebihiMargin?.Value == true)
        {
            result.TotalChecks++;
            var mismatches = new List<string>();

            if (pageLayouts.TryGetValue(block.ElementId, out var layout))
            {
                var availableWidth = GetAvailableWidthCm(layout);
                if (availableWidth.HasValue && availableWidth.Value > 0)
                {
                    foreach (var item in block.ImageItems)
                    {
                        if (!item.DrawingFormatId.HasValue ||
                            !drawingFormats.TryGetValue(item.DrawingFormatId.Value, out var format))
                        {
                            continue;
                        }

                        var widthCm = EmuToCm(format.DfdrCxEmu);
                        if (widthCm.HasValue && widthCm.Value > availableWidth.Value + 0.1m)
                        {
                            mismatches.Add($"{widthCm.Value:F2} cm (max {availableWidth.Value:F2} cm)");
                        }
                    }
                }
            }

            if (mismatches.Count == 0)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "gambar",
                    Message = "Lebar gambar melebihi margin halaman",
                    Expected = "Lebar <= lebar area teks",
                    Actual = string.Join(", ", mismatches.Distinct()),
                    Evidence = block.Evidence,
                    Locations = locations
                });
            }
        }

        if (rule.Position.CegahMemenuhiHalaman?.Value == true)
        {
            result.TotalChecks++;
            var mismatches = new List<string>();

            if (pageLayouts.TryGetValue(block.ElementId, out var layout))
            {
                var availableHeight = GetAvailableHeightCm(layout);
                if (availableHeight.HasValue && availableHeight.Value > 0)
                {
                    foreach (var item in block.ImageItems)
                    {
                        if (!item.DrawingFormatId.HasValue ||
                            !drawingFormats.TryGetValue(item.DrawingFormatId.Value, out var format))
                        {
                            continue;
                        }

                        var heightCm = EmuToCm(format.DfdrCyEmu);
                        if (heightCm.HasValue && heightCm.Value >= availableHeight.Value - 0.1m)
                        {
                            mismatches.Add($"{heightCm.Value:F2} cm (max {availableHeight.Value:F2} cm)");
                        }
                    }
                }
            }

            if (mismatches.Count == 0)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "gambar",
                    Message = "Tinggi gambar memenuhi halaman",
                    Expected = "Tinggi < tinggi area teks",
                    Actual = string.Join(", ", mismatches.Distinct()),
                    Evidence = block.Evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateCaptionFont(
        ValidationResult result,
        CaptionImageRule rule,
        CaptionInfo caption,
        Dictionary<uint, DokumenFormatText> textFormats,
        List<ErrorLocation> locations)
    {
        var captionTextFormats = caption.Content.TextFormatIds
            .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
            .Where(tf => tf != null)
            .ToList();

        if (captionTextFormats.Count == 0)
            return;

        var evidence = caption.NormalizedText.Length > 100
            ? caption.NormalizedText[..100] + "..."
            : caption.NormalizedText;

        var textFormatById = BuildTextFormatMap(captionTextFormats!);
        var runs = GetMeaningfulRuns(caption.Content.TextRuns);

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
                        Field = "caption_gambar",
                        Message = "Font caption gambar tidak sesuai",
                        Expected = expectedFontName,
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = captionTextFormats.Select(tf => tf!.DftxFontAscii ?? "unknown").Distinct().ToList();
                if (actuals.All(a => string.Equals(a, expectedFontName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "caption_gambar",
                        Message = "Font caption gambar tidak sesuai",
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
                        Field = "caption_gambar",
                        Message = "Ukuran font caption gambar tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = captionTextFormats
                    .Select(tf => tf!.DftxSizeHalfpt.HasValue ? (decimal?)tf.DftxSizeHalfpt.Value : null)
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
                        Field = "caption_gambar",
                        Message = "Ukuran font caption gambar tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt" : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

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
                        Field = "caption_gambar",
                        Message = expectedBold.Value ? "Caption gambar harus bold" : "Caption gambar tidak boleh bold",
                        Expected = expectedBold.Value ? "Bold" : "Tidak Bold",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = captionTextFormats.Select(tf => tf!.DftxBold).Distinct().ToList();
                if (actuals.All(a => a.HasValue && a.Value == expectedBold.Value))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "caption_gambar",
                        Message = expectedBold.Value ? "Caption gambar harus bold" : "Caption gambar tidak boleh bold",
                        Expected = expectedBold.Value ? "Bold" : "Tidak Bold",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value ? "Bold" : "Tidak Bold") : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

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
                        Field = "caption_gambar",
                        Message = expectedItalic.Value ? "Caption gambar harus italic" : "Caption gambar tidak boleh italic",
                        Expected = expectedItalic.Value ? "Italic" : "Tidak Italic",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = captionTextFormats.Select(tf => tf!.DftxItalic).Distinct().ToList();
                if (actuals.All(a => a.HasValue && a.Value == expectedItalic.Value))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "caption_gambar",
                        Message = expectedItalic.Value ? "Caption gambar harus italic" : "Caption gambar tidak boleh italic",
                        Expected = expectedItalic.Value ? "Italic" : "Tidak Italic",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value ? "Italic" : "Tidak Italic") : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

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
                        Field = "caption_gambar",
                        Message = expectedUnderline.Value ? "Caption gambar harus underline" : "Caption gambar tidak boleh underline",
                        Expected = expectedUnderline.Value ? "Underline" : "Tidak Underline",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
            else
            {
                var actuals = captionTextFormats.Select(tf =>
                {
                    if (string.IsNullOrWhiteSpace(tf!.DftxUnderline))
                        return (bool?)null;
                    return !tf.DftxUnderline.Equals("none", StringComparison.OrdinalIgnoreCase);
                }).Distinct().ToList();

                if (actuals.All(a => a.HasValue && a.Value == expectedUnderline.Value))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "caption_gambar",
                        Message = expectedUnderline.Value ? "Caption gambar harus underline" : "Caption gambar tidak boleh underline",
                        Expected = expectedUnderline.Value ? "Underline" : "Tidak Underline",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value ? "Underline" : "Tidak Underline") : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }
    }

    private void ValidateCaptionParagraphFormat(
        ValidationResult result,
        CaptionImageRule rule,
        DokumenFormatParagraf? format,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (format == null || rule.Paragraph == null)
            return;

        var expectedAlignment = rule.Paragraph.Alignment?.Value;
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
                    Field = "caption_gambar",
                    Message = "Alignment caption gambar tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var indentationValue = rule.Paragraph.Indentation?.Value;
        if (!string.IsNullOrWhiteSpace(indentationValue) &&
            indentationValue.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            result.TotalChecks++;
            if (!HasIndentation(format))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "caption_gambar",
                    Message = "Indentasi paragraf caption gambar harus none (left, right, special harus 0)",
                    Expected = "Left: 0, Right: 0, Special: 0",
                    Actual = GetIndentationDetails(new List<DokumenFormatParagraf?> { format }),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var spacingRule = rule.Paragraph.Spacing;
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
                    Field = "caption_gambar",
                    Message = "Line spacing caption gambar tidak sesuai",
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
                    Field = "caption_gambar",
                    Message = "Spacing before caption gambar tidak sesuai",
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
                    Field = "caption_gambar",
                    Message = "Spacing after caption gambar tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateCaptionNumbering(
        ValidationResult result,
        CaptionImageRule rule,
        CaptionInfo caption,
        List<ErrorLocation> locations)
    {
        var numberingRule = rule.Numbering;
        if (numberingRule == null)
            return;

        var evidence = caption.NormalizedText.Length > 100
            ? caption.NormalizedText[..100] + "..."
            : caption.NormalizedText;

        var expectedFormat = numberingRule.NumberFormat?.Value;
        string? expectedPrefix = null;
        if (!string.IsNullOrWhiteSpace(expectedFormat))
        {
            var prefixCandidate = expectedFormat.Split('[', StringSplitOptions.RemoveEmptyEntries)[0];
            expectedPrefix = NormalizeWhitespace(prefixCandidate);
        }

        if (!TryParseCaptionNumbering(caption.NormalizedText, expectedPrefix, out var parsedNumber, out var titleText))
        {
            if (!string.IsNullOrWhiteSpace(expectedFormat))
            {
                result.TotalChecks++;
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "caption_gambar",
                    Message = "Format nomor caption gambar tidak sesuai",
                    Expected = expectedFormat,
                    Actual = caption.NormalizedText,
                    Evidence = evidence,
                    Locations = locations
                });
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedFormat))
        {
            result.TotalChecks++;
            result.PassedChecks++;
        }

        var requireTitle = numberingRule.EnterAfterNumbering?.Value == true;
        if (requireTitle)
        {
            result.TotalChecks++;
            if (!string.IsNullOrWhiteSpace(titleText))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "caption_gambar",
                    Message = "Caption gambar harus memiliki judul setelah nomor",
                    Expected = "Judul setelah nomor",
                    Actual = "Tidak ada judul",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var caseRule = numberingRule.Case?.Value;
        if (!string.IsNullOrWhiteSpace(caseRule) &&
            caseRule.Equals("Title Case", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(titleText))
        {
            result.TotalChecks++;
            if (IsTitleCaseText(titleText))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "caption_gambar",
                    Message = "Judul caption gambar harus Title Case",
                    Expected = "Title Case",
                    Actual = titleText,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private static List<ImageItemInfo> ExtractImageItems(string? json)
    {
        var items = new List<ImageItemInfo>();
        if (string.IsNullOrWhiteSpace(json))
            return items;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return items;

        if (root.TryGetProperty("type", out var typeEl))
        {
            CollectImageItems(root, items, null);
            return items;
        }

            if (root.TryGetProperty("rId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String)
            {
                var relId = ridEl.GetString();
                ulong? drawingId = null;
                if (root.TryGetProperty("dfdr_id", out var dfdrEl) && dfdrEl.TryGetUInt64(out var dfdrId))
                    drawingId = dfdrId;

                items.Add(new ImageItemInfo
                {
                    RelationshipId = relId,
                    DrawingFormatId = drawingId
                });
                return items;
            }

            if (root.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in contentEl.EnumerateArray())
                    CollectImageItems(item, items, null);
            }
        }
        catch (JsonException)
        {
            // Ignore invalid JSON.
        }

        return items;
    }

    private static void CollectImageItems(
        JsonElement item,
        List<ImageItemInfo> items,
        ulong? inheritedDrawingId)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return;

        string? type = null;
        if (item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            type = typeEl.GetString();

        var drawingId = inheritedDrawingId;
        if (item.TryGetProperty("dfdr_id", out var dfdrEl) && dfdrEl.TryGetUInt64(out var dfdrId))
            drawingId = dfdrId;

        if (type != null && type.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            string? relId = null;
            if (item.TryGetProperty("rId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String)
                relId = ridEl.GetString();

            items.Add(new ImageItemInfo
            {
                RelationshipId = relId,
                DrawingFormatId = drawingId
            });
            return;
        }

        if (item.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in contentEl.EnumerateArray())
                CollectImageItems(child, items, drawingId);
        }
    }

    private static bool IsCaptionCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return Regex.IsMatch(text, "^Gambar\\b", RegexOptions.IgnoreCase);
    }

    private static bool TryParseCaptionNumbering(
        string text,
        string? expectedPrefix,
        out string numberText,
        out string? titleText)
    {
        numberText = string.Empty;
        titleText = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var prefix = string.IsNullOrWhiteSpace(expectedPrefix) ? "Gambar" : expectedPrefix.Trim();
        prefix = NormalizeWhitespace(prefix);

        var pattern = "^" + Regex.Escape(prefix) + "\\s+(\\d+)\\.(\\d+)\\s*(.*)$";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        numberText = $"{prefix} {match.Groups[1].Value}.{match.Groups[2].Value}".Trim();
        var rest = match.Groups[3].Value?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(rest))
        {
            rest = rest.TrimStart(' ', '-', ':', '.');
            titleText = rest.Trim();
        }

        return true;
    }

    private static bool IsTitleCaseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var matches = Regex.Matches(text, "[A-Za-z]+");
        if (matches.Count == 0)
            return true;

        foreach (Match match in matches)
        {
            var word = match.Value;
            if (string.IsNullOrWhiteSpace(word))
                continue;

            if (word.All(char.IsUpper))
                continue;

            if (!char.IsUpper(word[0]))
                return false;

            if (word.Length > 1)
            {
                var rest = word.Substring(1);
                if (!rest.All(char.IsLower))
                    return false;
            }
        }

        return true;
    }

    private static string BuildImageEvidence(IReadOnlyList<ImageItemInfo> items)
    {
        if (items.Count == 0)
            return "Gambar";

        var parts = new List<string>();
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.RelationshipId))
            {
                parts.Add($"rId:{item.RelationshipId}");
            }
            else if (item.DrawingFormatId.HasValue)
            {
                parts.Add($"dfdr:{item.DrawingFormatId.Value}");
            }
        }

        if (parts.Count == 0)
            return "Gambar";

        return "Gambar (" + string.Join(", ", parts.Distinct()) + ")";
    }

    private static decimal? EmuToCm(ulong? emu)
    {
        if (!emu.HasValue)
            return null;

        return Math.Round(emu.Value / EmusPerCm, 2);
    }

    private static decimal? GetAvailableWidthCm(PageLayoutSnapshot layout)
    {
        if (!layout.WidthCm.HasValue)
            return null;

        var left = layout.MarginLeftCm ?? 0m;
        var right = layout.MarginRightCm ?? 0m;
        var width = layout.WidthCm.Value - left - right;
        return width > 0 ? width : null;
    }

    private static decimal? GetAvailableHeightCm(PageLayoutSnapshot layout)
    {
        if (!layout.HeightCm.HasValue)
            return null;

        var top = layout.MarginTopCm ?? 0m;
        var bottom = layout.MarginBottomCm ?? 0m;
        var height = layout.HeightCm.Value - top - bottom;
        return height > 0 ? height : null;
    }

    private async Task<Dictionary<ulong, PageLayoutSnapshot>> LoadPageLayoutsAsync(
        IEnumerable<ulong> elementIds,
        CancellationToken cancellationToken)
    {
        var ids = elementIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<ulong, PageLayoutSnapshot>();

        var layouts = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where ids.Contains(e.DelemenId)
            select new
            {
                e.DelemenId,
                s.DsecPageWidthTwips,
                s.DsecPageHeightTwips,
                s.DsecMarginLeftTwips,
                s.DsecMarginRightTwips,
                s.DsecMarginTopTwips,
                s.DsecMarginBottomTwips
            }).ToListAsync(cancellationToken);

        return layouts
            .GroupBy(l => l.DelemenId)
            .ToDictionary(
                g => g.Key,
                g => new PageLayoutSnapshot
                {
                    WidthCm = TwipsToCm(g.First().DsecPageWidthTwips),
                    HeightCm = TwipsToCm(g.First().DsecPageHeightTwips),
                    MarginLeftCm = TwipsToCm(g.First().DsecMarginLeftTwips),
                    MarginRightCm = TwipsToCm(g.First().DsecMarginRightTwips),
                    MarginTopCm = TwipsToCm(g.First().DsecMarginTopTwips),
                    MarginBottomCm = TwipsToCm(g.First().DsecMarginBottomTwips)
                });
    }
}

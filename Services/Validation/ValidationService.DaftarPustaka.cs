using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private const string BibliographyCategory = "Referensi";

    private sealed class DaftarPustakaEntryInfo
    {
        public ulong ElementId { get; init; }
        public int OrderIndex { get; init; }
        public string Text { get; init; } = string.Empty;
        public ElementContentInfo Content { get; init; } = new();
    }

    private async Task<ValidationResult> ValidateDaftarPustakaAsync(
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
            return result;

        var detailCandidates = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKey == "daftar_pustaka")
            .Where(d => d.AturanDetailKategori == "Referensi" || d.AturanDetailKategori == "Isi Buku")
            .ToListAsync(cancellationToken);

        var daftarPustakaDetail = detailCandidates
            .OrderBy(d => string.Equals(d.AturanDetailKategori, "Referensi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        if (daftarPustakaDetail == null)
            return result;

        DaftarPustakaRule? rule = null;
        try
        {
            var rawJson = daftarPustakaDetail.AturanDetailJsonValue ?? "{}";
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("daftar_pustaka", out var daftarPustakaElement))
            {
                rule = JsonSerializer.Deserialize<DaftarPustakaRule>(
                    daftarPustakaElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                rule = JsonSerializer.Deserialize<DaftarPustakaRule>(
                    rawJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan daftar_pustaka");
            result.Errors.Add(new ValidationError
            {
                Category = BibliographyCategory,
                Field = "daftar_pustaka",
                Message = "Format aturan daftar pustaka tidak valid"
            });
            return result;
        }

        if (rule == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = BibliographyCategory,
                Field = "daftar_pustaka",
                Message = "Aturan daftar pustaka tidak valid"
            });
            return result;
        }

        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DsecRefTipe == "dokumen" && s.DsecRefId == (uint)dokumenId && p.DpartType == "body"
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
        var pageLayoutsById = await LoadPageLayoutsAsync(orderedElementIds, cancellationToken);

        var contentCache = new Dictionary<ulong, ElementContentInfo>();
        ElementContentInfo GetContent(BodyElementInfo element)
        {
            if (!contentCache.TryGetValue(element.DelemenId, out var content))
            {
                content = ParseElementContent(element.DelemenJsonTree);
                contentCache[element.DelemenId] = content;
            }

            return content;
        }

        var entries = new List<DaftarPustakaEntryInfo>();
        for (var index = 0; index < bodyElements.Count; index++)
        {
            var element = bodyElements[index];
            if (!labelMap.TryGetValue(element.DelemenId, out var rawLabel))
                continue;

            var normalizedLabel = NormalizeLabel(rawLabel);
            if (!IsDaftarPustakaLabel(normalizedLabel))
                continue;

            if (!IsParagraphElement(element.DelemenType) && !IsListItemElement(element.DelemenType))
                continue;

            var content = GetContent(element);
            var text = NormalizeWhitespace(content.PlainText);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            entries.Add(new DaftarPustakaEntryInfo
            {
                ElementId = element.DelemenId,
                OrderIndex = index,
                Text = text,
                Content = content
            });
        }

        if (entries.Count == 0)
            return result;

        var paragraphFormatIds = entries
            .Where(e => e.Content.ParagraphFormatId.HasValue)
            .Select(e => e.Content.ParagraphFormatId!.Value)
            .Distinct()
            .ToList();

        var paragraphFormats = paragraphFormatIds.Count > 0
            ? await _db.DokumenFormatParagrafs
                .Where(p => paragraphFormatIds.Contains(p.DfpId))
                .ToDictionaryAsync(p => p.DfpId, cancellationToken)
            : new Dictionary<uint, DokumenFormatParagraf>();

        var textFormatIds = entries
            .SelectMany(e => e.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var textFormatById = textFormatIds.Count > 0
            ? BuildTextFormatMap(await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToListAsync(cancellationToken))
            : new Dictionary<uint, DokumenFormatText>();

        foreach (var entry in entries.OrderBy(e => e.OrderIndex))
        {
            var errorStart = result.Errors.Count;
            var locations = await BuildElementLocationsAsync(entry.ElementId, cancellationToken);

            if (rule.Font != null)
                ValidateDaftarPustakaFont(result, rule.Font, entry, textFormatById, locations);

            if (entry.Content.ParagraphFormatId.HasValue &&
                paragraphFormats.TryGetValue(entry.Content.ParagraphFormatId.Value, out var paragraphFormat))
            {
                pageLayoutsById.TryGetValue(entry.ElementId, out var pageLayout);
                ValidateDaftarPustakaParagraph(result, rule.Paragraph, paragraphFormat, entry.Text, locations, pageLayout);
            }

            if (neighborContexts.TryGetValue(entry.ElementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, entry.ElementId);
        }

        ValidateDaftarPustakaAlphabetical(result, rule, entries);
        ValidateDaftarPustakaSpacingBetweenSources(result, rule, entries, bodyElements, contentCache);

        return result;
    }

    private void ValidateDaftarPustakaFont(
        ValidationResult result,
        ParagraphFontRule fontRule,
        DaftarPustakaEntryInfo entry,
        Dictionary<uint, DokumenFormatText> textFormatById,
        List<ErrorLocation> locations)
    {
        var runs = GetMeaningfulRuns(entry.Content.TextRuns);

        var expectedFontName = fontRule.FontName?.Value;
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
                        Category = BibliographyCategory,
                        Field = "daftar_pustaka",
                        Message = "Font daftar pustaka tidak sesuai",
                        Expected = expectedFontName,
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = entry.Text,
                        Locations = locations
                    });
                }
            }
            else
            {
                result.IncrementPassedChecks();
            }
        }

        var expectedFontSize = fontRule.FontSize?.Value;
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
                        Category = BibliographyCategory,
                        Field = "daftar_pustaka",
                        Message = "Ukuran font daftar pustaka tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = BuildMismatchSummary(mismatches),
                        Evidence = entry.Text,
                        Locations = locations
                    });
                }
            }
            else
            {
                result.IncrementPassedChecks();
            }
        }
    }

    private void ValidateDaftarPustakaParagraph(
        ValidationResult result,
        DaftarPustakaParagraphRule? paragraphRule,
        DokumenFormatParagraf format,
        string text,
        List<ErrorLocation> locations,
        PageLayoutSnapshot? pageLayout)
    {
        if (paragraphRule == null)
            return;

        var expectedAlignment = paragraphRule.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks();
            var actual = format.DfpJc ?? "unknown";
            var alignmentContext = CreateAlignmentContext(text, locations, pageLayout);
            if (AreAlignmentsEquivalent(actual, expectedAlignment, alignmentContext))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = BibliographyCategory,
                    Field = "daftar_pustaka",
                    Message = "Alignment daftar pustaka tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = text,
                    Locations = locations
                });
            }
        }

        var spacingRule = paragraphRule.Spacing;
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
                    Category = BibliographyCategory,
                    Field = "daftar_pustaka",
                    Message = "Line spacing daftar pustaka tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                    Evidence = text,
                    Locations = locations
                });
            }
        }

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
                    Category = BibliographyCategory,
                    Field = "daftar_pustaka",
                    Message = "Spacing before daftar pustaka tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = text,
                    Locations = locations
                });
            }
        }

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
                    Category = BibliographyCategory,
                    Field = "daftar_pustaka",
                    Message = "Spacing after daftar pustaka tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = text,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateDaftarPustakaAlphabetical(
        ValidationResult result,
        DaftarPustakaRule rule,
        List<DaftarPustakaEntryInfo> entries)
    {
        if (rule.UrutAbjad?.Value != true || entries.Count < 2)
            return;

        result.IncrementTotalChecks();

        var sorted = true;
        string? previousKey = null;
        string? previousText = null;
        string? currentText = null;

        foreach (var entry in entries.OrderBy(e => e.OrderIndex))
        {
            var currentKey = BuildDaftarPustakaSortKey(entry.Text);
            if (!string.IsNullOrWhiteSpace(previousKey) &&
                string.Compare(currentKey, previousKey, StringComparison.OrdinalIgnoreCase) < 0)
            {
                sorted = false;
                currentText = entry.Text;
                break;
            }

            previousKey = currentKey;
            previousText = entry.Text;
        }

        if (sorted)
        {
            result.IncrementPassedChecks();
            return;
        }

        result.Errors.Add(new ValidationError
        {
            Category = BibliographyCategory,
            Field = "daftar_pustaka_urut_abjad",
            Message = "Urutan daftar pustaka harus berdasarkan abjad",
            Expected = "Urut A-Z",
            Actual = BuildAlphabeticalEvidence(previousText, currentText)
        });
    }

    private void ValidateDaftarPustakaSpacingBetweenSources(
        ValidationResult result,
        DaftarPustakaRule rule,
        List<DaftarPustakaEntryInfo> entries,
        IReadOnlyList<BodyElementInfo> bodyElements,
        Dictionary<ulong, ElementContentInfo> contentCache)
    {
        if (rule.StrukturKonten?.SatuEnterAntarSumber?.Value != true || entries.Count < 2)
            return;

        result.IncrementTotalChecks();

        var orderedEntries = entries.OrderBy(e => e.OrderIndex).ToList();
        for (var i = 0; i < orderedEntries.Count - 1; i++)
        {
            var current = orderedEntries[i];
            var next = orderedEntries[i + 1];
            var emptyParagraphCount = CountEmptyParagraphsBetween(current.OrderIndex, next.OrderIndex, bodyElements, contentCache);
            if (emptyParagraphCount == 1)
                continue;

            result.Errors.Add(new ValidationError
            {
                Category = BibliographyCategory,
                Field = "daftar_pustaka_struktur_konten",
                Message = "Daftar pustaka harus dipisahkan tepat 1 baris kosong antar sumber",
                Expected = "1 baris kosong",
                Actual = $"{emptyParagraphCount} baris kosong",
                Evidence = current.Text
            });
            return;
        }

        result.IncrementPassedChecks();
    }

    private static bool IsDaftarPustakaLabel(string normalizedLabel)
    {
        return normalizedLabel == "daftar_pustaka" ||
               normalizedLabel == "bibliography" ||
               normalizedLabel == "bibliografi" ||
               normalizedLabel == "referensi";
    }

    private static string BuildDaftarPustakaSortKey(string text)
    {
        var normalized = NormalizeWhitespace(text).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        // Strip leading numbering markers like "[1]" / "1." / "(1)"
        var idx = 0;
        while (idx < normalized.Length &&
               (char.IsDigit(normalized[idx]) ||
                normalized[idx] == '[' ||
                normalized[idx] == ']' ||
                normalized[idx] == '(' ||
                normalized[idx] == ')' ||
                normalized[idx] == '.' ||
                normalized[idx] == ' '))
        {
            idx++;
        }

        normalized = idx < normalized.Length ? normalized[idx..].TrimStart() : normalized;
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var commaIndex = normalized.IndexOf(',');
        var candidate = commaIndex > 0 ? normalized[..commaIndex] : normalized;
        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? words[0] : candidate;
    }

    private static string BuildAlphabeticalEvidence(string? previousText, string? currentText)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(previousText))
            sb.Append("Sebelumnya: ").Append(previousText);
        if (!string.IsNullOrWhiteSpace(currentText))
        {
            if (sb.Length > 0)
                sb.Append(" | ");
            sb.Append("Sesudahnya: ").Append(currentText);
        }

        return sb.Length > 0 ? sb.ToString() : "Tidak terurut";
    }

    private static int CountEmptyParagraphsBetween(
        int startIndex,
        int endIndex,
        IReadOnlyList<BodyElementInfo> bodyElements,
        Dictionary<ulong, ElementContentInfo> contentCache)
    {
        var count = 0;
        for (var i = startIndex + 1; i < endIndex; i++)
        {
            var element = bodyElements[i];
            if (!IsParagraphElement(element.DelemenType))
                continue;

            if (!contentCache.TryGetValue(element.DelemenId, out var content))
            {
                content = ParseElementContent(element.DelemenJsonTree);
                contentCache[element.DelemenId] = content;
            }

            var text = NormalizeWhitespace(content.PlainText);
            if (string.IsNullOrWhiteSpace(text))
                count++;
        }

        return count;
    }
}


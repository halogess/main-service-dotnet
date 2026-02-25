using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private const string FootnoteCategory = "Referensi";

    private sealed class FootnoteContentInfo
    {
        public string PlainText { get; set; } = string.Empty;
        public List<uint> TextFormatIds { get; } = new();
        public List<TextRunInfo> TextRuns { get; } = new();
        public bool StartsWithTab { get; set; }
    }

    private sealed class FootnoteParagraphFormatInfo
    {
        public string Alignment { get; init; } = "left";
        public decimal LeftIndentCm { get; init; }
        public decimal FirstLineIndentCm { get; init; }
        public decimal? SpacingBeforePt { get; init; }
        public decimal? SpacingAfterPt { get; init; }
        public decimal? LineSpacing { get; init; }
    }

    private sealed class FootnoteEntryInfo
    {
        public uint RowId { get; init; }
        public ulong? ElementId { get; init; }
        public string NoteType { get; init; } = "normal";
        public int? NoteNumber { get; init; }
        public FootnoteContentInfo Content { get; init; } = new();
        public List<FootnoteParagraphFormatInfo> Paragraphs { get; } = new();
    }

    private async Task<ValidationResult> ValidateFootnoteAsync(int dokumenId, CancellationToken cancellationToken)
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

        if (IsBabScopedValidation())
            return result;

        var aturan = await _db.Aturans
            .Where(a => a.AturanStatus == 1)
            .OrderByDescending(a => a.AturanCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (aturan == null)
            return result;

        var footnoteDetails = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKey == "footnote")
            .Where(d => d.AturanDetailKategori == "Referensi" || d.AturanDetailKategori == "Isi Buku")
            .ToListAsync(cancellationToken);

        var footnoteDetail = footnoteDetails
            .OrderBy(d => string.Equals(d.AturanDetailKategori, "Referensi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        if (footnoteDetail == null)
            return result;

        FootnoteRule? rule = null;
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        try
        {
            var rawJson = footnoteDetail.AturanDetailJsonValue ?? "{}";
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("footnote", out var footnoteElement))
            {
                rule = JsonSerializer.Deserialize<FootnoteRule>(footnoteElement.GetRawText(), jsonOptions);
            }
            else
            {
                rule = JsonSerializer.Deserialize<FootnoteRule>(rawJson, jsonOptions);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan footnote");
            result.Errors.Add(new ValidationError
            {
                Category = FootnoteCategory,
                Field = "footnote",
                Message = "Format aturan footnote tidak valid"
            });
            return result;
        }

        if (rule == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = FootnoteCategory,
                Field = "footnote",
                Message = "Aturan footnote tidak valid"
            });
            return result;
        }

        var rawNotes = await _db.DokumenNotes
            .Where(n => n.DokumenId == (uint)dokumenId && n.DnoteKind == "footnote")
            .OrderBy(n => n.DnoteId)
            .Select(n => new
            {
                n.DnoteId,
                n.DelemenId,
                n.DnoteType,
                n.DnoteJsonTree,
                n.DnoteXml
            })
            .ToListAsync(cancellationToken);

        if (rawNotes.Count == 0)
            return result;

        var footnotes = rawNotes
            .Select(n => BuildFootnoteEntry(n.DnoteId, n.DelemenId, n.DnoteType, n.DnoteJsonTree, n.DnoteXml))
            .ToList();

        var normalFootnotes = footnotes
            .Where(n => string.IsNullOrWhiteSpace(n.NoteType) || string.Equals(n.NoteType, "normal", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (normalFootnotes.Count == 0)
            normalFootnotes = footnotes;

        var textFormatIds = normalFootnotes
            .SelectMany(n => n.Content.TextFormatIds)
            .Distinct()
            .ToList();

        var textFormatById = textFormatIds.Count > 0
            ? BuildTextFormatMap(await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToListAsync(cancellationToken))
            : new Dictionary<uint, DokumenFormatText>();

        ValidateFootnoteNumbering(result, rule, normalFootnotes);
        ValidateFootnoteSeparator(result, rule, footnotes);

        var locationsByElementId = new Dictionary<ulong, List<ErrorLocation>>();

        foreach (var footnote in normalFootnotes)
        {
            var locations = new List<ErrorLocation>();
            if (footnote.ElementId.HasValue)
            {
                if (!locationsByElementId.TryGetValue(footnote.ElementId.Value, out var cachedLocations))
                {
                    cachedLocations = await BuildElementLocationsAsync(footnote.ElementId.Value, cancellationToken);
                    locationsByElementId[footnote.ElementId.Value] = cachedLocations;
                }

                locations = cachedLocations;
            }

            var evidence = BuildFootnoteEvidence(footnote);
            var errorStart = result.Errors.Count;

            ValidateFootnoteTextFont(result, rule.FootnoteText?.Font, footnote, textFormatById, evidence, locations);
            ValidateFootnoteTextParagraph(result, rule.FootnoteText?.Paragraph, footnote, evidence, locations);
            ValidateFootnoteTextStructure(result, rule.FootnoteText?.StrukturKonten, footnote, evidence, locations);
            ValidateFootnoteSource(result, rule.Sumber, footnote, evidence, locations);

            if (footnote.ElementId.HasValue)
                ApplyElementIdToErrors(result.Errors, errorStart, footnote.ElementId.Value);
        }

        return result;
    }

    private static FootnoteEntryInfo BuildFootnoteEntry(
        uint rowId,
        ulong? elementId,
        string? noteType,
        string? noteJsonTree,
        string? noteXml)
    {
        var entry = new FootnoteEntryInfo
        {
            RowId = rowId,
            ElementId = elementId,
            NoteType = string.IsNullOrWhiteSpace(noteType) ? "normal" : noteType,
            NoteNumber = TryExtractNoteNumber(noteXml),
            Content = ParseFootnoteContent(noteJsonTree)
        };

        entry.Paragraphs.AddRange(ParseFootnoteParagraphs(noteXml));
        return entry;
    }

    private void ValidateFootnoteNumbering(
        ValidationResult result,
        FootnoteRule rule,
        List<FootnoteEntryInfo> footnotes)
    {
        if (footnotes.Count == 0)
            return;

        var numberingRule = rule.Numbering;
        if (numberingRule == null)
            return;

        var expectedNumberFormat = NormalizeWhitespace(numberingRule.NumberFormat?.Value ?? string.Empty).ToLowerInvariant();
        if (expectedNumberFormat is "arabic" or "arab")
        {
            result.IncrementTotalChecks();
            var allNumbersDetected = footnotes.All(n => n.NoteNumber.HasValue);
            if (allNumbersDetected)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_numbering_format",
                    Message = "Format nomor footnote tidak sesuai",
                    Expected = "arabic",
                    Actual = "unknown"
                });
            }
        }

        var expectedType = NormalizeWhitespace(numberingRule.Type?.Value ?? string.Empty).ToLowerInvariant();
        if (expectedType == "continuous")
        {
            result.IncrementTotalChecks();

            var noteNumbers = footnotes.Select(n => n.NoteNumber).ToList();
            var valid = true;

            if (noteNumbers.Any(n => !n.HasValue))
            {
                valid = false;
            }
            else
            {
                var values = noteNumbers.Select(n => n!.Value).ToList();
                for (var i = 1; i < values.Count; i++)
                {
                    if (values[i] <= values[i - 1])
                    {
                        valid = false;
                        break;
                    }
                }
            }

            if (valid)
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_numbering_type",
                    Message = "Penomoran footnote harus continuous",
                    Expected = "continuous",
                    Actual = "restart/non-monotonic"
                });
            }
        }
    }

    private void ValidateFootnoteSeparator(
        ValidationResult result,
        FootnoteRule rule,
        List<FootnoteEntryInfo> footnotes)
    {
        var separatorRule = rule.Separator;
        if (separatorRule == null)
            return;

        var separatorFootnotes = footnotes
            .Where(n => n.NoteType.Contains("separator", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (separatorFootnotes.Count == 0)
            return;

        var paragraphs = separatorFootnotes
            .SelectMany(n => n.Paragraphs)
            .ToList();

        if (paragraphs.Count == 0)
            return;

        var expectedAlignment = separatorRule.Paragraph?.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks();
            if (paragraphs.All(p => AreAlignmentsEquivalent(p.Alignment, expectedAlignment)))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                var actual = string.Join(", ", paragraphs.Select(p => NormalizeAlignmentValue(p.Alignment)).Distinct());
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_separator_alignment",
                    Message = "Alignment separator footnote tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = string.IsNullOrWhiteSpace(actual) ? "unknown" : actual
                });
            }
        }

        var expectedLeftIndent = separatorRule.Paragraph?.Indentation?.LeftIndent?.Value;
        if (expectedLeftIndent.HasValue)
        {
            result.IncrementTotalChecks();
            if (paragraphs.All(p => Math.Abs(p.LeftIndentCm - expectedLeftIndent.Value) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_separator_left_indent",
                    Message = "Left indent separator footnote tidak sesuai",
                    Expected = expectedLeftIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = string.Join(", ", paragraphs.Select(p => p.LeftIndentCm.ToString("F2", CultureInfo.InvariantCulture) + " cm").Distinct())
                });
            }
        }

        var expectedFirstLineIndent = separatorRule.Paragraph?.Indentation?.FirstLineIndent?.Value;
        if (expectedFirstLineIndent.HasValue)
        {
            result.IncrementTotalChecks();
            if (paragraphs.All(p => Math.Abs(p.FirstLineIndentCm - expectedFirstLineIndent.Value) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_separator_first_line_indent",
                    Message = "First line indent separator footnote tidak sesuai",
                    Expected = expectedFirstLineIndent.Value.ToString(CultureInfo.InvariantCulture) + " cm",
                    Actual = string.Join(", ", paragraphs.Select(p => p.FirstLineIndentCm.ToString("F2", CultureInfo.InvariantCulture) + " cm").Distinct())
                });
            }
        }

        var spacingRule = separatorRule.Paragraph?.Spacing;
        if (spacingRule?.LineSpacing?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.LineSpacing.Value.Value;
            var actuals = paragraphs.Select(p => p.LineSpacing).ToList();
            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_separator_line_spacing",
                    Message = "Line spacing separator footnote tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown").Distinct())
                });
            }
        }

        if (spacingRule?.Before?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.Before.Value.Value;
            var actuals = paragraphs.Select(p => p.SpacingBeforePt).ToList();
            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_separator_spacing_before",
                    Message = "Spacing before separator footnote tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown").Distinct())
                });
            }
        }

        if (spacingRule?.After?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.After.Value.Value;
            var actuals = paragraphs.Select(p => p.SpacingAfterPt).ToList();
            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_separator_spacing_after",
                    Message = "Spacing after separator footnote tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown").Distinct())
                });
            }
        }

        if (separatorRule.CegahTabAwal?.Value == true)
        {
            result.IncrementTotalChecks();
            if (separatorFootnotes.All(n => !n.Content.StartsWithTab))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_separator_cegah_tab_awal",
                    Message = "Separator footnote tidak boleh diawali tab",
                    Expected = "Tanpa tab di awal",
                    Actual = "Ditemukan tab di awal"
                });
            }
        }
    }

    private void ValidateFootnoteTextFont(
        ValidationResult result,
        ParagraphFontRule? fontRule,
        FootnoteEntryInfo footnote,
        Dictionary<uint, DokumenFormatText> textFormatById,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (fontRule == null)
            return;

        var runs = GetMeaningfulRuns(footnote.Content.TextRuns);
        if (runs.Count == 0)
            return;

        var expectedFontName = fontRule.FontName?.Value;
        if (!string.IsNullOrWhiteSpace(expectedFontName))
        {
            result.IncrementTotalChecks();
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
                    Category = FootnoteCategory,
                    Field = "footnote_font_name",
                    Message = "Font footnote tidak sesuai",
                    Expected = expectedFontName,
                    Actual = BuildMismatchSummary(mismatches),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var expectedFontSize = fontRule.FontSize?.Value;
        if (expectedFontSize.HasValue)
        {
            result.IncrementTotalChecks();
            var expectedHalfPt = expectedFontSize.Value * 2m;

            var mismatches = CollectRunMismatches(
                runs,
                textFormatById,
                tf =>
                {
                    if (!tf.DftxSizeHalfpt.HasValue)
                        return true;
                    return Math.Abs(tf.DftxSizeHalfpt.Value - expectedHalfPt) > 0.5m;
                },
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
                    Category = FootnoteCategory,
                    Field = "footnote_font_size",
                    Message = "Ukuran font footnote tidak sesuai",
                    Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = BuildMismatchSummary(mismatches),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateFootnoteTextParagraph(
        ValidationResult result,
        FootnoteTextParagraphRule? paragraphRule,
        FootnoteEntryInfo footnote,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (paragraphRule == null || footnote.Paragraphs.Count == 0)
            return;

        var expectedAlignment = paragraphRule.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks();
            if (footnote.Paragraphs.All(p => AreAlignmentsEquivalent(p.Alignment, expectedAlignment)))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                var actual = string.Join(", ", footnote.Paragraphs.Select(p => NormalizeAlignmentValue(p.Alignment)).Distinct());
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_alignment",
                    Message = "Alignment paragraf footnote tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = string.IsNullOrWhiteSpace(actual) ? "unknown" : actual,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var spacingRule = paragraphRule.Spacing;
        if (spacingRule?.LineSpacing?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.LineSpacing.Value.Value;
            var actuals = footnote.Paragraphs.Select(p => p.LineSpacing).ToList();

            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_line_spacing",
                    Message = "Line spacing footnote tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown").Distinct()),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (spacingRule?.Before?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.Before.Value.Value;
            var actuals = footnote.Paragraphs.Select(p => p.SpacingBeforePt).ToList();
            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_spacing_before",
                    Message = "Spacing before footnote tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown").Distinct()),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (spacingRule?.After?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.After.Value.Value;
            var actuals = footnote.Paragraphs.Select(p => p.SpacingAfterPt).ToList();
            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_spacing_after",
                    Message = "Spacing after footnote tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown").Distinct()),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateFootnoteTextStructure(
        ValidationResult result,
        FootnoteContentStructureRule? structureRule,
        FootnoteEntryInfo footnote,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (structureRule?.SatuEnterSebelum?.Value != true)
            return;

        // Best-effort interpretation with available extraction data:
        // when enabled, footnote text should not start with a tab.
        result.IncrementTotalChecks();
        if (!footnote.Content.StartsWithTab)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = FootnoteCategory,
                Field = "footnote_struktur_konten",
                Message = "Konten footnote tidak boleh diawali tab",
                Expected = "Tanpa tab di awal konten",
                Actual = "Ditemukan tab di awal konten",
                Evidence = evidence,
                Locations = locations
            });
        }
    }

    private void ValidateFootnoteSource(
        ValidationResult result,
        FootnoteSourceRule? sourceRule,
        FootnoteEntryInfo footnote,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (sourceRule == null)
            return;

        var normalizedText = NormalizeWhitespace(footnote.Content.PlainText);

        if (sourceRule.WajibBerisiSumber?.Value == true)
        {
            result.IncrementTotalChecks();
            if (HasMeaningfulSourceText(normalizedText))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = FootnoteCategory,
                    Field = "footnote_sumber",
                    Message = "Footnote wajib berisi sumber",
                    Expected = "Sumber terisi",
                    Actual = "Kosong/tidak terdeteksi",
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        var expectedFormats = sourceRule.FormatPenulisan?.Value?
            .Select(f => NormalizeWhitespace(f.Format ?? string.Empty))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (expectedFormats == null || expectedFormats.Count == 0)
            return;

        result.IncrementTotalChecks();
        if (expectedFormats.Any(format => SourceTextMatchesFormat(normalizedText, format)))
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = FootnoteCategory,
                Field = "footnote_sumber_format",
                Message = "Format penulisan sumber footnote tidak sesuai",
                Expected = string.Join(" | ", expectedFormats),
                Actual = string.IsNullOrWhiteSpace(normalizedText) ? "kosong" : normalizedText,
                Evidence = evidence,
                Locations = locations,
                IsRequired = false
            });
        }
    }

    private static bool HasMeaningfulSourceText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var compact = Regex.Replace(text, "\\s+", "");
        if (compact.Length < 3)
            return false;

        return compact.Any(char.IsLetterOrDigit);
    }

    private static bool SourceTextMatchesFormat(string text, string format)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(format))
            return false;

        try
        {
            if (Regex.IsMatch(text, format, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        }
        catch (ArgumentException)
        {
            // Fall back to plain containment when format is not a valid regex.
        }

        return text.Contains(format, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFootnoteEvidence(FootnoteEntryInfo footnote)
    {
        var normalized = NormalizeWhitespace(footnote.Content.PlainText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return footnote.NoteNumber.HasValue
                ? $"Footnote #{footnote.NoteNumber.Value}"
                : $"Footnote row {footnote.RowId}";
        }

        const int maxLength = 140;
        var clipped = normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
        return footnote.NoteNumber.HasValue
            ? $"Footnote #{footnote.NoteNumber.Value}: {clipped}"
            : clipped;
    }

    private static FootnoteContentInfo ParseFootnoteContent(string? json)
    {
        var info = new FootnoteContentInfo();
        if (string.IsNullOrWhiteSpace(json))
            return info;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
                return info;

            var allText = new StringBuilder();
            var hasFirstToken = false;

            foreach (var paragraph in contentElement.EnumerateArray())
            {
                if (paragraph.ValueKind != JsonValueKind.Object)
                    continue;

                var paragraphText = new StringBuilder();
                if (paragraph.TryGetProperty("content", out var paragraphContent) && paragraphContent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in paragraphContent.EnumerateArray())
                        AppendFootnoteTextItem(item, info, paragraphText, ref hasFirstToken);
                }
                else
                {
                    AppendFootnoteTextItem(paragraph, info, paragraphText, ref hasFirstToken);
                }

                if (paragraphText.Length > 0)
                {
                    if (allText.Length > 0)
                        allText.Append('\n');
                    allText.Append(paragraphText);
                }
            }

            info.PlainText = allText.ToString();
        }
        catch (JsonException)
        {
            // Ignore invalid JSON and leave empty defaults.
        }

        return info;
    }

    private static void AppendFootnoteTextItem(
        JsonElement item,
        FootnoteContentInfo info,
        StringBuilder paragraphText,
        ref bool hasFirstToken)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return;

        var type = item.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        if (type == "text" || type == "field")
        {
            var value = item.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.String
                ? valueElement.GetString() ?? string.Empty
                : string.Empty;

            if (!hasFirstToken)
            {
                info.StartsWithTab = StartsWithTab(value);
                hasFirstToken = true;
            }

            uint? runFormatId = null;
            if (type == "field" &&
                item.TryGetProperty("result_dftx_id", out var resultElement) &&
                resultElement.TryGetUInt32(out var resultId))
            {
                runFormatId = resultId;
            }
            else if (item.TryGetProperty("dftx_id", out var dftxElement) &&
                     dftxElement.TryGetUInt32(out var dftxId))
            {
                runFormatId = dftxId;
            }

            if (runFormatId.HasValue)
                info.TextFormatIds.Add(runFormatId.Value);

            if (!string.IsNullOrEmpty(value))
            {
                paragraphText.Append(value);
                info.TextRuns.Add(new TextRunInfo
                {
                    Text = value,
                    TextFormatId = runFormatId
                });
            }

            return;
        }

        if (type == "math")
        {
            var mathText = item.TryGetProperty("text", out var mathElement) && mathElement.ValueKind == JsonValueKind.String
                ? mathElement.GetString() ?? string.Empty
                : string.Empty;

            if (!hasFirstToken)
            {
                info.StartsWithTab = StartsWithTab(mathText);
                hasFirstToken = true;
            }

            if (!string.IsNullOrEmpty(mathText))
                paragraphText.Append(mathText);
        }
    }

    private static bool StartsWithTab(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var ch in value)
        {
            if (ch == '\r' || ch == '\n')
                continue;
            if (ch == '\t')
                return true;
            if (char.IsWhiteSpace(ch))
                continue;
            return false;
        }

        return false;
    }

    private static List<FootnoteParagraphFormatInfo> ParseFootnoteParagraphs(string? noteXml)
    {
        var paragraphs = new List<FootnoteParagraphFormatInfo>();
        if (string.IsNullOrWhiteSpace(noteXml))
            return paragraphs;

        try
        {
            var doc = XDocument.Parse(noteXml);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

            foreach (var paragraph in doc.Descendants(w + "p"))
            {
                var pPr = paragraph.Element(w + "pPr");
                var spacing = pPr?.Element(w + "spacing");
                var indent = pPr?.Element(w + "ind");

                var alignment = pPr?.Element(w + "jc")?.Attribute(w + "val")?.Value
                                ?? pPr?.Element(w + "jc")?.Attribute("val")?.Value
                                ?? "left";

                var beforeTwips = TryParseUIntAttribute(spacing, w + "before");
                var afterTwips = TryParseUIntAttribute(spacing, w + "after");
                var lineTwips = TryParseUIntAttribute(spacing, w + "line");
                var lineRule = spacing?.Attribute(w + "lineRule")?.Value
                               ?? spacing?.Attribute("lineRule")?.Value;

                var leftTwips = TryParseUIntAttribute(indent, w + "left") ?? TryParseUIntAttribute(indent, w + "start");
                var firstLineTwips = TryParseUIntAttribute(indent, w + "firstLine");
                var hangingTwips = TryParseUIntAttribute(indent, w + "hanging");

                decimal firstLineIndentCm = 0m;
                if (firstLineTwips.HasValue && firstLineTwips.Value > 0)
                {
                    firstLineIndentCm = firstLineTwips.Value / 1440m * 2.54m;
                }
                else if (hangingTwips.HasValue && hangingTwips.Value > 0)
                {
                    firstLineIndentCm = -(hangingTwips.Value / 1440m * 2.54m);
                }

                decimal? lineSpacing = 1m;
                if (lineTwips.HasValue)
                {
                    lineSpacing = string.IsNullOrWhiteSpace(lineRule) ||
                                  string.Equals(lineRule, "auto", StringComparison.OrdinalIgnoreCase)
                        ? lineTwips.Value / 240m
                        : null;
                }

                paragraphs.Add(new FootnoteParagraphFormatInfo
                {
                    Alignment = alignment,
                    LeftIndentCm = leftTwips.HasValue ? leftTwips.Value / 1440m * 2.54m : 0m,
                    FirstLineIndentCm = firstLineIndentCm,
                    SpacingBeforePt = beforeTwips.HasValue ? beforeTwips.Value / 20m : 0m,
                    SpacingAfterPt = afterTwips.HasValue ? afterTwips.Value / 20m : 0m,
                    LineSpacing = lineSpacing
                });
            }
        }
        catch
        {
            // Ignore XML parse failures for robust validation pipeline.
        }

        return paragraphs;
    }

    private static uint? TryParseUIntAttribute(XElement? element, XName name)
    {
        if (element == null)
            return null;

        var raw = element.Attribute(name)?.Value ?? element.Attribute(name.LocalName)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? TryExtractNoteNumber(string? noteXml)
    {
        if (string.IsNullOrWhiteSpace(noteXml))
            return null;

        try
        {
            var doc = XDocument.Parse(noteXml);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var raw = doc.Root?.Attribute(w + "id")?.Value ?? doc.Root?.Attribute("id")?.Value;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        catch
        {
            // Ignore XML parse failures and treat number as unknown.
        }

        return null;
    }
}


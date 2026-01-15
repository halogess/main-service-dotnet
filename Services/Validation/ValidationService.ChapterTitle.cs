using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private async Task<ValidationResult> ValidateChapterTitleAsync(int dokumenId, CancellationToken cancellationToken)
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

        var judulDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "judul_bab")
            .FirstOrDefaultAsync(cancellationToken);

        if (judulDetail == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Aturan judul bab tidak ditemukan"
            });
            return result;
        }

        ChapterTitleRule? rule = null;
        try
        {
            rule = JsonSerializer.Deserialize<ChapterTitleRule>(
                judulDetail.AturanDetailJsonValue ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse aturan judul_bab");
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Format aturan judul bab tidak valid"
            });
            return result;
        }

        if (rule == null)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Aturan judul bab tidak valid"
            });
            return result;
        }

        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DokumenId == (uint)dokumenId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new BodyElementInfo { DelemenId = e.DelemenId, DelemenType = e.DelemenType, DelemenJsonTree = e.DelemenJsonTree })
            .ToListAsync(cancellationToken);

        if (bodyElements.Count == 0)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Elemen dokumen tidak ditemukan"
            });
            return result;
        }

        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var orderedElementIds = bodyElements.Select(e => e.DelemenId).ToList();
        var elementJsonById = bodyElements.ToDictionary(e => e.DelemenId, e => e.DelemenJsonTree);
        var pageMarginsById = await LoadPageMarginsAsync(orderedElementIds, cancellationToken);
        var neighborContexts = BuildNeighborContexts(orderedElementIds, elementJsonById, labelMap, pageMarginsById);

        var elementsById = bodyElements.ToDictionary(e => e.DelemenId);
        var sectionHeaderIds = labelMap
            .Where(kv => kv.Value.Equals("section_header", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        var contentById = new Dictionary<ulong, ElementContentInfo>();
        var candidateParagraphIds = new HashSet<uint>();
        foreach (var id in sectionHeaderIds)
        {
            if (!elementsById.TryGetValue(id, out var elem))
                continue;

            var content = ParseElementContent(elem.DelemenJsonTree);
            contentById[id] = content;
            if (content.ParagraphFormatId.HasValue)
                candidateParagraphIds.Add(content.ParagraphFormatId.Value);
        }

        var paragraphFormats = await _db.DokumenFormatParagrafs
            .Where(p => candidateParagraphIds.Contains(p.DfpId))
            .ToDictionaryAsync(p => p.DfpId, cancellationToken);

        var titleIds = new HashSet<ulong>();
        foreach (var id in sectionHeaderIds)
        {
            if (!contentById.TryGetValue(id, out var content) || !content.ParagraphFormatId.HasValue)
                continue;

            if (paragraphFormats.TryGetValue(content.ParagraphFormatId.Value, out var format) &&
                string.Equals(format.DfpJc, "center", StringComparison.OrdinalIgnoreCase))
            {
                titleIds.Add(id);
            }
        }

        result.TotalChecks++;
        if (titleIds.Count == 0)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Judul Tidak ditemukan"
            });
            return result;
        }
        result.PassedChecks++;

        var firstElement = bodyElements[0];
        result.TotalChecks++;
        if (!titleIds.Contains(firstElement.DelemenId))
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Judul bab harus berada di elemen pertama"
            });
            return result;
        }
        result.PassedChecks++;

        var titleBlock = new List<ElementContentInfo>();
        foreach (var elem in bodyElements)
        {
            if (!titleIds.Contains(elem.DelemenId))
                break;

            if (!contentById.TryGetValue(elem.DelemenId, out var content))
            {
                content = ParseElementContent(elem.DelemenJsonTree);
                contentById[elem.DelemenId] = content;
            }

            titleBlock.Add(content);
        }

        if (titleBlock.Count == 0)
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Judul Tidak ditemukan"
            });
            return result;
        }

        var titleLines = ExtractTitleLines(titleBlock);
        var numberLine = titleLines.FirstOrDefault() ?? string.Empty;
        var titleLineCandidates = titleLines.Skip(1).ToList();
        var titleLinesNonEmpty = titleLineCandidates.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

        result.TotalChecks++;
        if (!HasDisallowedWhitespace(numberLine))
        {
            result.PassedChecks++;
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Nomor bab tidak boleh memiliki spasi di awal, tab, double space, atau lebih dari 1 spasi di akhir",
                Actual = numberLine
            });
        }

        var numberFormat = rule?.Numbering?.NumberFormat?.Value;
        if (!string.IsNullOrWhiteSpace(numberFormat))
        {
            result.TotalChecks++;
            if (MatchesNumberFormat(numberLine, numberFormat, rule?.Numbering?.Case?.Value))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Format nomor bab tidak sesuai",
                    Expected = numberFormat,
                    Actual = numberLine
                });
            }
        }

        result.TotalChecks++;
        if (titleLinesNonEmpty.Count > 0)
        {
            result.PassedChecks++;
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Judul bab tidak ditemukan setelah nomor bab"
            });
        }

        if (rule?.Numbering?.EnterAfterNumber?.Value == true)
        {
            result.TotalChecks++;
            if (titleLinesNonEmpty.Count > 0 && titleLineCandidates.Count > 0)
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Judul bab harus berada di baris setelah nomor bab"
                });
            }

            var firstTitleIndex = titleLineCandidates.FindIndex(line => !string.IsNullOrWhiteSpace(line));
            if (firstTitleIndex > 0)
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Terdapat baris kosong di antara nomor bab dan judul"
                });
            }
        }

        // Check for empty line after title (based on struktur_konten rule)
        var requireEmptyLineAfter = rule?.StrukturKonten?.SatuBarisKosongSetelah?.Value ?? true;
        var nextElementIndex = titleBlock.Count;
        
        if (requireEmptyLineAfter)
        {
            result.TotalChecks++;
            if (nextElementIndex < bodyElements.Count)
            {
                var emptyContent = ParseElementContent(bodyElements[nextElementIndex].DelemenJsonTree);
                if (IsEmptyElement(emptyContent))
                {
                    result.PassedChecks++;

                    // Check if empty paragraph has same font size as title (non-required check)
                    await ValidateEmptyParagraphFontSizeAsync(
                        result,
                        rule,
                        bodyElements[nextElementIndex].DelemenId,
                        cancellationToken);

                    result.TotalChecks++;
                    var afterEmptyIndex = nextElementIndex + 1;
                    if (afterEmptyIndex < bodyElements.Count)
                    {
                        var afterEmptyContent = ParseElementContent(bodyElements[afterEmptyIndex].DelemenJsonTree);
                        if (!IsEmptyElement(afterEmptyContent))
                        {
                            result.PassedChecks++;
                        }
                        else
                        {
                            result.Errors.Add(new ValidationError
                            {
                                Category = "Isi Buku",
                                Field = "judul_bab",
                                Message = "Hanya boleh ada 1 paragraf kosong setelah judul bab"
                            });
                        }
                    }
                    else
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "judul_bab",
                            Message = "Paragraf setelah paragraf kosong judul bab tidak ditemukan"
                        });
                    }
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_bab",
                        Message = "Setelah judul bab harus ada 1 paragraf kosong"
                    });
                }
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Paragraf kosong setelah judul bab tidak ditemukan"
                });
            }
        }

        var paragraphIds = titleBlock
            .Select(content => content.ParagraphFormatId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (paragraphIds.Count != titleBlock.Count)
        {
            result.TotalChecks++;
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Format paragraf judul bab tidak lengkap"
            });
        }

        var titleParagraphs = paragraphIds
            .Select(id => paragraphFormats.TryGetValue(id, out var pf) ? pf : null)
            .Where(pf => pf != null)
            .ToList();

        var expectedAlignment = rule?.Paragraph?.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment) && titleParagraphs.Count > 0)
        {
            result.TotalChecks++;
            var actualAlignments = titleParagraphs
                .Select(pf => pf!.DfpJc ?? "unknown")
                .Distinct()
                .ToList();

            if (actualAlignments.All(a => string.Equals(a, expectedAlignment, StringComparison.OrdinalIgnoreCase)))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Alignment judul bab tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = string.Join(", ", actualAlignments)
                });
            }
        }

        var indentationValue = rule?.Paragraph?.Indentation?.Value;
        if (!string.IsNullOrWhiteSpace(indentationValue) &&
            indentationValue.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            titleParagraphs.Count > 0)
        {
            result.TotalChecks++;
            if (titleParagraphs.All(pf => !HasIndentation(pf!)))
            {
                result.PassedChecks++;
            }
            else
            {
                var indentationDetails = GetIndentationDetails(titleParagraphs!);
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Indentasi judul bab harus none (left, right, special harus 0)",
                    Expected = "Left: 0, Right: 0, Special: 0",
                    Actual = indentationDetails
                });
            }
        }

        var spacingRule = rule?.Paragraph?.Spacing;
        if (spacingRule?.LineSpacing?.Value.HasValue == true && titleParagraphs.Count > 0)
        {
            result.TotalChecks++;
            var expected = spacingRule.LineSpacing.Value.Value;
            var actuals = titleParagraphs.Select(pf => GetLineSpacing(pf!)).ToList();

            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Line spacing judul bab tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown"))
                });
            }
        }

        if (spacingRule?.Before?.Value.HasValue == true && titleParagraphs.Count > 0)
        {
            result.TotalChecks++;
            var expected = spacingRule.Before.Value.Value;
            var actuals = titleParagraphs.Select(pf => TwipsToPoints(pf!.DfpSpacingBeforeTwips)).ToList();

            if (actuals.All(a => a.HasValue && IsWithinTolerance(a.Value, expected, 0.5m)) &&
                titleParagraphs.All(pf => !pf!.DfpSpacingBeforeAutospacing))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Spacing before judul bab tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown"))
                });
            }
        }

        if (spacingRule?.After?.Value.HasValue == true && titleParagraphs.Count > 0)
        {
            result.TotalChecks++;
            var expected = spacingRule.After.Value.Value;
            var actuals = titleParagraphs.Select(pf => TwipsToPoints(pf!.DfpSpacingAfterTwips)).ToList();

            if (actuals.All(a => a.HasValue && IsWithinTolerance(a.Value, expected, 0.5m)) &&
                titleParagraphs.All(pf => !pf!.DfpSpacingAfterAutospacing))
            {
                result.PassedChecks++;
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Spacing after judul bab tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown"))
                });
            }
        }

        var textFormatIds = titleBlock
            .SelectMany(content => content.TextFormatIds)
            .Distinct()
            .ToList();

        if (textFormatIds.Count == 0 && HasTitleFontRule(rule))
        {
            result.TotalChecks++;
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Format teks judul bab tidak ditemukan"
            });
            return result;
        }

        if (textFormatIds.Count > 0)
        {
            var textFormats = await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToListAsync(cancellationToken);

            if (textFormats.Count == 0)
            {
                result.TotalChecks++;
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "judul_bab",
                    Message = "Format teks judul bab tidak ditemukan"
                });
                return result;
            }

            var expectedFontName = rule?.Font?.FontName?.Value;
            if (!string.IsNullOrWhiteSpace(expectedFontName))
            {
                result.TotalChecks++;
                var actuals = textFormats
                    .Select(tf => tf.DftxFontAscii ?? "unknown")
                    .Distinct()
                    .ToList();

                if (actuals.All(a => string.Equals(a, expectedFontName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.PassedChecks++;
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_bab",
                        Message = "Font judul bab tidak sesuai",
                        Expected = expectedFontName,
                        Actual = string.Join(", ", actuals)
                    });
                }
            }

            var expectedFontSize = rule?.Font?.FontSize?.Value;
            if (expectedFontSize.HasValue)
            {
                result.TotalChecks++;
                var expectedPt = expectedFontSize.Value;
                var expectedHalfPt = expectedPt * 2m;
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
                        Field = "judul_bab",
                        Message = "Ukuran font judul bab tidak sesuai",
                        Expected = expectedPt.ToString(CultureInfo.InvariantCulture),
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) : "unknown"))
                    });
                }
            }

            var expectedBold = rule?.Font?.FontStyle?.Bold?.Value;
            if (expectedBold.HasValue)
            {
                result.TotalChecks++;
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
                        Field = "judul_bab",
                        Message = "Bold judul bab tidak sesuai",
                        Expected = expectedBold.Value.ToString(),
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? a.Value.ToString() : "unknown"))
                    });
                }
            }

            var expectedItalic = rule?.Font?.FontStyle?.Italic?.Value;
            if (expectedItalic.HasValue)
            {
                result.TotalChecks++;
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
                        Field = "judul_bab",
                        Message = "Italic judul bab tidak sesuai",
                        Expected = expectedItalic.Value.ToString(),
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? a.Value.ToString() : "unknown"))
                    });
                }
            }

            var expectedUnderline = rule?.Font?.FontStyle?.Underline?.Value;
            if (expectedUnderline.HasValue)
            {
                result.TotalChecks++;
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
                        Field = "judul_bab",
                        Message = "Underline judul bab tidak sesuai",
                        Expected = expectedUnderline.Value ? "true" : "false",
                        Actual = string.Join(", ", actuals.Select(a => a ?? "none"))
                    });
                }
            }
        }

        if (titleParagraphs.Count > 0)
        {
            var titleHasNumbering = titleParagraphs.Any(pf => pf != null &&
                (pf.DfpIsList || !string.IsNullOrWhiteSpace(pf.DfpNumprJson) || (pf.DfpListNumId ?? 0) > 0));
            var titleStyleId = titleParagraphs
                .Select(pf => pf?.DfpPStyleId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

            foreach (var error in result.Errors)
            {
                if (!string.Equals(error.Field, "judul_bab", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!error.HasNumbering.HasValue)
                    error.HasNumbering = titleHasNumbering;

                if (string.IsNullOrWhiteSpace(error.StyleId) && !string.IsNullOrWhiteSpace(titleStyleId))
                    error.StyleId = titleStyleId;

                if (string.IsNullOrWhiteSpace(error.StyleName) && !string.IsNullOrWhiteSpace(error.StyleId))
                    error.StyleName = error.StyleId;
            }
        }

        // Load page numbers from dokumen_elemen_visual and assign to errors
        if (result.Errors.Count > 0 && titleIds.Count > 0)
        {
            var pageNumbers = await LoadPageNumbersAsync(titleIds, cancellationToken);
            var mergedBbox = await LoadMergedBboxAsync(titleIds, cancellationToken);
            
            if (pageNumbers.Count > 0)
            {
                var firstPageNumber = pageNumbers.Values.Min();
                var locations = CreateLocations(firstPageNumber, mergedBbox);
                
                foreach (var error in result.Errors)
                {
                    if (string.Equals(error.Field, "judul_bab", StringComparison.OrdinalIgnoreCase) && error.Locations.Count == 0)
                    {
                        error.Locations.AddRange(locations);
                    }
                }
            }
        }

        // Validate: min satu paragraf sebelum subbab (struktur_konten rule)
        var requireParagraphBeforeSubchapter = rule?.StrukturKonten?.MinSatuParagrafSebelumSubbab?.Value ?? true;
        if (requireParagraphBeforeSubchapter)
        {
            await ValidateParagraphBeforeSubchapterAsync(result, bodyElements, labelMap, titleIds, cancellationToken);
        }

        ElementNeighborContext? titleContext = null;
        if (titleIds.Count > 0)
        {
            var firstTitleId = bodyElements.Select(e => e.DelemenId).FirstOrDefault(id => titleIds.Contains(id));
            if (firstTitleId != 0 && neighborContexts.TryGetValue(firstTitleId, out var found))
                titleContext = found;
        }
        else if (bodyElements.Count > 0 && neighborContexts.TryGetValue(bodyElements[0].DelemenId, out var fallback))
        {
            titleContext = fallback;
        }

        if (titleContext != null)
        {
            foreach (var error in result.Errors)
            {
                if (string.Equals(error.Field, "judul_bab", StringComparison.OrdinalIgnoreCase))
                    ApplyContext(error, titleContext);
            }
        }

        return result;
    }

    private async Task ValidateEmptyParagraphFontSizeAsync(
        ValidationResult result,
        ChapterTitleRule? rule,
        ulong emptyElementId,
        CancellationToken cancellationToken)
    {
        var expectedFontSize = rule?.Font?.FontSize?.Value;
        if (!expectedFontSize.HasValue)
            return; // No font size rule to validate

        // Load text format for the empty paragraph's paragraph mark
        var emptyElement = await _db.DokumenElemens
            .Where(e => e.DelemenId == emptyElementId)
            .FirstOrDefaultAsync(cancellationToken);

        if (emptyElement == null)
            return;

        var content = ParseElementContent(emptyElement.DelemenJsonTree);

        // Get paragraph format to check for paragraph mark formatting
        if (!content.ParagraphFormatId.HasValue)
            return;

        var paragraphFormat = await _db.DokumenFormatParagrafs
            .Where(p => p.DfpId == content.ParagraphFormatId.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (paragraphFormat == null)
            return;

        // Check rPr (run properties) for the paragraph mark - stored in paragraph format
        // The font size for empty paragraph is typically inherited from the paragraph mark style
        // We need to check the text format associated with the paragraph mark

        // If there are any text formats in the empty paragraph, check them
        if (content.TextFormatIds.Count > 0)
        {
            var textFormats = await _db.DokumenFormatTexts
                .Where(t => content.TextFormatIds.Contains(t.DftxId))
                .ToListAsync(cancellationToken);

            if (textFormats.Count > 0)
            {
                result.TotalChecks++;
                var expectedHalfPt = expectedFontSize.Value * 2m;
                var actuals = textFormats
                    .Select(tf => tf.DftxSizeHalfpt.HasValue ? (decimal?)tf.DftxSizeHalfpt.Value : null)
                    .Where(a => a.HasValue)
                    .ToList();

                if (actuals.Count > 0 && actuals.All(a => Math.Abs(a!.Value - expectedHalfPt) <= 0.5m))
                {
                    result.PassedChecks++;
                }
                else if (actuals.Count > 0)
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_bab",
                        Message = "Ukuran font paragraf kosong setelah judul bab tidak sesuai dengan judul",
                        Expected = expectedFontSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " pt",
                        Actual = string.Join(", ", actuals.Select(a => (a!.Value / 2m).ToString(System.Globalization.CultureInfo.InvariantCulture) + " pt")),
                        IsRequired = false // Non-required check
                    });
                }
            }
        }
    }

    private sealed class ElementContentInfo
    {
        public uint? ParagraphFormatId { get; set; }
        public string PlainText { get; set; } = string.Empty;
        public List<uint> TextFormatIds { get; } = new();
        public bool HasNonTextContent { get; set; }
    }

    private sealed class BodyElementInfo
    {
        public ulong DelemenId { get; set; }
        public string? DelemenType { get; set; }
        public string? DelemenJsonTree { get; set; }
    }

    private static ElementContentInfo ParseElementContent(string? json)
    {
        var info = new ElementContentInfo();
        if (string.IsNullOrWhiteSpace(json))
            return info;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("dfp_id", out var dfpEl) && dfpEl.TryGetUInt32(out var dfpId))
                info.ParagraphFormatId = dfpId;

            if (root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                info.PlainText = textEl.GetString() ?? string.Empty;
                return info;
            }

            if (root.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var item in contentEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var type = item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                        ? typeEl.GetString()
                        : null;

                    if (type == "text" || type == "field")
                    {
                        if (item.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.String)
                            sb.Append(valueEl.GetString());

                        if (item.TryGetProperty("dftx_id", out var dftxEl) && dftxEl.TryGetUInt32(out var dftxId))
                            info.TextFormatIds.Add(dftxId);

                        if (type == "field" &&
                            item.TryGetProperty("result_dftx_id", out var resultEl) &&
                            resultEl.TryGetUInt32(out var resultId))
                        {
                            info.TextFormatIds.Add(resultId);
                        }
                    }
                    else if (type == "math")
                    {
                        if (item.TryGetProperty("text", out var mathEl) && mathEl.ValueKind == JsonValueKind.String)
                            sb.Append(mathEl.GetString());
                    }
                    else
                    {
                        info.HasNonTextContent = true;
                    }
                }

                info.PlainText = sb.ToString();
            }
        }
        catch (JsonException)
        {
            // Ignore invalid JSON and use defaults.
        }

        return info;
    }

    private static List<string> ExtractTitleLines(IEnumerable<ElementContentInfo> titleElements)
    {
        var lines = new List<string>();
        foreach (var content in titleElements)
        {
            var text = content.PlainText ?? string.Empty;
            var normalized = text.Replace("\r", string.Empty);
            if (normalized.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            lines.AddRange(normalized.Split('\n'));
        }

        return lines;
    }

    private static bool IsEmptyElement(ElementContentInfo content)
    {
        return string.IsNullOrWhiteSpace(content.PlainText) && !content.HasNonTextContent;
    }

    /// <summary>
    /// Validates that there is at least one paragraph (text) between chapter title and first subchapter.
    /// </summary>
    private async Task ValidateParagraphBeforeSubchapterAsync(
        ValidationResult result,
        List<BodyElementInfo> bodyElements,
        Dictionary<ulong, string> labelMap,
        HashSet<ulong> titleIds,
        CancellationToken cancellationToken)
    {
        // Find the end of chapter title block (after title elements and empty line)
        int titleEndIndex = 0;
        for (int i = 0; i < bodyElements.Count; i++)
        {
            if (titleIds.Contains(bodyElements[i].DelemenId))
            {
                titleEndIndex = i + 1;
            }
            else
            {
                break;
            }
        }

        // Skip empty paragraph after title if exists
        if (titleEndIndex < bodyElements.Count)
        {
            var content = ParseElementContent(bodyElements[titleEndIndex].DelemenJsonTree);
            if (IsEmptyElement(content))
            {
                titleEndIndex++;
            }
        }

        // Find first subchapter (element with section_header label that matches X.X pattern)
        var subchapterPattern = new System.Text.RegularExpressions.Regex(@"^\d+\.\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
        int? firstSubchapterIndex = null;
        
        for (int i = titleEndIndex; i < bodyElements.Count; i++)
        {
            var elemId = bodyElements[i].DelemenId;
            if (labelMap.TryGetValue(elemId, out var label) && 
                label.Equals("section_header", StringComparison.OrdinalIgnoreCase))
            {
                var content = ParseElementContent(bodyElements[i].DelemenJsonTree);
                var plainText = content.PlainText?.Trim() ?? string.Empty;
                
                if (subchapterPattern.IsMatch(plainText))
                {
                    firstSubchapterIndex = i;
                    break;
                }
            }
        }

        if (!firstSubchapterIndex.HasValue)
        {
            // No subchapter found, no need to validate
            return;
        }

        // Check if there's at least one paragraph (text) between title and first subchapter
        result.TotalChecks++;
        bool hasParagraphBefore = false;
        
        for (int i = titleEndIndex; i < firstSubchapterIndex.Value; i++)
        {
            var elemId = bodyElements[i].DelemenId;
            if (labelMap.TryGetValue(elemId, out var label) && 
                label.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                var content = ParseElementContent(bodyElements[i].DelemenJsonTree);
                if (!IsEmptyElement(content))
                {
                    hasParagraphBefore = true;
                    break;
                }
            }
        }

        if (hasParagraphBefore)
        {
            result.PassedChecks++;
        }
        else
        {
            // Load location for error
            var firstSubchapterId = bodyElements[firstSubchapterIndex.Value].DelemenId;
            var pageNumbers = await LoadPageNumbersAsync(new[] { firstSubchapterId }, cancellationToken);
            var mergedBbox = await LoadMergedBboxAsync(new[] { firstSubchapterId }, cancellationToken);
            var pageNumber = pageNumbers.Values.FirstOrDefault();
            var locations = CreateLocations(pageNumber, mergedBbox);

            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "judul_bab",
                Message = "Harus ada minimal 1 paragraf setelah judul bab sebelum judul subbab",
                Expected = "Minimal 1 paragraf",
                Actual = "Langsung ke judul subbab",
                Locations = locations
            });
        }
    }

    private async Task<Dictionary<ulong, string>> LoadVisualLabelsAsync(
        IEnumerable<ulong> delemenIds,
        CancellationToken cancellationToken)
    {
        var ids = delemenIds.Distinct().ToList();
        var labels = new Dictionary<ulong, string>();

        if (ids.Count == 0)
            return labels;

        var (idColumn, labelColumn) = await ResolveVisualColumnsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn) || string.IsNullOrWhiteSpace(labelColumn))
            return labels;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            foreach (var chunk in ids.Chunk(500))
            {
                var idList = string.Join(",", chunk);
                var sql = $"SELECT `{idColumn}` AS delemen_id, `{labelColumn}` AS label " +
                          $"FROM `dokumen_elemen_visual` WHERE `{idColumn}` IN ({idList})";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader["delemen_id"] == DBNull.Value)
                        continue;

                    var id = Convert.ToUInt64(reader["delemen_id"]);
                    var label = reader["label"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(label))
                        labels[id] = label;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load labels from dokumen_elemen_visual");
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }

        return labels;
    }

    private async Task<Dictionary<ulong, int>> LoadPageNumbersAsync(
        IEnumerable<ulong> delemenIds,
        CancellationToken cancellationToken)
    {
        var ids = delemenIds.Distinct().ToList();
        var pageNumbers = new Dictionary<ulong, int>();

        if (ids.Count == 0)
            return pageNumbers;

        var (idColumn, _) = await ResolveVisualColumnsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn))
            return pageNumbers;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            foreach (var chunk in ids.Chunk(500))
            {
                var idList = string.Join(",", chunk);
                var sql = $"SELECT `{idColumn}` AS delemen_id, `dev_page` " +
                          $"FROM `dokumen_elemen_visual` WHERE `{idColumn}` IN ({idList}) AND `dev_page` IS NOT NULL";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader["delemen_id"] == DBNull.Value || reader["dev_page"] == DBNull.Value)
                        continue;

                    var id = Convert.ToUInt64(reader["delemen_id"]);
                    var page = Convert.ToInt32(reader["dev_page"]);
                    pageNumbers[id] = page;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load page numbers from dokumen_elemen_visual");
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }

        return pageNumbers;
    }

    private async Task<string?> LoadMergedBboxAsync(
        IEnumerable<ulong> delemenIds,
        CancellationToken cancellationToken)
    {
        var ids = delemenIds.Distinct().ToList();
        if (ids.Count == 0)
            return null;

        var (idColumn, _) = await ResolveVisualColumnsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn))
            return null;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        double? minX0 = null, minY0 = null, maxX1 = null, maxY1 = null;

        try
        {
            foreach (var chunk in ids.Chunk(500))
            {
                var idList = string.Join(",", chunk);
                var sql = $"SELECT `dev_bbox_x0`, `dev_bbox_y0`, `dev_bbox_x1`, `dev_bbox_y1` " +
                          $"FROM `dokumen_elemen_visual` WHERE `{idColumn}` IN ({idList})";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    double? x0 = reader["dev_bbox_x0"] != DBNull.Value ? Convert.ToDouble(reader["dev_bbox_x0"]) : null;
                    double? y0 = reader["dev_bbox_y0"] != DBNull.Value ? Convert.ToDouble(reader["dev_bbox_y0"]) : null;
                    double? x1 = reader["dev_bbox_x1"] != DBNull.Value ? Convert.ToDouble(reader["dev_bbox_x1"]) : null;
                    double? y1 = reader["dev_bbox_y1"] != DBNull.Value ? Convert.ToDouble(reader["dev_bbox_y1"]) : null;

                    if (x0.HasValue && y0.HasValue && x1.HasValue && y1.HasValue)
                    {
                        minX0 = minX0.HasValue ? Math.Min(minX0.Value, x0.Value) : x0.Value;
                        minY0 = minY0.HasValue ? Math.Min(minY0.Value, y0.Value) : y0.Value;
                        maxX1 = maxX1.HasValue ? Math.Max(maxX1.Value, x1.Value) : x1.Value;
                        maxY1 = maxY1.HasValue ? Math.Max(maxY1.Value, y1.Value) : y1.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load bbox from dokumen_elemen_visual");
            return null;
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }

        if (!minX0.HasValue || !minY0.HasValue || !maxX1.HasValue || !maxY1.HasValue)
            return null;

        var bbox = new { x0 = minX0.Value, y0 = minY0.Value, x1 = maxX1.Value, y1 = maxY1.Value };
        return JsonSerializer.Serialize(bbox);
    }

    private async Task<(string? IdColumn, string? LabelColumn)> ResolveVisualColumnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync(cancellationToken);

            var columns = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                                  "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dokumen_elemen_visual'";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name))
                        columns.Add(name);
                }
            }

            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();

            if (columns.Count == 0)
                return (null, null);

            var idColumn = columns.FirstOrDefault(c => c.Equals("delemen_id", StringComparison.OrdinalIgnoreCase))
                ?? columns.FirstOrDefault(c => c.EndsWith("delemen_id", StringComparison.OrdinalIgnoreCase))
                ?? columns.FirstOrDefault(c =>
                    c.IndexOf("elemen", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    c.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0);

            var labelColumn = columns.FirstOrDefault(c => c.Equals("label", StringComparison.OrdinalIgnoreCase))
                ?? columns.FirstOrDefault(c => c.EndsWith("label", StringComparison.OrdinalIgnoreCase))
                ?? columns.FirstOrDefault(c => c.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0);

            return (idColumn, labelColumn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve dokumen_elemen_visual columns");
            return (null, null);
        }
    }

    private static string NormalizeWhitespace(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return Regex.Replace(input.Trim(), "\\s+", " ");
    }

    private static bool HasDisallowedWhitespace(string input)
    {
        if (string.IsNullOrEmpty(input))
            return true;

        // Check for leading whitespace (not allowed)
        if (input.Length > 0 && char.IsWhiteSpace(input[0]))
            return true;

        // Check for tabs (not allowed)
        if (input.Contains('\t'))
            return true;

        // Check for double spaces (not allowed)
        if (input.Contains("  "))
            return true;

        // Allow single trailing space, but not more
        var trimmed = input.TrimEnd();
        var trailingSpaces = input.Length - trimmed.Length;
        if (trailingSpaces > 1)
            return true;

        return false;
    }

    private static bool MatchesNumberFormat(string numberLine, string template, string? caseRule)
    {
        var normalizedLine = NormalizeWhitespace(numberLine);
        var normalizedTemplate = NormalizeWhitespace(template);

        if (string.IsNullOrWhiteSpace(normalizedLine) || string.IsNullOrWhiteSpace(normalizedTemplate))
            return false;

        if (caseRule != null)
        {
            if (caseRule.Equals("UPPERCASE", StringComparison.OrdinalIgnoreCase) &&
                normalizedLine != normalizedLine.ToUpperInvariant())
                return false;
            if (caseRule.Equals("LOWERCASE", StringComparison.OrdinalIgnoreCase) &&
                normalizedLine != normalizedLine.ToLowerInvariant())
                return false;
        }

        var match = Regex.Match(normalizedTemplate, "\\b\\d+\\b|\\b[IVXLCDM]+\\b", RegexOptions.IgnoreCase);
        if (!match.Success)
            return string.Equals(normalizedLine, normalizedTemplate, StringComparison.OrdinalIgnoreCase);

        var prefix = normalizedTemplate.Substring(0, match.Index);
        var suffix = normalizedTemplate.Substring(match.Index + match.Length);
        var isNumeric = Regex.IsMatch(match.Value, "^\\d+$");

        var numberPattern = isNumeric
            ? "\\d+"
            : caseRule != null && caseRule.Equals("LOWERCASE", StringComparison.OrdinalIgnoreCase)
                ? "[ivxlcdm]+"
                : "[IVXLCDM]+";

        var pattern = "^" + Regex.Escape(prefix) + numberPattern + Regex.Escape(suffix) + "$";
        return Regex.IsMatch(normalizedLine, pattern);
    }

    private static bool HasIndentation(DokumenFormatParagraf format)
    {
        return (format.DfpIndLeftTwips ?? 0) != 0 ||
               (format.DfpIndRightTwips ?? 0) != 0 ||
               (format.DfpIndFirstLineTwips ?? 0) != 0 ||
               (format.DfpIndHangingTwips ?? 0) != 0 ||
               (format.DfpIndStartTwips ?? 0) != 0 ||
               (format.DfpIndEndTwips ?? 0) != 0 ||
               (format.DfpIndLeftChars ?? 0) != 0 ||
               (format.DfpIndRightChars ?? 0) != 0;
    }

    private static string GetIndentationDetails(List<DokumenFormatParagraf?> paragraphs)
    {
        var details = new List<string>();

        foreach (var pf in paragraphs.Where(p => p != null))
        {
            var leftTwips = pf!.DfpIndLeftTwips ?? pf.DfpIndStartTwips ?? 0;
            var rightTwips = pf.DfpIndRightTwips ?? pf.DfpIndEndTwips ?? 0;
            var firstLineTwips = pf.DfpIndFirstLineTwips ?? 0;
            var hangingTwips = pf.DfpIndHangingTwips ?? 0;

            // Convert twips to cm (1 twip = 1/1440 inch, 1 inch = 2.54 cm)
            var leftCm = leftTwips / 1440.0 * 2.54;
            var rightCm = rightTwips / 1440.0 * 2.54;

            // Special indent: positive = first line, negative = hanging
            var specialCm = firstLineTwips > 0
                ? firstLineTwips / 1440.0 * 2.54
                : hangingTwips > 0
                    ? -(hangingTwips / 1440.0 * 2.54)
                    : 0;

            var specialType = firstLineTwips > 0 ? "first line" : hangingTwips > 0 ? "hanging" : "none";

            details.Add($"Left: {leftCm:F2} cm, Right: {rightCm:F2} cm, Special: {specialCm:F2} cm ({specialType})");
        }

        return details.Count > 0 ? string.Join("; ", details.Distinct()) : "unknown";
    }

    private static decimal? GetLineSpacing(DokumenFormatParagraf format)
    {
        if (!format.DfpSpacingLineTwips.HasValue)
            return null;

        if (string.IsNullOrWhiteSpace(format.DfpSpacingLineRule) ||
            string.Equals(format.DfpSpacingLineRule, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return format.DfpSpacingLineTwips.Value / 240m;
        }

        return null;
    }

    private static decimal? TwipsToPoints(uint? twips)
    {
        return twips.HasValue ? twips.Value / 20m : null;
    }

    private static bool IsWithinTolerance(decimal actual, decimal expected, decimal tolerance)
    {
        return Math.Abs(actual - expected) <= tolerance;
    }

    private static bool HasTitleFontRule(ChapterTitleRule? rule)
    {
        return !string.IsNullOrWhiteSpace(rule?.Font?.FontName?.Value) ||
               rule?.Font?.FontSize?.Value.HasValue == true ||
               rule?.Font?.FontStyle?.Bold != null ||
               rule?.Font?.FontStyle?.Italic != null ||
               rule?.Font?.FontStyle?.Underline != null;
    }
}

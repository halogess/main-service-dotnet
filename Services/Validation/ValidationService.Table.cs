using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private sealed class TableContentAggregate
    {
        public List<uint> TextFormatIds { get; } = new();
        public List<TextRunInfo> TextRuns { get; } = new();
        public List<uint> ParagraphFormatIds { get; } = new();
        public bool HasImageContent { get; set; }
        public bool HasNonTextContent { get; set; }
    }

    private sealed class TableBlockInfo
    {
        public ulong ElementId { get; init; }
        public int OrderIndex { get; init; }
        public uint? TableFormatId { get; init; }
        public TableContentAggregate Content { get; init; } = new();
        public string Evidence { get; init; } = "Tabel";
    }

    private sealed class TableStructuralViolationInfo
    {
        public ulong ElementId { get; init; }
        public string? ElementType { get; init; }
        public string Evidence { get; init; } = "Tabel";
    }

    private async Task<ValidationResult> ValidateTableAsync(int dokumenId, CancellationToken cancellationToken)
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
            return result;

        var tableDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "tabel")
            .FirstOrDefaultAsync(cancellationToken);

        var captionDetail = await _db.AturanDetails
            .Where(d => d.AturanId == aturan.AturanId && d.AturanDetailStatus == 1)
            .Where(d => d.AturanDetailKategori == "Isi Buku")
            .Where(d => d.AturanDetailKey == "caption_tabel")
            .FirstOrDefaultAsync(cancellationToken);

        if (tableDetail == null && captionDetail == null)
            return result;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        TableRule? tableRule = null;
        TableCaptionRule? captionRule = null;

        if (tableDetail != null)
        {
            try
            {
                var rawJson = tableDetail.AturanDetailJsonValue ?? "{}";
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("tabel", out var tableElement))
                    {
                        tableRule = JsonSerializer.Deserialize<TableRule>(tableElement.GetRawText(), jsonOptions);
                    }
                    else
                    {
                        tableRule = JsonSerializer.Deserialize<TableRule>(rawJson, jsonOptions);
                    }

                    if (captionDetail == null &&
                        doc.RootElement.TryGetProperty("caption_tabel", out var captionElement))
                    {
                        captionRule = JsonSerializer.Deserialize<TableCaptionRule>(captionElement.GetRawText(), jsonOptions);
                    }
                }
                else
                {
                    tableRule = JsonSerializer.Deserialize<TableRule>(rawJson, jsonOptions);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse aturan tabel");
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "tabel",
                    Message = "Format aturan tabel tidak valid"
                });
                return result;
            }
        }

        if (captionDetail != null)
        {
            try
            {
                captionRule = JsonSerializer.Deserialize<TableCaptionRule>(
                    captionDetail.AturanDetailJsonValue ?? "{}",
                    jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse aturan caption_tabel");
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "caption_tabel",
                    Message = "Format aturan caption tabel tidak valid"
                });
            }
        }

        if (tableRule == null && captionRule == null)
            return result;

        var (sectionRefType, sectionRefId) = ResolveSectionRefForValidation(dokumenId);
        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DsecRefTipe == sectionRefType && s.DsecRefId == sectionRefId && p.DpartType == "body"
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
        var pageNumbersById = await LoadPageNumbersAsync(orderedElementIds, cancellationToken);
        var pagesWithParagraph = BuildPagesWithParagraph(bodyElements, labelMap, pageNumbersById);

        var captionPrefixes = BuildCaptionPrefixes(captionRule?.Numbering);

        var tableBlocks = new List<TableBlockInfo>();
        var structuralViolations = new List<TableStructuralViolationInfo>();
        var captionCandidates = new List<CaptionInfo>();

        for (var index = 0; index < bodyElements.Count; index++)
        {
            var elem = bodyElements[index];
            var normalizedLabel = labelMap.TryGetValue(elem.DelemenId, out var rawLabel)
                ? NormalizeLabel(rawLabel)
                : string.Empty;

            if (IsTableLabel(normalizedLabel) && IsTableImageElementType(elem.DelemenType))
            {
                structuralViolations.Add(new TableStructuralViolationInfo
                {
                    ElementId = elem.DelemenId,
                    ElementType = elem.DelemenType
                });
                continue;
            }

            if (IsTableElement(normalizedLabel))
            {
                var contentAggregate = ExtractTableContentAggregate(elem.DelemenJsonTree, out var tableFormatId);
                var evidence = tableFormatId.HasValue ? $"Tabel (dft:{tableFormatId.Value})" : "Tabel";
                tableBlocks.Add(new TableBlockInfo
                {
                    ElementId = elem.DelemenId,
                    OrderIndex = index,
                    TableFormatId = tableFormatId,
                    Content = contentAggregate,
                    Evidence = evidence
                });
            }

            if (captionRule != null)
            {
                if (normalizedLabel != "caption_tabel")
                    continue;

                var content = ParseElementContent(elem.DelemenJsonTree);
                var normalizedText = NormalizeWhitespace(content.PlainText);
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

        foreach (var violation in structuralViolations)
        {
            var errorStart = result.Errors.Count;
            var locations = await BuildElementLocationsAsync(violation.ElementId, cancellationToken);

            if (tableRule?.CegahGambarTabel?.Value == true)
            {
                result.IncrementTotalChecks();
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "tabel",
                    Message = "Tabel tidak boleh berupa gambar",
                    Expected = "Elemen tabel bertipe tabel, bukan gambar",
                    Actual = $"Tipe elemen: {violation.ElementType ?? "unknown"}",
                    Evidence = violation.Evidence,
                    Locations = locations
                });
            }

            if (neighborContexts.TryGetValue(violation.ElementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, violation.ElementId);
        }

        if (tableBlocks.Count == 0)
            return result;

        var tableFormatIds = tableBlocks
            .Select(b => b.TableFormatId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var tableFormats = tableFormatIds.Count > 0
            ? await _db.DokumenFormatTables
                .Where(t => tableFormatIds.Contains(t.DftId))
                .ToDictionaryAsync(t => t.DftId, cancellationToken)
            : new Dictionary<uint, DokumenFormatTable>();

        var textFormatIds = tableBlocks
            .SelectMany(b => b.Content.TextFormatIds)
            .Concat(captionCandidates.SelectMany(c => c.Content.TextFormatIds))
            .Distinct()
            .ToList();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        var paragraphFormatIds = tableBlocks
            .SelectMany(b => b.Content.ParagraphFormatIds)
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

        var usedCaptionIds = new HashSet<ulong>();

        for (var i = 0; i < tableBlocks.Count; i++)
        {
            var block = tableBlocks[i];
            var errorStart = result.Errors.Count;
            var locations = await BuildElementLocationsAsync(block.ElementId, cancellationToken);

            if (tableRule != null)
            {
                if (tableRule.Position?.Alignment?.Value != null &&
                    tableFormats.TryGetValue(block.TableFormatId ?? 0, out var tableFormat))
                {
                    var expectedAlignment = tableRule.Position.Alignment.Value;
                    result.IncrementTotalChecks();
                    var actualAlignment = tableFormat.DftJc ?? "unknown";
                    pageLayoutsById.TryGetValue(block.ElementId, out var tableLayout);
                    var isNearFullWidthCenterCase = IsCenterAlignmentSatisfiedByNearFullWidth(
                        expectedAlignment,
                        tableFormat,
                        tableLayout,
                        locations);

                    if (AreAlignmentsEquivalent(actualAlignment, expectedAlignment) || isNearFullWidthCenterCase)
                    {
                        result.IncrementPassedChecks();
                    }
                    else
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "tabel",
                            Message = "Alignment tabel tidak sesuai",
                            Expected = expectedAlignment,
                            Actual = actualAlignment,
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                }

                if (tableRule.Position?.IndentFromLeft?.Value.HasValue == true &&
                    tableFormats.TryGetValue(block.TableFormatId ?? 0, out var indentFormat))
                {
                    result.IncrementTotalChecks();
                    var expectedIndent = tableRule.Position.IndentFromLeft.Value.Value;
                    var actualTwips = indentFormat.DftTblIndTwips ?? 0;
                    var actualIndent = actualTwips / 1440.0m * 2.54m;
                    if (Math.Abs(actualIndent - expectedIndent) <= 0.05m)
                    {
                        result.IncrementPassedChecks();
                    }
                    else
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "tabel",
                            Message = "Indentasi tabel dari kiri tidak sesuai",
                            Expected = expectedIndent.ToString(CultureInfo.InvariantCulture) + " cm",
                            Actual = actualIndent.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                }

                if (tableRule.Position?.CegahMelebihiMargin?.Value == true &&
                    tableFormats.TryGetValue(block.TableFormatId ?? 0, out var widthFormat) &&
                    pageLayoutsById.TryGetValue(block.ElementId, out var layout))
                {
                    result.IncrementTotalChecks();

                    var availableWidth = GetAvailableWidthCm(layout);
                    var indentTwips = widthFormat.DftTblIndTwips ?? 0;
                    var indentCm = indentTwips / 1440.0m * 2.54m;
                    if (availableWidth.HasValue)
                        availableWidth = Math.Max(0m, availableWidth.Value - Math.Max(0m, indentCm));

                    var tableWidth = ResolveTableWidthCm(widthFormat, layout);

                    if (tableWidth.HasValue && availableWidth.HasValue && tableWidth.Value > availableWidth.Value + 0.2m)
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "tabel",
                            Message = "Lebar tabel melebihi margin halaman",
                            Expected = "Lebar <= lebar area teks",
                            Actual = $"{tableWidth.Value:F2} cm (max {availableWidth.Value:F2} cm)",
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                    else
                    {
                        result.IncrementPassedChecks();
                    }
                }

                if (tableRule.Position?.CegahMemenuhiHalaman?.Value == true)
                {
                    if (pageNumbersById.TryGetValue(block.ElementId, out var pageNumber) && pageNumber > 0)
                    {
                        result.IncrementTotalChecks();
                        if (pagesWithParagraph.Contains(pageNumber))
                        {
                            result.IncrementPassedChecks();
                        }
                        else
                        {
                            result.Errors.Add(new ValidationError
                            {
                                Category = "Isi Buku",
                                Field = "tabel",
                                Message = "Halaman tabel tidak boleh hanya berisi tabel dan caption",
                                Expected = "Ada paragraf lain di halaman",
                                Actual = "Tidak ada paragraf lain",
                                Evidence = block.Evidence,
                                Locations = locations
                            });
                        }
                    }
                }

                if (tableRule.CegahGambarTabel?.Value == true)
                {
                    result.IncrementTotalChecks();
                    if (!block.Content.HasImageContent)
                    {
                        result.IncrementPassedChecks();
                    }
                    else
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "tabel",
                            Message = "Tabel tidak boleh berisi gambar",
                            Expected = "Tidak ada gambar di tabel",
                            Actual = "Gambar terdeteksi",
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                }

                if (tableRule.KontenTabel?.Font != null)
                {
                    var elementTextFormats = block.Content.TextFormatIds
                        .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                        .Where(tf => tf != null)
                        .ToList();

                    ValidateTableContentFont(result, tableRule.KontenTabel.Font, elementTextFormats!, block.Content.TextRuns, block.Evidence, locations);
                }

                if (tableRule.KontenTabel?.Paragraph?.Spacing != null && block.Content.ParagraphFormatIds.Count > 0)
                {
                    var tableParagraphFormats = block.Content.ParagraphFormatIds
                        .Select(id => paragraphFormats.TryGetValue(id, out var pf) ? pf : null)
                        .Where(pf => pf != null)
                        .ToList();

                    ValidateTableContentSpacing(result, tableRule.KontenTabel.Paragraph, tableParagraphFormats!, block.Evidence, locations);
                }
            }

            if (captionRule != null)
            {
                var nextTableIndex = i + 1 < tableBlocks.Count ? tableBlocks[i + 1].OrderIndex : int.MaxValue;
                var prevTableIndex = i > 0 ? tableBlocks[i - 1].OrderIndex : -1;

                var captionAfterCandidates = captionCandidates
                    .Where(c => !usedCaptionIds.Contains(c.ElementId) &&
                                c.OrderIndex > block.OrderIndex &&
                                c.OrderIndex < nextTableIndex)
                    .OrderBy(c => c.OrderIndex)
                    .ToList();

                var captionBeforeCandidates = captionCandidates
                    .Where(c => !usedCaptionIds.Contains(c.ElementId) &&
                                c.OrderIndex < block.OrderIndex &&
                                c.OrderIndex > prevTableIndex)
                    .OrderBy(c => c.OrderIndex)
                    .ToList();

                CaptionInfo? captionAfter = SelectPreferredCaptionCandidate(captionAfterCandidates, captionPrefixes, preferNearestBefore: false);
                CaptionInfo? captionBefore = SelectPreferredCaptionCandidate(captionBeforeCandidates, captionPrefixes, preferNearestBefore: true);

                CaptionInfo? selectedCaption = null;
                var positionRule = captionRule.Position?.Value?.Trim();
                var normalizedPosition = string.IsNullOrWhiteSpace(positionRule)
                    ? null
                    : positionRule.ToLowerInvariant();

                if (normalizedPosition == "before")
                {
                    if (captionBefore != null)
                    {
                        selectedCaption = captionBefore;
                    }
                    else if (captionAfter != null)
                    {
                        result.IncrementTotalChecks();
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "caption_tabel",
                            Message = "Posisi caption tabel harus sebelum tabel",
                            Expected = "before",
                            Actual = "after",
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                    else
                    {
                        result.IncrementTotalChecks();
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "caption_tabel",
                            Message = "Caption tabel tidak ditemukan",
                            Expected = "Caption sebelum tabel",
                            Actual = "Tidak ada caption",
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                }
                else if (normalizedPosition == "after")
                {
                    if (captionAfter != null)
                    {
                        selectedCaption = captionAfter;
                    }
                    else if (captionBefore != null)
                    {
                        result.IncrementTotalChecks();
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "caption_tabel",
                            Message = "Posisi caption tabel harus setelah tabel",
                            Expected = "after",
                            Actual = "before",
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                    else
                    {
                        result.IncrementTotalChecks();
                        result.Errors.Add(new ValidationError
                        {
                            Category = "Isi Buku",
                            Field = "caption_tabel",
                            Message = "Caption tabel tidak ditemukan",
                            Expected = "Caption setelah tabel",
                            Actual = "Tidak ada caption",
                            Evidence = block.Evidence,
                            Locations = locations
                        });
                    }
                }
                else
                {
                    selectedCaption = captionAfter ?? captionBefore;
                }

                if (selectedCaption != null)
                {
                    var continuationCaption = SelectCaptionContinuationCandidate(
                        selectedCaption,
                        block.OrderIndex,
                        captionBeforeCandidates,
                        captionAfterCandidates,
                        captionPrefixes,
                        captionRule.Numbering);

                    usedCaptionIds.Add(selectedCaption.ElementId);
                    if (continuationCaption != null)
                        usedCaptionIds.Add(continuationCaption.ElementId);

                    IEnumerable<ulong> captionLocationIds = continuationCaption != null
                        ? new[] { selectedCaption.ElementId, continuationCaption.ElementId }
                        : new[] { selectedCaption.ElementId };

                    var captionLocations = await BuildElementLocationsAsync(captionLocationIds, cancellationToken);
                    var captionErrorStart = result.Errors.Count;

                    var captionTextFormats = selectedCaption.Content.TextFormatIds
                        .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                        .Where(tf => tf != null)
                        .ToList();

                    ValidateCaptionFont(result, captionRule.Font, selectedCaption, captionTextFormats!, "caption_tabel", "caption tabel", captionLocations);

                    if (selectedCaption.Content.ParagraphFormatId.HasValue &&
                        paragraphFormats.TryGetValue(selectedCaption.Content.ParagraphFormatId.Value, out var captionFormat))
                    {
                        pageLayoutsById.TryGetValue(selectedCaption.ElementId, out var captionPageLayout);
                        ValidateCaptionParagraphFormat(result, captionRule.Paragraph, captionFormat, "caption_tabel", "caption tabel", selectedCaption.NormalizedText, captionLocations, captionPageLayout);
                    }

                    var numberingTextOverride = continuationCaption != null
                        ? NormalizeWhitespace(selectedCaption.NormalizedText + " " + continuationCaption.NormalizedText)
                        : null;

                    ValidateCaptionNumbering(result, captionRule.Numbering, selectedCaption, "caption_tabel", "caption tabel", captionLocations, numberingTextOverride);

                    if (neighborContexts.TryGetValue(selectedCaption.ElementId, out var captionContext))
                        ApplyContextToErrors(result.Errors, captionErrorStart, captionContext);

                    ApplyElementIdToErrors(result.Errors, captionErrorStart, selectedCaption.ElementId);
                }
            }

            if (neighborContexts.TryGetValue(block.ElementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, block.ElementId);
        }

        return result;
    }

    private static bool IsTableElement(string normalizedLabel)
    {
        if (IsCodeLabel(normalizedLabel))
            return false;

        return normalizedLabel == "tabel" || normalizedLabel == "table";
    }

    private static bool IsTableLabel(string normalizedLabel)
    {
        return normalizedLabel == "tabel" || normalizedLabel == "table";
    }

    private static bool IsTableImageElementType(string? elementType)
    {
        if (string.IsNullOrWhiteSpace(elementType))
            return false;

        return elementType.Equals("gambar", StringComparison.OrdinalIgnoreCase) ||
               IsImageType(elementType);
    }

    private static TableContentAggregate ExtractTableContentAggregate(string? json, out uint? tableFormatId)
    {
        var aggregate = new TableContentAggregate();
        tableFormatId = null;

        if (string.IsNullOrWhiteSpace(json))
            return aggregate;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("dft_id", out var dftEl) && dftEl.TryGetUInt32(out var dftId))
                tableFormatId = dftId;

            CollectTableContent(root, aggregate);
        }
        catch (JsonException)
        {
            // Ignore invalid JSON.
        }

        return aggregate;
    }

    private static void CollectTableContent(JsonElement tableObj, TableContentAggregate aggregate)
    {
        if (tableObj.ValueKind != JsonValueKind.Object)
            return;

        if (!tableObj.TryGetProperty("content", out var contentObj) || contentObj.ValueKind != JsonValueKind.Object)
            return;

        if (!contentObj.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var row in rowsEl.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;

            if (!row.TryGetProperty("cells", out var cellsEl) || cellsEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var cell in cellsEl.EnumerateArray())
            {
                if (cell.ValueKind != JsonValueKind.Object)
                    continue;

                if (!cell.TryGetProperty("content", out var cellContent) || cellContent.ValueKind != JsonValueKind.Array)
                    continue;

                CollectCellContent(cellContent, aggregate);
            }
        }
    }

    private static void CollectCellContent(JsonElement cellContent, TableContentAggregate aggregate)
    {
        foreach (var item in cellContent.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            string? type = null;
            if (item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                type = typeEl.GetString();

            if (!string.IsNullOrWhiteSpace(type) && type.Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                if (item.TryGetProperty("content", out var nestedTable) && nestedTable.ValueKind == JsonValueKind.Object)
                    CollectTableContent(nestedTable, aggregate);
                continue;
            }

            if (IsImageType(type))
            {
                aggregate.HasImageContent = true;
                aggregate.HasNonTextContent = true;
                continue;
            }

            if (item.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
            {
                var info = ParseElementContent(item.GetRawText());
                AppendContentInfo(aggregate, info);
                if (info.HasNonTextContent)
                    aggregate.HasNonTextContent = true;

                if (ContainsImageItem(contentEl))
                    aggregate.HasImageContent = true;
                continue;
            }

            aggregate.HasNonTextContent = true;
        }
    }

    private static void AppendContentInfo(TableContentAggregate aggregate, ElementContentInfo info)
    {
        if (info.ParagraphFormatId.HasValue)
            aggregate.ParagraphFormatIds.Add(info.ParagraphFormatId.Value);

        aggregate.TextFormatIds.AddRange(info.TextFormatIds);
        aggregate.TextRuns.AddRange(info.TextRuns);

        if (info.HasNonTextContent)
            aggregate.HasNonTextContent = true;
    }

    private static bool ContainsImageItem(JsonElement contentArray)
    {
        foreach (var item in contentArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            {
                var type = typeEl.GetString();
                if (IsImageType(type))
                    return true;
            }
        }

        return false;
    }

    private static bool IsImageType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type.Equals("image", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("shape", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("chart", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("composite", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? ResolveTableWidthCm(DokumenFormatTable format, PageLayoutSnapshot layout)
    {
        if (format == null)
            return null;

        var widthType = format.DftTblWType ?? string.Empty;
        if (widthType.Equals("dxa", StringComparison.OrdinalIgnoreCase) && format.DftTblWTwips.HasValue)
            return Math.Round(format.DftTblWTwips.Value / 1440.0m * 2.54m, 2);

        if (widthType.Equals("pct", StringComparison.OrdinalIgnoreCase) && format.DftTblWPct50.HasValue && layout.WidthCm.HasValue)
        {
            var fraction = format.DftTblWPct50.Value / 5000m;
            return Math.Round(layout.WidthCm.Value * fraction, 2);
        }

        return null;
    }

    private static bool IsCenterAlignmentSatisfiedByNearFullWidth(
        string? expectedAlignment,
        DokumenFormatTable tableFormat,
        PageLayoutSnapshot? layout,
        IReadOnlyList<ErrorLocation>? locations)
    {
        if (!string.Equals(NormalizeAlignmentValue(expectedAlignment), "center", StringComparison.OrdinalIgnoreCase))
            return false;

        if (layout == null)
            return false;

        var availableWidth = GetAvailableWidthCm(layout);
        if (!availableWidth.HasValue || availableWidth.Value <= 0m)
            return false;

        var indentTwips = tableFormat.DftTblIndTwips ?? 0;
        var indentCm = indentTwips / 1440.0m * 2.54m;
        var effectiveAvailableWidth = Math.Max(0m, availableWidth.Value - Math.Max(0m, indentCm));
        if (effectiveAvailableWidth <= 0m)
            return false;

        var tableWidth = ResolveTableWidthCm(tableFormat, layout);
        if (!tableWidth.HasValue || tableWidth.Value <= 0m)
            tableWidth = ResolveTableWidthFromLocationsCm(locations, layout);

        if (!tableWidth.HasValue || tableWidth.Value <= 0m)
            return false;

        const decimal nearFullToleranceCm = 0.2m;
        var remaining = effectiveAvailableWidth - tableWidth.Value;
        return remaining >= 0m && remaining <= nearFullToleranceCm;
    }

    private static decimal? ResolveTableWidthFromLocationsCm(
        IReadOnlyList<ErrorLocation>? locations,
        PageLayoutSnapshot layout)
    {
        if (locations == null || locations.Count == 0 || !layout.WidthCm.HasValue || layout.WidthCm.Value <= 0m)
            return null;

        decimal? minX0 = null;
        decimal? maxX1 = null;
        foreach (var location in locations)
        {
            var bbox = location.Bbox;
            if (bbox == null)
                continue;

            minX0 = !minX0.HasValue ? bbox.X0 : Math.Min(minX0.Value, bbox.X0);
            maxX1 = !maxX1.HasValue ? bbox.X1 : Math.Max(maxX1.Value, bbox.X1);
        }

        if (!minX0.HasValue || !maxX1.HasValue)
            return null;

        if (!TryNormalizeHorizontalCoordinate(minX0.Value, layout, out var leftRatio) ||
            !TryNormalizeHorizontalCoordinate(maxX1.Value, layout, out var rightRatio))
            return null;

        var widthRatio = rightRatio - leftRatio;
        if (widthRatio <= 0m)
            return null;

        return Math.Round(layout.WidthCm.Value * widthRatio, 2);
    }

    private static List<string> BuildCaptionPrefixes(FlexibleCaptionNumberingRule? numberingRule)
    {
        var prefixes = new List<string>();
        var formats = numberingRule?.NumberFormat?.Value;
        if (formats == null || formats.Count == 0)
            return prefixes;

        foreach (var format in formats)
        {
            var prefix = ExtractCaptionPrefix(format);
            if (!string.IsNullOrWhiteSpace(prefix))
                prefixes.Add(prefix);
        }

        return prefixes;
    }

    private static string ExtractCaptionPrefix(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return string.Empty;

        var trimmed = format.Trim();
        var bracketIndex = trimmed.IndexOf('[');
        var prefix = bracketIndex > 0 ? trimmed[..bracketIndex] : trimmed;
        prefix = NormalizeWhitespace(prefix);
        return prefix.Trim().TrimEnd('.', ':', '-', ' ');
    }

    private static CaptionInfo? SelectPreferredCaptionCandidate(
        IReadOnlyList<CaptionInfo> candidates,
        IReadOnlyList<string> prefixes,
        bool preferNearestBefore)
    {
        if (candidates.Count == 0)
            return null;

        if (prefixes.Count > 0)
        {
            if (preferNearestBefore)
            {
                for (var i = candidates.Count - 1; i >= 0; i--)
                {
                    if (MatchesCaptionNumbering(candidates[i].NormalizedText, prefixes))
                        return candidates[i];
                }
            }
            else
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (MatchesCaptionNumbering(candidates[i].NormalizedText, prefixes))
                        return candidates[i];
                }
            }
        }

        return preferNearestBefore ? candidates[^1] : candidates[0];
    }

    private static CaptionInfo? SelectCaptionContinuationCandidate(
        CaptionInfo selectedCaption,
        int tableOrderIndex,
        IReadOnlyList<CaptionInfo> beforeCandidates,
        IReadOnlyList<CaptionInfo> afterCandidates,
        IReadOnlyList<string> prefixes,
        FlexibleCaptionNumberingRule? numberingRule)
    {
        if (numberingRule?.EnterAfterNumbering?.Value != true)
            return null;

        if (!TryParseCaptionNumberingWithPrefixes(selectedCaption.NormalizedText, prefixes, out var _parsedNumber, out var titleText))
            return null;

        if (!string.IsNullOrWhiteSpace(titleText))
            return null;

        CaptionInfo? continuation = null;

        if (selectedCaption.OrderIndex < tableOrderIndex)
        {
            continuation = beforeCandidates.FirstOrDefault(c =>
                c.OrderIndex > selectedCaption.OrderIndex &&
                c.ElementId != selectedCaption.ElementId);
        }
        else if (selectedCaption.OrderIndex > tableOrderIndex)
        {
            continuation = afterCandidates.FirstOrDefault(c =>
                c.OrderIndex > selectedCaption.OrderIndex &&
                c.ElementId != selectedCaption.ElementId);
        }

        if (continuation == null || string.IsNullOrWhiteSpace(continuation.NormalizedText))
            return null;

        if (MatchesCaptionNumbering(continuation.NormalizedText, prefixes))
            return null;

        return continuation;
    }

    private static bool MatchesCaptionNumbering(string text, IReadOnlyList<string> prefixes)
    {
        if (string.IsNullOrWhiteSpace(text) || prefixes.Count == 0)
            return false;

        foreach (var prefix in prefixes)
        {
            if (TryParseCaptionNumbering(text, prefix, out var _parsedNumber, out var _parsedTitle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCaptionNumberingWithPrefixes(
        string text,
        IReadOnlyList<string> prefixes,
        out string numberText,
        out string? titleText)
    {
        numberText = string.Empty;
        titleText = null;

        if (string.IsNullOrWhiteSpace(text) || prefixes.Count == 0)
            return false;

        foreach (var prefix in prefixes)
        {
            if (TryParseCaptionNumbering(text, prefix, out numberText, out titleText))
                return true;
        }

        return false;
    }

    private void ValidateTableContentFont(
        ValidationResult result,
        ParagraphFontRule rule,
        List<DokumenFormatText> textFormats,
        IReadOnlyList<TextRunInfo> textRuns,
        string evidence,
        List<ErrorLocation> locations)
    {
        if (textFormats.Count == 0)
            return;

        var textFormatById = BuildTextFormatMap(textFormats);
        var runs = GetMeaningfulRuns(textRuns);

        var expectedFontName = rule?.FontName?.Value;
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
                        Field = "tabel",
                        Message = "Font konten tabel tidak sesuai",
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
                        Field = "tabel",
                        Message = "Font konten tabel tidak sesuai",
                        Expected = expectedFontName,
                        Actual = string.Join(", ", actuals),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedFontSize = rule?.FontSize?.Value;
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
                        Field = "tabel",
                        Message = "Ukuran font konten tabel tidak sesuai",
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
                        Field = "tabel",
                        Message = "Ukuran font konten tabel tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt" : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }
    }

    private void ValidateTableContentSpacing(
        ValidationResult result,
        TableContentParagraphRule rule,
        List<DokumenFormatParagraf> paragraphFormats,
        string evidence,
        List<ErrorLocation> locations)
    {
        var spacingRule = rule?.Spacing;
        if (spacingRule == null || paragraphFormats.Count == 0)
            return;

        if (spacingRule.LineSpacing?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.LineSpacing.Value.Value;
            var actuals = paragraphFormats.Select(GetLineSpacing).Distinct().ToList();

            if (actuals.All(a => a.HasValue && Math.Abs(a.Value - expected) <= 0.05m))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "tabel",
                    Message = "Line spacing konten tabel tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown")),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (spacingRule.Before?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.Before.Value.Value;
            var actuals = paragraphFormats.Select(pf => TwipsToPoints(pf.DfpSpacingBeforeTwips)).Distinct().ToList();

            if (actuals.All(a => a.HasValue && IsWithinTolerance(a.Value, expected, 0.5m)))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "tabel",
                    Message = "Spacing before konten tabel tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown")),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }

        if (spacingRule.After?.Value.HasValue == true)
        {
            result.IncrementTotalChecks();
            var expected = spacingRule.After.Value.Value;
            var actuals = paragraphFormats.Select(pf => TwipsToPoints(pf.DfpSpacingAfterTwips)).Distinct().ToList();

            if (actuals.All(a => a.HasValue && IsWithinTolerance(a.Value, expected, 0.5m)))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "tabel",
                    Message = "Spacing after konten tabel tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = string.Join(", ", actuals.Select(a => a?.ToString(CultureInfo.InvariantCulture) ?? "unknown")),
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateCaptionFont(
        ValidationResult result,
        TitleFontRule? fontRule,
        CaptionInfo caption,
        List<DokumenFormatText> textFormats,
        string field,
        string label,
        List<ErrorLocation> locations)
    {
        if (fontRule == null || textFormats.Count == 0)
            return;

        var evidence = caption.NormalizedText.Length > 100
            ? caption.NormalizedText[..100] + "..."
            : caption.NormalizedText;

        var textFormatById = BuildTextFormatMap(textFormats);
        var runs = GetMeaningfulRuns(caption.Content.TextRuns);

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
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Font {label} tidak sesuai",
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
                        Field = field,
                        Message = $"Font {label} tidak sesuai",
                        Expected = expectedFontName,
                        Actual = string.Join(", ", actuals),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
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
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Ukuran font {label} tidak sesuai",
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
                        Field = field,
                        Message = $"Ukuran font {label} tidak sesuai",
                        Expected = expectedFontSize.Value.ToString(CultureInfo.InvariantCulture) + " pt",
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? (a.Value / 2m).ToString(CultureInfo.InvariantCulture) + " pt" : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedBold = fontRule.FontStyle?.Bold?.Value;
        if (expectedBold.HasValue)
        {
            result.IncrementTotalChecks();
            if (runs.Count > 0)
            {
                var mismatches = CollectRunMismatches(
                    runs,
                    textFormatById,
                    tf => !tf.DftxBold.HasValue || tf.DftxBold.Value != expectedBold.Value,
                    tf => tf.DftxBold.HasValue ? (tf.DftxBold.Value ? "Bold" : "Tidak Bold") : "unknown");

                if (mismatches.Count == 0)
                {
                    result.IncrementPassedChecks();
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Bold {label} tidak sesuai",
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
                    result.IncrementPassedChecks();
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Bold {label} tidak sesuai",
                        Expected = expectedBold.Value.ToString(),
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? a.Value.ToString() : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedItalic = fontRule.FontStyle?.Italic?.Value;
        if (expectedItalic.HasValue)
        {
            result.IncrementTotalChecks();
            if (runs.Count > 0)
            {
                var mismatches = CollectRunMismatches(
                    runs,
                    textFormatById,
                    tf => !tf.DftxItalic.HasValue || tf.DftxItalic.Value != expectedItalic.Value,
                    tf => tf.DftxItalic.HasValue ? (tf.DftxItalic.Value ? "Italic" : "Tidak Italic") : "unknown");

                if (mismatches.Count == 0)
                {
                    result.IncrementPassedChecks();
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Italic {label} tidak sesuai",
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
                    result.IncrementPassedChecks();
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Italic {label} tidak sesuai",
                        Expected = expectedItalic.Value.ToString(),
                        Actual = string.Join(", ", actuals.Select(a => a.HasValue ? a.Value.ToString() : "unknown")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }

        var expectedUnderline = fontRule.FontStyle?.Underline?.Value;
        if (expectedUnderline.HasValue)
        {
            result.IncrementTotalChecks();
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
                    result.IncrementPassedChecks();
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Underline {label} tidak sesuai",
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
                    result.IncrementPassedChecks();
                }
                else
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = field,
                        Message = $"Underline {label} tidak sesuai",
                        Expected = expectedUnderline.Value ? "true" : "false",
                        Actual = string.Join(", ", actuals.Select(a => a ?? "none")),
                        Evidence = evidence,
                        Locations = locations
                    });
                }
            }
        }
    }

    private void ValidateCaptionParagraphFormat(
        ValidationResult result,
        TitleParagraphRule? paragraphRule,
        DokumenFormatParagraf? format,
        string field,
        string label,
        string evidenceText,
        List<ErrorLocation> locations,
        PageLayoutSnapshot? pageLayout = null)
    {
        if (paragraphRule == null || format == null)
            return;

        var expectedAlignment = paragraphRule.Alignment?.Value;
        if (!string.IsNullOrWhiteSpace(expectedAlignment))
        {
            result.IncrementTotalChecks();
            var actual = format.DfpJc ?? "unknown";
            var alignmentContext = CreateAlignmentContext(evidenceText, locations, pageLayout);
            if (AreAlignmentsEquivalent(actual, expectedAlignment, alignmentContext))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = field,
                    Message = $"Alignment {label} tidak sesuai",
                    Expected = expectedAlignment,
                    Actual = actual,
                    Evidence = evidenceText,
                    Locations = locations
                });
            }
        }

        var indentationValue = paragraphRule.Indentation?.Value;
        if (!string.IsNullOrWhiteSpace(indentationValue) &&
            indentationValue.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            result.IncrementTotalChecks();
            if (!HasIndentation(format))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = field,
                    Message = $"Indentasi {label} harus none (left, right, special harus 0)",
                    Expected = "Left: 0, Right: 0, Special: 0",
                    Actual = GetIndentationDetails(new List<DokumenFormatParagraf?> { format }),
                    Evidence = evidenceText,
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
                    Category = "Isi Buku",
                    Field = field,
                    Message = $"Line spacing {label} tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture),
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                    Evidence = evidenceText,
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
                    Category = "Isi Buku",
                    Field = field,
                    Message = $"Spacing before {label} tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidenceText,
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
                    Category = "Isi Buku",
                    Field = field,
                    Message = $"Spacing after {label} tidak sesuai",
                    Expected = expected.ToString(CultureInfo.InvariantCulture) + " pt",
                    Actual = actual?.ToString(CultureInfo.InvariantCulture) + " pt" ?? "unknown",
                    Evidence = evidenceText,
                    Locations = locations
                });
            }
        }
    }

    private void ValidateCaptionNumbering(
        ValidationResult result,
        FlexibleCaptionNumberingRule? numberingRule,
        CaptionInfo caption,
        string field,
        string label,
        List<ErrorLocation> locations,
        string? numberingTextOverride = null)
    {
        if (numberingRule == null)
            return;

        var formats = numberingRule.NumberFormat?.Value ?? new List<string>();
        if (formats.Count == 0)
            return;

        var textForNumbering = string.IsNullOrWhiteSpace(numberingTextOverride)
            ? caption.NormalizedText
            : NormalizeWhitespace(numberingTextOverride);

        var prefixes = formats
            .Select(ExtractCaptionPrefix)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        var evidence = textForNumbering.Length > 100
            ? textForNumbering[..100] + "..."
            : textForNumbering;

        string numberText;
        string? titleText;
        var matched = false;
        numberText = string.Empty;
        titleText = null;

        foreach (var prefix in prefixes)
        {
            if (TryParseCaptionNumbering(textForNumbering, prefix, out numberText, out titleText))
            {
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            result.IncrementTotalChecks();
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = field,
                Message = $"Format nomor {label} tidak sesuai",
                Expected = string.Join(" / ", formats),
                Actual = textForNumbering,
                Evidence = evidence,
                Locations = locations
            });
            return;
        }

        result.IncrementTotalChecks();
        result.IncrementPassedChecks();

        var requireTitle = numberingRule.EnterAfterNumbering?.Value == true;
        if (requireTitle)
        {
            result.IncrementTotalChecks();
            if (!string.IsNullOrWhiteSpace(titleText))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = field,
                    Message = $"{label} harus memiliki judul setelah nomor",
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
            result.IncrementTotalChecks();
            if (IsTitleCaseText(titleText))
            {
                result.IncrementPassedChecks();
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = field,
                    Message = $"Judul {label} harus Title Case",
                    Expected = "Title Case",
                    Actual = titleText,
                    Evidence = evidence,
                    Locations = locations
                });
            }
        }
    }
}


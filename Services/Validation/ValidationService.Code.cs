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

    private sealed class CodeStructuralViolationInfo
    {
        public ulong ElementId { get; init; }
        public string? ElementType { get; init; }
        public ElementContentInfo Content { get; init; } = new();
        public bool IsTableType { get; init; }
        public bool IsImageType { get; init; }
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
        var paragraphRule = await LoadParagraphRuleAsync(aturan.AturanId, "blank code paragraph validation", cancellationToken);

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
        var visualSummaryById = await LoadVisualElementSummariesAsync(orderedElementIds, cancellationToken);
        var elementIndexById = bodyElements
            .Select((element, index) => new { element.DelemenId, Index = index })
            .ToDictionary(item => item.DelemenId, item => item.Index);
        var elementContentById = bodyElements.ToDictionary(
            element => element.DelemenId,
            element => ParseElementContent(element.DelemenJsonTree));
        var titlePrefixes = BuildCaptionPrefixes(titleRule?.Numbering);
        if (titlePrefixes.Count == 0)
        {
            titlePrefixes.Add("Algoritma");
            titlePrefixes.Add("Segmen Program");
        }
        var normalizedTitlePosition = NormalizePositionValue(titleRule?.Position?.Value);

        var codeElements = new List<CodeElementInfo>();
        var structuralViolations = new List<CodeStructuralViolationInfo>();
        var captionCandidates = new List<CaptionInfo>();

        for (var index = 0; index < bodyElements.Count; index++)
        {
            var elem = bodyElements[index];
            var normalizedLabel = labelMap.TryGetValue(elem.DelemenId, out var rawLabel)
                ? NormalizeLabel(rawLabel)
                : string.Empty;
            var parsedContent = false;
            ElementContentInfo content = new();

            ElementContentInfo GetContent()
            {
                if (!parsedContent)
                {
                    content = ParseElementContent(elem.DelemenJsonTree);
                    parsedContent = true;
                }

                return content;
            }

            if (IsCodeLabel(normalizedLabel))
            {
                var isTableType = IsCodeTableElementType(elem.DelemenType);
                var isImageType = IsCodeImageElementType(elem.DelemenType);
                if (isTableType || isImageType)
                {
                    structuralViolations.Add(new CodeStructuralViolationInfo
                    {
                        ElementId = elem.DelemenId,
                        ElementType = elem.DelemenType,
                        Content = GetContent(),
                        IsTableType = isTableType,
                        IsImageType = isImageType
                    });
                    continue;
                }

                var codeContent = GetContent();
                var normalizedText = NormalizeWhitespace(codeContent.PlainText);
                if (string.IsNullOrWhiteSpace(normalizedText) && !codeContent.HasNonTextContent)
                    continue;

                codeElements.Add(new CodeElementInfo
                {
                    ElementId = elem.DelemenId,
                    OrderIndex = index,
                    Content = codeContent
                });
            }

            if (titleRule != null)
            {
                var captionContent = GetContent();
                var normalizedText = NormalizeWhitespace(captionContent.PlainText);
                if (string.IsNullOrWhiteSpace(normalizedText))
                    continue;

                if (!IsCodeTitleCandidateLabel(normalizedLabel, normalizedText, titlePrefixes))
                    continue;

                captionCandidates.Add(new CaptionInfo
                {
                    ElementId = elem.DelemenId,
                    OrderIndex = index,
                    Content = captionContent,
                    NormalizedText = normalizedText
                });
            }
        }

        if (captionCandidates.Count > 0)
        {
            var structuralViolationIds = structuralViolations
                .Select(violation => violation.ElementId)
                .ToHashSet();
            var codeElementIds = codeElements
                .Select(element => element.ElementId)
                .ToHashSet();

            foreach (var caption in captionCandidates)
            {
                foreach (var targetIndex in GetAdjacentElementIndices(caption.OrderIndex, bodyElements.Count, normalizedTitlePosition))
                {
                    var targetElement = bodyElements[targetIndex];
                    if (structuralViolationIds.Contains(targetElement.DelemenId) ||
                        codeElementIds.Contains(targetElement.DelemenId))
                    {
                        continue;
                    }

                    var targetLabel = labelMap.TryGetValue(targetElement.DelemenId, out var rawTargetLabel)
                        ? NormalizeLabel(rawTargetLabel)
                        : string.Empty;

                    if (TryDescribeImageLikeElement(targetElement, targetLabel, out var _evidence, out var elementType, out var content))
                    {
                        structuralViolations.Add(new CodeStructuralViolationInfo
                        {
                            ElementId = targetElement.DelemenId,
                            ElementType = elementType,
                            Content = content,
                            IsImageType = true
                        });
                        structuralViolationIds.Add(targetElement.DelemenId);
                        break;
                    }

                    if (!IsCodeTableElementType(targetElement.DelemenType) && !IsTableLabel(targetLabel))
                        continue;

                    structuralViolations.Add(new CodeStructuralViolationInfo
                    {
                        ElementId = targetElement.DelemenId,
                        ElementType = targetElement.DelemenType,
                        Content = ParseElementContent(targetElement.DelemenJsonTree),
                        IsTableType = true
                    });
                    structuralViolationIds.Add(targetElement.DelemenId);
                    break;
                }
            }
        }

        foreach (var violation in structuralViolations)
        {
            var errorStart = result.Errors.Count;
            var locations = await BuildElementLocationsAsync(violation.ElementId, cancellationToken);
            var evidence = BuildCodeEvidence(violation.Content);
            var actualType = GetCodeElementTypeDisplay(violation.ElementType);

            if (violation.IsTableType && codeRule?.CegahTabelKode?.Value == true)
            {
                result.IncrementTotalChecks();
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "kode",
                    Message = "Kode tidak boleh berupa tabel",
                    Expected = "Elemen kode bertipe paragraf/teks, bukan tabel",
                    Actual = $"Tipe elemen: {actualType}",
                    Evidence = evidence,
                    Locations = locations
                });
            }

            if (violation.IsImageType && codeRule?.CegahGambarKode?.Value == true)
            {
                result.IncrementTotalChecks();
                result.Errors.Add(new ValidationError
                {
                    Category = "Isi Buku",
                    Field = "kode",
                    Message = "Kode tidak boleh berupa gambar",
                    Expected = "Elemen kode bertipe paragraf/teks, bukan gambar",
                    Actual = $"Tipe elemen: {actualType}",
                    Evidence = evidence,
                    Locations = locations
                });
            }

            if (neighborContexts.TryGetValue(violation.ElementId, out var context))
                ApplyContextToErrors(result.Errors, errorStart, context);

            ApplyElementIdToErrors(result.Errors, errorStart, violation.ElementId);
        }

        if (codeElements.Count == 0)
            return result;

        var listLevelByElementId = await LoadCodeListLevelsFromVisualAsync(
            codeElements.Select(e => e.ElementId),
            cancellationToken);

        var codeBlocks = BuildCodeBlocks(codeElements);
        var codeBlockIndexByElementId = BuildCodeBlockIndexMap(codeBlocks);
        var codeMergeSetKeyByElementId = BuildCodeMergeSetKeyMap(
            codeElements,
            codeBlockIndexByElementId,
            listLevelByElementId);

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

        var codeErrorStart = result.Errors.Count;

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
            pageLayoutsById.TryGetValue(codeElement.ElementId, out var codePageLayout);

            if (codeRule?.Font != null)
                ValidateCodeFont(result, codeRule.Font, elementTextFormats!, content.TextRuns, evidence, locations);

            if (codeRule?.Paragraph != null)
                ValidateCodeParagraphFormat(result, codeRule.Paragraph, paragraphFormat, evidence, locations, plainText, codePageLayout);

            if (codeRule?.Numbering != null)
                ValidateCodeNumbering(result, codeRule.Numbering, content, paragraphFormat, evidence, locations);

            if (codeRule?.CegahTabelKode?.Value == true)
            {
                result.IncrementTotalChecks();
                result.IncrementPassedChecks();
            }

            if (codeRule?.CegahGambarKode?.Value == true)
            {
                result.IncrementTotalChecks();
                if (!content.HasNonTextContent)
                {
                    result.IncrementPassedChecks();
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

        MergeIdenticalCodeLineErrorsWithinBlockPage(
            result.Errors,
            codeErrorStart,
            codeMergeSetKeyByElementId);

        var usedCaptionIds = new HashSet<ulong>();

        for (var i = 0; i < codeBlocks.Count; i++)
        {
            var block = codeBlocks[i];
            var blockLocations = await BuildElementLocationsAsync(block.ElementIds, cancellationToken);
            CaptionInfo? selectedCaption = null;

            if (titleRule != null && IsContinuationCaptionRequired(titleRule.WajibCaptionLanjutanJikaLintasHalaman))
            {
                result.IncrementTotalChecks();
                if (SpansMultiplePages(blockLocations))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Category = "Isi Buku",
                        Field = "judul_kode",
                        Message = "Kode lintas halaman harus memiliki caption lanjutan",
                        Expected = "Kode yang berlanjut ke halaman berikutnya harus dipecah per halaman dan bagian lanjutan diberi judul '(Lanjutan)'",
                        Actual = $"Satu blok kode terdeteksi melintasi halaman {DescribeLocationPages(blockLocations)}",
                        Evidence = block.Evidence,
                        Locations = blockLocations
                    });
                }
                else
                {
                    result.IncrementPassedChecks();
                }
            }

            if (titleRule != null)
            {
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

                var positionRule = titleRule.Position?.Value?.Trim();
                var normalizedPosition = string.IsNullOrWhiteSpace(positionRule)
                    ? normalizedTitlePosition
                    : positionRule.ToLowerInvariant();

                if (normalizedPosition == "after")
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
                        result.IncrementTotalChecks();
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
                        result.IncrementTotalChecks();
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
                        result.IncrementTotalChecks();
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
                        pageLayoutsById.TryGetValue(selectedCaption.ElementId, out var captionPageLayout);
                        ValidateCaptionParagraphFormat(result, titleRule.Paragraph, captionFormat, "judul_kode", "judul kode", selectedCaption.NormalizedText, selectedCaption.Content.PlainText, captionLocations, captionPageLayout);
                    }

                    ValidateCaptionNumbering(result, titleRule.Numbering, selectedCaption, "judul_kode", "judul kode", captionLocations);

                    if (neighborContexts.TryGetValue(selectedCaption.ElementId, out var captionContext))
                        ApplyContextToErrors(result.Errors, captionErrorStart, captionContext);

                    ApplyElementIdToErrors(result.Errors, captionErrorStart, selectedCaption.ElementId);
                }
            }

            var blockLocationIds = new List<ulong>(block.ElementIds);
            var blockStartIndex = block.StartIndex;
            var blockEndIndex = block.EndIndex;
            var blockStartElementId = bodyElements[blockStartIndex].DelemenId;
            var blockEndElementId = bodyElements[blockEndIndex].DelemenId;

            if (selectedCaption != null)
            {
                blockLocationIds.Add(selectedCaption.ElementId);
                if (elementIndexById.TryGetValue(selectedCaption.ElementId, out var captionIndex))
                {
                    if (captionIndex < blockStartIndex)
                    {
                        blockStartIndex = captionIndex;
                        blockStartElementId = selectedCaption.ElementId;
                    }

                    if (captionIndex > blockEndIndex)
                    {
                        blockEndIndex = captionIndex;
                        blockEndElementId = selectedCaption.ElementId;
                    }
                }
            }

            if (codeRule != null)
            {
                await ValidateMediaBlankParagraphStructureAsync(
                    result,
                    field: "kode",
                    elementLabel: "blok kode",
                    evidence: block.Evidence,
                    startIndex: blockStartIndex,
                    startElementId: blockStartElementId,
                    endIndex: blockEndIndex,
                    endElementId: blockEndElementId,
                    locationElementIds: blockLocationIds,
                    primaryElementId: block.ElementIds.First(),
                    bodyElements: bodyElements,
                    elementContentById: elementContentById,
                    elementJsonById: elementJsonById,
                    visualSummaryById: visualSummaryById,
                    contextById: neighborContexts,
                    paragraphRule: paragraphRule,
                    structureRule: codeRule.StrukturKonten,
                    cancellationToken: cancellationToken);
            }
        }

        return result;
    }

    private static bool IsCodeLabel(string normalizedLabel)
    {
        return normalizedLabel == "kode" || normalizedLabel == "code";
    }

    private static bool IsCodeTableElementType(string? elementType)
    {
        if (string.IsNullOrWhiteSpace(elementType))
            return false;

        return elementType.Equals("table", StringComparison.OrdinalIgnoreCase) ||
               elementType.Equals("tabel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCodeImageElementType(string? elementType)
    {
        if (string.IsNullOrWhiteSpace(elementType))
            return false;

        return elementType.Equals("gambar", StringComparison.OrdinalIgnoreCase) ||
               IsImageType(elementType);
    }

    private static string GetCodeElementTypeDisplay(string? elementType)
    {
        if (string.IsNullOrWhiteSpace(elementType))
            return "unknown";

        return elementType.Trim();
    }

    private static bool IsCodeCaptionLabel(string normalizedLabel)
    {
        return normalizedLabel == "judul_kode";
    }

    private readonly record struct CodeLineErrorMergeKey(
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
        string DisallowedActions,
        bool IsRequired);

    private async Task<Dictionary<ulong, int>> LoadCodeListLevelsFromVisualAsync(
        IEnumerable<ulong> elementIds,
        CancellationToken cancellationToken)
    {
        var ids = elementIds.Distinct().ToList();
        var levels = new Dictionary<ulong, int>();
        if (ids.Count == 0)
            return levels;

        var (idColumn, labelColumn) = await ResolveVisualColumnsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn) || string.IsNullOrWhiteSpace(labelColumn))
            return levels;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
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
                    if (reader["delemen_id"] == DBNull.Value || reader["label"] == DBNull.Value)
                        continue;

                    var elementId = Convert.ToUInt64(reader["delemen_id"]);
                    var label = reader["label"]?.ToString();
                    var level = TryParseListLabelLevel(label);
                    if (!level.HasValue)
                        continue;

                    var normalizedLevel = level.Value + 1;
                    if (!levels.TryGetValue(elementId, out var existing) || normalizedLevel < existing)
                        levels[elementId] = normalizedLevel;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load list levels for kode from dokumen_elemen_visual");
        }
        finally
        {
            if (shouldClose && connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }

        return levels;
    }

    private static Dictionary<ulong, int> BuildCodeBlockIndexMap(IReadOnlyList<CodeBlockInfo> codeBlocks)
    {
        var map = new Dictionary<ulong, int>();
        for (var i = 0; i < codeBlocks.Count; i++)
        {
            foreach (var elementId in codeBlocks[i].ElementIds)
                map[elementId] = i;
        }

        return map;
    }

    private static Dictionary<ulong, string> BuildCodeMergeSetKeyMap(
        IReadOnlyList<CodeElementInfo> codeElements,
        IReadOnlyDictionary<ulong, int> codeBlockIndexByElementId,
        IReadOnlyDictionary<ulong, int> listLevelByElementId)
    {
        var map = new Dictionary<ulong, string>(codeElements.Count);
        foreach (var codeElement in codeElements)
        {
            if (listLevelByElementId.TryGetValue(codeElement.ElementId, out var listLevel))
            {
                map[codeElement.ElementId] = $"list_level_{listLevel}";
                continue;
            }

            if (codeBlockIndexByElementId.TryGetValue(codeElement.ElementId, out var blockIndex))
            {
                map[codeElement.ElementId] = $"block_{blockIndex}";
            }
        }

        return map;
    }

    private static void MergeIdenticalCodeLineErrorsWithinBlockPage(
        List<ValidationError> errors,
        int startIndex,
        IReadOnlyDictionary<ulong, string> codeMergeSetKeyByElementId)
    {
        if (errors.Count == 0 || startIndex >= errors.Count || codeMergeSetKeyByElementId.Count == 0)
            return;

        var merged = new List<ValidationError>();
        var grouped = new Dictionary<CodeLineErrorMergeKey, int>();

        for (var i = startIndex; i < errors.Count; i++)
        {
            var error = errors[i];
            var mergeKey = TryBuildCodeLineErrorMergeKey(error, codeMergeSetKeyByElementId);
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
            target.Locations = MergeErrorLocations(target.Locations, error.Locations);

            if (!target.DokumenElemenId.HasValue && error.DokumenElemenId.HasValue)
                target.DokumenElemenId = error.DokumenElemenId;
        }

        errors.RemoveRange(startIndex, errors.Count - startIndex);
        errors.AddRange(merged);
    }

    private static CodeLineErrorMergeKey? TryBuildCodeLineErrorMergeKey(
        ValidationError error,
        IReadOnlyDictionary<ulong, string> codeMergeSetKeyByElementId)
    {
        if (!string.Equals(error.Field, "kode", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!error.DokumenElemenId.HasValue ||
            !codeMergeSetKeyByElementId.TryGetValue(error.DokumenElemenId.Value, out var setKey))
            return null;

        var pageNumber = TryGetSingleLocationPage(error.Locations);
        if (!pageNumber.HasValue)
            return null;

        return new CodeLineErrorMergeKey(
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
            BuildActionToken(error.DisallowedActions),
            error.IsRequired);
    }

    private static int? TryGetSingleLocationPage(IReadOnlyList<ErrorLocation> locations)
    {
        if (locations == null || locations.Count == 0)
            return null;

        var pages = locations
            .Select(loc => loc.HalamanKe)
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToList();

        return pages.Count == 1 ? pages[0] : null;
    }

    private static bool SpansMultiplePages(IReadOnlyList<ErrorLocation>? locations)
    {
        if (locations == null || locations.Count == 0)
            return false;

        return locations
            .Select(loc => loc.HalamanKe)
            .Where(page => page > 0)
            .Distinct()
            .Take(2)
            .Count() > 1;
    }

    private static string DescribeLocationPages(IReadOnlyList<ErrorLocation>? locations)
    {
        if (locations == null || locations.Count == 0)
            return "yang tidak teridentifikasi";

        var pages = locations
            .Select(loc => loc.HalamanKe)
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .Select(page => page.ToString(CultureInfo.InvariantCulture))
            .ToList();

        return pages.Count == 0
            ? "yang tidak teridentifikasi"
            : string.Join(", ", pages);
    }

    private static string BuildActionToken(IReadOnlyList<string>? actions)
    {
        if (actions == null || actions.Count == 0)
            return string.Empty;

        return string.Join("|", actions.Select(action => action?.Trim() ?? string.Empty));
    }

    private static List<ErrorLocation> MergeErrorLocations(
        IReadOnlyList<ErrorLocation> first,
        IReadOnlyList<ErrorLocation> second)
    {
        var merged = new List<ErrorLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddDistinctLocations(merged, seen, first);
        AddDistinctLocations(merged, seen, second);

        return merged
            .OrderBy(loc => loc.HalamanKe)
            .ThenBy(loc => loc.Bbox?.Y0 ?? decimal.MinValue)
            .ThenBy(loc => loc.Bbox?.X0 ?? decimal.MinValue)
            .ToList();
    }

    private static void AddDistinctLocations(
        List<ErrorLocation> target,
        HashSet<string> seen,
        IReadOnlyList<ErrorLocation> source)
    {
        if (source == null || source.Count == 0)
            return;

        foreach (var loc in source)
        {
            var token = BuildLocationToken(loc);
            if (!seen.Add(token))
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

    private static string BuildLocationToken(ErrorLocation loc)
    {
        if (loc.Bbox == null)
            return loc.HalamanKe.ToString(CultureInfo.InvariantCulture) + "|null";

        return string.Join("|",
            loc.HalamanKe.ToString(CultureInfo.InvariantCulture),
            loc.Bbox.X0.ToString(CultureInfo.InvariantCulture),
            loc.Bbox.Y0.ToString(CultureInfo.InvariantCulture),
            loc.Bbox.X1.ToString(CultureInfo.InvariantCulture),
            loc.Bbox.Y1.ToString(CultureInfo.InvariantCulture));
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
                    result.IncrementPassedChecks();
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
                    result.IncrementPassedChecks();
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
                    result.IncrementPassedChecks();
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
                    result.IncrementPassedChecks();
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
                    result.IncrementPassedChecks();
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
        List<ErrorLocation> locations,
        string paragraphText,
        PageLayoutSnapshot? pageLayout)
    {
        if (format == null)
            return;

        var expectedAlignment = rule.Alignment?.Value;
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
        const decimal indentToleranceCm = 0.2m;

        var leftTwips = format.DfpIndLeftTwips.HasValue && format.DfpIndLeftTwips.Value != 0
            ? format.DfpIndLeftTwips.Value
            : format.DfpIndStartTwips ?? 0;

        var hangingTwips = format.DfpIndHangingTwips ?? 0;
        var hasNumbering = format.DfpIsList || (format.DfpListNumId ?? 0) > 0;
        if (hasNumbering &&
            expectedLeftIndent.HasValue &&
            Math.Abs(expectedLeftIndent.Value) <= indentToleranceCm &&
            leftTwips > 0 &&
            hangingTwips > leftTwips)
        {
            // List paragraphs can expose effective tab-stop hanging that exceeds
            // paragraph left indent (visual layout). For kode rule we validate
            // format indent value, so normalize to left indent in this case.
            hangingTwips = leftTwips;
        }

        var hangingCm = hangingTwips / 1440.0m * 2.54m;

        if (expectedHanging.HasValue)
        {
            result.IncrementTotalChecks();
            if (Math.Abs(hangingCm - expectedHanging.Value) <= indentToleranceCm)
            {
                result.IncrementPassedChecks();
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
            result.IncrementTotalChecks();
            var leftCm = leftTwips / 1440.0m * 2.54m;
            var alignedLeftCm = expectedHanging.HasValue
                ? Math.Max(0m, leftCm - hangingCm)
                : leftCm;

            if (Math.Abs(alignedLeftCm - expectedLeftIndent.Value) <= indentToleranceCm)
            {
                result.IncrementPassedChecks();
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

        var expectedRightIndent = rule.Indentation?.RightIndent?.Value ?? 0m;
        result.IncrementTotalChecks();
        var rightCm = GetRightIndentCm(format);
        if (Math.Abs(rightCm - expectedRightIndent) <= indentToleranceCm)
        {
            result.IncrementPassedChecks();
        }
        else
        {
            result.Errors.Add(new ValidationError
            {
                Category = "Isi Buku",
                Field = "kode",
                Message = "Right indent kode tidak sesuai",
                Expected = expectedRightIndent.ToString(CultureInfo.InvariantCulture) + " cm",
                Actual = rightCm.ToString("F2", CultureInfo.InvariantCulture) + " cm",
                Evidence = evidence,
                Locations = locations
            });
        }

        var spacingRule = rule.Spacing;
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
            result.IncrementTotalChecks();
            if (expectedUse.Value == hasNumbering)
            {
                result.IncrementPassedChecks();
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
            result.IncrementTotalChecks();
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
            result.IncrementTotalChecks();
            result.IncrementPassedChecks();
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


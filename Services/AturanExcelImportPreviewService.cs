using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IAturanExcelImportPreviewService
{
    Task<AturanExcelImportPreviewResult> PreviewAsync(uint aturanId, IFormFile file, CancellationToken cancellationToken = default);
}

public sealed class AturanExcelImportPreviewResult
{
    public int TotalRows { get; init; }
    public int ChangedRows { get; init; }
    public int ChangedDetails { get; init; }
    public List<AturanExcelImportPreviewDetail> Details { get; init; } = [];
}

public sealed class AturanExcelImportPreviewDetail
{
    public uint AturanDetailId { get; init; }
    public string Kategori { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string JsonValue { get; init; } = string.Empty;
}

public sealed partial class AturanExcelImportPreviewService : IAturanExcelImportPreviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static readonly IReadOnlyDictionary<string, string[]> SplitElementKeys =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["gambar"] = ["gambar", "caption_gambar"],
            ["tabel"] = ["tabel", "caption_tabel"],
            ["kode"] = ["kode", "judul_kode"]
        };

    private static readonly HashSet<string> WrapperMetaKeys =
    [
        "value",
        "is_editable",
        "is_hard_constraint"
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, JsonObject>> TemplateRootByKey = new(BuildTemplateRootMap);

    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<AturanExcelImportPreviewService> _logger;

    public AturanExcelImportPreviewService(
        KorektorBukuDbContext db,
        ILogger<AturanExcelImportPreviewService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AturanExcelImportPreviewResult> PreviewAsync(
        uint aturanId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File import tidak boleh kosong");

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File import harus berformat .xlsx");

        var visibleDetails = AturanDetailVisibility.FilterVisible(await _db.AturanDetails
            .Where(detail => detail.AturanId == aturanId)
            .OrderBy(detail => detail.AturanDetailId)
            .ToListAsync(cancellationToken));

        if (visibleDetails.Count == 0)
            throw new InvalidOperationException("Template belum memiliki detail aturan yang bisa diimpor");

        var schemaRows = BuildSchemaRows(visibleDetails);
        if (schemaRows.Count == 0)
            throw new InvalidOperationException("Schema export template kosong");

        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets
            .FirstOrDefault(sheet => string.Equals(sheet.Name, AturanExcelExportBuilder.WorksheetName, StringComparison.Ordinal));
        if (worksheet == null)
            throw new InvalidOperationException($"Sheet `{AturanExcelExportBuilder.WorksheetName}` tidak ditemukan");

        ValidateHeaders(worksheet);

        var schemaByAnchor = schemaRows.ToDictionary(
            row => row.Anchor,
            row => row,
            StringComparer.OrdinalIgnoreCase);

        var importedRows = ReadImportedRows(worksheet);
        if (importedRows.Count != schemaRows.Count)
        {
            throw new InvalidOperationException(
                $"Jumlah row workbook tidak cocok. Diharapkan {schemaRows.Count} row data, diterima {importedRows.Count}.");
        }

        var importedByAnchor = new Dictionary<string, ImportedWorkbookRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var importedRow in importedRows)
        {
            if (!schemaByAnchor.ContainsKey(importedRow.Anchor))
                throw new InvalidOperationException($"Row tidak dikenal pada baris Excel {importedRow.ExcelRow}: {importedRow.DisplayAnchor}");

            if (!importedByAnchor.TryAdd(importedRow.Anchor, importedRow))
                throw new InvalidOperationException($"Ada row duplikat pada workbook untuk {importedRow.DisplayAnchor}");
        }

        var missingRows = schemaRows
            .Where(row => !importedByAnchor.ContainsKey(row.Anchor))
            .Select(row => row.DisplayAnchor)
            .ToList();
        if (missingRows.Count > 0)
        {
            throw new InvalidOperationException(
                $"Workbook tidak lengkap. Row berikut hilang: {string.Join("; ", missingRows.Take(5))}");
        }

        var detailStates = visibleDetails.ToDictionary(
            detail => NormalizeKey(detail.AturanDetailKey),
            detail => new DetailState(
                detail,
                ParseDetailRoot(detail)),
            StringComparer.OrdinalIgnoreCase);

        var hardConstraintAssignments = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var changedRows = 0;

        foreach (var schemaRow in schemaRows)
        {
            var importedRow = importedByAnchor[schemaRow.Anchor];
            var importedHardConstraint = ParseHardConstraint(importedRow);
            var importedValueNode = ParseImportedValue(schemaRow, importedRow);

            var rowChanged = false;
            if (!schemaRow.HasBackingDetail)
            {
                var importedValueText = FormatScalarValue(importedValueNode, schemaRow.EffectiveKey);
                if (importedHardConstraint != schemaRow.HardConstraint ||
                    !string.Equals(importedValueText, schemaRow.ValueText, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Row {schemaRow.DisplayAnchor} berasal dari aturan default export dan belum tersedia di template target. " +
                        $"Expected value `{schemaRow.ValueText}` / HC `{schemaRow.HardConstraint}`, " +
                        $"got `{importedValueText}` / HC `{importedHardConstraint}`.");
                }

                continue;
            }

            if (!detailStates.TryGetValue(NormalizeKey(schemaRow.AturanDetailKey), out var detailState))
            {
                throw new InvalidOperationException(
                    $"Detail aturan `{schemaRow.AturanDetailKey}` tidak ditemukan saat memproses import.");
            }

            if (!JsonNodesEqual(importedValueNode, GetNodeAtPath(detailState.Root, schemaRow.ValuePath)))
            {
                SetNodeAtPath(detailState.Root, schemaRow.ValuePath, importedValueNode);
                detailState.MarkChanged();
                rowChanged = true;
            }

            if (schemaRow.HardConstraintPath.Length == 0)
            {
                if (importedHardConstraint != schemaRow.HardConstraint)
                {
                    throw new InvalidOperationException(
                        $"Kolom Hard Constraint untuk row {schemaRow.DisplayAnchor} tidak bisa diubah.");
                }
            }
            else
            {
                var assignmentKey = $"{NormalizeKey(schemaRow.AturanDetailKey)}::{SerializePath(schemaRow.HardConstraintPath)}";
                if (hardConstraintAssignments.TryGetValue(assignmentKey, out var existingAssignment) &&
                    existingAssignment != importedHardConstraint)
                {
                    throw new InvalidOperationException(
                        $"Kolom Hard Constraint tidak konsisten untuk row yang berbagi wrapper pada {schemaRow.DisplayAnchor}.");
                }

                hardConstraintAssignments[assignmentKey] = importedHardConstraint;
                if (importedHardConstraint != schemaRow.HardConstraint)
                {
                    SetHardConstraintAtPath(detailState.Root, schemaRow.HardConstraintPath, importedHardConstraint);
                    detailState.MarkChanged();
                    rowChanged = true;
                }
            }

            if (rowChanged)
                changedRows++;
        }

        var changedDetails = new List<AturanExcelImportPreviewDetail>();
        foreach (var state in detailStates.Values.OrderBy(item => item.Detail.AturanDetailId))
        {
            if (!state.IsChanged)
                continue;

            var rawJson = state.Root.ToJsonString(JsonOptions);
            if (!AturanDetailCanonicalizer.TryCanonicalize(
                    state.Detail.AturanDetailKey,
                    rawJson,
                    out var canonicalJson,
                    out var canonicalChanged,
                    out var errorMessage))
            {
                throw new InvalidOperationException(
                    $"Import menghasilkan json_value tidak valid untuk aturan `{state.Detail.AturanDetailKey}`: {errorMessage}");
            }

            if (!AturanDetailShapeValidator.TryValidate(state.Detail.AturanDetailKey, canonicalJson, out var shapeError))
            {
                throw new InvalidOperationException(
                    $"Import menghasilkan json_value yang tidak sesuai schema untuk aturan `{state.Detail.AturanDetailKey}`: {shapeError}");
            }

            changedDetails.Add(new AturanExcelImportPreviewDetail
            {
                AturanDetailId = state.Detail.AturanDetailId,
                Kategori = state.Detail.AturanDetailKategori ?? string.Empty,
                Key = state.Detail.AturanDetailKey ?? string.Empty,
                JsonValue = canonicalJson ?? rawJson
            });
        }

        _logger.LogInformation(
            "Preview import XLSX aturan {AturanId} selesai. ChangedRows={ChangedRows}, ChangedDetails={ChangedDetails}",
            aturanId,
            changedRows,
            changedDetails.Count);

        return new AturanExcelImportPreviewResult
        {
            TotalRows = schemaRows.Count,
            ChangedRows = changedRows,
            ChangedDetails = changedDetails.Count,
            Details = changedDetails
        };
    }

    private static IReadOnlyDictionary<string, JsonObject> BuildTemplateRootMap()
    {
        return AturanExportCatalog.CreateDefaultDetails(0)
            .Where(detail => !string.IsNullOrWhiteSpace(detail.AturanDetailKey))
            .Select(detail => new
            {
                Key = NormalizeKey(detail.AturanDetailKey),
                Root = ParseJsonObject(detail.AturanDetailJsonValue, detail.AturanDetailKey)
            })
            .ToDictionary(item => item.Key, item => item.Root, StringComparer.OrdinalIgnoreCase);
    }

    private static JsonObject ParseDetailRoot(AturanDetail detail)
    {
        return ParseJsonObject(detail.AturanDetailJsonValue, detail.AturanDetailKey);
    }

    private static JsonObject ParseJsonObject(string? rawJson, string? detailKey)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(rawJson) as JsonObject
                ?? throw new InvalidOperationException(
                    $"json_value untuk aturan `{detailKey}` harus berupa object JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"json_value untuk aturan `{detailKey}` tidak valid: {ex.Message}");
        }
    }

    private static List<ImportedWorkbookRow> ReadImportedRows(IXLWorksheet worksheet)
    {
        return worksheet.RowsUsed()
            .Skip(1)
            .Select(row => new ImportedWorkbookRow(
                row.RowNumber(),
                row.Cell(1).GetString().Trim(),
                row.Cell(2).GetString().Trim(),
                row.Cell(3).GetString().Trim(),
                row.Cell(4).GetString().Trim(),
                row.Cell(5),
                row.Cell(6)))
            .ToList();
    }

    private static void ValidateHeaders(IXLWorksheet worksheet)
    {
        for (var index = 0; index < AturanExcelExportBuilder.Headers.Length; index++)
        {
            var actual = worksheet.Cell(1, index + 1).GetString().Trim();
            var expected = AturanExcelExportBuilder.Headers[index];
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Header kolom ke-{index + 1} tidak valid. Diharapkan `{expected}`, diterima `{actual}`.");
            }
        }
    }

    private static List<AturanExcelSchemaRow> BuildSchemaRows(IReadOnlyList<AturanDetail> details)
    {
        var visibleDetails = AturanDetailVisibility.FilterVisible(details);
        var mergedDetails = AturanExportCatalog.MergeValidationTemplates(visibleDetails);
        var existingKeys = visibleDetails
            .Select(detail => NormalizeKey(detail.AturanDetailKey))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var schemaRows = new List<AturanExcelSchemaRow>();
        foreach (var detail in mergedDetails)
        {
            AppendDetailRows(schemaRows, detail, existingKeys);
        }

        return schemaRows;
    }

    private static void AppendDetailRows(
        List<AturanExcelSchemaRow> rows,
        AturanDetail detail,
        IReadOnlySet<string> existingDetailKeys)
    {
        var detailKey = (detail.AturanDetailKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(detailKey))
            return;

        if (string.IsNullOrWhiteSpace(detail.AturanDetailJsonValue))
        {
            rows.Add(new AturanExcelSchemaRow(
                detail.AturanDetailId,
                detail.AturanDetailKategori ?? string.Empty,
                detailKey,
                existingDetailKeys.Contains(NormalizeKey(detailKey)),
                detailKey,
                AturanExcelExportBuilder.FormatElementLabel(detailKey),
                "Umum",
                string.Empty,
                "Value",
                string.Empty,
                false,
                [],
                [],
                string.Empty,
                null,
                null));
            return;
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(detail.AturanDetailJsonValue);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"json_value untuk aturan `{detailKey}` tidak valid: {ex.Message}");
        }

        if (rootNode is not JsonObject rootObject)
        {
            throw new InvalidOperationException(
                $"json_value untuk aturan `{detailKey}` harus berupa object JSON.");
        }

        foreach (var expandedNode in ExpandElementNodes(detailKey, rootObject, existingDetailKeys))
        {
            FlattenNode(
                rows,
                detail,
                existingDetailKeys.Contains(NormalizeKey(detailKey)),
                expandedNode.ElementKey,
                expandedNode.Node,
                [],
                expandedNode.FullPathPrefix,
                [],
                null);
        }
    }

    private static IEnumerable<ExpandedElementNode> ExpandElementNodes(
        string detailKey,
        JsonObject rootObject,
        IReadOnlySet<string> existingDetailKeys)
    {
        if (!SplitElementKeys.TryGetValue(detailKey, out var splitKeys))
        {
            yield return new ExpandedElementNode(detailKey, rootObject, []);
            yield break;
        }

        var hasAnySplitNode = false;
        foreach (var splitKey in splitKeys)
        {
            if (!rootObject.TryGetPropertyValue(splitKey, out var childNode) || childNode == null)
                continue;

            if (!splitKey.Equals(detailKey, StringComparison.OrdinalIgnoreCase) &&
                existingDetailKeys.Contains(NormalizeKey(splitKey)))
            {
                continue;
            }

            hasAnySplitNode = true;
            yield return new ExpandedElementNode(splitKey, childNode, [splitKey]);
        }

        if (!hasAnySplitNode)
            yield return new ExpandedElementNode(detailKey, rootObject, []);
    }

    private static void FlattenNode(
        List<AturanExcelSchemaRow> rows,
        AturanDetail detail,
        bool hasBackingDetail,
        string elementKey,
        JsonNode? node,
        IReadOnlyList<string> displayPath,
        IReadOnlyList<string> actualPath,
        IReadOnlyList<string> hardConstraintPath,
        bool? hardConstraint)
    {
        if (node == null)
        {
            AddSchemaRow(rows, detail, hasBackingDetail, elementKey, displayPath, actualPath, hardConstraintPath, null, hardConstraint ?? false);
            return;
        }

        switch (node)
        {
            case JsonObject jsonObject:
                FlattenObject(rows, detail, hasBackingDetail, elementKey, jsonObject, displayPath, actualPath, hardConstraintPath, hardConstraint);
                return;
            case JsonArray jsonArray:
                FlattenArray(rows, detail, hasBackingDetail, elementKey, jsonArray, displayPath, actualPath, hardConstraintPath, hardConstraint);
                return;
            default:
                AddSchemaRow(rows, detail, hasBackingDetail, elementKey, displayPath, actualPath, hardConstraintPath, node, hardConstraint ?? false);
                return;
        }
    }

    private static void FlattenObject(
        List<AturanExcelSchemaRow> rows,
        AturanDetail detail,
        bool hasBackingDetail,
        string elementKey,
        JsonObject jsonObject,
        IReadOnlyList<string> displayPath,
        IReadOnlyList<string> actualPath,
        IReadOnlyList<string> inheritedHardConstraintPath,
        bool? inheritedHardConstraint)
    {
        if (jsonObject.TryGetPropertyValue("value", out var valueNode))
        {
            var wrapperPath = actualPath.ToArray();
            var wrapperHardConstraint = ReadBooleanProperty(jsonObject, "is_hard_constraint") ?? inheritedHardConstraint ?? false;
            if (valueNode is JsonObject or JsonArray)
            {
                FlattenNode(
                    rows,
                    detail,
                    hasBackingDetail,
                    elementKey,
                    valueNode,
                    displayPath,
                    AppendSegment(actualPath, "value"),
                    wrapperPath,
                    wrapperHardConstraint);
            }
            else
            {
                AddSchemaRow(
                    rows,
                    detail,
                    hasBackingDetail,
                    elementKey,
                    displayPath,
                    AppendSegment(actualPath, "value"),
                    wrapperPath,
                    valueNode,
                    wrapperHardConstraint);
            }

            foreach (var property in jsonObject)
            {
                if (WrapperMetaKeys.Contains(property.Key))
                    continue;

                FlattenNode(
                    rows,
                    detail,
                    hasBackingDetail,
                    elementKey,
                    property.Value,
                    AppendSegment(displayPath, property.Key),
                    AppendSegment(actualPath, property.Key),
                    wrapperPath,
                    wrapperHardConstraint);
            }

            return;
        }

        foreach (var property in jsonObject)
        {
            FlattenNode(
                rows,
                detail,
                hasBackingDetail,
                elementKey,
                property.Value,
                AppendSegment(displayPath, property.Key),
                AppendSegment(actualPath, property.Key),
                inheritedHardConstraintPath,
                inheritedHardConstraint);
        }
    }

    private static void FlattenArray(
        List<AturanExcelSchemaRow> rows,
        AturanDetail detail,
        bool hasBackingDetail,
        string elementKey,
        JsonArray jsonArray,
        IReadOnlyList<string> displayPath,
        IReadOnlyList<string> actualPath,
        IReadOnlyList<string> hardConstraintPath,
        bool? hardConstraint)
    {
        if (jsonArray.Count == 0)
        {
            AddSchemaRow(rows, detail, hasBackingDetail, elementKey, displayPath, actualPath, hardConstraintPath, null, hardConstraint ?? false);
            return;
        }

        for (var index = 0; index < jsonArray.Count; index++)
        {
            var indexToken = $"[{index + 1}]";
            FlattenNode(
                rows,
                detail,
                hasBackingDetail,
                elementKey,
                jsonArray[index],
                AppendSegment(displayPath, indexToken),
                AppendSegment(actualPath, indexToken),
                hardConstraintPath,
                hardConstraint);
        }
    }

    private static void AddSchemaRow(
        List<AturanExcelSchemaRow> rows,
        AturanDetail detail,
        bool hasBackingDetail,
        string elementKey,
        IReadOnlyList<string> displayPath,
        IReadOnlyList<string> actualPath,
        IReadOnlyList<string> hardConstraintPath,
        JsonNode? valueNode,
        bool hardConstraint)
    {
        if (ShouldSkipExportRow(elementKey, displayPath))
            return;

        var descriptor = BuildDescriptor(displayPath);
        var effectiveKey = GetEffectiveKey(displayPath);
        var templateValueNode = TryGetTemplateValueNode(detail.AturanDetailKey, actualPath) ?? valueNode?.DeepClone();

        rows.Add(new AturanExcelSchemaRow(
            detail.AturanDetailId,
            detail.AturanDetailKategori ?? string.Empty,
            detail.AturanDetailKey ?? string.Empty,
            hasBackingDetail,
            elementKey,
            AturanExcelExportBuilder.FormatElementLabel(elementKey),
            descriptor.Category,
            descriptor.SubCategory,
            descriptor.Criteria,
            FormatScalarValue(valueNode, effectiveKey),
            hardConstraint,
            actualPath.ToArray(),
            hardConstraintPath.ToArray(),
            effectiveKey,
            valueNode?.DeepClone(),
            templateValueNode));
    }

    private static JsonNode? TryGetTemplateValueNode(string? detailKey, IReadOnlyList<string> actualPath)
    {
        var key = NormalizeKey(detailKey);
        if (!TemplateRootByKey.Value.TryGetValue(key, out var templateRoot))
            return null;

        return GetNodeAtPath(templateRoot, actualPath)?.DeepClone();
    }

    private static string[] AppendSegment(IReadOnlyList<string> path, string segment)
    {
        var result = new string[path.Count + 1];
        for (var index = 0; index < path.Count; index++)
            result[index] = path[index];
        result[^1] = segment;
        return result;
    }

    private static bool ParseHardConstraint(ImportedWorkbookRow importedRow)
    {
        if (TryParseBoolean(importedRow.HardConstraintCell, out var boolValue))
            return boolValue;

        throw new InvalidOperationException(
            $"Kolom Hard Constraint pada baris Excel {importedRow.ExcelRow} harus berisi true atau false.");
    }

    private static JsonNode? ParseImportedValue(AturanExcelSchemaRow schemaRow, ImportedWorkbookRow importedRow)
    {
        var templateNode = schemaRow.TemplateValueNode ?? schemaRow.CurrentValueNode;
        if (templateNode is JsonValue templateValue)
        {
            if (templateValue.TryGetValue<bool>(out var _))
            {
                if (!TryParseBoolean(importedRow.ValueCell, out var boolValue))
                    throw new InvalidOperationException($"Value pada row {schemaRow.DisplayAnchor} harus true atau false.");

                return JsonValue.Create(boolValue);
            }

            if (IsNumericScalar(templateValue))
            {
                if (!TryParseDecimal(importedRow.ValueCell, out var decimalValue))
                    throw new InvalidOperationException($"Value pada row {schemaRow.DisplayAnchor} harus berupa angka.");

                return JsonValue.Create(decimalValue);
            }

            if (templateValue.TryGetValue<string>(out var _))
                return JsonValue.Create(ParseStringValue(schemaRow, importedRow.ValueCell));
        }

        if (schemaRow.CurrentValueNode is JsonValue currentValue)
        {
            if (currentValue.TryGetValue<bool>(out var _))
            {
                if (!TryParseBoolean(importedRow.ValueCell, out var boolValue))
                    throw new InvalidOperationException($"Value pada row {schemaRow.DisplayAnchor} harus true atau false.");

                return JsonValue.Create(boolValue);
            }

            if (IsNumericScalar(currentValue))
            {
                if (!TryParseDecimal(importedRow.ValueCell, out var decimalValue))
                    throw new InvalidOperationException($"Value pada row {schemaRow.DisplayAnchor} harus berupa angka.");

                return JsonValue.Create(decimalValue);
            }
        }

        return JsonValue.Create(ParseStringValue(schemaRow, importedRow.ValueCell));
    }

    private static string ParseStringValue(AturanExcelSchemaRow schemaRow, IXLCell cell)
    {
        var percentPlaceholder = TryReadPercentPlaceholder(schemaRow, cell);
        if (!string.IsNullOrWhiteSpace(percentPlaceholder))
            return percentPlaceholder;

        var text = ReadCellText(cell);
        if (schemaRow.EffectiveKey.Equals("prefix", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(text) ||
                text.Equals("(tanpa prefix)", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }

        if (schemaRow.EffectiveKey.Equals("indentation", StringComparison.OrdinalIgnoreCase) &&
            text.Equals("none (tanpa indentasi)", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("(kosong)", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return text;
    }

    private static string? TryReadPercentPlaceholder(AturanExcelSchemaRow schemaRow, IXLCell cell)
    {
        var expectedPlaceholder = GetExpectedPercentPlaceholder(schemaRow);
        if (string.IsNullOrWhiteSpace(expectedPlaceholder))
        {
            return null;
        }

        if (cell.TryGetValue<double>(out var numericDoubleValue) ||
            cell.TryGetValue<decimal>(out var numericDecimalValue))
            return expectedPlaceholder;

        var formatted = cell.GetFormattedString().Trim();
        if (formatted.EndsWith("%", StringComparison.Ordinal))
        {
            var digits = new string(formatted[..^1].Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits))
                return $"%{digits}";
        }

        var raw = cell.GetString().Trim();
        if (raw.EndsWith("%", StringComparison.Ordinal))
        {
            var digits = new string(raw[..^1].Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits))
                return $"%{digits}";
        }

        return null;
    }

    private static bool LooksLikePercentPlaceholder(JsonNode? node)
    {
        return node is JsonValue jsonValue &&
               jsonValue.TryGetValue<string>(out var stringValue) &&
               !string.IsNullOrWhiteSpace(stringValue) &&
               stringValue.StartsWith('%') &&
               stringValue.Skip(1).All(char.IsDigit);
    }

    private static string? GetExpectedPercentPlaceholder(AturanExcelSchemaRow schemaRow)
    {
        if (LooksLikePercentPlaceholder(schemaRow.CurrentValueNode) &&
            schemaRow.CurrentValueNode is JsonValue currentValue &&
            currentValue.TryGetValue<string>(out var currentText))
        {
            return currentText;
        }

        if (LooksLikePercentPlaceholder(schemaRow.TemplateValueNode) &&
            schemaRow.TemplateValueNode is JsonValue templateValue &&
            templateValue.TryGetValue<string>(out var templateText))
        {
            return templateText;
        }

        return null;
    }

    private static bool TryParseBoolean(IXLCell cell, out bool value)
    {
        if (cell.TryGetValue<bool>(out value))
            return true;

        var text = ReadCellText(cell);
        if (bool.TryParse(text, out value))
            return true;

        if (text == "1")
        {
            value = true;
            return true;
        }

        if (text == "0")
        {
            value = false;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseDecimal(IXLCell cell, out decimal value)
    {
        if (cell.TryGetValue<double>(out var doubleValue))
        {
            value = (decimal)doubleValue;
            return true;
        }

        var text = ReadCellText(cell);
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        if (decimal.TryParse(text, NumberStyles.Number, new CultureInfo("id-ID"), out value))
            return true;

        var normalized = text.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string ReadCellText(IXLCell cell)
    {
        if (cell.TryGetValue<double>(out var doubleValue))
            return doubleValue.ToString(CultureInfo.InvariantCulture);

        if (cell.TryGetValue<bool>(out var boolValue))
            return boolValue ? "true" : "false";

        return cell.GetString().Trim();
    }

    private static JsonNode? GetNodeAtPath(JsonNode root, IReadOnlyList<string> path)
    {
        JsonNode? current = root;
        foreach (var segment in path)
        {
            if (current == null)
                return null;

            if (IsIndexToken(segment))
            {
                if (current is not JsonArray jsonArray)
                    return null;

                var index = ParseIndexToken(segment);
                if (index < 0 || index >= jsonArray.Count)
                    return null;

                current = jsonArray[index];
                continue;
            }

            if (current is not JsonObject jsonObject)
                return null;

            if (!jsonObject.TryGetPropertyValue(segment, out current))
                return null;
        }

        return current;
    }

    private static void SetNodeAtPath(JsonObject root, IReadOnlyList<string> path, JsonNode? value)
    {
        if (path.Count == 0)
            throw new InvalidOperationException("Path value kosong tidak didukung untuk import.");

        var parent = NavigateToParentNode(root, path);
        var lastSegment = path[^1];
        if (IsIndexToken(lastSegment))
        {
            if (parent is not JsonArray jsonArray)
                throw new InvalidOperationException($"Path array tidak valid: {SerializePath(path)}");

            jsonArray[ParseIndexToken(lastSegment)] = value?.DeepClone();
            return;
        }

        if (parent is not JsonObject jsonObject)
            throw new InvalidOperationException($"Path object tidak valid: {SerializePath(path)}");

        jsonObject[lastSegment] = value?.DeepClone();
    }

    private static void SetHardConstraintAtPath(JsonObject root, IReadOnlyList<string> path, bool hardConstraint)
    {
        if (path.Count == 0)
            throw new InvalidOperationException("Path hard constraint kosong tidak didukung untuk import.");

        var node = GetNodeAtPath(root, path);
        if (node is not JsonObject wrapperObject)
            throw new InvalidOperationException($"Wrapper hard constraint tidak ditemukan: {SerializePath(path)}");

        wrapperObject["is_hard_constraint"] = JsonValue.Create(hardConstraint);
    }

    private static JsonNode NavigateToParentNode(JsonObject root, IReadOnlyList<string> path)
    {
        JsonNode current = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            var segment = path[index];
            if (IsIndexToken(segment))
            {
                if (current is not JsonArray jsonArray)
                    throw new InvalidOperationException($"Path array tidak valid: {SerializePath(path)}");

                current = jsonArray[ParseIndexToken(segment)]
                    ?? throw new InvalidOperationException($"Index array tidak ditemukan: {SerializePath(path)}");
                continue;
            }

            if (current is not JsonObject jsonObject ||
                !jsonObject.TryGetPropertyValue(segment, out var nextNode) ||
                nextNode == null)
            {
                throw new InvalidOperationException($"Path object tidak ditemukan: {SerializePath(path)}");
            }

            current = nextNode;
        }

        return current;
    }

    private static bool JsonNodesEqual(JsonNode? left, JsonNode? right)
    {
        if (left == null && right == null)
            return true;

        if (left == null || right == null)
            return false;

        return JsonNode.DeepEquals(left, right);
    }

    private static string SerializePath(IReadOnlyList<string> path)
    {
        return string.Join(".", path);
    }

    private static bool ShouldSkipExportRow(string elementKey, IReadOnlyList<string> path)
    {
        return elementKey.Equals("tabel", StringComparison.OrdinalIgnoreCase) &&
               PathEndsWith(path, "konten_tabel", "font", "font_size");
    }

    private static string GetEffectiveKey(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return string.Empty;

        return IsIndexToken(path[^1]) && path.Count > 1
            ? path[^2]
            : path[^1];
    }

    private static (string Category, string SubCategory, string Criteria) BuildDescriptor(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return ("Umum", string.Empty, "Value");

        if (IsIndexToken(path[^1]))
        {
            var indexToken = path[^1];
            var baseSegments = path.Take(path.Count - 1).ToList();
            if (baseSegments.Count == 0)
                return ("Umum", string.Empty, indexToken);

            if (baseSegments.Count == 1)
            {
                return (
                    "Umum",
                    string.Empty,
                    $"{HumanizeSegment(baseSegments[0], true)} {indexToken}");
            }

            return (
                HumanizeSegment(baseSegments[0], false),
                FormatSubCategory(baseSegments.Skip(1).Take(baseSegments.Count - 2)),
                $"{HumanizeSegment(baseSegments[^1], true)} {indexToken}");
        }

        if (path.Count == 1)
            return ("Umum", string.Empty, HumanizeSegment(path[0], true));

        return (
            HumanizeSegment(path[0], false),
            FormatSubCategory(path.Skip(1).Take(path.Count - 2)),
            HumanizeSegment(path[^1], true));
    }

    private static string FormatSubCategory(IEnumerable<string> segments)
    {
        var parts = new List<string>();
        foreach (var segment in segments)
        {
            if (IsIndexToken(segment))
            {
                if (parts.Count == 0)
                    parts.Add(segment);
                else
                    parts[^1] += $" {segment}";

                continue;
            }

            parts.Add(HumanizeSegment(segment, false));
        }

        return string.Join(" / ", parts);
    }

    private static string HumanizeSegment(string rawSegment, bool isCriteria)
    {
        if (string.IsNullOrWhiteSpace(rawSegment))
            return string.Empty;

        if (IsIndexToken(rawSegment))
            return rawSegment;

        var normalized = rawSegment.Trim();
        var builder = new StringBuilder(normalized.Length + 8);
        var capitalizeNext = true;

        foreach (var character in normalized)
        {
            if (character == '_')
            {
                builder.Append(' ');
                capitalizeNext = true;
                continue;
            }

            builder.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
            capitalizeNext = false;
        }

        var label = builder.ToString();
        if (!isCriteria)
            return label;

        return rawSegment switch
        {
            "font_size" => "Font Size (pt)",
            "max_baris_kosong" => "Max Baris Kosong (baris)",
            "jumlah_baris_kosong_sebelum" => "Jumlah Baris Kosong Sebelum (baris)",
            "jumlah_baris_kosong_setelah" => "Jumlah Baris Kosong Setelah (baris)",
            "left_indent" => "Left Indent (cm)",
            "right_indent" => "Right Indent (cm)",
            "first_line_indent" => "First Line Indent (cm)",
            "first_line_indent_cm" => "First Line Indent (cm)",
            "hanging" => "Hanging (cm)",
            "hanging_min_cm" => "Hanging Min (cm)",
            "hanging_max_cm" => "Hanging Max (cm)",
            "left_cm" => "Left (cm)",
            "left_indent_cm" => "Left Indent (cm)",
            "overall_indent_cm" => "Overall Indent (cm)",
            "position_cm" => "Position (cm)",
            "distance_from_equation_cm" => "Distance From Equation (cm)",
            "header_from_top" => "Header From Top (cm)",
            "footer_from_bottom" => "Footer From Bottom (cm)",
            "gutter" => "Gutter (cm)",
            _ => label
        };
    }

    private static string FormatScalarValue(JsonNode? valueNode, string effectiveKey)
    {
        if (valueNode == null)
            return effectiveKey == "prefix" ? "(tanpa prefix)" : "(kosong)";

        if (valueNode is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue ? "true" : "false";

            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
                return decimalValue.ToString(CultureInfo.InvariantCulture);

            if (jsonValue.TryGetValue<double>(out var doubleValue))
                return doubleValue.ToString(CultureInfo.InvariantCulture);

            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue.ToString(CultureInfo.InvariantCulture);

            if (jsonValue.TryGetValue<long>(out var longValue))
                return longValue.ToString(CultureInfo.InvariantCulture);

            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                var trimmed = stringValue?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(trimmed))
                    return effectiveKey == "prefix" ? "(tanpa prefix)" : "(kosong)";

                if (effectiveKey == "prefix" &&
                    trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return "(tanpa prefix)";
                }

                if (effectiveKey == "indentation" &&
                    trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return "none (tanpa indentasi)";
                }

                return trimmed;
            }
        }

        return valueNode.ToJsonString(JsonOptions);
    }

    private static bool? ReadBooleanProperty(JsonObject jsonObject, string propertyName)
    {
        if (!jsonObject.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue jsonValue)
            return null;

        if (jsonValue.TryGetValue<bool>(out var boolValue))
            return boolValue;

        if (jsonValue.TryGetValue<string>(out var stringValue) &&
            bool.TryParse(stringValue, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsNumericScalar(JsonValue value)
    {
        return value.TryGetValue<int>(out var intValue) ||
               value.TryGetValue<long>(out var longValue) ||
               value.TryGetValue<decimal>(out var decimalValue) ||
               value.TryGetValue<double>(out var doubleValue);
    }

    private static bool PathEndsWith(IReadOnlyList<string> path, params string[] suffix)
    {
        if (path.Count < suffix.Length)
            return false;

        var offset = path.Count - suffix.Length;
        for (var index = 0; index < suffix.Length; index++)
        {
            if (!path[offset + index].Equals(suffix[index], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsIndexToken(string value)
    {
        return value.Length >= 3 && value[0] == '[' && value[^1] == ']';
    }

    private static int ParseIndexToken(string value)
    {
        return int.Parse(value[1..^1], CultureInfo.InvariantCulture) - 1;
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }

    private sealed record ExpandedElementNode(string ElementKey, JsonNode Node, string[] FullPathPrefix);

    private sealed class DetailState
    {
        public DetailState(AturanDetail detail, JsonObject root)
        {
            Detail = detail;
            Root = root;
        }

        public AturanDetail Detail { get; }
        public JsonObject Root { get; }
        public bool IsChanged { get; private set; }

        public void MarkChanged()
        {
            IsChanged = true;
        }
    }

    private sealed class ImportedWorkbookRow
    {
        public ImportedWorkbookRow(
            int excelRow,
            string elemen,
            string kategori,
            string subKategori,
            string kriteria,
            IXLCell valueCell,
            IXLCell hardConstraintCell)
        {
            ExcelRow = excelRow;
            Elemen = elemen;
            Kategori = kategori;
            SubKategori = subKategori;
            Kriteria = kriteria;
            ValueCell = valueCell;
            HardConstraintCell = hardConstraintCell;
        }

        public int ExcelRow { get; }
        public string Elemen { get; }
        public string Kategori { get; }
        public string SubKategori { get; }
        public string Kriteria { get; }
        public IXLCell ValueCell { get; }
        public IXLCell HardConstraintCell { get; }

        public string Anchor => string.Join("\u001f", Elemen, Kategori, SubKategori, Kriteria);
        public string DisplayAnchor => $"{Elemen} / {Kategori} / {SubKategori} / {Kriteria}";
    }

    private sealed record AturanExcelSchemaRow(
        uint AturanDetailId,
        string AturanDetailKategori,
        string AturanDetailKey,
        bool HasBackingDetail,
        string ElemenKey,
        string ElemenLabel,
        string Kategori,
        string SubKategori,
        string Kriteria,
        string ValueText,
        bool HardConstraint,
        string[] ValuePath,
        string[] HardConstraintPath,
        string EffectiveKey,
        JsonNode? CurrentValueNode,
        JsonNode? TemplateValueNode)
    {
        public string Anchor => string.Join("\u001f", ElemenLabel, Kategori, SubKategori, Kriteria);
        public string DisplayAnchor => $"{ElemenLabel} / {Kategori} / {SubKategori} / {Kriteria}";
    }
}

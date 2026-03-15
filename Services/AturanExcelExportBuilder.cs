using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public static class AturanExcelExportBuilder
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

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

    public static byte[] BuildWorkbook(string? aturanVersi, IReadOnlyList<AturanDetail> details)
    {
        var rows = BuildExportRows(details);
        var validationChecks = ValidationCheckCatalog.BuildRows(details);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Aturan");

        var headers = new[]
        {
            "Elemen",
            "Kategori",
            "Sub Kategori",
            "Kriteria",
            "Value",
            "Hard Constraint",
            "Note"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            worksheet.Cell(excelRow, 1).Value = FormatElementLabel(row.Elemen);
            worksheet.Cell(excelRow, 2).Value = row.Kategori;
            worksheet.Cell(excelRow, 3).Value = row.SubKategori;
            worksheet.Cell(excelRow, 4).Value = row.Kriteria;
            var valueCell = worksheet.Cell(excelRow, 5);
            if (row.NumericValue.HasValue)
                valueCell.Value = row.NumericValue.Value;
            else
                valueCell.Value = row.ValueText;
            worksheet.Cell(excelRow, 6).Value = row.HardConstraint;
            worksheet.Cell(excelRow, 7).Value = row.Note;
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(rows.Count + 1, 2), headers.Length);
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        usedRange.Style.Alignment.WrapText = true;
        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, 1, headers.Length).SetAutoFilter();
        worksheet.Columns().AdjustToContents();
        worksheet.Column(5).Width = Math.Max(worksheet.Column(5).Width, 18);
        worksheet.Column(6).Width = Math.Max(worksheet.Column(6).Width, 16);
        worksheet.Column(7).Width = Math.Max(worksheet.Column(7).Width, 28);

        var validationWorksheet = workbook.Worksheets.Add("Cek Validasi");
        var validationHeaders = new[]
        {
            "Elemen",
            "Field Error",
            "Sumber",
            "Path Aturan",
            "Aktif Saat Ini",
            "Bisa Error Tanpa DB",
            "Yang Dicek"
        };

        for (var column = 0; column < validationHeaders.Length; column++)
        {
            var cell = validationWorksheet.Cell(1, column + 1);
            cell.Value = validationHeaders[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        for (var index = 0; index < validationChecks.Count; index++)
        {
            var row = validationChecks[index];
            var excelRow = index + 2;
            validationWorksheet.Cell(excelRow, 1).Value = FormatElementLabel(row.Elemen);
            validationWorksheet.Cell(excelRow, 2).Value = row.FieldError;
            validationWorksheet.Cell(excelRow, 3).Value = row.Sumber;
            validationWorksheet.Cell(excelRow, 4).Value = row.AturanPath;
            validationWorksheet.Cell(excelRow, 5).Value = row.AktifSaatIni;
            validationWorksheet.Cell(excelRow, 6).Value = row.BisaErrorTanpaDb;
            validationWorksheet.Cell(excelRow, 7).Value = row.YangDicek;
        }

        var validationRange = validationWorksheet.Range(1, 1, Math.Max(validationChecks.Count + 1, 2), validationHeaders.Length);
        validationRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        validationRange.Style.Alignment.WrapText = true;
        validationWorksheet.SheetView.FreezeRows(1);
        validationWorksheet.Range(1, 1, 1, validationHeaders.Length).SetAutoFilter();
        validationWorksheet.Columns().AdjustToContents();
        validationWorksheet.Column(4).Width = Math.Max(validationWorksheet.Column(4).Width, 28);
        validationWorksheet.Column(7).Width = Math.Max(validationWorksheet.Column(7).Width, 52);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static List<AturanExcelExportRow> BuildRows(IReadOnlyList<AturanDetail> details)
    {
        var rows = new List<AturanExcelExportRow>();
        var detailKeys = details
            .Select(d => d.AturanDetailKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in details)
        {
            AppendDetailRows(rows, detail, detailKeys);
        }

        return rows;
    }

    private static List<AturanExcelExportRow> BuildExportRows(IReadOnlyList<AturanDetail> details)
    {
        var mergedDetails = AturanExportCatalog.MergeValidationTemplates(details);
        var syntheticElements = AturanExportCatalog.GetSyntheticElementKeys(details);
        return BuildRows(mergedDetails)
            .Select(row => syntheticElements.Contains(row.Elemen)
                ? row with { Note = AturanExportCatalog.AppendTemplateNote(row.Note) }
                : row)
            .ToList();
    }

    private static void AppendDetailRows(
        List<AturanExcelExportRow> rows,
        AturanDetail detail,
        IReadOnlySet<string> allDetailKeys)
    {
        var detailKey = (detail.AturanDetailKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(detailKey))
            return;

        if (string.IsNullOrWhiteSpace(detail.AturanDetailJsonValue))
        {
            rows.Add(new AturanExcelExportRow(detailKey, "Umum", string.Empty, "Value", string.Empty, null, false, "Value kosong"));
            return;
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(detail.AturanDetailJsonValue);
        }
        catch (JsonException)
        {
            rows.Add(new AturanExcelExportRow(detailKey, "Umum", string.Empty, "Raw JSON", detail.AturanDetailJsonValue, null, false, "JSON tidak valid"));
            return;
        }

        if (rootNode is not JsonObject rootObject)
        {
            rows.Add(CreateRow(detailKey, [], rootNode, false));
            return;
        }

        foreach (var (elementKey, elementNode) in ExpandElementNodes(detailKey, rootObject, allDetailKeys))
        {
            FlattenNode(rows, elementKey, elementNode, [], null);
        }
    }

    private static IEnumerable<(string ElementKey, JsonNode Node)> ExpandElementNodes(
        string detailKey,
        JsonObject rootObject,
        IReadOnlySet<string> allDetailKeys)
    {
        if (!SplitElementKeys.TryGetValue(detailKey, out var splitKeys))
        {
            yield return (detailKey, rootObject);
            yield break;
        }

        var hasAnySplitNode = false;
        foreach (var splitKey in splitKeys)
        {
            if (!rootObject.TryGetPropertyValue(splitKey, out var childNode) || childNode == null)
                continue;

            if (!splitKey.Equals(detailKey, StringComparison.OrdinalIgnoreCase) &&
                allDetailKeys.Contains(splitKey))
            {
                continue;
            }

            hasAnySplitNode = true;
            yield return (splitKey, childNode);
        }

        if (!hasAnySplitNode)
            yield return (detailKey, rootObject);
    }

    private static void FlattenNode(
        List<AturanExcelExportRow> rows,
        string elementKey,
        JsonNode? node,
        List<string> path,
        bool? hardConstraint)
    {
        if (node == null)
        {
            rows.Add(CreateRow(elementKey, path, null, hardConstraint ?? false));
            return;
        }

        switch (node)
        {
            case JsonObject jsonObject:
                FlattenObject(rows, elementKey, jsonObject, path, hardConstraint);
                return;
            case JsonArray jsonArray:
                FlattenArray(rows, elementKey, jsonArray, path, hardConstraint);
                return;
            default:
                rows.Add(CreateRow(elementKey, path, node, hardConstraint ?? false));
                return;
        }
    }

    private static void FlattenObject(
        List<AturanExcelExportRow> rows,
        string elementKey,
        JsonObject jsonObject,
        List<string> path,
        bool? inheritedHardConstraint)
    {
        if (jsonObject.TryGetPropertyValue("value", out var valueNode))
        {
            var wrapperHardConstraint = ReadBooleanProperty(jsonObject, "is_hard_constraint") ?? inheritedHardConstraint ?? false;
            if (valueNode is JsonObject or JsonArray)
            {
                FlattenNode(rows, elementKey, valueNode, path, wrapperHardConstraint);
            }
            else
            {
                rows.Add(CreateRow(elementKey, path, valueNode, wrapperHardConstraint));
            }

            foreach (var property in jsonObject)
            {
                if (WrapperMetaKeys.Contains(property.Key))
                    continue;

                FlattenNode(rows, elementKey, property.Value, [.. path, property.Key], wrapperHardConstraint);
            }

            return;
        }

        foreach (var property in jsonObject)
        {
            FlattenNode(rows, elementKey, property.Value, [.. path, property.Key], inheritedHardConstraint);
        }
    }

    private static void FlattenArray(
        List<AturanExcelExportRow> rows,
        string elementKey,
        JsonArray jsonArray,
        List<string> path,
        bool? hardConstraint)
    {
        if (jsonArray.Count == 0)
        {
            rows.Add(CreateRow(elementKey, path, null, hardConstraint ?? false));
            return;
        }

        for (var index = 0; index < jsonArray.Count; index++)
        {
            FlattenNode(rows, elementKey, jsonArray[index], [.. path, $"[{index + 1}]"], hardConstraint);
        }
    }

    private static AturanExcelExportRow CreateRow(string elementKey, IReadOnlyList<string> path, JsonNode? valueNode, bool hardConstraint)
    {
        var descriptor = BuildDescriptor(path);
        var effectiveKey = GetEffectiveKey(path);
        return new AturanExcelExportRow(
            elementKey,
            descriptor.Category,
            descriptor.SubCategory,
            descriptor.Criteria,
            FormatScalarValue(valueNode, effectiveKey),
            GetNumericCellValue(valueNode),
            hardConstraint,
            BuildNote(path, valueNode));
    }

    private static string GetEffectiveKey(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return string.Empty;

        return IsIndexToken(path[^1]) && path.Count > 1
            ? path[^2]
            : path[^1];
    }

    private static double? GetNumericCellValue(JsonNode? valueNode)
    {
        if (valueNode is not JsonValue jsonValue)
            return null;

        if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            return decimal.ToDouble(decimalValue);

        if (jsonValue.TryGetValue<double>(out var doubleValue))
            return doubleValue;

        if (jsonValue.TryGetValue<int>(out var intValue))
            return intValue;

        if (jsonValue.TryGetValue<long>(out var longValue))
            return longValue;

        return null;
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

        foreach (var ch in normalized)
        {
            if (ch == '_')
            {
                builder.Append(' ');
                capitalizeNext = true;
                continue;
            }

            builder.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
            capitalizeNext = false;
        }

        var label = builder.ToString();

        if (!isCriteria)
            return label;

        return rawSegment switch
        {
            "font_size" => "Font Size (pt)",
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

        return valueNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static string BuildNote(IReadOnlyList<string> path, JsonNode? valueNode)
    {
        var effectiveKey = GetEffectiveKey(path);

        if (valueNode is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var _boolValue))
            return "Pilihan: true, false";

        if (effectiveKey.StartsWith("cegah_", StringComparison.OrdinalIgnoreCase) ||
            effectiveKey.StartsWith("minimal_", StringComparison.OrdinalIgnoreCase) ||
            effectiveKey.StartsWith("wajib_", StringComparison.OrdinalIgnoreCase) ||
            effectiveKey.StartsWith("satu_", StringComparison.OrdinalIgnoreCase) ||
            effectiveKey is "continue" or "different_first_page" or "allow_other_content" or "is_empty" or
                "use_numbering" or "enter_after_number" or "enter_after_numbering" or "depends_on_equation_length")
        {
            return "Pilihan: true, false";
        }

        return effectiveKey switch
        {
            "alignment" => "Pilihan umum: left, center, right, justify",
            "location" => "Pilihan: header, footer",
            "layout_option" => "Pilihan: inline_with_text",
            "position" => "Pilihan umum: before, after",
            "orientation" => "Pilihan: PORTRAIT, LANDSCAPE",
            "size" => "Pilihan umum: A4, A3",
            "indentation" => "Isi `none` atau angka 0 berarti tanpa indentasi",
            "case" => "Pilihan umum: UPPERCASE, Title Case, lowercase",
            "type" => "Pilihan umum: arab, arabic, decimal, lowerRoman, upperRoman, lowerLetter, upperLetter",
            "font_name" => "Teks bebas. Contoh: Times New Roman",
            "number_format" => "Teks/pola format. Contoh: BAB I atau Gambar [nomor_bab].[nomor_gambar]",
            "prefix" => "Kosong = tanpa prefix",
            "line_spacing" => "Angka desimal. Contoh: 1, 1.5, 2",
            "font_size" => "Angka dalam pt",
            "left_indent" or "right_indent" or "first_line_indent" or "first_line_indent_cm" or "hanging" or
                "hanging_min_cm" or "hanging_max_cm" or "left_cm" or "left_indent_cm" or "overall_indent_cm" or
                "position_cm" or "distance_from_equation_cm" or "header_from_top" or "footer_from_bottom" or
                "gutter" => "Angka desimal dalam cm",
            _ => valueNode is JsonValue scalarValue && scalarValue.TryGetValue<string>(out var _stringValue)
                ? "Teks bebas"
                : valueNode is JsonValue
                    ? "Angka atau teks sesuai kebutuhan aturan"
                    : string.Empty
        };
    }

    private static string FormatElementLabel(string rawElement)
    {
        if (string.IsNullOrWhiteSpace(rawElement))
            return string.Empty;

        return string.Join(
            ' ',
            rawElement
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment => HumanizeSegment(segment, false)));
    }

    private static bool IsIndexToken(string value)
    {
        return value.Length >= 3 && value[0] == '[' && value[^1] == ']';
    }
}

public sealed record AturanExcelExportRow(
    string Elemen,
    string Kategori,
    string SubKategori,
    string Kriteria,
    string ValueText,
    double? NumericValue,
    bool HardConstraint,
    string Note);

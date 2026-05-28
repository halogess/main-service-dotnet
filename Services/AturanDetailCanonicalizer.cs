using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public static class AturanDetailCanonicalizer
{
    private const string ValueProperty = "value";
    private const string IsEditableProperty = "is_editable";
    private const string IsHardConstraintProperty = "is_hard_constraint";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private static readonly Lazy<IReadOnlyDictionary<string, string>> TemplateJsonByKey = new(BuildTemplateMap);

    public static bool TryCanonicalize(
        string? detailKey,
        string rawJson,
        out string? canonicalJson,
        out bool changed,
        out string? errorMessage)
    {
        canonicalJson = null;
        changed = false;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errorMessage = "json_value tidak boleh kosong";
            return false;
        }

        if (!AturanDetailParagraphCutover.TryTransform(detailKey, rawJson, out var normalizedJson, out var paragraphChanged, out errorMessage))
            return false;

        if (string.IsNullOrWhiteSpace(normalizedJson))
        {
            errorMessage = "json_value tidak valid";
            return false;
        }

        var key = NormalizeKey(detailKey);
        if (!TemplateJsonByKey.Value.TryGetValue(key, out var templateJson))
        {
            canonicalJson = normalizedJson;
            changed = !string.Equals(rawJson, canonicalJson, StringComparison.Ordinal);
            return true;
        }

        try
        {
            if (JsonNode.Parse(normalizedJson) is not JsonObject currentRoot)
            {
                errorMessage = "json_value harus berupa object JSON.";
                return false;
            }

            if (JsonNode.Parse(templateJson) is not JsonObject templateRoot)
            {
                errorMessage = $"Template canonical untuk key `{key}` tidak valid.";
                return false;
            }

            ApplyLegacyAliases(key, currentRoot);

            var canonicalRoot = MergeObject(templateRoot, currentRoot, []);
            ApplyCanonicalRemovals(key, canonicalRoot);
            AturanDetailEditablePolicy.Apply(key, canonicalRoot);
            canonicalJson = canonicalRoot.ToJsonString(SerializerOptions);
            changed = paragraphChanged || !string.Equals(normalizedJson, canonicalJson, StringComparison.Ordinal);

            if (!AturanDetailShapeValidator.TryValidate(detailKey, canonicalJson, out errorMessage))
                return false;

            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"json_value tidak valid: {ex.Message}";
            return false;
        }
    }

    public static string? CanonicalizeOrOriginal(string? detailKey, string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return rawJson;

        return TryCanonicalize(detailKey, rawJson, out var canonicalJson, out var _, out var _)
            ? canonicalJson
            : rawJson;
    }

    private static IReadOnlyDictionary<string, string> BuildTemplateMap()
    {
        return AturanExportCatalog
            .CreateDefaultDetails(0)
            .ToDictionary(
                detail => NormalizeKey(detail.AturanDetailKey),
                detail => detail.AturanDetailJsonValue ?? "{}",
                StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyLegacyAliases(string key, JsonObject root)
    {
        switch (key)
        {
            case "judul_bab":
                MoveLegacyCountValue(root,
                    ["struktur_konten", "satu_baris_kosong_setelah"],
                    ["struktur_konten", "jumlah_baris_kosong_setelah"],
                    enabledValue: 1,
                    disabledValue: 0);
                MoveLegacyCountValue(root,
                    ["struktur_konten", "min_satu_paragraf_sebelum_subbab"],
                    ["struktur_konten", "minimal_paragraf_sebelum_subbab"],
                    enabledValue: 1,
                    disabledValue: 0);
                break;
            case "judul_subbab":
                MoveLegacyCountValue(root,
                    ["struktur_konten", "minimal_satu_paragraf_setelah"],
                    ["struktur_konten", "minimal_paragraf_setelah"],
                    enabledValue: 1,
                    disabledValue: 0);
                MoveLegacyCountValue(root,
                    ["struktur_konten", "cegah_subbab_tunggal"],
                    ["struktur_konten", "minimal_subbab_level_sama"],
                    enabledValue: 2,
                    disabledValue: 1);
                break;
            case "gambar":
                MovePathValue(root,
                    ["caption_gambar", "numbering", "enter_after_number"],
                    ["caption_gambar", "numbering", "enter_after_numbering"]);
                break;
            case "tabel":
                MovePathValue(root,
                    ["caption_tabel", "numbering", "enter_after_number"],
                    ["caption_tabel", "numbering", "enter_after_numbering"]);
                break;
            case "kode":
                MovePathValue(root,
                    ["judul_kode", "numbering", "enter_after_number"],
                    ["judul_kode", "numbering", "enter_after_numbering"]);
                break;
        }
    }

    private static void ApplyCanonicalRemovals(string key, JsonObject root)
    {
        if (key == "nomor_halaman")
            RemovePath(root, ["paragraph", "spacing", "line_spacing"]);
    }

    private static void MoveLegacyCountValue(
        JsonObject root,
        IReadOnlyList<string> sourcePath,
        IReadOnlyList<string> targetPath,
        decimal enabledValue,
        decimal disabledValue)
    {
        MovePathValue(
            root,
            sourcePath,
            targetPath,
            sourceValue => ConvertLegacyCountNode(sourceValue, enabledValue, disabledValue));
    }

    private static JsonObject MergeObject(JsonObject templateObject, JsonNode? currentNode, IReadOnlyList<string> path)
    {
        var currentObject = UnwrapObjectNode(currentNode);
        var result = new JsonObject();

        foreach (var property in templateObject)
        {
            var childPath = AppendPath(path, property.Key);
            JsonNode? currentValue = null;
            currentObject?.TryGetPropertyValue(property.Key, out currentValue);
            result[property.Key] = MergeNode(property.Value, currentValue, childPath);
        }

        return result;
    }

    private static JsonNode? MergeNode(JsonNode? templateNode, JsonNode? currentNode, IReadOnlyList<string> path)
    {
        if (templateNode is JsonObject templateObject)
        {
            if (IsWrapper(templateObject))
                return MergeWrapper(templateObject, currentNode, path);

            return MergeObject(templateObject, currentNode, path);
        }

        if (templateNode is JsonArray templateArray)
        {
            return currentNode is JsonArray currentArray
                ? currentArray.DeepClone()
                : templateArray.DeepClone();
        }

        if (currentNode == null)
            return templateNode?.DeepClone();

        return currentNode.DeepClone();
    }

    private static JsonObject MergeWrapper(JsonObject templateWrapper, JsonNode? currentNode, IReadOnlyList<string> path)
    {
        var templateValue = templateWrapper[ValueProperty];
        var templateIsEditable = ReadBooleanFlag(templateWrapper, IsEditableProperty, defaultValue: false);
        var templateIsHardConstraint = ReadBooleanFlag(templateWrapper, IsHardConstraintProperty, defaultValue: false);

        JsonObject? currentWrapper = null;
        JsonNode? currentValue = null;
        if (currentNode is JsonObject currentObject && IsWrapper(currentObject))
        {
            currentWrapper = currentObject;
            currentValue = currentObject[ValueProperty];
        }
        else
        {
            currentValue = currentNode;
        }

        var mergedValue = MergeWrapperValue(templateValue, currentValue, path);
        var result = CreateWrapper(
            mergedValue,
            currentWrapper != null
                ? ReadBooleanFlag(currentWrapper, IsEditableProperty, templateIsEditable)
                : templateIsEditable,
            currentWrapper != null
                ? ReadBooleanFlag(currentWrapper, IsHardConstraintProperty, templateIsHardConstraint)
                : templateIsHardConstraint);

        return result;
    }

    private static JsonNode? MergeWrapperValue(JsonNode? templateValue, JsonNode? currentValue, IReadOnlyList<string> path)
    {
        if (currentValue == null)
            return templateValue?.DeepClone();

        if (templateValue is JsonObject templateObject && !IsWrapper(templateObject))
        {
            var currentObject = UnwrapObjectNode(currentValue);
            if (currentObject != null)
                return MergeObject(templateObject, currentObject, path);

            return templateValue.DeepClone();
        }

        if (templateValue is JsonArray templateArray)
        {
            return currentValue is JsonArray currentArray
                ? currentArray.DeepClone()
                : templateArray.DeepClone();
        }

        if (templateValue is JsonValue templateScalar && currentValue is JsonValue currentScalar)
            return CoerceScalarValue(templateScalar, currentScalar);

        return currentValue.DeepClone();
    }

    private static JsonNode CoerceScalarValue(JsonValue templateScalar, JsonValue currentScalar)
    {
        if (templateScalar.TryGetValue<bool>(out var _))
            return JsonValue.Create(ReadScalarBoolean(currentScalar));

        if (IsNumericScalar(templateScalar))
        {
            if (TryReadDecimal(currentScalar, out var decimalValue))
                return JsonValue.Create(decimalValue);
        }

        if (templateScalar.TryGetValue<string>(out var _))
        {
            if (currentScalar.TryGetValue<string>(out var stringValue))
                return JsonValue.Create(stringValue);
            if (currentScalar.TryGetValue<bool>(out var boolValue))
                return JsonValue.Create(boolValue ? "true" : "false");
            if (TryReadDecimal(currentScalar, out var decimalValue))
                return JsonValue.Create(decimalValue.ToString(CultureInfo.InvariantCulture));
        }

        return currentScalar.DeepClone();
    }

    private static JsonObject? UnwrapObjectNode(JsonNode? node)
    {
        if (node is JsonObject jsonObject && !IsWrapper(jsonObject))
            return jsonObject;

        if (node is JsonObject wrapper &&
            IsWrapper(wrapper) &&
            wrapper[ValueProperty] is JsonObject wrappedValue &&
            !IsWrapper(wrappedValue))
        {
            return wrappedValue;
        }

        return null;
    }

    private static void MovePathValue(JsonObject root, IReadOnlyList<string> sourcePath, IReadOnlyList<string> targetPath)
    {
        MovePathValue(root, sourcePath, targetPath, null);
    }

    private static void MovePathValue(
        JsonObject root,
        IReadOnlyList<string> sourcePath,
        IReadOnlyList<string> targetPath,
        Func<JsonNode, JsonNode?>? transform)
    {
        if (sourcePath.Count == 0 || targetPath.Count == 0)
            return;

        if (!TryGetParentObject(root, sourcePath, createIfMissing: false, out var sourceParent))
            return;

        if (!sourceParent!.TryGetPropertyValue(sourcePath[^1], out var sourceValue) || sourceValue == null)
            return;

        if (!TryGetParentObject(root, targetPath, createIfMissing: true, out var targetParent))
            return;

        if (targetParent![targetPath[^1]] == null)
            targetParent[targetPath[^1]] = transform?.Invoke(sourceValue) ?? sourceValue.DeepClone();

        sourceParent.Remove(sourcePath[^1]);
    }

    private static void RemovePath(JsonObject root, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return;

        if (!TryGetParentObject(root, path, createIfMissing: false, out var parent))
            return;

        parent!.Remove(path[^1]);
    }

    private static JsonNode? ConvertLegacyCountNode(JsonNode sourceValue, decimal enabledValue, decimal disabledValue)
    {
        if (sourceValue is JsonObject sourceObject && IsWrapper(sourceObject))
        {
            var convertedWrapper = new JsonObject();
            foreach (var property in sourceObject)
            {
                convertedWrapper[property.Key] = property.Key.Equals(ValueProperty, StringComparison.OrdinalIgnoreCase)
                    ? ConvertLegacyCountScalar(property.Value, enabledValue, disabledValue)
                    : property.Value?.DeepClone();
            }

            return convertedWrapper;
        }

        return ConvertLegacyCountScalar(sourceValue, enabledValue, disabledValue);
    }

    private static JsonNode? ConvertLegacyCountScalar(JsonNode? sourceValue, decimal enabledValue, decimal disabledValue)
    {
        if (sourceValue is not JsonValue scalarValue)
            return sourceValue?.DeepClone();

        if (TryReadDecimal(scalarValue, out var decimalValue))
            return JsonValue.Create(decimalValue);

        return JsonValue.Create(ReadScalarBoolean(scalarValue) ? enabledValue : disabledValue);
    }

    private static bool TryGetParentObject(
        JsonObject root,
        IReadOnlyList<string> path,
        bool createIfMissing,
        out JsonObject? parent)
    {
        parent = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            var segment = path[index];
            if (parent[segment] is JsonObject nextObject && !IsWrapper(nextObject))
            {
                parent = nextObject;
                continue;
            }

            if (!createIfMissing)
            {
                parent = null;
                return false;
            }

            var created = new JsonObject();
            parent[segment] = created;
            parent = created;
        }

        return true;
    }

    private static JsonObject CreateWrapper(JsonNode? valueNode, bool isEditable, bool isHardConstraint)
    {
        return new JsonObject
        {
            [ValueProperty] = valueNode?.DeepClone(),
            [IsEditableProperty] = JsonValue.Create(isEditable),
            [IsHardConstraintProperty] = JsonValue.Create(isHardConstraint)
        };
    }

    private static bool ReadBooleanFlag(JsonObject source, string propertyName, bool defaultValue)
    {
        foreach (var property in source)
        {
            if (!property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            return ReadScalarBoolean(property.Value, defaultValue);
        }

        return defaultValue;
    }

    private static bool ReadScalarBoolean(JsonNode? node, bool defaultValue = false)
    {
        if (node is not JsonValue value)
            return defaultValue;

        if (value.TryGetValue<bool>(out var boolValue))
            return boolValue;

        if (value.TryGetValue<string>(out var stringValue))
        {
            if (bool.TryParse(stringValue, out var parsedBool))
                return parsedBool;
            if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                return parsedInt != 0;
            return defaultValue;
        }

        if (value.TryGetValue<int>(out var intValue))
            return intValue != 0;

        if (value.TryGetValue<long>(out var longValue))
            return longValue != 0;

        if (value.TryGetValue<decimal>(out var decimalValue))
            return decimalValue != 0;

        return defaultValue;
    }

    private static bool TryReadDecimal(JsonValue value, out decimal result)
    {
        if (value.TryGetValue<decimal>(out result))
            return true;

        if (value.TryGetValue<int>(out var intValue))
        {
            result = intValue;
            return true;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            result = longValue;
            return true;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            result = (decimal)doubleValue;
            return true;
        }

        if (value.TryGetValue<string>(out var stringValue) &&
            decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    private static bool IsNumericScalar(JsonValue value)
    {
        return value.TryGetValue<int>(out var intValue) ||
               value.TryGetValue<long>(out var longValue) ||
               value.TryGetValue<decimal>(out var decimalValue) ||
               value.TryGetValue<double>(out var doubleValue);
    }

    private static bool IsWrapper(JsonObject node)
    {
        foreach (var property in node)
        {
            if (property.Key.Equals(ValueProperty, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> AppendPath(IReadOnlyList<string> path, string segment)
    {
        if (path.Count == 0)
            return [segment];

        var result = new string[path.Count + 1];
        for (var index = 0; index < path.Count; index++)
            result[index] = path[index];
        result[^1] = segment;
        return result;
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}

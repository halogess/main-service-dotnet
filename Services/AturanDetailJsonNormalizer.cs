using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ValidasiTugasAkhir.MainService.Services;

public static class AturanDetailJsonNormalizer
{
    private const string ValueProperty = "value";
    private const string IsEditableProperty = "is_editable";
    private const string IsHardConstraintProperty = "is_hard_constraint";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static bool TryNormalize(
        string rawJson,
        out string? normalizedJson,
        out string? errorMessage)
    {
        normalizedJson = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errorMessage = "json_value tidak boleh kosong";
            return false;
        }

        try
        {
            var parsed = JsonNode.Parse(rawJson);
            if (parsed == null)
            {
                errorMessage = "json_value tidak valid";
                return false;
            }

            var normalizedNode = NormalizeNode(parsed);
            normalizedJson = normalizedNode.ToJsonString(SerializerOptions);
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"json_value tidak valid: {ex.Message}";
            return false;
        }
    }

    private static JsonNode NormalizeNode(JsonNode node)
    {
        return node switch
        {
            JsonObject jsonObject => NormalizeObject(jsonObject),
            JsonArray jsonArray => CreateWrapper(jsonArray.DeepClone(), false, false),
            JsonValue jsonValue => CreateWrapper(jsonValue.DeepClone(), false, false),
            _ => CreateWrapper(node.DeepClone(), false, false)
        };
    }

    private static JsonNode NormalizeObject(JsonObject jsonObject)
    {
        if (TryGetPropertyIgnoreCase(jsonObject, ValueProperty, out var valueNode))
        {
            var normalizedValue = valueNode?.DeepClone();
            var isEditable = ReadBooleanFlag(jsonObject, IsEditableProperty);
            var isHardConstraint = ReadBooleanFlag(jsonObject, IsHardConstraintProperty);

            var normalizedWrapper = CreateWrapper(normalizedValue, isEditable, isHardConstraint);
            foreach (var property in jsonObject)
            {
                if (property.Key.Equals(ValueProperty, StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals(IsEditableProperty, StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals(IsHardConstraintProperty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                normalizedWrapper[property.Key] = property.Value?.DeepClone();
            }

            return normalizedWrapper;
        }

        var normalizedObject = new JsonObject();
        foreach (var property in jsonObject)
        {
            normalizedObject[property.Key] = NormalizeChildProperty(property.Value);
        }

        return normalizedObject;
    }

    private static JsonNode? NormalizeChildProperty(JsonNode? child)
    {
        if (child == null)
            return null;

        return child switch
        {
            JsonObject childObject => NormalizeObject(childObject),
            JsonArray childArray => CreateWrapper(childArray.DeepClone(), false, false),
            JsonValue childValue => CreateWrapper(childValue.DeepClone(), false, false),
            _ => CreateWrapper(child.DeepClone(), false, false)
        };
    }

    private static JsonObject CreateWrapper(JsonNode? valueNode, bool isEditable, bool isHardConstraint)
    {
        return new JsonObject
        {
            [ValueProperty] = valueNode,
            [IsEditableProperty] = JsonValue.Create(isEditable),
            [IsHardConstraintProperty] = JsonValue.Create(isHardConstraint)
        };
    }

    private static bool ReadBooleanFlag(JsonObject source, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(source, propertyName, out var node))
            return false;

        return ToBoolean(node);
    }

    private static bool ToBoolean(JsonNode? node)
    {
        if (node == null)
            return false;

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue;

            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                if (bool.TryParse(stringValue, out var parsedBool))
                    return parsedBool;

                if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                    return parsedInt != 0;

                return false;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue != 0;

            if (jsonValue.TryGetValue<long>(out var longValue))
                return longValue != 0;

            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
                return decimalValue != 0;
        }

        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonObject source, string propertyName, out JsonNode? value)
    {
        foreach (var property in source)
        {
            if (property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}

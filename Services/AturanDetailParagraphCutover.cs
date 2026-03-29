using System.Text.Json;
using System.Text.Json.Nodes;

namespace ValidasiTugasAkhir.MainService.Services;

public static class AturanDetailParagraphCutover
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static bool TryTransform(string? detailKey, string rawJson, out string? transformedJson, out bool changed, out string? errorMessage)
    {
        transformedJson = null;
        changed = false;
        errorMessage = null;

        if (!AturanDetailJsonNormalizer.TryNormalize(rawJson, out var normalizedJson, out errorMessage))
            return false;

        var key = NormalizeKey(detailKey);
        if (key is not ("judul_subbab" or "paragraf"))
        {
            transformedJson = normalizedJson;
            return true;
        }

        try
        {
            if (JsonNode.Parse(normalizedJson!) is not JsonObject root)
            {
                errorMessage = "json_value harus berupa object JSON.";
                return false;
            }

            changed = key switch
            {
                "judul_subbab" => TransformSubchapter(root),
                "paragraf" => TransformParagraph(root),
                _ => false
            };

            transformedJson = root.ToJsonString(SerializerOptions);

            if (!AturanDetailShapeValidator.TryValidate(detailKey, transformedJson, out errorMessage))
                return false;

            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"json_value tidak valid: {ex.Message}";
            return false;
        }
    }

    private static bool TransformSubchapter(JsonObject root)
    {
        if (!TryGetObject(root, "paragraph", out var paragraph))
            return false;

        var changed = false;
        var indentation = GetOrCreateObject(paragraph, "indentation", ref changed);

        changed |= MoveOrDefault(paragraph, indentation, "left_indent", CreateDecimalWrapper(0m));
        changed |= MoveOrDefault(paragraph, indentation, "right_indent", CreateDecimalWrapper(0m));

        return changed;
    }

    private static bool TransformParagraph(JsonObject root)
    {
        if (!TryGetObject(root, "paragraph", out var paragraph))
            return false;

        var changed = false;
        var indentation = GetOrCreateObject(paragraph, "indentation", ref changed);

        changed |= MoveOrDefault(paragraph, indentation, "left_indent", CreateDecimalWrapper(0m));
        changed |= MoveOrDefault(paragraph, indentation, "right_indent", CreateDecimalWrapper(0m));
        changed |= MoveOrDefault(paragraph, indentation, "first_line_indent", CreateDecimalWrapper(1.27m));

        return changed;
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName, ref bool changed)
    {
        if (parent[propertyName] is JsonObject existing && !IsWrapper(existing))
            return existing;

        var created = new JsonObject();
        parent[propertyName] = created;
        changed = true;
        return created;
    }

    private static bool MoveOrDefault(JsonObject sourceParent, JsonObject targetParent, string propertyName, JsonObject defaultValue)
    {
        var changed = false;

        if (targetParent[propertyName] == null)
        {
            if (sourceParent[propertyName] is JsonNode existingSource)
            {
                targetParent[propertyName] = existingSource.DeepClone();
                changed = true;
            }
            else
            {
                targetParent[propertyName] = defaultValue;
                changed = true;
            }
        }

        if (sourceParent[propertyName] != null)
        {
            sourceParent.Remove(propertyName);
            changed = true;
        }

        return changed;
    }

    private static JsonObject CreateDecimalWrapper(decimal value)
    {
        return new JsonObject
        {
            ["value"] = value,
            ["is_editable"] = true,
            ["is_hard_constraint"] = false
        };
    }

    private static bool TryGetObject(JsonObject source, string propertyName, out JsonObject value)
    {
        value = null!;
        if (source[propertyName] is not JsonObject objectValue || IsWrapper(objectValue))
            return false;

        value = objectValue;
        return true;
    }

    private static bool IsWrapper(JsonObject node)
    {
        JsonNode? ignored;
        return node.TryGetPropertyValue("value", out ignored);
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}

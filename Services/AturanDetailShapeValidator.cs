using System.Text.Json;
using System.Text.Json.Nodes;

namespace ValidasiTugasAkhir.MainService.Services;

public static class AturanDetailShapeValidator
{
    public static bool TryValidate(string? detailKey, string? normalizedJson, out string? errorMessage)
    {
        errorMessage = null;

        var key = NormalizeKey(detailKey);
        if (key is not ("judul_subbab" or "paragraf"))
            return true;

        if (string.IsNullOrWhiteSpace(normalizedJson))
            return true;

        try
        {
            if (JsonNode.Parse(normalizedJson) is not JsonObject root)
            {
                errorMessage = "json_value harus berupa object JSON.";
                return false;
            }

            return key switch
            {
                "judul_subbab" => TryValidateSubchapterShape(root, out errorMessage),
                "paragraf" => TryValidateParagraphShape(root, out errorMessage),
                _ => true
            };
        }
        catch (JsonException ex)
        {
            errorMessage = $"json_value tidak valid: {ex.Message}";
            return false;
        }
    }

    private static bool TryValidateSubchapterShape(JsonObject root, out string? errorMessage)
    {
        errorMessage = null;

        if (!TryGetObject(root, "paragraph", out var paragraph))
        {
            errorMessage = "judul_subbab harus memiliki object `paragraph`.";
            return false;
        }

        if (HasProperty(paragraph, "left_indent") || HasProperty(paragraph, "right_indent"))
        {
            errorMessage = "judul_subbab harus memakai `paragraph.indentation.left_indent/right_indent`, bukan `paragraph.left_indent/right_indent`.";
            return false;
        }

        if (!TryGetObject(paragraph, "indentation", out var indentation))
        {
            errorMessage = "judul_subbab harus memiliki object `paragraph.indentation`.";
            return false;
        }

        if (!IsWrapper(indentation["left_indent"]) || !IsWrapper(indentation["right_indent"]))
        {
            errorMessage = "judul_subbab harus memiliki wrapper `paragraph.indentation.left_indent` dan `paragraph.indentation.right_indent`.";
            return false;
        }

        return true;
    }

    private static bool TryValidateParagraphShape(JsonObject root, out string? errorMessage)
    {
        errorMessage = null;

        if (!TryGetObject(root, "paragraph", out var paragraph))
        {
            errorMessage = "paragraf harus memiliki object `paragraph`.";
            return false;
        }

        if (HasProperty(paragraph, "left_indent") || HasProperty(paragraph, "right_indent") || HasProperty(paragraph, "first_line_indent"))
        {
            errorMessage = "paragraf harus memakai `paragraph.indentation.left_indent/right_indent/first_line_indent`, bukan field flat di `paragraph`.";
            return false;
        }

        if (!TryGetObject(paragraph, "indentation", out var indentation))
        {
            errorMessage = "paragraf harus memiliki object `paragraph.indentation`.";
            return false;
        }

        if (!IsWrapper(indentation["left_indent"]) ||
            !IsWrapper(indentation["right_indent"]) ||
            !IsWrapper(indentation["first_line_indent"]))
        {
            errorMessage = "paragraf harus memiliki wrapper `paragraph.indentation.left_indent/right_indent/first_line_indent`.";
            return false;
        }

        return true;
    }

    private static bool TryGetObject(JsonObject source, string propertyName, out JsonObject value)
    {
        value = null!;
        if (source[propertyName] is not JsonObject objectValue || IsWrapper(objectValue))
            return false;

        value = objectValue;
        return true;
    }

    private static bool HasProperty(JsonObject source, string propertyName)
    {
        JsonNode? ignored;
        return source.TryGetPropertyValue(propertyName, out ignored);
    }

    private static bool IsWrapper(JsonNode? node)
    {
        JsonNode? ignored;
        return node is JsonObject jsonObject && jsonObject.TryGetPropertyValue("value", out ignored);
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}

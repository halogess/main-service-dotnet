using System.Text.Json;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// GeminiService partial - Response parsing methods
/// </summary>
public partial class GeminiService
{
    private static bool TryParseErrorGuidance(string response, out List<GeminiErrorDetail> results)
    {
        results = new List<GeminiErrorDetail>();
        if (string.IsNullOrWhiteSpace(response))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                TryGetPropertyCaseInsensitive(doc.RootElement, "errors", out var errorsProp) &&
                errorsProp.ValueKind == JsonValueKind.Array)
            {
                var structured = JsonSerializer.Deserialize<GeminiErrorGuidancePayload>(response, JsonReadOptions);
                if (structured?.Errors != null)
                {
                    results = structured.Errors;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to legacy parsing.
        }

        var payload = ExtractJsonPayload(response);
        var parsed = ParseErrorGuidanceJson(payload);
        if (parsed != null)
        {
            results = parsed;
            return true;
        }

        var repaired = TryRepairErrorGuidancePayload(payload ?? response);
        if (!string.IsNullOrWhiteSpace(repaired))
        {
            parsed = ParseErrorGuidanceJson(repaired);
            if (parsed != null)
            {
                results = parsed;
                return true;
            }
        }

        return false;
    }

    private static List<GeminiErrorDetail>? ParseErrorGuidanceJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            JsonElement errorsElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                errorsElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     TryGetPropertyCaseInsensitive(root, "errors", out var errorsProp) &&
                     errorsProp.ValueKind == JsonValueKind.Array)
            {
                errorsElement = errorsProp;
            }
            else
            {
                return null;
            }

            var results = new List<GeminiErrorDetail>();
            foreach (var item in errorsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var detail = new GeminiErrorDetail
                {
                    Index = ReadIndexValue(item),
                    Title = item.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                        ? titleEl.GetString() ?? string.Empty
                        : string.Empty,
                    Explanation = item.TryGetProperty("explanation", out var expEl) && expEl.ValueKind == JsonValueKind.String
                        ? expEl.GetString() ?? string.Empty
                        : string.Empty
                };

                if (item.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var step in stepsEl.EnumerateArray())
                    {
                        if (step.ValueKind == JsonValueKind.String)
                            detail.Steps.Add(step.GetString() ?? string.Empty);
                    }
                }

                if (item.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.Object)
                {
                    detail.Location = new GeminiErrorLocation
                    {
                        HalamanKe = locEl.TryGetProperty("halaman_ke", out var halamanEl) && halamanEl.TryGetInt32(out var halamanInt)
                            ? halamanInt
                            : null,
                        Section = locEl.TryGetProperty("section", out var sectionEl) && sectionEl.ValueKind == JsonValueKind.String
                            ? sectionEl.GetString()
                            : null
                    };
                }

                results.Add(detail);
            }

            return results;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadIndexValue(JsonElement item)
    {
        if (!item.TryGetProperty("index", out var idxEl))
            return -1;

        if (idxEl.ValueKind == JsonValueKind.Number && idxEl.TryGetInt32(out var idx))
            return idx;

        if (idxEl.ValueKind == JsonValueKind.String && int.TryParse(idxEl.GetString(), out var parsed))
            return parsed;

        return -1;
    }

    private static string? ExtractJsonPayload(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var trimmed = response.Trim();
        var objStart = trimmed.IndexOf('{');
        var arrStart = trimmed.IndexOf('[');
        if (objStart < 0 && arrStart < 0)
            return null;

        var start = objStart >= 0 && (arrStart < 0 || objStart < arrStart) ? objStart : arrStart;
        var end = start == objStart ? trimmed.LastIndexOf('}') : trimmed.LastIndexOf(']');
        if (end <= start)
            return null;

        return trimmed.Substring(start, end - start + 1);
    }

    private static string? TryRepairErrorGuidancePayload(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var errorsIndex = response.IndexOf("\"errors\"", StringComparison.OrdinalIgnoreCase);
        if (errorsIndex < 0)
            return null;

        var arrayStart = response.IndexOf('[', errorsIndex);
        if (arrayStart < 0)
            return null;

        var inString = false;
        var escape = false;
        var braceDepth = 0;
        int? lastObjectEnd = null;

        for (var i = arrayStart + 1; i < response.Length; i++)
        {
            var ch = response[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                braceDepth++;
                continue;
            }

            if (ch == '}')
            {
                if (braceDepth > 0)
                    braceDepth--;
                if (braceDepth == 0)
                    lastObjectEnd = i;
            }
        }

        if (!lastObjectEnd.HasValue)
            return null;

        var objectsSegment = response.Substring(arrayStart + 1, lastObjectEnd.Value - arrayStart);
        if (string.IsNullOrWhiteSpace(objectsSegment))
            return null;

        return "{\"errors\":[" + objectsSegment.Trim() + "]}";
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

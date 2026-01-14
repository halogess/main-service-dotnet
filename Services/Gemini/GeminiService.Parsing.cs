using System.Text.Json;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// GeminiService partial - Response parsing methods
/// </summary>
public partial class GeminiService
{
    private static List<GeminiSuggestion> ParseSuggestions(string response)
    {
        // Simple parsing - in production, consider structured output from Gemini
        var suggestions = new List<GeminiSuggestion>();
        
        // For now, return the full response as a single suggestion
        if (!string.IsNullOrEmpty(response))
        {
            suggestions.Add(new GeminiSuggestion
            {
                Category = "Analisis",
                Issue = "Hasil analisis dokumen",
                Recommendation = response
            });
        }

        return suggestions;
    }

    private static List<GeminiErrorDetail> ParseErrorGuidance(string response)
    {
        try
        {
            var structured = JsonSerializer.Deserialize<GeminiErrorGuidancePayload>(response, JsonReadOptions);
            if (structured?.Errors != null)
                return structured.Errors;
        }
        catch (JsonException)
        {
            // Fall through to legacy parsing.
        }

        var payload = ExtractJsonPayload(response);
        if (string.IsNullOrWhiteSpace(payload))
            return new List<GeminiErrorDetail>();

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
                     root.TryGetProperty("errors", out var errorsProp) &&
                     errorsProp.ValueKind == JsonValueKind.Array)
            {
                errorsElement = errorsProp;
            }
            else
            {
                return new List<GeminiErrorDetail>();
            }

            var results = new List<GeminiErrorDetail>();
            foreach (var item in errorsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var detail = new GeminiErrorDetail
                {
                    Index = item.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var idx) ? idx : -1,
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
            return new List<GeminiErrorDetail>();
        }
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
}

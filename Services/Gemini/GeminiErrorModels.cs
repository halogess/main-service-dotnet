using System.Text.Json.Serialization;

namespace ValidasiTugasAkhir.MainService.Services;

public class GeminiErrorDetail
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<string> Steps { get; set; } = new();
}

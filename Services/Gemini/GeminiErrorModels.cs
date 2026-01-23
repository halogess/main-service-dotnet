using System.Text.Json.Serialization;

namespace ValidasiTugasAkhir.MainService.Services;

public class GeminiErrorDetail
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("is_error")]
    public bool? IsError { get; set; }

    [JsonPropertyName("skip_reason")]
    public string? SkipReason { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<string> Steps { get; set; } = new();

    [JsonPropertyName("location")]
    public GeminiErrorLocation? Location { get; set; }
}

public class GeminiErrorLocation
{
    [JsonPropertyName("halaman_ke")]
    public int? HalamanKe { get; set; }

    [JsonPropertyName("section")]
    public string? Section { get; set; }
}

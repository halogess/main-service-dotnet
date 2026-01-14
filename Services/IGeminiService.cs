using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IGeminiService
{
    Task<string> GenerateRecommendationAsync(string prompt);
    Task<GeminiAnalysisResult> AnalyzeDocumentAsync(int dokumenId, string documentContent, List<string>? errors = null);
    Task<List<GeminiErrorDetail>> GenerateErrorGuidanceAsync(
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<AturanDetail> activeRules,
        CancellationToken cancellationToken = default);
}

public class GeminiAnalysisResult
{
    public bool Success { get; set; }
    public string? Recommendation { get; set; }
    public string? ErrorMessage { get; set; }
    public List<GeminiSuggestion> Suggestions { get; set; } = new();
}

public class GeminiSuggestion
{
    public string Category { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public class GeminiErrorDetail
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new();
}

using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IGeminiService
{
    Task<List<GeminiErrorDetail>> GenerateErrorGuidanceAsync(
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<AturanDetail> activeRules,
        CancellationToken cancellationToken = default,
        uint? antrianId = null,
        int? batchNumber = null,
        int? totalBatches = null);
}

public class GeminiErrorDetail
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new();
    public GeminiErrorLocation? Location { get; set; }
}

public class GeminiErrorLocation
{
    public int? HalamanKe { get; set; }
    public string? Section { get; set; }
}

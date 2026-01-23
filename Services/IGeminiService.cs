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
        int? totalBatches = null,
        uint? dokumenId = null);
}

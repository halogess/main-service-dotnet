using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private async Task<IReadOnlyDictionary<string, AturanDetail>> LoadCanonicalDetailsAsync(
        uint aturanId,
        CancellationToken cancellationToken,
        params string[] keys)
    {
        var normalizedKeys = keys
            .Where(AturanDetailContract.IsCanonicalKey)
            .Select(key => AturanDetailContract.NormalizeCanonicalKey(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalizedKeys.Count == 0)
            return new Dictionary<string, AturanDetail>(StringComparer.OrdinalIgnoreCase);

        var rawDetails = AturanDetailVisibility.FilterVisible(await _db.AturanDetails
            .Where(detail => detail.AturanId == aturanId)
            .ToListAsync(cancellationToken));

        return AturanDetailContract.BuildDetailMap(rawDetails
            .Where(detail => normalizedKeys.Contains((detail.AturanDetailKey ?? string.Empty).Trim().ToLowerInvariant())));
    }
}

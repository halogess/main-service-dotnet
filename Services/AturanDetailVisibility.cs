using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

internal static class AturanDetailVisibility
{
    private static readonly HashSet<string> HiddenRuleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "daftar_pustaka"
    };

    public static bool IsVisible(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return true;

        return !HiddenRuleKeys.Contains(key.Trim());
    }

    public static List<AturanDetail> FilterVisible(IEnumerable<AturanDetail> details)
    {
        return details
            .Where(detail => IsVisible(detail.AturanDetailKey))
            .ToList();
    }
}

using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public sealed record BukuDeletionEligibility(
    bool CanDelete,
    bool HasFailedBab,
    string? DeleteBlockReason);

public static class BukuHistoryDeletionPolicy
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "lolos",
        "tidak_lolos",
        "dibatalkan"
    };

    public static BukuDeletionEligibility Evaluate(string? bukuStatus, IEnumerable<Antrian>? queues)
    {
        var queueList = queues?.ToList() ?? [];
        var hasFailedBab = queueList.Any(HasFailedState);
        if (hasFailedBab)
            return new BukuDeletionEligibility(true, true, null);

        var normalizedStatus = (bukuStatus ?? string.Empty).Trim();
        if (TerminalStatuses.Contains(normalizedStatus))
            return new BukuDeletionEligibility(true, false, null);

        if (string.Equals(normalizedStatus, "dalam_antrian", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedStatus, "diproses", StringComparison.OrdinalIgnoreCase))
        {
            return new BukuDeletionEligibility(
                false,
                false,
                "Validasi buku masih dalam antrian atau sedang diproses.");
        }

        return new BukuDeletionEligibility(
            false,
            false,
            "Status buku belum terminal dan tidak dapat dihapus.");
    }

    public static bool HasFailedState(Antrian queue)
    {
        return string.Equals(queue.AntrianExtractionStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(queue.AntrianLabelingStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(queue.AntrianValidationStatus, "failed", StringComparison.OrdinalIgnoreCase);
    }
}

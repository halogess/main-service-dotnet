using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public static class QueueCancellationHelper
{
    public const string CancelledByUserMessage = "Dibatalkan oleh pengguna.";

    public static bool HasFailedStage(Antrian? queue)
    {
        if (queue == null)
            return false;

        return IsFailedStage(queue.AntrianExtractionStatus) ||
               IsFailedStage(queue.AntrianLabelingStatus) ||
               IsFailedStage(queue.AntrianValidationStatus);
    }

    public static bool ClearActiveStages(Antrian queue, string? errorMessage = null, DateTime? updatedAt = null)
    {
        var changed = false;

        if (IsActiveStage(queue.AntrianExtractionStatus))
        {
            queue.AntrianExtractionStatus = null;
            changed = true;
        }

        if (IsActiveStage(queue.AntrianLabelingStatus))
        {
            queue.AntrianLabelingStatus = null;
            changed = true;
        }

        if (IsActiveStage(queue.AntrianValidationStatus))
        {
            queue.AntrianValidationStatus = null;
            changed = true;
        }

        if (!changed)
            return false;

        queue.AntrianErrorMessage = Truncate(errorMessage ?? CancelledByUserMessage, 255);
        queue.AntrianUpdatedAt = updatedAt ?? AppClock.Now;
        return true;
    }

    public static async Task<bool> TryHandleCancelledResourceAsync(
        KorektorBukuDbContext db,
        Antrian queue,
        CancellationToken cancellationToken)
    {
        if (!await IsResourceCancelledAsync(db, queue, cancellationToken))
            return false;

        if (!ClearActiveStages(queue))
            return true;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> IsResourceCancelledAsync(
        KorektorBukuDbContext db,
        Antrian queue,
        CancellationToken cancellationToken)
    {
        if (string.Equals(queue.AntrianTipe, "dokumen", StringComparison.OrdinalIgnoreCase) &&
            queue.DokumenId.HasValue)
        {
            var dokumen = await db.Dokumens
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DokumenId == (int)queue.DokumenId.Value, cancellationToken);

            return string.Equals(dokumen?.DokumenStatus, "dibatalkan", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(queue.AntrianTipe, "buku", StringComparison.OrdinalIgnoreCase))
        {
            var bukuId = queue.BukuId;
            if (!bukuId.HasValue && queue.BabId.HasValue)
            {
                bukuId = await db.Babs
                    .AsNoTracking()
                    .Where(b => b.BabId == queue.BabId.Value)
                    .Select(b => b.BukuId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (!bukuId.HasValue)
                return false;

            var buku = await db.Bukus
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BukuId == (int)bukuId.Value, cancellationToken);

            return string.Equals(buku?.BukuStatus, "dibatalkan", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsActiveStage(string? status)
        => string.Equals(status, "in_queue", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "processing", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailedStage(string? status)
        => string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}

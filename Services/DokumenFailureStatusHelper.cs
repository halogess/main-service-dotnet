namespace ValidasiTugasAkhir.MainService.Services;

public readonly record struct DokumenFailureNotification(string Nrp, int DokumenId);

public static class DokumenFailureStatusHelper
{
    public static async Task<DokumenFailureNotification?> TryMarkDokumenTidakLolosAsync(
        KorektorBukuDbContext db,
        uint? dokumenId,
        CancellationToken cancellationToken)
    {
        if (!dokumenId.HasValue)
            return null;

        var dokumen = await db.Dokumens.FindAsync(new object[] { (int)dokumenId.Value }, cancellationToken);
        if (dokumen == null ||
            string.Equals(dokumen.DokumenStatus, "dibatalkan", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var shouldNotify = !string.Equals(dokumen.DokumenStatus, "tidak_lolos", StringComparison.OrdinalIgnoreCase);

        dokumen.DokumenStatus = "tidak_lolos";
        dokumen.DokumenSkor = null;
        dokumen.DokumenSkorMinimal = null;
        dokumen.DokumenJumlahKesalahan = null;
        dokumen.DokumenReportPath = null;
        dokumen.DokumenUpdatedAt = DateTime.Now;

        return shouldNotify
            ? new DokumenFailureNotification(dokumen.MhsNrp, dokumen.DokumenId)
            : null;
    }
}

namespace ValidasiTugasAkhir.MainService.Services;

public readonly record struct BukuFailureNotification(string Nrp, int BukuId);

public static class BukuFailureStatusHelper
{
    public static async Task<BukuFailureNotification?> TryMarkBukuTidakLolosAsync(
        KorektorBukuDbContext db,
        uint? bukuId,
        CancellationToken cancellationToken)
    {
        if (!bukuId.HasValue)
            return null;

        var buku = await db.Bukus.FindAsync(new object[] { (int)bukuId.Value }, cancellationToken);
        if (buku == null ||
            string.Equals(buku.BukuStatus, "dibatalkan", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var shouldNotify = !string.Equals(buku.BukuStatus, "tidak_lolos", StringComparison.OrdinalIgnoreCase);

        buku.BukuStatus = "tidak_lolos";
        buku.BukuSkor = null;
        buku.BukuJumlahKesalahan = null;
        buku.BukuReportPath = null;
        buku.BukuUpdatedAt = DateTime.Now;

        return shouldNotify
            ? new BukuFailureNotification(buku.MhsNrp, buku.BukuId)
            : null;
    }
}

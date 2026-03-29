namespace ValidasiTugasAkhir.MainService.Services;

public static class AturanStatusValues
{
    public const string Diproses = "diproses";
    public const string MenungguReview = "menunggu_review";
    public const string TidakAktif = "tidak_aktif";
    public const string Aktif = "aktif";
    public const string Gagal = "gagal";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Diproses,
            MenungguReview,
            TidakAktif,
            Aktif,
            Gagal
        };
}

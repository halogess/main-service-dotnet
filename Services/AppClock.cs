namespace ValidasiTugasAkhir.MainService.Services;

public static class AppClock
{
    private static readonly TimeSpan UtcPlusSevenOffset = TimeSpan.FromHours(7);

    public static DateTime Now =>
        DateTime.SpecifyKind(DateTime.UtcNow.Add(UtcPlusSevenOffset), DateTimeKind.Unspecified);
}

using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;

namespace _.Services;

public class AdobeQuotaResetService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AdobeQuotaResetService> _logger;

    public AdobeQuotaResetService(IServiceProvider serviceProvider, ILogger<AdobeQuotaResetService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunStartupCatchUpResetAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = AppClock.Now;
            var nextReset = GetNextQuotaReset(now);
            var delay = nextReset - now;

            _logger.LogInformation("Next Adobe quota reset: {NextReset}", nextReset);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var resetCount = await ResetQuotaIfNeededAsync(AppClock.Now, stoppingToken);
                _logger.LogInformation("Adobe quota reset completed for {Count} credentials", resetCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting Adobe quota");
            }
        }
    }

    private async Task RunStartupCatchUpResetAsync(CancellationToken stoppingToken)
    {
        try
        {
            var now = AppClock.Now;
            var resetCount = await ResetQuotaIfNeededAsync(now, stoppingToken);
            if (resetCount == 0)
            {
                _logger.LogInformation(
                    "Adobe quota catch-up reset not needed at startup for period {PeriodStart}",
                    GetCurrentQuotaPeriodStart(now));
                return;
            }

            _logger.LogInformation(
                "Adobe quota catch-up reset completed at startup for {Count} credentials",
                resetCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing Adobe quota catch-up reset at startup");
        }
    }

    private async Task<int> ResetQuotaIfNeededAsync(DateTime now, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
        var currentPeriodStart = GetCurrentQuotaPeriodStart(now);

        var credentials = await dbContext.AdobeCredentials.ToListAsync(stoppingToken);
        var credentialsToReset = credentials
            .Where(cred => ShouldResetQuotaForCurrentPeriod(cred, currentPeriodStart))
            .ToList();

        if (credentialsToReset.Count == 0)
            return 0;

        foreach (var cred in credentialsToReset)
        {
            cred.AdobeCredentialsQuotaUsed = 0;
            cred.AdobeCredentialsResetDate = now;
            cred.AdobeCredentialsUpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(stoppingToken);
        return credentialsToReset.Count;
    }

    private static DateTime GetCurrentQuotaPeriodStart(DateTime now)
        => new(now.Year, now.Month, 1);

    private static DateTime GetNextQuotaReset(DateTime now)
        => GetCurrentQuotaPeriodStart(now).AddMonths(1);

    private static bool ShouldResetQuotaForCurrentPeriod(AdobeCredential credential, DateTime currentPeriodStart)
    {
        if (credential.AdobeCredentialsResetDate.HasValue)
            return credential.AdobeCredentialsResetDate.Value < currentPeriodStart;

        var lastKnownActivity = credential.AdobeCredentialsUpdatedAt ?? credential.AdobeCredentialsCreatedAt;
        if (lastKnownActivity.HasValue)
            return lastKnownActivity.Value < currentPeriodStart;

        return credential.AdobeCredentialsQuotaUsed > 0;
    }
}

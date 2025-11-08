using Microsoft.EntityFrameworkCore;

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
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextReset = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            var delay = nextReset - now;

            _logger.LogInformation("Next Adobe quota reset: {NextReset}", nextReset);

            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();

                var credentials = await dbContext.AdobeCredentials.ToListAsync(stoppingToken);

                foreach (var cred in credentials)
                {
                    cred.AdobeCredentialsQuotaUsed = 0;
                    cred.AdobeCredentialsResetDate = DateTime.Now;
                }

                await dbContext.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Adobe quota reset completed for {Count} credentials", credentials.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting Adobe quota");
            }
        }
    }
}

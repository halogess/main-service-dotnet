using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using Xunit;
using _.Services;

namespace Tests;

public class AdobeQuotaResetServiceTests
{
    [Fact]
    public async Task ResetQuotaIfNeededAsync_ShouldResetOnlyCredentialsFromPreviousMonth()
    {
        using var provider = BuildServiceProvider();
        var now = new DateTime(2026, 4, 12, 10, 15, 0);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
            db.AdobeCredentials.AddRange(
                new AdobeCredential
                {
                    AdobeCredentialsId = 1,
                    AdobeClientId = "client-old",
                    AdobeClientSecret = "secret-old",
                    AdobeCredentialsQuotaUsed = 12,
                    AdobeCredentialsQuotaLimit = 500,
                    AdobeCredentialsResetDate = new DateTime(2026, 3, 1, 0, 0, 0),
                    AdobeCredentialsCreatedAt = new DateTime(2026, 2, 10, 8, 0, 0),
                    AdobeCredentialsUpdatedAt = new DateTime(2026, 3, 20, 9, 0, 0)
                },
                new AdobeCredential
                {
                    AdobeCredentialsId = 2,
                    AdobeClientId = "client-current",
                    AdobeClientSecret = "secret-current",
                    AdobeCredentialsQuotaUsed = 7,
                    AdobeCredentialsQuotaLimit = 500,
                    AdobeCredentialsResetDate = new DateTime(2026, 4, 1, 0, 5, 0),
                    AdobeCredentialsCreatedAt = new DateTime(2026, 3, 15, 8, 0, 0),
                    AdobeCredentialsUpdatedAt = new DateTime(2026, 4, 2, 11, 0, 0)
                });
            await db.SaveChangesAsync();
        }

        var service = new AdobeQuotaResetService(provider, NullLogger<AdobeQuotaResetService>.Instance);
        var resetCount = await InvokeResetQuotaIfNeededAsync(service, now);

        Assert.Equal(1, resetCount);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
        var oldCredential = await verifyDb.AdobeCredentials.SingleAsync(c => c.AdobeCredentialsId == 1);
        var currentCredential = await verifyDb.AdobeCredentials.SingleAsync(c => c.AdobeCredentialsId == 2);

        Assert.Equal(0, oldCredential.AdobeCredentialsQuotaUsed);
        Assert.Equal(now, oldCredential.AdobeCredentialsResetDate);
        Assert.Equal(now, oldCredential.AdobeCredentialsUpdatedAt);

        Assert.Equal(7, currentCredential.AdobeCredentialsQuotaUsed);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 5, 0), currentCredential.AdobeCredentialsResetDate);
        Assert.Equal(new DateTime(2026, 4, 2, 11, 0, 0), currentCredential.AdobeCredentialsUpdatedAt);
    }

    [Fact]
    public async Task ResetQuotaIfNeededAsync_ShouldUseLastKnownActivityWhenResetDateMissing()
    {
        using var provider = BuildServiceProvider();
        var now = new DateTime(2026, 4, 12, 10, 15, 0);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
            db.AdobeCredentials.AddRange(
                new AdobeCredential
                {
                    AdobeCredentialsId = 1,
                    AdobeClientId = "client-stale",
                    AdobeClientSecret = "secret-stale",
                    AdobeCredentialsQuotaUsed = 9,
                    AdobeCredentialsQuotaLimit = 500,
                    AdobeCredentialsCreatedAt = new DateTime(2026, 3, 19, 8, 0, 0),
                    AdobeCredentialsUpdatedAt = new DateTime(2026, 3, 25, 14, 30, 0)
                },
                new AdobeCredential
                {
                    AdobeCredentialsId = 2,
                    AdobeClientId = "client-fresh",
                    AdobeClientSecret = "secret-fresh",
                    AdobeCredentialsQuotaUsed = 4,
                    AdobeCredentialsQuotaLimit = 500,
                    AdobeCredentialsCreatedAt = new DateTime(2026, 4, 2, 8, 0, 0),
                    AdobeCredentialsUpdatedAt = new DateTime(2026, 4, 5, 9, 0, 0)
                });
            await db.SaveChangesAsync();
        }

        var service = new AdobeQuotaResetService(provider, NullLogger<AdobeQuotaResetService>.Instance);
        var resetCount = await InvokeResetQuotaIfNeededAsync(service, now);

        Assert.Equal(1, resetCount);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
        var staleCredential = await verifyDb.AdobeCredentials.SingleAsync(c => c.AdobeCredentialsId == 1);
        var freshCredential = await verifyDb.AdobeCredentials.SingleAsync(c => c.AdobeCredentialsId == 2);

        Assert.Equal(0, staleCredential.AdobeCredentialsQuotaUsed);
        Assert.Equal(now, staleCredential.AdobeCredentialsResetDate);

        Assert.Equal(4, freshCredential.AdobeCredentialsQuotaUsed);
        Assert.Null(freshCredential.AdobeCredentialsResetDate);
        Assert.Equal(new DateTime(2026, 4, 5, 9, 0, 0), freshCredential.AdobeCredentialsUpdatedAt);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<KorektorBukuDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static async Task<int> InvokeResetQuotaIfNeededAsync(AdobeQuotaResetService service, DateTime now)
    {
        var method = typeof(AdobeQuotaResetService).GetMethod(
            "ResetQuotaIfNeededAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = method!.Invoke(service, [now, CancellationToken.None]);
        var typedTask = Assert.IsAssignableFrom<Task<int>>(task);
        return await typedTask;
    }
}

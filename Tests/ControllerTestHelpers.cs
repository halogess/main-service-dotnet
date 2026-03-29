using Microsoft.EntityFrameworkCore;

namespace Tests;

internal static class ControllerTestHelpers
{
    public static KorektorBukuDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new KorektorBukuDbContext(options);
    }

    public static SttsDbContext CreateSttsDbContext()
    {
        var options = new DbContextOptionsBuilder<SttsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SttsDbContext(options);
    }
}

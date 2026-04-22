using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

[Collection("storage-path")]
public sealed class DokumenVisualOwnershipCleanupTests
{
    [Fact]
    public async Task ExecuteCleanupAsync_ShouldDeleteOnlyVisualsOwnedByExpiredDokumen()
    {
        using var storageScope = new StoragePathScope();
        await using var connection = await OpenSqliteConnectionAsync();
        using var provider = BuildServiceProvider(connection);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedDokumenGraph(db, 1, "05111740000111", DateTime.Now.AddDays(-40), 11, 101, 1001);
            SeedDokumenGraph(db, 2, "05111740000222", DateTime.Now.AddDays(-5), 22, 202, 2002);

            db.DokumenElemenVisuals.AddRange(
                new DokumenElemenVisual { DevId = 9001, DevRefTipe = "dokumen", DevRefId = 1 },
                new DokumenElemenVisual { DevId = 9002, DevRefTipe = "dokumen", DevRefId = 2 },
                new DokumenElemenVisual { DevId = 9003, DevRefTipe = "dokumen", DevRefId = 999, DokumenElemenId = 1001 },
                new DokumenElemenVisual { DevId = 9004, DevRefTipe = "dokumen", DevRefId = 9999 });

            await db.SaveChangesAsync();
        }

        var service = new DokumenAutoDeleteService(provider, NullLogger<DokumenAutoDeleteService>.Instance);
        var deletedCount = await service.ExecuteCleanupAsync();

        Assert.Equal(1, deletedCount);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();

        Assert.Null(await verifyDb.Dokumens.FindAsync(1));
        Assert.NotNull(await verifyDb.Dokumens.FindAsync(2));

        var remainingVisualIds = await verifyDb.DokumenElemenVisuals
            .Select(visual => visual.DevId)
            .ToListAsync();
        remainingVisualIds.Sort();

        Assert.Equal([9002UL, 9004UL], remainingVisualIds);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_ShouldDeleteExpiredDokumenRegardlessOfQueueStatus()
    {
        using var storageScope = new StoragePathScope();
        await using var connection = await OpenSqliteConnectionAsync();
        using var provider = BuildServiceProvider(connection);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedDokumenGraph(db, 11, "05111740000111", DateTime.Now.AddDays(-40), 111, 1111, 11111);
            SeedDokumenGraph(db, 12, "05111740000112", DateTime.Now.AddDays(-40), 122, 1222, 12222);
            SeedDokumenGraph(db, 13, "05111740000113", DateTime.Now.AddDays(-40), 133, 1333, 13333);
            SeedDokumenGraph(db, 14, "05111740000114", DateTime.Now.AddDays(-5), 144, 1444, 14444);

            db.Antrians.AddRange(
                new Antrian
                {
                    AntrianId = 5011,
                    AntrianTipe = "dokumen",
                    DokumenId = 11,
                    AntrianExtractionStatus = "in_queue",
                    AntrianCreatedAt = DateTime.Now.AddDays(-40),
                    AntrianUpdatedAt = DateTime.Now.AddDays(-40)
                },
                new Antrian
                {
                    AntrianId = 5012,
                    AntrianTipe = "dokumen",
                    DokumenId = 12,
                    AntrianExtractionStatus = "processing",
                    AntrianLabelingStatus = "processing",
                    AntrianCreatedAt = DateTime.Now.AddDays(-40),
                    AntrianUpdatedAt = DateTime.Now.AddDays(-40)
                },
                new Antrian
                {
                    AntrianId = 5013,
                    AntrianTipe = "dokumen",
                    DokumenId = 13,
                    AntrianExtractionStatus = "completed",
                    AntrianLabelingStatus = "completed",
                    AntrianValidationStatus = "completed",
                    AntrianCreatedAt = DateTime.Now.AddDays(-40),
                    AntrianUpdatedAt = DateTime.Now.AddDays(-40)
                },
                new Antrian
                {
                    AntrianId = 5014,
                    AntrianTipe = "dokumen",
                    DokumenId = 14,
                    AntrianExtractionStatus = "processing",
                    AntrianCreatedAt = DateTime.Now.AddDays(-5),
                    AntrianUpdatedAt = DateTime.Now.AddDays(-5)
                });

            await db.SaveChangesAsync();
        }

        var service = new DokumenAutoDeleteService(provider, NullLogger<DokumenAutoDeleteService>.Instance);
        var deletedCount = await service.ExecuteCleanupAsync();

        Assert.Equal(3, deletedCount);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();

        Assert.Null(await verifyDb.Dokumens.FindAsync(11));
        Assert.Null(await verifyDb.Dokumens.FindAsync(12));
        Assert.Null(await verifyDb.Dokumens.FindAsync(13));
        Assert.NotNull(await verifyDb.Dokumens.FindAsync(14));

        var remainingQueues = await verifyDb.Antrians.ToListAsync();
        Assert.DoesNotContain(remainingQueues, queue => queue.DokumenId == 11u || queue.DokumenId == 12u || queue.DokumenId == 13u);
        Assert.Contains(remainingQueues, queue => queue.DokumenId == 14u);
    }

    [Fact]
    public async Task PurgeAllAsync_ShouldPreserveOrphanDokumenVisuals()
    {
        using var storageScope = new StoragePathScope();
        await using var connection = await OpenSqliteConnectionAsync();
        var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new KorektorBukuDbContext(options);
        await db.Database.EnsureCreatedAsync();

        SeedDokumenGraph(db, 1, "05111740000111", DateTime.Now.AddDays(-60), 11, 101, 1001);
        SeedDokumenGraph(db, 2, "05111740000222", DateTime.Now.AddDays(-20), 22, 202, 2002);

        db.DokumenElemenVisuals.AddRange(
            new DokumenElemenVisual { DevId = 9101, DevRefTipe = "dokumen", DevRefId = 1 },
            new DokumenElemenVisual { DevId = 9102, DevRefTipe = "dokumen", DevRefId = 2 },
            new DokumenElemenVisual { DevId = 9103, DevRefTipe = "dokumen", DevRefId = 999, DokumenElemenId = 1001 },
            new DokumenElemenVisual { DevId = 9104, DokumenElemenId = 2002 },
            new DokumenElemenVisual { DevId = 9105, DevRefTipe = "dokumen", DevRefId = 9999 });

        await db.SaveChangesAsync();

        var service = new DokumenHistoryPurgeService(db, NullLogger<DokumenHistoryPurgeService>.Instance);
        var result = await service.PurgeAllAsync();

        Assert.Equal(2, result.DeletedDokumen);
        Assert.Equal(4, result.DeletedVisual);

        var remainingVisuals = await db.DokumenElemenVisuals
            .AsNoTracking()
            .Select(visual => new { visual.DevId, visual.DevRefId, visual.DokumenElemenId })
            .ToListAsync();
        remainingVisuals = remainingVisuals
            .OrderBy(visual => visual.DevId)
            .ToList();

        Assert.Single(remainingVisuals);
        Assert.Equal((ulong)9105, remainingVisuals[0].DevId);
        Assert.Equal((uint?)9999, remainingVisuals[0].DevRefId);
        Assert.Null(remainingVisuals[0].DokumenElemenId);
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldExcludeOrphanDokumenVisualsFromVisualCount()
    {
        using var storageScope = new StoragePathScope();
        await using var connection = await OpenSqliteConnectionAsync();
        var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new KorektorBukuDbContext(options);
        await db.Database.EnsureCreatedAsync();

        SeedDokumenGraph(db, 1, "05111740000111", DateTime.Now.AddDays(-60), 11, 101, 1001);
        SeedDokumenGraph(db, 2, "05111740000222", DateTime.Now.AddDays(-20), 22, 202, 2002);

        db.DokumenElemenVisuals.AddRange(
            new DokumenElemenVisual { DevId = 9201, DevRefTipe = "dokumen", DevRefId = 1 },
            new DokumenElemenVisual { DevId = 9202, DevRefTipe = "dokumen", DevRefId = 2 },
            new DokumenElemenVisual { DevId = 9203, DevRefTipe = "dokumen", DevRefId = 999, DokumenElemenId = 1001 },
            new DokumenElemenVisual { DevId = 9204, DevRefTipe = "dokumen", DevRefId = 9999 });

        await db.SaveChangesAsync();

        var service = new DokumenHistoryPurgeService(db, NullLogger<DokumenHistoryPurgeService>.Instance);
        var summary = await service.GetSummaryAsync();

        Assert.Equal(2, summary.TotalDokumen);
        Assert.Equal(3, summary.TotalVisual);
    }

    private static ServiceProvider BuildServiceProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<KorektorBukuDbContext>(options => options.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    private static async Task<SqliteConnection> OpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static void SeedDokumenGraph(
        KorektorBukuDbContext db,
        int dokumenId,
        string nrp,
        DateTime createdAt,
        uint sectionId,
        uint partId,
        ulong elementId)
    {
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = dokumenId,
            MhsNrp = nrp,
            DokumenFilename = $"dokumen-{dokumenId}.docx",
            DokumenStatus = "lolos",
            DokumenCreatedAt = createdAt,
            DokumenUpdatedAt = createdAt
        });

        db.DokumenSections.Add(new DokumenSection
        {
            DsecId = sectionId,
            DsecRefTipe = "dokumen",
            DsecRefId = (uint)dokumenId,
            DsecIndex = 0
        });

        db.DokumenParts.Add(new DokumenPart
        {
            DpartId = partId,
            DsecId = sectionId,
            DpartType = "body"
        });

        db.DokumenElemens.Add(new DokumenElemen
        {
            DelemenId = elementId,
            DpartId = partId,
            DelemenSequence = 1,
            DelemenType = "paragraph",
            DelemenJsonTree = """{"text":"isi"}""",
            DelemenXml = "<w:p />"
        });
    }

    private sealed class StoragePathScope : IDisposable
    {
        private readonly string? _previousStoragePath;

        public StoragePathScope()
        {
            _previousStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
            StoragePath = Path.Combine(Path.GetTempPath(), $"dokumen-visual-ownership-{Guid.NewGuid():N}");
            Directory.CreateDirectory(StoragePath);
            Environment.SetEnvironmentVariable("STORAGE_PATH", StoragePath);
        }

        public string StoragePath { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", _previousStoragePath);

            try
            {
                if (Directory.Exists(StoragePath))
                    Directory.Delete(StoragePath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temporary test folders.
            }
        }
    }
}

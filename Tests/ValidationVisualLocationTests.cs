using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ValidationVisualLocationTests
{
    [Fact]
    public async Task LoadPageBboxMapAsync_ShouldMatchForLegacyAndPreMergedSamePageRows()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddVisual(devId: 1, elementId: 1001, page: 7, bbox: [10f, 10f, 30f, 20f]);
        fixture.AddVisual(devId: 2, elementId: 1001, page: 7, bbox: [10f, 20f, 45f, 50f]);
        fixture.AddVisual(devId: 3, elementId: 1002, page: 7, bbox: [10f, 10f, 45f, 50f]);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);

        var legacyMap = await InvokeValidationServiceAsync<Dictionary<int, ErrorBbox>>(
            service,
            "LoadPageBboxMapAsync",
            new ulong[] { 1001 },
            CancellationToken.None);
        var preMergedMap = await InvokeValidationServiceAsync<Dictionary<int, ErrorBbox>>(
            service,
            "LoadPageBboxMapAsync",
            new ulong[] { 1002 },
            CancellationToken.None);

        AssertBboxMapEqual(legacyMap, preMergedMap);
        Assert.Single(legacyMap);
        Assert.True(legacyMap.TryGetValue(7, out var bbox));
        Assert.NotNull(bbox);
        AssertBboxEqual(bbox!, 10m, 10m, 45m, 50m);
    }

    [Fact]
    public async Task CreateLocations_ShouldReturnTwoPagesForOneElementWhenPageBboxMapHasTwoPages()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddVisual(devId: 10, elementId: 2001, page: 2, bbox: [5f, 5f, 25f, 25f]);
        fixture.AddVisual(devId: 11, elementId: 2001, page: 3, bbox: [6f, 6f, 30f, 35f]);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var pageNumbers = await InvokeValidationServiceAsync<Dictionary<ulong, int>>(
            service,
            "LoadPageNumbersAsync",
            new ulong[] { 2001 },
            CancellationToken.None);
        var pageBboxMap = await InvokeValidationServiceAsync<Dictionary<int, ErrorBbox>>(
            service,
            "LoadPageBboxMapAsync",
            new ulong[] { 2001 },
            CancellationToken.None);

        var locations = ValidationServiceProxy.CallCreateLocations(pageNumbers.Values, pageBboxMap);

        Assert.Equal([2, 3], locations.Select(loc => loc.HalamanKe).ToArray());
        Assert.NotNull(locations[0].Bbox);
        Assert.NotNull(locations[1].Bbox);
        AssertBboxEqual(locations[0].Bbox!, 5m, 5m, 25m, 25m);
        AssertBboxEqual(locations[1].Bbox!, 6m, 6m, 30m, 35m);
    }

    [Fact]
    public async Task LoadPageNumbersAsync_ShouldKeepEarliestPageForRepeatedElement()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddVisual(devId: 20, elementId: 3001, page: 2, bbox: [1f, 1f, 2f, 2f]);
        fixture.AddVisual(devId: 21, elementId: 3001, page: 4, bbox: [1f, 1f, 2f, 2f]);
        fixture.AddVisual(devId: 22, elementId: 3001, page: 5, bbox: [1f, 1f, 2f, 2f]);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var pageNumbers = await InvokeValidationServiceAsync<Dictionary<ulong, int>>(
            service,
            "LoadPageNumbersAsync",
            new ulong[] { 3001 },
            CancellationToken.None);

        Assert.True(pageNumbers.TryGetValue(3001, out var page));
        Assert.Equal(2, page);
    }

    [Fact]
    public async Task LoadPageNumbersByElementIdAsync_ShouldKeepEarliestPageForRepeatedElement()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddVisual(devId: 30, elementId: 4001, page: 3, bbox: [1f, 1f, 3f, 3f], refType: "dokumen", refId: 77);
        fixture.AddVisual(devId: 31, elementId: 4001, page: 6, bbox: [1f, 1f, 3f, 3f], refType: "dokumen", refId: 77);
        fixture.AddVisual(devId: 32, elementId: 4001, page: 8, bbox: [1f, 1f, 3f, 3f], refType: "dokumen", refId: 77);
        fixture.AddVisual(devId: 33, elementId: 4001, page: 1, bbox: [1f, 1f, 3f, 3f], refType: "bab", refId: 77);
        await fixture.Db.SaveChangesAsync();

        var service = new ValidationQueueBackgroundService(
            Mock.Of<IServiceProvider>(),
            NullLogger<ValidationQueueBackgroundService>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        var pageNumbers = await InvokeQueueBackgroundServiceAsync<Dictionary<ulong, int>>(
            service,
            "LoadPageNumbersByElementIdAsync",
            fixture.Db,
            "dokumen",
            77u,
            new ulong[] { 4001 },
            CancellationToken.None);

        Assert.True(pageNumbers.TryGetValue(4001, out var page));
        Assert.Equal(3, page);
    }

    private static ValidationService CreateValidationService(KorektorBukuDbContext db)
        => new(db, NullLogger<ValidationService>.Instance);

    private static async Task<T> InvokeValidationServiceAsync<T>(
        ValidationService service,
        string methodName,
        params object[] args)
    {
        var method = typeof(ValidationService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return await InvokeAsync<T>(method!, service, args);
    }

    private static async Task<T> InvokeQueueBackgroundServiceAsync<T>(
        ValidationQueueBackgroundService service,
        string methodName,
        params object[] args)
    {
        var method = typeof(ValidationQueueBackgroundService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return await InvokeAsync<T>(method!, service, args);
    }

    private static async Task<T> InvokeAsync<T>(MethodInfo method, object target, object[] args)
    {
        var task = method.Invoke(target, args) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        var value = resultProperty!.GetValue(task);
        return Assert.IsType<T>(value);
    }

    private static void AssertBboxMapEqual(
        IReadOnlyDictionary<int, ErrorBbox> expected,
        IReadOnlyDictionary<int, ErrorBbox> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(key => key), actual.Keys.OrderBy(key => key));
        foreach (var (page, bbox) in expected)
        {
            Assert.True(actual.TryGetValue(page, out var actualBbox));
            Assert.NotNull(actualBbox);
            AssertBboxEqual(actualBbox!, bbox.X0, bbox.Y0, bbox.X1, bbox.Y1);
        }
    }

    private static void AssertBboxEqual(ErrorBbox bbox, decimal x0, decimal y0, decimal x1, decimal y1)
    {
        Assert.Equal(x0, bbox.X0);
        Assert.Equal(y0, bbox.Y0);
        Assert.Equal(x1, bbox.X1);
        Assert.Equal(y1, bbox.Y1);
    }

    private sealed class ValidationServiceProxy : ValidationService
    {
        public ValidationServiceProxy(KorektorBukuDbContext db)
            : base(db, NullLogger<ValidationService>.Instance)
        {
        }

        public static List<ErrorLocation> CallCreateLocations(
            IEnumerable<int> pageNumbers,
            Dictionary<int, ErrorBbox>? pageBboxMap)
            => CreateLocations(pageNumbers, pageBboxMap);
    }

    private sealed class SqliteValidationFixture : IDisposable
    {
        public SqliteValidationFixture()
        {
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
                .UseSqlite(Connection)
                .Options;

            Db = new KorektorBukuDbContext(options);
            Db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE dokumen_elemen_visual (
                    dev_id INTEGER PRIMARY KEY,
                    dev_ref_tipe TEXT NULL,
                    dev_ref_id INTEGER NULL,
                    dev_page INTEGER NULL,
                    dokumen_elemen_id INTEGER NULL,
                    dev_bbox_x0 REAL NULL,
                    dev_bbox_y0 REAL NULL,
                    dev_bbox_x1 REAL NULL,
                    dev_bbox_y1 REAL NULL,
                    dev_label TEXT NULL,
                    dev_text TEXT NULL,
                    dev_label_struktural TEXT NULL
                );
                """);
        }

        public SqliteConnection Connection { get; }

        public KorektorBukuDbContext Db { get; }

        public void AddVisual(
            ulong devId,
            ulong elementId,
            uint page,
            float[] bbox,
            string refType = "dokumen",
            uint refId = 10)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = devId,
                DokumenElemenId = elementId,
                DevPage = page,
                DevBboxX0 = bbox[0],
                DevBboxY0 = bbox[1],
                DevBboxX1 = bbox[2],
                DevBboxY1 = bbox[3],
                DevLabel = "table",
                DevLabelStruktural = "tabel",
                DevRefTipe = refType,
                DevRefId = refId
            });
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

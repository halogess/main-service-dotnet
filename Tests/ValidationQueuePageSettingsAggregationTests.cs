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

public class ValidationQueuePageSettingsAggregationTests
{
    [Fact]
    public async Task BuildLlmErrorsAsync_ShouldKeepMaxBlankLineErrorOnItsActualPage()
    {
        using var fixture = new SqliteQueueFixture();
        fixture.AddSection(11, 1, 10);
        fixture.AddBodyPart(21, 11);
        fixture.AddParagraphElement(31, 21, 1, """{"type":"paragraph","children":[{"type":"text","text":"Paragraf anchor"}]}""");
        fixture.AddVisual(41, 31, 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateService();
        var errors = new List<ValidationError>
        {
            CreateMaxBlankLinesError(page: 3, detectedBlankLines: 4)
        };

        var result = await InvokeBuildLlmErrorsAsync(service, fixture.Db, errors);

        var error = Assert.Single(result);
        var location = Assert.Single(error.Locations);
        Assert.Equal(3, location.HalamanKe);
        Assert.Equal(700m, location.Bbox!.Y0);
        Assert.Null(error.ScopeHint);
    }

    [Fact]
    public async Task BuildLlmErrorsAsync_ShouldNotAggregateMultipleMaxBlankLineErrors()
    {
        using var fixture = new SqliteQueueFixture();
        fixture.AddSection(11, 1, 10);
        fixture.AddBodyPart(21, 11);
        fixture.AddParagraphElement(31, 21, 1, """{"type":"paragraph","children":[{"type":"text","text":"Paragraf anchor"}]}""");
        fixture.AddVisual(41, 31, 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateService();
        var errors = new List<ValidationError>
        {
            CreateMaxBlankLinesError(page: 3, detectedBlankLines: 4),
            CreateMaxBlankLinesError(page: 5, detectedBlankLines: 6)
        };

        var result = await InvokeBuildLlmErrorsAsync(service, fixture.Db, errors);

        Assert.Collection(
            result,
            first =>
            {
                var location = Assert.Single(first.Locations);
                Assert.Equal(3, location.HalamanKe);
                Assert.Contains("halaman 3", first.Message);
            },
            second =>
            {
                var location = Assert.Single(second.Locations);
                Assert.Equal(5, location.HalamanKe);
                Assert.Contains("halaman 5", second.Message);
            });
    }

    [Fact]
    public async Task BuildLlmErrorsAsync_ShouldNotAggregateMultipleEmptyPageErrors()
    {
        using var fixture = new SqliteQueueFixture();
        fixture.AddSection(11, 1, 10);
        fixture.AddBodyPart(21, 11);
        fixture.AddParagraphElement(31, 21, 1, """{"type":"paragraph","children":[{"type":"text","text":"Paragraf anchor"}]}""");
        fixture.AddVisual(41, 31, 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateService();
        var errors = new List<ValidationError>
        {
            CreateEmptyPageError(page: 3),
            CreateEmptyPageError(page: 5)
        };

        var result = await InvokeBuildLlmErrorsAsync(service, fixture.Db, errors);

        Assert.Collection(
            result,
            first =>
            {
                var location = Assert.Single(first.Locations);
                Assert.Equal(3, location.HalamanKe);
                Assert.Equal(0m, location.Bbox!.X0);
                Assert.Equal(0m, location.Bbox.Y0);
                Assert.Equal(595.35m, location.Bbox.X1);
                Assert.Equal(841.95m, location.Bbox.Y1);
                Assert.Contains("Halaman 3 kosong", first.Message);
            },
            second =>
            {
                var location = Assert.Single(second.Locations);
                Assert.Equal(5, location.HalamanKe);
                Assert.Equal(0m, location.Bbox!.X0);
                Assert.Equal(0m, location.Bbox.Y0);
                Assert.Equal(595.35m, location.Bbox.X1);
                Assert.Equal(841.95m, location.Bbox.Y1);
                Assert.Contains("Halaman 5 kosong", second.Message);
            });
    }

    [Fact]
    public async Task BuildLlmErrorsAsync_ShouldBackfillMissingLocationFromDokumenElemenId()
    {
        using var fixture = new SqliteQueueFixture();
        fixture.AddSection(11, 1, 10);
        fixture.AddBodyPart(21, 11);
        fixture.AddParagraphElement(31, 21, 1, """{"type":"paragraph","children":[{"type":"image","rId":"rId5"}]}""");
        fixture.AddVisual(41, 31, 4);
        await fixture.Db.SaveChangesAsync();

        var service = CreateService();
        var errors = new List<ValidationError>
        {
            new()
            {
                Category = "Isi Buku",
                Field = "gambar",
                Message = "Caption gambar tidak ditemukan",
                Expected = "Caption setelah gambar",
                Actual = "Tidak ada caption",
                Evidence = "image:rId5",
                DokumenElemenId = 31
            }
        };

        var result = await InvokeBuildLlmErrorsAsync(service, fixture.Db, errors);

        var error = Assert.Single(result);
        var location = Assert.Single(error.Locations);
        Assert.Equal(4, location.HalamanKe);
        Assert.NotNull(location.Bbox);
        Assert.Equal(113.4m, Math.Round(location.Bbox!.X0, 2));
        Assert.Equal(114m, Math.Round(location.Bbox.Y0, 2));
        Assert.Equal(510.25m, Math.Round(location.Bbox.X1, 2));
        Assert.Equal(127m, Math.Round(location.Bbox.Y1, 2));
    }

    private static ValidationQueueBackgroundService CreateService()
        => new(
            Mock.Of<IServiceProvider>(),
            NullLogger<ValidationQueueBackgroundService>.Instance);

    private static ValidationError CreateMaxBlankLinesError(int page, int detectedBlankLines)
        => new()
        {
            Category = "Pengaturan Halaman",
            Field = "max_baris_kosong_akhir_halaman",
            Message = $"Sisa ruang kosong di akhir halaman {page} melebihi batas, maksimal 3 baris, terdeteksi sekitar {detectedBlankLines} baris",
            Expected = "Maksimal 3 baris kosong",
            Actual = $"Sekitar {detectedBlankLines} baris kosong",
            SectionIndex = 1,
            Locations =
            [
                new ErrorLocation
                {
                    HalamanKe = page,
                    Bbox = new ErrorBbox
                    {
                        X0 = 113.4m,
                        Y0 = 700m,
                        X1 = 510.25m,
                        Y1 = 756.9m
                    }
                }
            ]
        };

    private static ValidationError CreateEmptyPageError(int page)
        => new()
        {
            Category = "Pengaturan Halaman",
            Field = "cegah_halaman_kosong",
            Message = $"Halaman {page} kosong, tidak ada isi body selain whitespace",
            Expected = "Setiap halaman harus memiliki isi body",
            Actual = "Halaman hanya berisi whitespace atau header/footer",
            SectionIndex = 1,
            Locations =
            [
                new ErrorLocation
                {
                    HalamanKe = page,
                    Bbox = new ErrorBbox
                    {
                        X0 = 0m,
                        Y0 = 0m,
                        X1 = 595.35m,
                        Y1 = 841.95m
                    }
                }
            ]
        };

    private static async Task<List<ValidationError>> InvokeBuildLlmErrorsAsync(
        ValidationQueueBackgroundService service,
        KorektorBukuDbContext db,
        IReadOnlyList<ValidationError> errors)
    {
        var method = typeof(ValidationQueueBackgroundService).GetMethod(
            "BuildLlmErrorsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, [db, 10u, "dokumen", 10u, errors, CancellationToken.None]) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<List<ValidationError>>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteQueueFixture : IDisposable
    {
        public SqliteQueueFixture()
        {
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
                .UseSqlite(Connection)
                .Options;

            Db = new KorektorBukuDbContext(options);
            Db.Database.EnsureCreated();
        }

        public SqliteConnection Connection { get; }

        public KorektorBukuDbContext Db { get; }

        public void AddSection(uint sectionId, uint index, uint refId)
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = sectionId,
                DsecRefTipe = "dokumen",
                DsecRefId = refId,
                DsecIndex = index
            });
        }

        public void AddBodyPart(uint partId, uint sectionId)
        {
            Db.DokumenParts.Add(new DokumenPart
            {
                DpartId = partId,
                DsecId = sectionId,
                DpartType = "body"
            });
        }

        public void AddParagraphElement(ulong elementId, uint partId, uint sequence, string jsonTree)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = partId,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = jsonTree,
                DelemenXml = "<w:p/>"
            });
        }

        public void AddVisual(ulong devId, ulong elementId, uint page)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = devId,
                DokumenElemenId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DevBboxX0 = 113.4f,
                DevBboxY0 = 114f,
                DevBboxX1 = 510.25f,
                DevBboxY1 = 127f,
                DevLabel = "paragraph",
                DevLabelStruktural = "paragraf"
            });
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

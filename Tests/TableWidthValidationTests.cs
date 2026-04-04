using System.Reflection;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class TableWidthValidationTests
{
    [Fact]
    public async Task ValidateTableAsync_ShouldUseTextAreaWidthForPctTableWidth()
    {
        using var fixture = new SqliteTableWidthFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddWidthRule();
        fixture.AddTableFormat(900, "pct", pct50: 5000);
        fixture.AddTableElement(1001, 1, 900, page: 1, x0: 10, x1: 180, y0: 100f, y1: 180f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateTableAsync", fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Lebar tabel melebihi margin halaman");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldStillFlagPctTableWidthThatExceedsTextAreaAfterIndent()
    {
        using var fixture = new SqliteTableWidthFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddWidthRule();
        fixture.AddTableFormat(901, "pct", pct50: 5000, indentTwips: 720);
        fixture.AddTableElement(1002, 1, 901, page: 1, x0: 10, x1: 180, y0: 100f, y1: 180f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateTableAsync", fixture.Db);

        var error = Assert.Single(result.Errors, item => item.Message == "Lebar tabel melebihi margin halaman");
        Assert.Equal(
            $"{14m.ToString("F2", CultureInfo.CurrentCulture)} cm (max {12.73m.ToString("F2", CultureInfo.CurrentCulture)} cm)",
            error.Actual);
    }

    private static async Task<ValidationResult> InvokeValidationAsync(string methodName, KorektorBukuDbContext db)
    {
        var service = new ValidationService(db, NullLogger<ValidationService>.Instance);
        var method = typeof(ValidationService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, new object?[] { 10, CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task!.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<ValidationResult>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteTableWidthFixture : IDisposable
    {
        public SqliteTableWidthFixture()
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

        public void AddDokumen()
        {
            Db.Dokumens.Add(new Dokumen
            {
                DokumenId = 10,
                MhsNrp = "1234567890",
                DokumenFilename = "table-width.docx",
                DokumenStatus = "selesai"
            });
        }

        public void AddBodyStructure()
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = 1,
                DsecRefTipe = "dokumen",
                DsecRefId = 10,
                DsecIndex = 1,
                DsecPageWidthTwips = 11907,
                DsecPageHeightTwips = 16839,
                DsecOrientation = "portrait",
                DsecMarginTopTwips = 2268,
                DsecMarginBottomTwips = 1701,
                DsecMarginLeftTwips = 2268,
                DsecMarginRightTwips = 1701,
                DsecHeaderMarginTwips = 1417,
                DsecFooterMarginTwips = 850,
                DsecColumnCount = 1
            });

            Db.DokumenParts.Add(new DokumenPart
            {
                DpartId = 1,
                DsecId = 1,
                DpartType = "body"
            });
        }

        public void AddActiveAturan()
        {
            Db.Aturans.Add(new Aturan
            {
                AturanId = 1,
                AturanVersi = "test",
                AturanStatus = AturanStatusValues.Aktif,
                AturanCreatedAt = DateTime.UtcNow
            });
        }

        public void AddWidthRule()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 1,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "tabel",
                AturanDetailStatus = 1,
                AturanDetailJsonValue =
                    """
                    {
                      "tabel": {
                        "position": {
                          "cegah_melebihi_margin": { "value": true, "is_editable": true }
                        }
                      }
                    }
                    """
            });
        }

        public void AddTableFormat(uint id, string widthType, uint? pct50 = null, uint? widthTwips = null, int? indentTwips = null)
        {
            Db.DokumenFormatTables.Add(new DokumenFormatTable
            {
                DftId = id,
                DftTblWType = widthType,
                DftTblWPct50 = pct50,
                DftTblWTwips = widthTwips,
                DftTblIndType = indentTwips.HasValue ? "dxa" : null,
                DftTblIndTwips = indentTwips,
                DftJc = "center",
                DftTblLayoutType = "fixed"
            });
        }

        public void AddTableElement(ulong elementId, uint sequence, uint tableFormatId, uint page, float x0, float x1, float y0, float y1)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "table",
                DelemenJsonTree = $"{{\"dft_id\":{tableFormatId},\"content\":{{\"rows\":[]}}}}",
                DelemenXml = string.Empty
            });

            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DokumenElemenId = elementId,
                DevBboxX0 = x0,
                DevBboxY0 = y0,
                DevBboxX1 = x1,
                DevBboxY1 = y1,
                DevLabel = "tabel",
                DevLabelStruktural = "tabel"
            });
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

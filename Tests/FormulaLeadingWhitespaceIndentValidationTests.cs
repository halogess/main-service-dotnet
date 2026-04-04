using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class FormulaLeadingWhitespaceIndentValidationTests
{
    [Fact]
    public async Task ValidateFormulaAsync_ShouldReportFirstLineIndent_WhenFormulaStartsWithLeadingWhitespace()
    {
        using var fixture = new SqliteFormulaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRule();
        fixture.AddFormats();
        fixture.AddFormulaElement(
            elementId: 1001,
            sequence: 1,
            paragraphFormatId: 101,
            textFormatId: 201,
            text: "\t  E = mc^2");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeFormulaValidationAsync(fixture.Db);

        var error = Assert.Single(result.Errors, item => item.Message == "First line indent rumus tidak sesuai karena diawali spasi/tab");
        Assert.Equal("rumus", error.Field);
        Assert.Equal("0 cm", error.Expected);
        Assert.Equal("0.00 cm + 1 tab awal dan 2 spasi awal", error.Actual);
    }

    [Fact]
    public async Task ValidateFormulaAsync_ShouldPassFirstLineIndent_WhenFormulaDoesNotStartWithLeadingWhitespace()
    {
        using var fixture = new SqliteFormulaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRule();
        fixture.AddFormats();
        fixture.AddFormulaElement(
            elementId: 1001,
            sequence: 1,
            paragraphFormatId: 101,
            textFormatId: 201,
            text: "E = mc^2");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeFormulaValidationAsync(fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message.Contains("First line indent rumus", StringComparison.Ordinal));
    }

    private static async Task<ValidationResult> InvokeFormulaValidationAsync(KorektorBukuDbContext db)
    {
        var service = new ValidationService(db, NullLogger<ValidationService>.Instance);
        var method = typeof(ValidationService).GetMethod("ValidateFormulaAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, new object?[] { 10, CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task!.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<ValidationResult>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteFormulaFixture : IDisposable
    {
        public SqliteFormulaFixture()
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
                DokumenFilename = "uji-rumus-indent.docx",
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

        public void AddActiveRule()
        {
            Db.Aturans.Add(new Aturan
            {
                AturanId = 1,
                AturanVersi = "test",
                AturanStatus = AturanStatusValues.Aktif,
                AturanCreatedAt = DateTime.UtcNow
            });

            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 1,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "rumus",
                AturanDetailStatus = 1,
                AturanDetailJsonValue =
                    """
                    {
                      "paragraph": {
                        "indentation": {
                          "first_line_indent": { "value": 0, "is_editable": true }
                        }
                      }
                    }
                    """
            });
        }

        public void AddFormats()
        {
            Db.DokumenFormatParagrafs.Add(new DokumenFormatParagraf
            {
                DfpId = 101,
                DfpJc = "center",
                DfpIndLeftTwips = 0,
                DfpIndRightTwips = 0,
                DfpIndFirstLineTwips = 0,
                DfpSpacingBeforeTwips = 0,
                DfpSpacingAfterTwips = 0,
                DfpSpacingLineTwips = 240,
                DfpSpacingLineRule = "auto"
            });

            Db.DokumenFormatTexts.Add(new DokumenFormatText
            {
                DftxId = 201,
                DftxFontAscii = "Times New Roman",
                DftxSizeHalfpt = 24,
                DftxBold = false,
                DftxItalic = false,
                DftxUnderline = "none"
            });
        }

        public void AddFormulaElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId, string text)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateFormulaJson(paragraphFormatId, textFormatId, text),
                DelemenXml = string.Empty
            });

            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = elementId,
                DokumenElemenId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = 1,
                DevBboxX0 = 40,
                DevBboxY0 = 120,
                DevBboxX1 = 520,
                DevBboxY1 = 150,
                DevLabel = "rumus",
                DevLabelStruktural = "rumus",
                DevText = text
            });
        }

        private static string CreateFormulaJson(uint paragraphFormatId, uint textFormatId, string text)
        {
            return $$"""{"dfp_id":{{paragraphFormatId}},"content":[{"type":"text","dftx_id":{{textFormatId}},"value":{{JsonSerializer.Serialize(text)}}}]}""";
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

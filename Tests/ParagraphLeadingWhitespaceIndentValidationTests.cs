using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ParagraphLeadingWhitespaceIndentValidationTests
{
    [Fact]
    public async Task ValidateParagraphAsync_ShouldReportFirstLineIndent_WhenParagraphStartsWithTabAndSpaces()
    {
        using var fixture = new SqliteParagraphFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRules();
        fixture.AddFormats();
        fixture.AddParagraphElement(
            elementId: 1001,
            sequence: 1,
            paragraphFormatId: 101,
            textFormatId: 201,
            text: "\t  Ini paragraf uji dengan tab dan spasi di awal.");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeParagraphValidationAsync(fixture.Db);

        var error = Assert.Single(result.Errors, item => item.Message == "First line indent paragraf tidak sesuai karena diawali spasi/tab");
        Assert.Equal("paragraf", error.Field);
        Assert.Equal("1.27 cm", error.Expected);
        Assert.Equal("1.27 cm + 1 tab awal dan 2 spasi awal", error.Actual);
    }

    [Fact]
    public async Task ValidateParagraphAsync_ShouldPassFirstLineIndent_WhenParagraphDoesNotStartWithWhitespace()
    {
        using var fixture = new SqliteParagraphFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRules();
        fixture.AddFormats();
        fixture.AddParagraphElement(
            elementId: 1001,
            sequence: 1,
            paragraphFormatId: 101,
            textFormatId: 201,
            text: "Ini paragraf uji tanpa whitespace awal.");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeParagraphValidationAsync(fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message.Contains("First line indent paragraf", StringComparison.Ordinal));
    }

    private static async Task<ValidationResult> InvokeParagraphValidationAsync(KorektorBukuDbContext db)
    {
        var service = new ValidationService(db, NullLogger<ValidationService>.Instance);
        var method = typeof(ValidationService).GetMethod("ValidateParagraphAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, new object?[] { 10, null, null, CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task!.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<ValidationResult>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteParagraphFixture : IDisposable
    {
        public SqliteParagraphFixture()
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
                DokumenFilename = "uji-paragraf-indent.docx",
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

        public void AddActiveRules()
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
                AturanDetailKey = "paragraf",
                AturanDetailStatus = 1,
                AturanDetailJsonValue =
                    """
                    {
                      "font": {
                        "font_name": { "value": "Times New Roman", "is_editable": true },
                        "font_size": { "value": 12, "is_editable": true }
                      },
                      "paragraph": {
                        "alignment": { "value": "justify", "is_editable": true },
                        "indentation": {
                          "left_indent": { "value": 0, "is_editable": true },
                          "right_indent": { "value": 0, "is_editable": true },
                          "first_line_indent": { "value": 1.27, "is_editable": true }
                        },
                        "spacing": {
                          "line_spacing": { "value": 1.5, "is_editable": true },
                          "before": { "value": 0, "is_editable": true },
                          "after": { "value": 0, "is_editable": true }
                        }
                      },
                      "struktur_konten": {
                        "minimal_kalimat": { "value": 0, "is_editable": true }
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
                DfpJc = "both",
                DfpIndLeftTwips = 0,
                DfpIndRightTwips = 0,
                DfpIndFirstLineTwips = 720,
                DfpSpacingLineTwips = 360,
                DfpSpacingLineRule = "auto",
                DfpSpacingBeforeTwips = 0,
                DfpSpacingAfterTwips = 0
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

        public void AddParagraphElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId, string text)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateParagraphJson(paragraphFormatId, textFormatId, text),
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
                DevBboxY0 = 100,
                DevBboxX1 = 520,
                DevBboxY1 = 140,
                DevLabel = "paragraf",
                DevLabelStruktural = "paragraf",
                DevText = text
            });
        }

        private static string CreateParagraphJson(uint paragraphFormatId, uint textFormatId, string text)
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

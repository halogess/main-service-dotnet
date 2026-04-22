using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ListItemIndentValidationTests
{
    [Fact]
    public async Task ValidateListItemAsync_ShouldAllowParagraphLikeManualIndent_WhenRuleExpectsCanonicalHangingIndent()
    {
        using var fixture = new SqliteListItemIndentFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRules();
        fixture.AddTextFormat();
        fixture.AddParagraphFormat(101, leftTwips: 425, hangingTwips: 0, firstLineTwips: 0);
        fixture.AddListItemElement(1001, 1, 101, "e. Jika jenis awalan adalah none maka algoritma akan menampilkan kata tersebut.");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeListItemValidationAsync(fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message.StartsWith("Left indent item daftar tidak sesuai", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, item => item.Message == "Hanging indent item daftar tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "First line indent item daftar harus 0");
    }

    [Fact]
    public async Task ValidateListItemAsync_ShouldKeepPassingCanonicalHangingIndent()
    {
        using var fixture = new SqliteListItemIndentFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRules();
        fixture.AddTextFormat();
        fixture.AddParagraphFormat(101, leftTwips: 425, hangingTwips: 425, firstLineTwips: 0, isList: true, listNumId: 7, listIlvl: 0);
        fixture.AddListItemElement(1001, 1, 101, "e. Jika jenis awalan adalah none maka algoritma akan menampilkan kata tersebut.");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeListItemValidationAsync(fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message.StartsWith("Left indent item daftar tidak sesuai", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, item => item.Message == "Hanging indent item daftar tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "First line indent item daftar harus 0");
    }

    private static async Task<ValidationResult> InvokeListItemValidationAsync(KorektorBukuDbContext db)
    {
        var service = new ValidationService(db, NullLogger<ValidationService>.Instance);
        var method = typeof(ValidationService).GetMethod("ValidateListItemAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, new object?[] { 10, null, CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task!.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<ValidationResult>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteListItemIndentFixture : IDisposable
    {
        public SqliteListItemIndentFixture()
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
                DokumenFilename = "uji-item-daftar-indent.docx",
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
                AturanDetailKey = "item_daftar",
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
                          "hanging": { "value": 0.75, "is_editable": true },
                          "right_indent": { "value": 0, "is_editable": true }
                        },
                        "spacing": {
                          "line_spacing": { "value": 1.5, "is_editable": true },
                          "before": { "value": 0, "is_editable": true },
                          "after": { "value": 0, "is_editable": true }
                        }
                      }
                    }
                    """
            });
        }

        public void AddTextFormat()
        {
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

        public void AddParagraphFormat(
            uint paragraphFormatId,
            long leftTwips,
            long hangingTwips,
            long firstLineTwips,
            bool isList = false,
            uint? listNumId = null,
            uint? listIlvl = null)
        {
            Db.DokumenFormatParagrafs.Add(new DokumenFormatParagraf
            {
                DfpId = paragraphFormatId,
                DfpJc = "both",
                DfpIndLeftTwips = leftTwips,
                DfpIndStartTwips = leftTwips,
                DfpIndHangingTwips = hangingTwips,
                DfpIndFirstLineTwips = firstLineTwips,
                DfpIndRightTwips = 0,
                DfpSpacingLineTwips = 360,
                DfpSpacingLineRule = "auto",
                DfpSpacingBeforeTwips = 0,
                DfpSpacingAfterTwips = 0,
                DfpIsList = isList,
                DfpListNumId = listNumId,
                DfpListIlvl = listIlvl
            });
        }

        public void AddListItemElement(ulong elementId, uint sequence, uint paragraphFormatId, string text)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateParagraphJson(paragraphFormatId, 201, text),
                DelemenXml = string.Empty
            });

            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = elementId,
                DokumenElemenId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = 1,
                DevBboxX0 = 50,
                DevBboxY0 = 100 + (sequence * 40),
                DevBboxX1 = 520,
                DevBboxY1 = 130 + (sequence * 40),
                DevLabel = "list_level_1",
                DevLabelStruktural = "list_level_1",
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

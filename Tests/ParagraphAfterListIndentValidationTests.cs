using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ParagraphAfterListIndentValidationTests
{
    [Fact]
    public async Task ValidateParagraphAsync_ShouldNotTreatNormalParagraphsAfterMultiItemListAsListContinuation()
    {
        using var fixture = new SqliteParagraphAfterListFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRules();
        fixture.AddFormats();

        fixture.AddListItemElement(1001, 1, "1. Item pertama");
        fixture.AddListItemElement(1002, 2, "2. Item kedua");
        fixture.AddListItemElement(1003, 3, "3. Item ketiga");
        fixture.AddListItemElement(1004, 4, "4. Item keempat");
        fixture.AddParagraphElement(1005, 5, "Paragraf baru sesudah list yang seharusnya mengikuti aturan paragraf biasa.");
        fixture.AddParagraphElement(1006, 6, "Paragraf berikutnya juga paragraf biasa dan bukan lanjutan item daftar.");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeParagraphValidationAsync(fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Left indent paragraf setelah list tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "First line indent paragraf setelah list tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "Hanging indent paragraf setelah list tidak sesuai");
    }

    [Fact]
    public async Task ValidateParagraphAsync_ShouldAllowContinuationIndentAfterMultiItemList()
    {
        using var fixture = new SqliteParagraphAfterListFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveRules();
        fixture.AddFormats();

        fixture.AddListItemElement(1001, 1, "1. Item pertama");
        fixture.AddListItemElement(1002, 2, "2. Item kedua");
        fixture.AddListItemElement(1003, 3, "3. Item ketiga");
        fixture.AddListItemElement(1004, 4, "4. Item keempat");
        fixture.AddContinuationParagraphElement(1005, 5, "Lanjutan penjelasan untuk item keempat tanpa nomor 5.");
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeParagraphValidationAsync(fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Left indent paragraf setelah list tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "First line indent paragraf setelah list tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "Hanging indent paragraf setelah list tidak sesuai");
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

    private sealed class SqliteParagraphAfterListFixture : IDisposable
    {
        public SqliteParagraphAfterListFixture()
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
                DokumenFilename = "uji-paragraf-setelah-list.docx",
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

            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 2,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "item_daftar",
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
                          "hanging": { "value": 0.75, "is_editable": true }
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

        public void AddFormats()
        {
            Db.DokumenFormatParagrafs.AddRange(
                new DokumenFormatParagraf
                {
                    DfpId = 101,
                    DfpJc = "both",
                    DfpIndLeftTwips = 720,
                    DfpIndHangingTwips = 720,
                    DfpIndFirstLineTwips = 0,
                    DfpSpacingLineTwips = 360,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0
                },
                new DokumenFormatParagraf
                {
                    DfpId = 102,
                    DfpJc = "both",
                    DfpIndLeftTwips = 0,
                    DfpIndRightTwips = 0,
                    DfpIndFirstLineTwips = 720,
                    DfpSpacingLineTwips = 360,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0
                },
                new DokumenFormatParagraf
                {
                    DfpId = 103,
                    DfpJc = "both",
                    DfpIndLeftTwips = 425,
                    DfpIndRightTwips = 0,
                    DfpIndFirstLineTwips = 0,
                    DfpIndHangingTwips = 0,
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

        public void AddListItemElement(ulong elementId, uint sequence, string text)
        {
            AddElement(elementId, sequence, 101, text, "list_level_1");
        }

        public void AddParagraphElement(ulong elementId, uint sequence, string text)
        {
            AddElement(elementId, sequence, 102, text, "paragraf");
        }

        public void AddContinuationParagraphElement(ulong elementId, uint sequence, string text)
        {
            AddElement(elementId, sequence, 103, text, "paragraf");
        }

        private void AddElement(ulong elementId, uint sequence, uint paragraphFormatId, string text, string label)
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
                DevBboxX0 = 40,
                DevBboxY0 = 100 + (sequence * 40),
                DevBboxX1 = 520,
                DevBboxY1 = 130 + (sequence * 40),
                DevLabel = label,
                DevLabelStruktural = label,
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

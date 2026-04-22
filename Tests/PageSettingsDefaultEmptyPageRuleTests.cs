using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class PageSettingsDefaultEmptyPageRuleTests
{
    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldDefaultPreventEmptyPages_WhenRuleFieldIsMissing()
    {
        using var fixture = new SqlitePageSettingsDefaultFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturanWithoutExplicitEmptyPageRule();
        fixture.AddNomorHalamanRule();
        fixture.AddSection(1, 1);
        fixture.AddBodyPart(1, 1);
        fixture.AddFooterPart(12, 1);

        fixture.AddPageFieldElement(1001, 12, 1, 1, "1", 11, 730f, 745f);
        fixture.AddBodyElement(1002, 1, 2, 2, "Paragraf isi pada halaman kedua", 610f, 650f);
        fixture.AddPageFieldElement(1003, 12, 3, 2, "2", 11, 730f, 745f);
        await fixture.Db.SaveChangesAsync();

        var service = new ValidationService(fixture.Db, NullLogger<ValidationService>.Instance);
        var result = await service.ValidatePageSettingsAsync(10);

        var error = Assert.Single(result.Errors, item => item.Field == "cegah_halaman_kosong");
        Assert.Contains("Halaman 1 kosong", error.Message);
        Assert.Equal("Setiap halaman harus memiliki isi body", error.Expected);
        Assert.Equal("Halaman hanya berisi whitespace atau header/footer", error.Actual);
        var location = Assert.Single(error.Locations);
        Assert.Equal(1, location.HalamanKe);
        Assert.NotNull(location.Bbox);
        Assert.Equal(0m, location.Bbox!.X0);
        Assert.Equal(0m, location.Bbox.Y0);
        Assert.Equal(595.35m, location.Bbox.X1);
        Assert.Equal(841.95m, location.Bbox.Y1);
    }

    private sealed class SqlitePageSettingsDefaultFixture : IDisposable
    {
        public SqlitePageSettingsDefaultFixture()
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
                DokumenFilename = "uji-page-settings-default-empty-page.docx",
                DokumenStatus = "selesai"
            });
        }

        public void AddActiveAturanWithoutExplicitEmptyPageRule()
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
                AturanDetailKategori = "Pengaturan Halaman",
                AturanDetailKey = "page_settings",
                AturanDetailJsonValue =
                    """
                    {
                      "akhir_halaman": {
                        "max_baris_kosong": { "value": 3, "is_editable": true }
                      }
                    }
                    """
            });
        }

        public void AddNomorHalamanRule()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 2,
                AturanId = 1,
                AturanDetailKategori = "Nomor Halaman",
                AturanDetailKey = "nomor_halaman",
                AturanDetailJsonValue =
                    """
                    {
                      "variation": {
                        "different_first_page": {
                          "enabled": { "value": false, "is_editable": true }
                        }
                      }
                    }
                    """
            });
        }

        public void AddFormats()
        {
            Db.DokumenFormatParagrafs.AddRange(
                CreateParagraphFormat(11, "center"),
                CreateParagraphFormat(12, "both"));

            Db.DokumenFormatTexts.Add(new DokumenFormatText
            {
                DftxId = 100,
                DftxFontAscii = "Times New Roman",
                DftxSizeHalfpt = 24,
                DftxBold = false,
                DftxItalic = false,
                DftxUnderline = "none"
            });
        }

        public void AddSection(uint sectionId, uint index)
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = sectionId,
                DsecRefTipe = "dokumen",
                DsecRefId = 10,
                DsecIndex = index,
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

        public void AddFooterPart(uint partId, uint sectionId)
        {
            Db.DokumenParts.Add(new DokumenPart
            {
                DpartId = partId,
                DsecId = sectionId,
                DpartType = "footer",
                DpartPosition = "default"
            });
        }

        public void AddBodyElement(ulong elementId, uint partId, uint sequence, uint page, string text, float y0, float y1)
        {
            AddElement(elementId, partId, sequence, CreateTextParagraphJson(12, text), page, y0, y1);
        }

        public void AddPageFieldElement(ulong elementId, uint partId, uint sequence, uint page, string displayedValue, uint paragraphFormatId, float y0, float y1)
        {
            AddElement(elementId, partId, sequence, CreatePageFieldParagraphJson(paragraphFormatId, displayedValue), page, y0, y1);
        }

        private void AddElement(ulong elementId, uint partId, uint sequence, string json, uint page, float y0, float y1)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = partId,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = json,
                DelemenXml = string.Empty
            });

            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DokumenElemenId = elementId,
                DevBboxX0 = 72,
                DevBboxY0 = y0,
                DevBboxX1 = 460,
                DevBboxY1 = y1
            });
        }

        private static DokumenFormatParagraf CreateParagraphFormat(uint id, string alignment)
        {
            return new DokumenFormatParagraf
            {
                DfpId = id,
                DfpJc = alignment,
                DfpSpacingBeforeTwips = 0,
                DfpSpacingAfterTwips = 0,
                DfpSpacingLineTwips = 240,
                DfpSpacingLineRule = "auto"
            };
        }

        private static string CreateTextParagraphJson(uint paragraphFormatId, string text)
        {
            return $$"""
            {
              "type": "paragraph",
              "dfp_id": {{paragraphFormatId}},
              "content": [
                {
                  "type": "text",
                  "value": "{{text}}",
                  "dftx_id": 100
                }
              ]
            }
            """;
        }

        private static string CreatePageFieldParagraphJson(uint paragraphFormatId, string displayedValue)
        {
            return $$"""
            {
              "type": "paragraph",
              "dfp_id": {{paragraphFormatId}},
              "content": [
                {
                  "type": "field",
                  "field_type": "PAGE",
                  "display": "{{displayedValue}}",
                  "dftx_id": 100
                }
              ]
            }
            """;
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

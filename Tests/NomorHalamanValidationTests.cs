using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class NomorHalamanValidationTests
{
    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldApplyDifferentFirstPageOnlyToFirstPhysicalPageSection()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddPageFieldElement(1002, 12, 1, 2, "2", paragraphFormatId: 11);

        fixture.AddSection(2, 2, hasTitlePage: false);
        fixture.AddBodyPart(2, 2);
        fixture.AddBodyElement(201, 2, 1, 3, "Isi halaman ketiga");
        fixture.AddHeaderFooterPart(22, 2, "footer", "default");
        fixture.AddPageFieldElement(2002, 22, 1, 3, "3", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field != "max_baris_kosong_akhir_halaman");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldAllowManualFirstPageNumberWhenDisplayedValueMatchesExpectedPage()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true, pageNumStart: 4);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddTextElement(1001, 11, 1, 1, "4", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddPageFieldElement(1002, 12, 1, 2, "5", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field == "page_number_location");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldRejectManualFirstPageNumberWhenDisplayedValueDoesNotMatchExpectedPage()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true, pageNumStart: 4);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddTextElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddPageFieldElement(1002, 12, 1, 2, "5", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.Contains(result.Errors, error =>
            error.Field == "page_number_location" &&
            error.Expected == "header" &&
            error.Actual == "tidak ditemukan");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldAllowManualDefaultPageNumberWhenDisplayedValueMatchesExpectedPage()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddTextElement(1002, 12, 1, 2, "2", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field == "page_number_location");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldRejectStaticManualDefaultPageNumberWhenDisplayedOnMultiplePages()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddBodyElement(103, 1, 3, 3, "Isi halaman ketiga");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddTextElement(1002, 12, 1, 2, "2", paragraphFormatId: 11);
        fixture.AddAdditionalVisualPage(1002, 5002, 3);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.Contains(result.Errors, error =>
            error.Field == "page_number_location" &&
            error.Expected == "footer" &&
            error.Actual == "tidak ditemukan");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldKeepSingleSectionFieldBasedDifferentFirstPageWorking()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddPageFieldElement(1002, 12, 1, 2, "2", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field != "max_baris_kosong_akhir_halaman");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldDetectPageFieldNestedInsideShapeTextbox()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddNestedPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddNestedPageFieldElement(1002, 12, 1, 2, "2", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field == "page_number_location");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldInheritLinkedHeaderFooterFromPreviousSection()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: true);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);

        fixture.AddSection(2, 2, hasTitlePage: false);
        fixture.AddBodyPart(2, 2);
        fixture.AddBodyElement(201, 2, 1, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(22, 2, "footer", "default");
        fixture.AddPageFieldElement(2002, 22, 1, 2, "2", paragraphFormatId: 11);

        fixture.AddSection(3, 3, hasTitlePage: false);
        fixture.AddBodyPart(3, 3);
        fixture.AddBodyElement(301, 3, 1, 3, "Isi halaman ketiga");

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error =>
            error.Field == "page_number_location" &&
            error.SectionIndex == 3);
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldAllowManualFirstPageWhenFirstPageNumberIsCorrect()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule();

        fixture.AddSection(1, 1, hasTitlePage: false);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddHeaderFooterPart(11, 1, "header", "default");
        fixture.AddPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);

        fixture.AddSection(2, 2, hasTitlePage: false);
        fixture.AddBodyPart(2, 2);
        fixture.AddBodyElement(201, 2, 1, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(22, 2, "footer", "default");
        fixture.AddPageFieldElement(2002, 22, 1, 2, "2", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field == "different_first_page");
        Assert.DoesNotContain(result.Errors, error => error.Field == "page_number_location");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldIgnoreLegacyPageNumberLineSpacingRule()
    {
        using var fixture = new SqlitePageNumberFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule("""
            {
              "paragraph": {
                "spacing": {
                  "line_spacing": { "value": 2, "is_editable": true, "is_hard_constraint": true },
                  "before": { "value": 12, "is_editable": true, "is_hard_constraint": false },
                  "after": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                }
              },
              "variation": {
                "different_first_page": {
                  "enabled": { "value": true }
                }
              }
            }
            """);

        fixture.AddSection(1, 1, hasTitlePage: true);
        fixture.AddBodyPart(1, 1);
        fixture.AddBodyElement(101, 1, 1, 1, "Isi halaman pertama");
        fixture.AddBodyElement(102, 1, 2, 2, "Isi halaman kedua");
        fixture.AddHeaderFooterPart(11, 1, "header", "first");
        fixture.AddPageFieldElement(1001, 11, 1, 1, "1", paragraphFormatId: 10);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");
        fixture.AddPageFieldElement(1002, 12, 1, 2, "2", paragraphFormatId: 11);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field == "page_number_line_spacing");
        Assert.Contains(result.Errors, error => error.Field == "page_number_spacing_before");
    }

    private static ValidationService CreateValidationService(KorektorBukuDbContext db)
        => new(db, NullLogger<ValidationService>.Instance);

    private sealed class SqlitePageNumberFixture : IDisposable
    {
        public SqlitePageNumberFixture()
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
                DokumenFilename = "uji.docx",
                DokumenStatus = "selesai"
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

        public void AddNomorHalamanRule(string? jsonValue = null)
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 1,
                AturanId = 1,
                AturanDetailKategori = "Nomor Halaman",
                AturanDetailKey = "nomor_halaman",
                AturanDetailJsonValue = jsonValue ?? """{"variation":{"different_first_page":{"enabled":{"value":true}}}}""",
            });
        }

        public void AddFormats()
        {
            Db.DokumenFormatParagrafs.AddRange(
                CreateParagraphFormat(10, "right"),
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

        public void AddSection(uint sectionId, uint index, bool hasTitlePage, uint? pageNumStart = null)
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = sectionId,
                DsecRefTipe = "dokumen",
                DsecRefId = 10,
                DsecIndex = index,
                DsecHasTitlePage = hasTitlePage,
                DsecDifferentOddEven = false,
                DsecPageNumStart = pageNumStart,
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

        public void AddHeaderFooterPart(uint partId, uint sectionId, string type, string position)
        {
            Db.DokumenParts.Add(new DokumenPart
            {
                DpartId = partId,
                DsecId = sectionId,
                DpartType = type,
                DpartPosition = position
            });
        }

        public void AddBodyElement(ulong elementId, uint partId, uint sequence, uint page, string text)
        {
            AddElement(elementId, partId, sequence, CreateTextParagraphJson(12, text), page);
        }

        public void AddPageFieldElement(ulong elementId, uint partId, uint sequence, uint page, string displayedValue, uint paragraphFormatId)
        {
            AddElement(elementId, partId, sequence, CreatePageFieldParagraphJson(paragraphFormatId, displayedValue), page);
        }

        public void AddTextElement(ulong elementId, uint partId, uint sequence, uint page, string text, uint paragraphFormatId)
        {
            AddElement(elementId, partId, sequence, CreateTextParagraphJson(paragraphFormatId, text), page);
        }

        public void AddNestedPageFieldElement(ulong elementId, uint partId, uint sequence, uint page, string displayedValue, uint paragraphFormatId)
        {
            AddElement(elementId, partId, sequence, CreateNestedPageFieldParagraphJson(paragraphFormatId, displayedValue), page);
        }

        public void AddAdditionalVisualPage(ulong elementId, ulong visualId, uint page)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = visualId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DokumenElemenId = elementId,
                DevBboxX0 = 10,
                DevBboxY0 = 10,
                DevBboxX1 = 50,
                DevBboxY1 = 20
            });
        }

        private void AddElement(ulong elementId, uint partId, uint sequence, string json, uint page)
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
                DevBboxX0 = 10,
                DevBboxY0 = 10,
                DevBboxX1 = 50,
                DevBboxY1 = 20
            });
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }

        private static DokumenFormatParagraf CreateParagraphFormat(uint id, string alignment)
        {
            return new DokumenFormatParagraf
            {
                DfpId = id,
                DfpJc = alignment,
                DfpIndLeftTwips = 0,
                DfpIndRightTwips = 0,
                DfpIndFirstLineTwips = 0,
                DfpSpacingBeforeTwips = 0,
                DfpSpacingAfterTwips = 0,
                DfpSpacingLineTwips = 240,
                DfpSpacingLineRule = "auto"
            };
        }

        private static string CreatePageFieldParagraphJson(uint paragraphFormatId, string displayedValue)
        {
            return $$"""
            {"dfp_id":{{paragraphFormatId}},"content":[{"type":"field","field_type":"PAGE","value":"{{displayedValue}}","result_dftx_id":100}]}
            """;
        }

        private static string CreateTextParagraphJson(uint paragraphFormatId, string text)
        {
            return $$"""
            {"dfp_id":{{paragraphFormatId}},"content":[{"type":"text","value":"{{text}}","dftx_id":100}]}
            """;
        }

        private static string CreateNestedPageFieldParagraphJson(uint paragraphFormatId, string displayedValue)
        {
            return $$"""
            {"dfp_id":{{paragraphFormatId}},"content":[{"type":"shape","content":[{"type":"textbox","content":[{"type":"field","field_type":"PAGE","value":"{{displayedValue}}","result_dftx_id":100}]}]}]}
            """;
        }
    }
}

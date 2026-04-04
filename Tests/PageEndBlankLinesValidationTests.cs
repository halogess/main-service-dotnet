using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class PageEndBlankLinesValidationTests
{
    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldUseDefaultMaxBlankLinesForLegacyRule()
    {
        using var fixture = new SqlitePageEndFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule(differentFirstPage: false);
        fixture.AddSection(1, 1, hasTitlePage: false);
        fixture.AddBodyPart(1, 1);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");

        fixture.AddBodyElement(101, 1, 1, 1, "Paragraf halaman pertama", 610f, 650f);
        fixture.AddPageFieldElement(1001, 12, 2, 1, "1", paragraphFormatId: 11, y0: 730f, y1: 745f);
        fixture.AddBodyElement(102, 1, 3, 2, "Paragraf halaman kedua", 700f, 730f);
        fixture.AddPageFieldElement(1002, 12, 4, 2, "2", paragraphFormatId: 11, y0: 730f, y1: 745f);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        var error = Assert.Single(result.Errors, e => e.Field == "max_baris_kosong_akhir_halaman");
        Assert.Contains("maksimal 3 baris", error.Message);
        Assert.Equal("Maksimal 3 baris kosong", error.Expected);
        Assert.Equal("Sekitar 5 baris kosong", error.Actual);
        Assert.Equal(1, error.Locations.Single().HalamanKe);
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldIgnoreLargeGapOnLastPage()
    {
        using var fixture = new SqlitePageEndFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule(differentFirstPage: false);
        fixture.AddSection(1, 1, hasTitlePage: false);
        fixture.AddBodyPart(1, 1);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");

        fixture.AddBodyElement(101, 1, 1, 1, "Paragraf halaman pertama", 690f, 720f);
        fixture.AddPageFieldElement(1001, 12, 2, 1, "1", paragraphFormatId: 11, y0: 730f, y1: 745f);
        fixture.AddBodyElement(102, 1, 3, 2, "Paragraf halaman kedua", 610f, 650f);
        fixture.AddPageFieldElement(1002, 12, 4, 2, "2", paragraphFormatId: 11, y0: 730f, y1: 745f);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field == "max_baris_kosong_akhir_halaman");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldIgnoreFooterWhenFindingLastElementOnPage()
    {
        using var fixture = new SqlitePageEndFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule(differentFirstPage: false);
        fixture.AddSection(1, 1, hasTitlePage: false);
        fixture.AddBodyPart(1, 1);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");

        fixture.AddBodyElement(101, 1, 1, 1, "Paragraf halaman pertama", 610f, 650f);
        fixture.AddPageFieldElement(1001, 12, 2, 1, "1", paragraphFormatId: 11, y0: 730f, y1: 752f);
        fixture.AddBodyElement(102, 1, 3, 2, "Paragraf halaman kedua", 700f, 730f);
        fixture.AddPageFieldElement(1002, 12, 4, 2, "2", paragraphFormatId: 11, y0: 730f, y1: 745f);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.Contains(result.Errors, error =>
            error.Field == "max_baris_kosong_akhir_halaman" &&
            error.Locations.Any(location => location.HalamanKe == 1));
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldTreatNonLastPageWithOnlyHeaderFooterAsEmptyTextArea()
    {
        using var fixture = new SqlitePageEndFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule(differentFirstPage: false);
        fixture.AddSection(1, 1, hasTitlePage: false);
        fixture.AddBodyPart(1, 1);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");

        fixture.AddPageFieldElement(1001, 12, 2, 1, "1", paragraphFormatId: 11, y0: 730f, y1: 745f);
        fixture.AddBodyElement(102, 1, 3, 2, "Paragraf halaman kedua", 700f, 730f);
        fixture.AddPageFieldElement(1002, 12, 4, 2, "2", paragraphFormatId: 11, y0: 730f, y1: 745f);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        var error = Assert.Single(result.Errors, e => e.Field == "cegah_halaman_kosong");
        var bbox = Assert.Single(error.Locations).Bbox;
        Assert.NotNull(bbox);
        Assert.Equal(0m, bbox!.Y0);
        Assert.Equal(841.95m, bbox.Y1);
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldMeasureBlankLinesUntilFootnoteTop()
    {
        using var fixture = new SqlitePageEndFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule(differentFirstPage: false);
        fixture.AddSection(1, 1, hasTitlePage: false);
        fixture.AddBodyPart(1, 1);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");

        fixture.AddBodyElement(101, 1, 1, 1, "Paragraf halaman pertama", 610f, 650f);
        fixture.AddFootnoteVisual(2001, 1, 700f, 742f);
        fixture.AddPageFieldElement(1001, 12, 2, 1, "1", paragraphFormatId: 11, y0: 730f, y1: 745f);
        fixture.AddBodyElement(102, 1, 3, 2, "Paragraf halaman kedua", 700f, 730f);
        fixture.AddPageFieldElement(1002, 12, 4, 2, "2", paragraphFormatId: 11, y0: 730f, y1: 745f);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        Assert.DoesNotContain(result.Errors, error => error.Field == "max_baris_kosong_akhir_halaman");
    }

    [Fact]
    public async Task ValidatePageSettingsAsync_ShouldReportGapBeforeFootnote_WhenStillExceedsLimit()
    {
        using var fixture = new SqlitePageEndFixture();
        fixture.AddDokumen();
        fixture.AddFormats();
        fixture.AddActiveAturan();
        fixture.AddNomorHalamanRule(differentFirstPage: false);
        fixture.AddSection(1, 1, hasTitlePage: false);
        fixture.AddBodyPart(1, 1);
        fixture.AddHeaderFooterPart(12, 1, "footer", "default");

        fixture.AddBodyElement(101, 1, 1, 1, "Paragraf halaman pertama", 610f, 650f);
        fixture.AddFootnoteVisual(2001, 1, 730f, 742f);
        fixture.AddPageFieldElement(1001, 12, 2, 1, "1", paragraphFormatId: 11, y0: 730f, y1: 745f);
        fixture.AddBodyElement(102, 1, 3, 2, "Paragraf halaman kedua", 700f, 730f);
        fixture.AddPageFieldElement(1002, 12, 4, 2, "2", paragraphFormatId: 11, y0: 730f, y1: 745f);

        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await service.ValidatePageSettingsAsync(10);

        var error = Assert.Single(result.Errors, e => e.Field == "max_baris_kosong_akhir_halaman");
        Assert.Equal("Sekitar 4 baris kosong", error.Actual);
        var bbox = Assert.Single(error.Locations).Bbox;
        Assert.NotNull(bbox);
        Assert.Equal(730m, bbox!.Y1);
    }

    private static ValidationService CreateValidationService(KorektorBukuDbContext db)
        => new(db, NullLogger<ValidationService>.Instance);

    private sealed class SqlitePageEndFixture : IDisposable
    {
        public SqlitePageEndFixture()
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

        public void AddNomorHalamanRule(bool differentFirstPage)
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 1,
                AturanId = 1,
                AturanDetailKategori = "Nomor Halaman",
                AturanDetailKey = "nomor_halaman",
                AturanDetailJsonValue = $"{{\"variation\":{{\"different_first_page\":{{\"enabled\":{{\"value\":{differentFirstPage.ToString().ToLowerInvariant()}}}}}}}}}",
                AturanDetailStatus = 1
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

        public void AddSection(uint sectionId, uint index, bool hasTitlePage)
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = sectionId,
                DsecRefTipe = "dokumen",
                DsecRefId = 10,
                DsecIndex = index,
                DsecHasTitlePage = hasTitlePage,
                DsecDifferentOddEven = false,
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

        public void AddFootnoteVisual(ulong visualId, uint page, float y0, float y1)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = visualId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DokumenElemenId = null,
                DevBboxX0 = 72,
                DevBboxY0 = y0,
                DevBboxX1 = 460,
                DevBboxY1 = y1,
                DevLabel = "footnote",
                DevLabelStruktural = "footnote"
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
    }
}

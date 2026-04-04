using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanImportServiceTests
{
    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldUseListLevelSamplesForItemDaftar()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifacts(db);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(1);

        var detail = db.AturanDetails.Single(item => item.AturanId == 1 && item.AturanDetailKey == "item_daftar");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.Equal(0.75m, json["paragraph"]!["indentation"]!["left_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(0.75m, json["paragraph"]!["indentation"]!["hanging"]!["value"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldMarkSplitChapterTitleAsEnterAfterNumber()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifacts(db);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(1);

        var detail = db.AturanDetails.Single(item => item.AturanId == 1 && item.AturanDetailKey == "judul_bab");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.True(json["numbering"]!["enter_after_number"]!["value"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldInferCaptionCaseAndEnterAfterFromSplitCaptionParagraphs()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifacts(db);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(1);

        var detail = db.AturanDetails.Single(item => item.AturanId == 1 && item.AturanDetailKey == "gambar");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.True(json["caption_gambar"]!["numbering"]!["enter_after_numbering"]!["value"]!.GetValue<bool>());
        Assert.Equal("Title Case", json["caption_gambar"]!["numbering"]!["case"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldIgnoreNumberingIndentForParagrafRule()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifactsWithNumberedParagraphSamples(db, aturanId: 2);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(2);

        var detail = db.AturanDetails.Single(item => item.AturanId == 2 && item.AturanDetailKey == "paragraf");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.Equal(0m, json["paragraph"]!["indentation"]!["left_indent"]!["value"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldIgnoreNumberingIndentForSubchapterAndCodeRules()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifactsWithNumberedSubchapterAndCodeSamples(db, aturanId: 3);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(3);

        var subchapterDetail = db.AturanDetails.Single(item => item.AturanId == 3 && item.AturanDetailKey == "judul_subbab");
        var subchapterJson = JsonNode.Parse(subchapterDetail.AturanDetailJsonValue!)!.AsObject();

        Assert.Equal(0m, subchapterJson["paragraph"]!["indentation"]!["left_indent"]!["value"]!.GetValue<decimal>());

        var codeDetail = db.AturanDetails.Single(item => item.AturanId == 3 && item.AturanDetailKey == "kode");
        var codeJson = JsonNode.Parse(codeDetail.AturanDetailJsonValue!)!.AsObject();

        Assert.Equal(0m, codeJson["kode"]!["paragraph"]!["indentation"]!["left_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(1.27m, codeJson["kode"]!["paragraph"]!["indentation"]!["hanging"]!["value"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldInferDifferentFirstPageFromFirstSectionOnly()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifactsForDifferentFirstPageInference(db, aturanId: 4, firstHasTitlePage: true, secondHasTitlePage: false);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(4);

        var detail = db.AturanDetails.Single(item => item.AturanId == 4 && item.AturanDetailKey == "nomor_halaman");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.True(json["variation"]!["different_first_page"]!["enabled"]!["value"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldIgnoreLaterSectionDifferentFirstPageWhenFirstSectionDoesNotUseIt()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifactsForDifferentFirstPageInference(db, aturanId: 5, firstHasTitlePage: false, secondHasTitlePage: true);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(5);

        var detail = db.AturanDetails.Single(item => item.AturanId == 5 && item.AturanDetailKey == "nomor_halaman");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.False(json["variation"]!["different_first_page"]!["enabled"]!["value"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldPersistPageSettingsProvenance()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifacts(db);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(1);

        var detail = db.AturanDetails.Single(item => item.AturanId == 1 && item.AturanDetailKey == "page_settings");
        Assert.Equal(
            "template_extracted=paper|margin|header_footer|gutter|column; manual_default=akhir_halaman",
            detail.AturanDetailCatatan);
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldPersistSubchapterHangingRangeAndManualDefaultNotes()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifactsWithNumberedSubchapterAndCodeSamples(db, aturanId: 3);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(3);

        var detail = db.AturanDetails.Single(item => item.AturanId == 3 && item.AturanDetailKey == "judul_subbab");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.Equal(1.27m, json["paragraph"]!["hanging_min_cm"]!["value"]!.GetValue<decimal>());
        Assert.Equal(1.27m, json["paragraph"]!["hanging_max_cm"]!["value"]!.GetValue<decimal>());
        Assert.Null(json["paragraph"]!["indentation"]!["hanging"]);
        Assert.Contains("par.hanging_range", detail.AturanDetailCatatan);
        Assert.Contains("manual_default=num.format|struct", detail.AturanDetailCatatan);
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldKeepManualDefaultCodeNumberingAndCompactCatatan()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifactsWithNumberedSubchapterAndCodeSamples(db, aturanId: 3);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(3);

        var detail = db.AturanDetails.Single(item => item.AturanId == 3 && item.AturanDetailKey == "kode");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.True(json["kode"]!["numbering"]!["use_numbering"]!["value"]!.GetValue<bool>());
        Assert.Contains("manual_default=code.num", detail.AturanDetailCatatan);
        Assert.DoesNotContain("template_extracted=code.num", detail.AturanDetailCatatan);
        Assert.True(detail.AturanDetailCatatan!.Length <= 255);
    }

    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldMarkTableContinuationAndConstraintsAsManualDefaultWhenNoTemplateSampleExists()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        SeedAturanArtifacts(db);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.ImportFromArtifactsAsync(1);

        var detail = db.AturanDetails.Single(item => item.AturanId == 1 && item.AturanDetailKey == "tabel");
        var json = JsonNode.Parse(detail.AturanDetailJsonValue!)!.AsObject();

        Assert.Contains("tbl.no_image", detail.AturanDetailCatatan);
        Assert.Contains("cap.cont", detail.AturanDetailCatatan);
        Assert.DoesNotContain("template_extracted=tbl.no_image", detail.AturanDetailCatatan);
        Assert.True(json["caption_tabel"]!["wajib_caption_lanjutan_jika_lintas_halaman"]!["value"]!.GetValue<bool>());
    }

    private static AturanImportService CreateService(KorektorBukuDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        return new AturanImportService(
            db,
            new StubEmailService(),
            configuration,
            NullLogger<AturanImportService>.Instance);
    }

    private static void SeedAturanArtifacts(KorektorBukuDbContext db)
    {
        db.Aturans.Add(new Aturan
        {
            AturanId = 1,
            AturanVersi = "Template Importer",
            AturanStatus = AturanStatusValues.Diproses
        });

        db.DokumenSections.Add(new DokumenSection
        {
            DsecId = 1,
            DsecRefTipe = "aturan",
            DsecRefId = 1,
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

        db.DokumenParts.Add(new DokumenPart
        {
            DpartId = 1,
            DsecId = 1,
            DpartType = "body"
        });

        db.DokumenFormatParagrafs.AddRange(
            new DokumenFormatParagraf
            {
                DfpId = 10,
                DfpJc = "center",
                DfpSpacingLineTwips = 360,
                DfpSpacingLineRule = "auto"
            },
            new DokumenFormatParagraf
            {
                DfpId = 20,
                DfpJc = "both",
                DfpIndLeftTwips = 425,
                DfpIndHangingTwips = 425,
                DfpSpacingLineTwips = 360,
                DfpSpacingLineRule = "auto",
                DfpIsList = true
            },
            new DokumenFormatParagraf
            {
                DfpId = 30,
                DfpJc = "center",
                DfpSpacingLineTwips = 240,
                DfpSpacingLineRule = "auto"
            });

        db.DokumenFormatTexts.AddRange(
            new DokumenFormatText
            {
                DftxId = 100,
                DftxFontAscii = "Times New Roman",
                DftxSizeHalfpt = 32,
                DftxBold = true
            },
            new DokumenFormatText
            {
                DftxId = 101,
                DftxFontAscii = "Times New Roman",
                DftxSizeHalfpt = 24,
                DftxBold = false
            },
            new DokumenFormatText
            {
                DftxId = 102,
                DftxFontAscii = "Times New Roman",
                DftxSizeHalfpt = 24,
                DftxBold = true
            });

        db.DokumenElemens.AddRange(
            new DokumenElemen
            {
                DelemenId = 1,
                DpartId = 1,
                DelemenSequence = 1,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(10, 100, "BAB I")
            },
            new DokumenElemen
            {
                DelemenId = 2,
                DpartId = 1,
                DelemenSequence = 2,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(10, 100, "PENDAHULUAN")
            },
            new DokumenElemen
            {
                DelemenId = 3,
                DpartId = 1,
                DelemenSequence = 3,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(20, 101, "a. Item pertama")
            },
            new DokumenElemen
            {
                DelemenId = 4,
                DpartId = 1,
                DelemenSequence = 4,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(20, 101, "b. Item kedua")
            },
            new DokumenElemen
            {
                DelemenId = 5,
                DpartId = 1,
                DelemenSequence = 5,
                DelemenType = "image",
                DelemenXml = "<w:drawing />",
                DelemenJsonTree = """{"content":[{"type":"drawing","dfdr_id":1}]}"""
            },
            new DokumenElemen
            {
                DelemenId = 6,
                DpartId = 1,
                DelemenSequence = 6,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(30, 102, "Gambar 1.1")
            },
            new DokumenElemen
            {
                DelemenId = 7,
                DpartId = 1,
                DelemenSequence = 7,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(30, 102, "Posisi Potret Elang")
            });

        db.DokumenElemenVisuals.AddRange(
            CreateVisual(1, 1, "judul_bab"),
            CreateVisual(2, 2, "judul_bab"),
            CreateVisual(3, 3, "list_level_1"),
            CreateVisual(4, 4, "list_level_2"),
            CreateVisual(5, 5, "gambar"),
            CreateVisual(6, 6, "caption_gambar"),
            CreateVisual(7, 7, "caption_gambar"));
    }

    private static void SeedAturanArtifactsWithNumberedParagraphSamples(KorektorBukuDbContext db, uint aturanId)
    {
        db.Aturans.Add(new Aturan
        {
            AturanId = aturanId,
            AturanVersi = "Template Importer Numbered Paragraphs",
            AturanStatus = AturanStatusValues.Diproses
        });

        db.DokumenSections.Add(new DokumenSection
        {
            DsecId = aturanId,
            DsecRefTipe = "aturan",
            DsecRefId = aturanId,
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

        db.DokumenParts.Add(new DokumenPart
        {
            DpartId = aturanId,
            DsecId = aturanId,
            DpartType = "body"
        });

        db.DokumenFormatParagrafs.Add(new DokumenFormatParagraf
        {
            DfpId = 40,
            DfpJc = "both",
            DfpIndLeftTwips = 720,
            DfpIndHangingTwips = 360,
            DfpSpacingLineTwips = 360,
            DfpSpacingLineRule = "auto",
            DfpIsList = true,
            DfpListNumId = 11
        });

        db.DokumenFormatTexts.Add(new DokumenFormatText
        {
            DftxId = 200,
            DftxFontAscii = "Times New Roman",
            DftxSizeHalfpt = 24,
            DftxBold = false
        });

        db.DokumenElemens.AddRange(
            new DokumenElemen
            {
                DelemenId = 20,
                DpartId = aturanId,
                DelemenSequence = 1,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(40, 200, "Paragraf contoh pertama.")
            },
            new DokumenElemen
            {
                DelemenId = 21,
                DpartId = aturanId,
                DelemenSequence = 2,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(40, 200, "Paragraf contoh kedua.")
            });

        db.DokumenElemenVisuals.AddRange(
            CreateVisual(20, 20, "paragraf", aturanId),
            CreateVisual(21, 21, "paragraf", aturanId));
    }

    private static void SeedAturanArtifactsWithNumberedSubchapterAndCodeSamples(KorektorBukuDbContext db, uint aturanId)
    {
        db.Aturans.Add(new Aturan
        {
            AturanId = aturanId,
            AturanVersi = "Template Importer Numbered Subchapter And Code",
            AturanStatus = AturanStatusValues.Diproses
        });

        db.DokumenSections.Add(new DokumenSection
        {
            DsecId = aturanId,
            DsecRefTipe = "aturan",
            DsecRefId = aturanId,
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

        db.DokumenParts.Add(new DokumenPart
        {
            DpartId = aturanId,
            DsecId = aturanId,
            DpartType = "body"
        });

        db.DokumenFormatParagrafs.AddRange(
            new DokumenFormatParagraf
            {
                DfpId = 50,
                DfpJc = "left",
                DfpIndLeftTwips = 720,
                DfpIndHangingTwips = 720,
                DfpSpacingLineTwips = 360,
                DfpSpacingLineRule = "auto",
                DfpIsList = true,
                DfpListNumId = 21
            },
            new DokumenFormatParagraf
            {
                DfpId = 51,
                DfpJc = "left",
                DfpIndLeftTwips = 720,
                DfpIndHangingTwips = 720,
                DfpSpacingLineTwips = 240,
                DfpSpacingLineRule = "auto",
                DfpIsList = true,
                DfpListNumId = 22
            });

        db.DokumenFormatTexts.AddRange(
            new DokumenFormatText
            {
                DftxId = 210,
                DftxFontAscii = "Times New Roman",
                DftxSizeHalfpt = 28,
                DftxBold = true
            },
            new DokumenFormatText
            {
                DftxId = 211,
                DftxFontAscii = "Courier New",
                DftxSizeHalfpt = 20,
                DftxBold = false
            });

        db.DokumenElemens.AddRange(
            new DokumenElemen
            {
                DelemenId = 30,
                DpartId = aturanId,
                DelemenSequence = 1,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(50, 210, "1.1 Latar Belakang")
            },
            new DokumenElemen
            {
                DelemenId = 31,
                DpartId = aturanId,
                DelemenSequence = 2,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(50, 210, "1.2 Rumusan Masalah")
            },
            new DokumenElemen
            {
                DelemenId = 32,
                DpartId = aturanId,
                DelemenSequence = 3,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(51, 211, "1 Input nilai")
            },
            new DokumenElemen
            {
                DelemenId = 33,
                DpartId = aturanId,
                DelemenSequence = 4,
                DelemenType = "paragraph",
                DelemenXml = "<w:p />",
                DelemenJsonTree = CreateParagraphJson(51, 211, "2 Tampilkan hasil")
            });

        db.DokumenElemenVisuals.AddRange(
            CreateVisual(30, 30, "judul_subbab", aturanId),
            CreateVisual(31, 31, "judul_subbab", aturanId),
            CreateVisual(32, 32, "kode", aturanId),
            CreateVisual(33, 33, "kode", aturanId));
    }

    private static void SeedAturanArtifactsForDifferentFirstPageInference(
        KorektorBukuDbContext db,
        uint aturanId,
        bool firstHasTitlePage,
        bool secondHasTitlePage)
    {
        db.Aturans.Add(new Aturan
        {
            AturanId = aturanId,
            AturanVersi = "Template Importer Page Order",
            AturanStatus = AturanStatusValues.Diproses
        });

        db.DokumenSections.AddRange(
            CreateAturanSection((uint)(aturanId * 10 + 1), aturanId, 1, firstHasTitlePage),
            CreateAturanSection((uint)(aturanId * 10 + 2), aturanId, 2, secondHasTitlePage));
    }

    private static DokumenSection CreateAturanSection(uint sectionId, uint aturanId, uint index, bool hasTitlePage)
    {
        return new DokumenSection
        {
            DsecId = sectionId,
            DsecRefTipe = "aturan",
            DsecRefId = aturanId,
            DsecIndex = index,
            DsecHasTitlePage = hasTitlePage,
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
        };
    }

    private static DokumenElemenVisual CreateVisual(ulong visualId, ulong elementId, string label, uint aturanId = 1)
    {
        return new DokumenElemenVisual
        {
            DevId = visualId,
            DevRefTipe = "aturan",
            DevRefId = aturanId,
            DokumenElemenId = elementId,
            DevLabelStruktural = label
        };
    }

    private static string CreateParagraphJson(uint paragraphFormatId, uint textFormatId, string text)
    {
        return $$"""
        {"dfp_id":{{paragraphFormatId}},"content":[{"type":"text","value":"{{text}}","dftx_id":{{textFormatId}}}]}
        """;
    }

    private sealed class StubEmailService : IEmailService
    {
        public Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string bodyHtml)
            => Task.FromResult(true);

        public Task<bool> SendEmailAsync(List<(string Email, string Name)> recipients, string subject, string bodyHtml)
            => Task.FromResult(true);

        public Task<bool> SendEmailWithAttachmentAsync(string toEmail, string toName, string subject, string bodyHtml, string attachmentPath)
            => Task.FromResult(true);

        public Task<bool> SendValidationCompleteNotificationAsync(
            string toEmail,
            string toName,
            string resourceType,
            int resourceId,
            string resourceTitle,
            bool isLolos,
            int errorCount)
            => Task.FromResult(true);
    }
}

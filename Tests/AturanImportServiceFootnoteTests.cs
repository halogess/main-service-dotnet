using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanImportServiceFootnoteTests
{
    [Fact]
    public async Task ImportFromArtifactsAsync_ShouldPopulateFootnoteRuleFromDokumenNote()
    {
        using var db = ControllerTestHelpers.CreateDbContext();

        db.Aturans.Add(new Aturan
        {
            AturanId = 7,
            AturanVersi = "v-test",
            AturanStatus = AturanStatusValues.Diproses
        });

        db.DokumenFormatParagrafs.Add(new DokumenFormatParagraf
        {
            DfpId = 77,
            DfpJc = "left",
            DfpSpacingLineTwips = 240,
            DfpSpacingLineRule = "auto",
            DfpSpacingBeforeTwips = 0,
            DfpSpacingAfterTwips = 0
        });

        db.DokumenFormatTexts.Add(new DokumenFormatText
        {
            DftxId = 55,
            DftxFontAscii = "Garamond",
            DftxSizeHalfpt = 20
        });

        db.DokumenNotes.Add(new DokumenNote
        {
            DnoteId = 99,
            DnoteRefTipe = "aturan",
            DnoteRefId = 7,
            DnoteKind = "footnote",
            DnoteType = "normal",
            DnoteNumber = 1,
            DnoteJsonTree = """{"content":[{"type":"paragraph","dfp_id":77,"content":[{"type":"text","dftx_id":55,"value":"Catatan aturan"}]}]}"""
        });

        await db.SaveChangesAsync();

        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(service => service.SendEmailAsync(It.IsAny<List<(string Email, string Name)>>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new AturanImportService(
            db,
            emailService.Object,
            configuration,
            NullLogger<AturanImportService>.Instance);

        await service.ImportFromArtifactsAsync(7);

        var detail = db.AturanDetails.Single(item => item.AturanId == 7 && item.AturanDetailKey == "footnote");
        using var json = JsonDocument.Parse(detail.AturanDetailJsonValue ?? "{}");

        var root = json.RootElement;
        Assert.Equal("Garamond", root.GetProperty("footnote_text").GetProperty("font").GetProperty("font_name").GetProperty("value").GetString());
        Assert.Equal(10m, root.GetProperty("footnote_text").GetProperty("font").GetProperty("font_size").GetProperty("value").GetDecimal());
        Assert.Equal("left", root.GetProperty("footnote_text").GetProperty("paragraph").GetProperty("alignment").GetProperty("value").GetString());
        Assert.Equal(1m, root.GetProperty("footnote_text").GetProperty("paragraph").GetProperty("spacing").GetProperty("line_spacing").GetProperty("value").GetDecimal());
        Assert.Contains("text.font", detail.AturanDetailCatatan);
        Assert.Contains("text.par", detail.AturanDetailCatatan);
    }
}

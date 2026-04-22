using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanServiceTests
{
    [Fact]
    public async Task GetByIdWithDetailsAsync_ShouldReturnCanonicalDetailsOrderedByCatalog()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 1,
            AturanVersi = "v1",
            AturanStatus = AturanStatusValues.Aktif
        });
        db.AturanDetails.AddRange(
            new AturanDetail
            {
                AturanDetailId = 20,
                AturanId = 1,
                AturanDetailKey = "gambar"
            },
            new AturanDetail
            {
                AturanDetailId = 10,
                AturanId = 1,
                AturanDetailKey = "page_settings"
            },
            new AturanDetail
            {
                AturanDetailId = 30,
                AturanId = 1,
                AturanDetailKey = "paper"
            });
        await db.SaveChangesAsync();

        var service = new AturanService(
            db,
            Mock.Of<IFileService>(),
            Mock.Of<IExtractionArtifactCleanupService>(),
            Mock.Of<ILogger<AturanService>>());

        var result = await service.GetByIdWithDetailsAsync(1);

        Assert.NotNull(result);
        Assert.Equal(["page_settings", "gambar"], result!.Details.Select(d => d.AturanDetailKey).ToList());
        Assert.Equal([(uint)10, (uint)20], result.Details.Select(d => d.AturanDetailId).ToList());
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_ShouldCanonicalizeEditablePolicyForReadResponses()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 2,
            AturanVersi = "v2",
            AturanStatus = AturanStatusValues.Aktif
        });
        db.AturanDetails.AddRange(
            new AturanDetail
            {
                AturanDetailId = 30,
                AturanId = 2,
                AturanDetailKey = "judul_bab",
                AturanDetailJsonValue = """
                    {
                      "paragraph": {
                        "alignment": {
                          "value": "center",
                          "is_editable": false,
                          "is_hard_constraint": false
                        }
                      },
                      "numbering": {
                        "number_format": {
                          "value": "BAB I",
                          "is_editable": true,
                          "is_hard_constraint": false
                        }
                      }
                    }
                    """
            },
            new AturanDetail
            {
                AturanDetailId = 40,
                AturanId = 2,
                AturanDetailKey = "footnote",
                AturanDetailJsonValue = """
                    {
                      "separator": {
                        "paragraph": {
                          "alignment": {
                            "value": "left",
                            "is_editable": false,
                            "is_hard_constraint": false
                          }
                        }
                      }
                    }
                    """
            });
        await db.SaveChangesAsync();

        var service = new AturanService(
            db,
            Mock.Of<IFileService>(),
            Mock.Of<IExtractionArtifactCleanupService>(),
            Mock.Of<ILogger<AturanService>>());

        var result = await service.GetByIdWithDetailsAsync(2);

        Assert.NotNull(result);

        var chapter = JsonNode.Parse(result!.Details.Single(detail => detail.AturanDetailKey == "judul_bab").AturanDetailJsonValue!)!.AsObject();
        Assert.True(chapter["paragraph"]!["alignment"]!["is_editable"]!.GetValue<bool>());
        Assert.False(chapter["numbering"]!["number_format"]!["is_editable"]!.GetValue<bool>());

        var footnote = JsonNode.Parse(result.Details.Single(detail => detail.AturanDetailKey == "footnote").AturanDetailJsonValue!)!.AsObject();
        Assert.True(footnote["separator"]!["paragraph"]!["alignment"]!["is_editable"]!.GetValue<bool>());
    }
}

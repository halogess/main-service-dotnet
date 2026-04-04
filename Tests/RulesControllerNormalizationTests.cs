using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using Xunit;

namespace Tests;

public class RulesControllerNormalizationTests
{
    [Fact]
    public async Task Create_ShouldNormalizeDetailJsonValue()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        var controller = new RulesController(db);

        var request = new RulesCreateRequest
        {
            aturan_versi = "v1",
            details =
            [
                new RulesDetailRequest
                {
                    aturan_detail_kategori = "Nomor Halaman",
                    aturan_detail_key = "nomor_halaman_akhir",
                    aturan_detail_json_value = """{"continue":true}"""
                }
            ]
        };

        var result = await controller.Create(request);

        Assert.IsType<OkObjectResult>(result);
        var stored = db.AturanDetails.Single();
        Assert.Equal(
            """{"continue":{"value":true,"is_editable":false,"is_hard_constraint":false}}""",
            stored.AturanDetailJsonValue);
    }

    [Fact]
    public async Task Update_ShouldNormalizeExistingAndNewDetailJsonValues()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 2,
            AturanVersi = "v2"
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 20,
            AturanId = 2,
            AturanDetailKey = "nomor_halaman_akhir",
            AturanDetailJsonValue = """{"continue":true}"""
        });
        await db.SaveChangesAsync();

        var controller = new RulesController(db);
        var request = new RulesUpdateRequest
        {
            details =
            [
                new RulesDetailUpdateRequest
                {
                    aturan_detail_id = 20,
                    aturan_detail_json_value = """{"continue":false}"""
                },
                new RulesDetailUpdateRequest
                {
                    aturan_detail_kategori = "Nomor Halaman",
                    aturan_detail_key = "nomor_halaman_isi",
                    aturan_detail_json_value = """{"continue":false,"different_first_page":"True"}"""
                }
            ]
        };

        var result = await controller.Update(2, request);

        Assert.IsType<OkObjectResult>(result);

        var details = db.AturanDetails
            .Where(d => d.AturanId == 2)
            .OrderBy(d => d.AturanDetailId)
            .ToList();

        Assert.Equal(2, details.Count);
        Assert.Equal(
            """{"continue":{"value":false,"is_editable":false,"is_hard_constraint":false}}""",
            details[0].AturanDetailJsonValue);
        Assert.Equal(
            """{"continue":{"value":false,"is_editable":false,"is_hard_constraint":false},"different_first_page":{"value":"True","is_editable":false,"is_hard_constraint":false}}""",
            details[1].AturanDetailJsonValue);
    }

    [Fact]
    public async Task Create_ShouldCanonicalizeLegacyCodeShape()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        var controller = new RulesController(db);

        var request = new RulesCreateRequest
        {
            aturan_versi = "v-canonical",
            details =
            [
                new RulesDetailRequest
                {
                    aturan_detail_kategori = "Isi Buku",
                    aturan_detail_key = "kode",
                    aturan_detail_json_value = """{"judul_kode":{"numbering":{"enter_after_number":true}}}"""
                }
            ]
        };

        var result = await controller.Create(request);

        Assert.IsType<OkObjectResult>(result);
        var stored = db.AturanDetails.Single();
        var json = System.Text.Json.Nodes.JsonNode.Parse(stored.AturanDetailJsonValue!)!.AsObject();

        Assert.True(json["judul_kode"]!["numbering"]!["enter_after_numbering"]!["value"]!.GetValue<bool>());
        Assert.Null(json["judul_kode"]!["numbering"]!["enter_after_number"]);
        Assert.True(json["judul_kode"]!["wajib_caption_lanjutan_jika_lintas_halaman"]!["value"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Create_ShouldCanonicalizeLegacyParagrafParagraphShape()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        var controller = new RulesController(db);

        var request = new RulesCreateRequest
        {
            aturan_versi = "v1",
            details =
            [
                new RulesDetailRequest
                {
                    aturan_detail_kategori = "Isi Buku",
                    aturan_detail_key = "paragraf",
                    aturan_detail_json_value = """{"paragraph":{"alignment":"justify","left_indent":0,"right_indent":0,"first_line_indent":1.27,"spacing":{"line_spacing":1.5,"before":0,"after":0}}}"""
                }
            ]
        };

        var result = await controller.Create(request);

        Assert.IsType<OkObjectResult>(result);

        var stored = db.AturanDetails.Single();
        var json = System.Text.Json.Nodes.JsonNode.Parse(stored.AturanDetailJsonValue!)!.AsObject();

        Assert.Equal(0m, json["paragraph"]!["indentation"]!["left_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(0m, json["paragraph"]!["indentation"]!["right_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(1.27m, json["paragraph"]!["indentation"]!["first_line_indent"]!["value"]!.GetValue<decimal>());
        Assert.Null(json["paragraph"]!["left_indent"]);
        Assert.Null(json["paragraph"]!["right_indent"]);
        Assert.Null(json["paragraph"]!["first_line_indent"]);
    }
}

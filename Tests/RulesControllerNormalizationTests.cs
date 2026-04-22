using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;
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
                    aturan_detail_key = "nomor_halaman",
                    aturan_detail_json_value = """{"numbering":{"number_format":"decimal"}}"""
                }
            ]
        };

        var result = await controller.Create(request);

        Assert.IsType<OkObjectResult>(result);
        var stored = db.AturanDetails.Single();
        Assert.Equal("nomor_halaman", stored.AturanDetailKey);
        Assert.Equal("Nomor Halaman", stored.AturanDetailKategori);
        var json = JsonNode.Parse(stored.AturanDetailJsonValue!)!.AsObject();
        Assert.Equal("decimal", json["numbering"]!["number_format"]!["value"]!.GetValue<string>());
        Assert.False(json["numbering"]!["number_format"]!["is_editable"]!.GetValue<bool>());
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
            AturanDetailKategori = "Nomor Halaman",
            AturanDetailKey = "nomor_halaman",
            AturanDetailJsonValue = """{"numbering":{"number_format":{"value":"decimal","is_editable":false,"is_hard_constraint":false}}}"""
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
                    aturan_detail_key = "nomor_halaman",
                    aturan_detail_json_value = """{"variation":{"different_odd_even":{"enabled":true}}}"""
                },
                new RulesDetailUpdateRequest
                {
                    aturan_detail_kategori = "Isi Buku",
                    aturan_detail_key = "judul_bab",
                    aturan_detail_json_value = """{"numbering":{"number_format":"BAB I"}}"""
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
        Assert.Equal(["nomor_halaman", "judul_bab"], details.Select(detail => detail.AturanDetailKey).ToArray());

        var pageNumberJson = JsonNode.Parse(details[0].AturanDetailJsonValue!)!.AsObject();
        Assert.True(pageNumberJson["variation"]!["different_odd_even"]!["enabled"]!["value"]!.GetValue<bool>());
        Assert.False(pageNumberJson["numbering"]!["number_format"]!["is_editable"]!.GetValue<bool>());

        var chapterJson = JsonNode.Parse(details[1].AturanDetailJsonValue!)!.AsObject();
        Assert.Equal("BAB I", chapterJson["numbering"]!["number_format"]!["value"]!.GetValue<string>());
        Assert.False(chapterJson["numbering"]!["number_format"]!["is_editable"]!.GetValue<bool>());
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

    [Fact]
    public async Task GetById_ShouldCanonicalizeEditablePolicyForReadResponses()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 3,
            AturanVersi = "v-read"
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 31,
            AturanId = 3,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "gambar",
            AturanDetailJsonValue = """
                {
                  "caption_gambar": {
                    "paragraph": {
                      "alignment": {
                        "value": "center",
                        "is_editable": false,
                        "is_hard_constraint": false
                      }
                    },
                    "numbering": {
                      "number_format": {
                        "value": "Gambar [nomor_bab].[nomor_gambar]",
                        "is_editable": true,
                        "is_hard_constraint": false
                      }
                    }
                  }
                }
                """
        });
        await db.SaveChangesAsync();

        var controller = new RulesController(db);

        var result = await controller.GetById(3);

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = ok.Value!;
        var details = Assert.IsAssignableFrom<System.Collections.IEnumerable>(value.GetType().GetProperty("details")!.GetValue(value));
        var firstDetail = details.Cast<object>().Single();
        var jsonValue = Assert.IsType<string>(firstDetail.GetType().GetProperty("aturan_detail_json_value")!.GetValue(firstDetail));
        var json = JsonNode.Parse(jsonValue)!.AsObject();

        Assert.True(json["caption_gambar"]!["paragraph"]!["alignment"]!["is_editable"]!.GetValue<bool>());
        Assert.False(json["caption_gambar"]!["numbering"]!["number_format"]!["is_editable"]!.GetValue<bool>());
    }
}

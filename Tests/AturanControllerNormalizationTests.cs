using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanControllerNormalizationTests
{
    [Fact]
    public async Task GetAturanById_ShouldIncludeAturanMetadataForAdminConsumers()
    {
        var aturanService = new Mock<IAturanService>();
        aturanService
            .Setup(service => service.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(new AturanWithDetails
            {
                Aturan = new Aturan
                {
                    AturanId = 1,
                    AturanVersi = "v1",
                    AturanStatus = AturanStatusValues.Aktif,
                    AturanSkorMinimum = 90,
                    AturanTemplateFilePath = "templates/template.dotx",
                    AturanCreatedAt = new DateTime(2026, 3, 1, 10, 0, 0),
                    AturanUpdatedAt = new DateTime(2026, 3, 2, 11, 0, 0)
                },
                Details =
                [
                    new AturanDetail
                    {
                        AturanDetailId = 10,
                        AturanId = 1,
                        AturanDetailKategori = "Isi Buku",
                        AturanDetailKey = "paragraf",
                        AturanDetailJsonValue = """{"font":{"font_name":"Times New Roman"}}""",
                        AturanDetailCatatan = "template_extracted=font|par; manual_default=struct"
                    }
                ]
            });

        await using var db = ControllerTestHelpers.CreateDbContext();
        var controller = new AturanController(aturanService.Object, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items["Role"] = "admin";

        var result = await controller.GetAturanById(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = ok.Value!;
        var valueType = value.GetType();

        Assert.Equal((uint)1, valueType.GetProperty("id")!.GetValue(value));
        Assert.Equal((uint)90, valueType.GetProperty("skor_minimum")!.GetValue(value));
        Assert.Equal("templates/template.dotx", valueType.GetProperty("template_file_path")!.GetValue(value));
        var detailRows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(valueType.GetProperty("aturan_detail")!.GetValue(value));
        var firstDetail = detailRows.Cast<object>().First();
        var detailType = firstDetail.GetType();

        Assert.Equal("template_extracted=font|par; manual_default=struct", detailType.GetProperty("catatan")!.GetValue(firstDetail));
        Assert.Equal("template_extracted=font|par; manual_default=struct", detailType.GetProperty("aturan_detail_catatan")!.GetValue(firstDetail));
    }

    [Fact]
    public async Task PatchAturanDetail_ShouldNormalizeJsonValueBeforeSaving()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 1,
            AturanVersi = "v1",
            AturanStatus = AturanStatusValues.MenungguReview
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 10,
            AturanId = 1,
            AturanDetailKey = "nomor_halaman_akhir",
            AturanDetailJsonValue = """{"continue":true}"""
        });
        await db.SaveChangesAsync();

        var controller = new AturanController(Mock.Of<IAturanService>(), db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items["Role"] = "admin";

        var request = new AturanDetailPatchRequest
        {
            details =
            [
                new AturanDetailPatchItem
                {
                    aturan_detail_id = 10,
                    json_value = """{"continue":true}"""
                }
            ]
        };

        var result = await controller.PatchAturanDetail(1, request);

        Assert.IsType<OkObjectResult>(result);
        var updated = await db.AturanDetails.FindAsync((uint)10);
        Assert.Equal(
            """{"continue":{"value":true,"is_editable":false,"is_hard_constraint":false}}""",
            updated!.AturanDetailJsonValue);
        var aturan = await db.Aturans.FindAsync((uint)1);
        Assert.Equal(AturanStatusValues.TidakAktif, aturan!.AturanStatus);
    }

    [Fact]
    public async Task PatchAturanDetail_ShouldKeepActiveStatusWhenEditingActiveAturan()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 2,
            AturanVersi = "v2",
            AturanStatus = AturanStatusValues.Aktif
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 20,
            AturanId = 2,
            AturanDetailKey = "nomor_halaman_akhir",
            AturanDetailJsonValue = """{"continue":{"value":false,"is_editable":false,"is_hard_constraint":false}}"""
        });
        await db.SaveChangesAsync();

        var controller = new AturanController(Mock.Of<IAturanService>(), db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items["Role"] = "admin";

        var request = new AturanDetailPatchRequest
        {
            details =
            [
                new AturanDetailPatchItem
                {
                    aturan_detail_id = 20,
                    json_value = """{"continue":true}"""
                }
            ]
        };

        var result = await controller.PatchAturanDetail(2, request);

        Assert.IsType<OkObjectResult>(result);
        var aturan = await db.Aturans.FindAsync((uint)2);
        Assert.Equal(AturanStatusValues.Aktif, aturan!.AturanStatus);
    }

    [Fact]
    public async Task PatchAturanDetail_ShouldRejectInvalidJson()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 1,
            AturanVersi = "v1"
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 10,
            AturanId = 1,
            AturanDetailKey = "paper",
            AturanDetailJsonValue = """{"section":{"isi":{"value":"A4","is_editable":true}}}"""
        });
        await db.SaveChangesAsync();

        var controller = new AturanController(Mock.Of<IAturanService>(), db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items["Role"] = "admin";

        var request = new AturanDetailPatchRequest
        {
            details =
            [
                new AturanDetailPatchItem
                {
                    aturan_detail_id = 10,
                    json_value = """{"section":"""
                }
            ]
        };

        var result = await controller.PatchAturanDetail(1, request);

        Assert.IsType<BadRequestObjectResult>(result);
        var unchanged = await db.AturanDetails.FindAsync((uint)10);
        Assert.Equal(
            """{"section":{"isi":{"value":"A4","is_editable":true}}}""",
            unchanged!.AturanDetailJsonValue);
    }

    [Fact]
    public async Task PatchAturanDetail_ShouldRejectLegacyJudulSubbabParagraphShape()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 1,
            AturanVersi = "v1"
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 11,
            AturanId = 1,
            AturanDetailKey = "judul_subbab",
            AturanDetailJsonValue = """{"paragraph":{"alignment":{"value":"justify","is_editable":true,"is_hard_constraint":false},"indentation":{"left_indent":{"value":0,"is_editable":true,"is_hard_constraint":false},"right_indent":{"value":0,"is_editable":true,"is_hard_constraint":false}},"hanging_min_cm":{"value":1.27,"is_editable":true,"is_hard_constraint":false},"hanging_max_cm":{"value":2.5,"is_editable":true,"is_hard_constraint":false}}}"""
        });
        await db.SaveChangesAsync();

        var controller = new AturanController(Mock.Of<IAturanService>(), db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items["Role"] = "admin";

        var request = new AturanDetailPatchRequest
        {
            details =
            [
                new AturanDetailPatchItem
                {
                    aturan_detail_id = 11,
                    json_value = """{"paragraph":{"alignment":"justify","left_indent":0,"right_indent":0,"hanging_min_cm":1.27,"hanging_max_cm":2.5}}"""
                }
            ]
        };

        var result = await controller.PatchAturanDetail(1, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("paragraph.indentation.left_indent/right_indent", badRequest.Value!.ToString());
    }
}

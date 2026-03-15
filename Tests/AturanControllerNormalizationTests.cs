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
    public async Task PatchAturanDetail_ShouldNormalizeJsonValueBeforeSaving()
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
}

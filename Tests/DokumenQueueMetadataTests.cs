using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class DokumenQueueMetadataTests
{
    [Fact]
    public async Task GetDokumenById_ShouldExposeQueueFailureMetadata()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 41,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab1.docx",
            DokumenStatus = "diproses"
        });
        db.Antrians.Add(new Antrian
        {
            AntrianId = 411,
            AntrianTipe = "dokumen",
            DokumenId = 41,
            AntrianExtractionStatus = "completed",
            AntrianLabelingStatus = "failed",
            AntrianValidationStatus = "in_queue",
            AntrianErrorMessage = "Adobe conversion gagal",
            AntrianCreatedAt = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Local)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, sttsDb);

        var result = await controller.GetDokumenById(41);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.True(root.GetProperty("has_failed_queue").GetBoolean());
        Assert.Equal("completed", root.GetProperty("extraction_status").GetString());
        Assert.Equal("failed", root.GetProperty("labeling_status").GetString());
        Assert.Equal("in_queue", root.GetProperty("validation_status").GetString());
        Assert.Equal("Adobe conversion gagal", root.GetProperty("error_message").GetString());
    }

    [Fact]
    public async Task GetDokumen_ShouldUseLatestQueueMetadataInsteadOfOlderFailure()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 42,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab2.docx",
            DokumenStatus = "diproses"
        });
        db.Antrians.AddRange(
            new Antrian
            {
                AntrianId = 421,
                AntrianTipe = "dokumen",
                DokumenId = 42,
                AntrianExtractionStatus = "failed",
                AntrianErrorMessage = "Failure lama",
                AntrianCreatedAt = new DateTime(2026, 4, 15, 9, 0, 0, DateTimeKind.Local)
            },
            new Antrian
            {
                AntrianId = 422,
                AntrianTipe = "dokumen",
                DokumenId = 42,
                AntrianExtractionStatus = "completed",
                AntrianLabelingStatus = "completed",
                AntrianValidationStatus = "completed",
                AntrianCreatedAt = new DateTime(2026, 4, 15, 11, 0, 0, DateTimeKind.Local)
            });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            sttsDb,
            new DokumenService(
                Mock.Of<IFileService>(),
                db,
                Mock.Of<ILogger<DokumenService>>(),
                Mock.Of<IWebSocketService>()));

        var result = controller.GetDokumen();

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var item = json.RootElement.GetProperty("data")[0];

        Assert.False(item.GetProperty("has_failed_queue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("error_message").ValueKind);
    }

    private static DokumenController CreateController(
        KorektorBukuDbContext db,
        SttsDbContext sttsDb,
        IDokumenService? dokumenService = null)
    {
        var controller = new DokumenController(
            db,
            sttsDb,
            dokumenService ?? Mock.Of<IDokumenService>(),
            Mock.Of<IDokumenImportService>(),
            Mock.Of<IDokumenHistoryPurgeService>(),
            Mock.Of<IValidationReportService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["Nrp"] = "05111740000123";
        controller.HttpContext.Items["Role"] = "mahasiswa";
        return controller;
    }
}

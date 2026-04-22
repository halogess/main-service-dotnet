using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class BukuControllerCancellationTests
{
    [Fact]
    public async Task BatalBuku_ShouldCancelBukuAndClearLegacyBabQueues()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();

        db.Bukus.Add(new Buku
        {
            BukuId = 31,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Cancel",
            BukuStatus = "diproses"
        });
        db.Babs.Add(new Bab
        {
            BabId = 311,
            BukuId = 31,
            BabOrder = 1,
            BabFilename = "BAB I.docx"
        });
        db.Antrians.AddRange(
            new Antrian
            {
                AntrianId = 3111,
                AntrianTipe = "buku",
                BukuId = 31,
                BabId = 311,
                AntrianLabelingStatus = "processing",
                AntrianValidationStatus = "failed"
            },
            new Antrian
            {
                AntrianId = 3112,
                AntrianTipe = "buku",
                BabId = 311,
                AntrianExtractionStatus = "in_queue",
                AntrianValidationStatus = "completed"
            });
        await db.SaveChangesAsync();

        var wsService = new Mock<IWebSocketService>(MockBehavior.Strict);
        wsService
            .Setup(service => service.NotifyBukuCancelled("05111740000123", 31))
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            db,
            sttsDb,
            Mock.Of<IBukuService>(),
            wsService.Object);

        var result = await controller.BatalBuku(31);

        Assert.IsType<OkObjectResult>(result);

        var buku = await db.Bukus.SingleAsync();
        var queues = await db.Antrians.OrderBy(a => a.AntrianId).ToListAsync();

        Assert.Equal("dibatalkan", buku.BukuStatus);
        Assert.NotNull(buku.BukuUpdatedAt);

        Assert.Null(queues[0].AntrianLabelingStatus);
        Assert.Equal("failed", queues[0].AntrianValidationStatus);
        Assert.Equal(QueueCancellationHelper.CancelledByUserMessage, queues[0].AntrianErrorMessage);

        Assert.Null(queues[1].AntrianExtractionStatus);
        Assert.Equal("completed", queues[1].AntrianValidationStatus);
        Assert.Equal(QueueCancellationHelper.CancelledByUserMessage, queues[1].AntrianErrorMessage);

        wsService.Verify(service => service.NotifyBukuCancelled("05111740000123", 31), Times.Once);
        wsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CanUpload_ShouldReturnTrueWhenExistingBookIsDiproses()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 32,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Aktif",
            BukuStatus = "diproses"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            sttsDb,
            Mock.Of<IBukuService>(),
            Mock.Of<IWebSocketService>());

        var result = controller.CanUpload();

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("can_upload").GetBoolean());
    }

    [Fact]
    public async Task UploadBuku_ShouldAllowWhenExistingBookIsDiproses()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 33,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Diproses",
            BukuStatus = "diproses"
        });
        await db.SaveChangesAsync();

        var bukuService = new Mock<IBukuService>(MockBehavior.Strict);
        bukuService
            .Setup(service => service.UploadBuku(
                "05111740000123",
                "Judul TA",
                It.Is<List<IFormFile>>(files => files.Count == 1 && files[0].FileName == "BAB 1 Pendahuluan.docx")))
            .ReturnsAsync(new Buku
            {
                BukuId = 330,
                MhsNrp = "05111740000123",
                BukuJudul = "Judul TA"
            });
        var controller = CreateController(
            db,
            sttsDb,
            bukuService.Object,
            Mock.Of<IWebSocketService>());

        var result = await controller.UploadBuku(
            "Judul TA",
            [CreateFormFile("BAB 1 Pendahuluan.docx")]);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(330, json.RootElement.GetProperty("buku_id").GetInt32());
        bukuService.VerifyAll();
    }

    [Fact]
    public async Task UploadBuku_ShouldRejectWhenExistingBookIsDalamAntrian()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 35,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Antrian",
            BukuStatus = "dalam_antrian"
        });
        await db.SaveChangesAsync();

        var bukuService = new Mock<IBukuService>(MockBehavior.Strict);
        var controller = CreateController(
            db,
            sttsDb,
            bukuService.Object,
            Mock.Of<IWebSocketService>());

        var result = await controller.UploadBuku(
            "Judul TA",
            [CreateFormFile("BAB 1 Pendahuluan.docx")]);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(badRequest.Value));
        Assert.Equal("Masih ada buku dalam antrian", json.RootElement.GetProperty("message").GetString());
        bukuService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetBukuById_ShouldResolveFailedBabQueueFromLegacyBabOnlyQueue()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();

        db.Bukus.Add(new Buku
        {
            BukuId = 34,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Legacy Queue",
            BukuStatus = "diproses",
            BukuJumlahBab = 1
        });
        db.Babs.Add(new Bab
        {
            BabId = 341,
            BukuId = 34,
            BabOrder = 1,
            BabFilename = "BAB I.docx"
        });
        db.Antrians.Add(new Antrian
        {
            AntrianId = 3411,
            AntrianTipe = "buku",
            BabId = 341,
            AntrianExtractionStatus = "failed",
            AntrianErrorMessage = "Konversi legacy gagal",
            AntrianCreatedAt = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Local)
        });
        await db.SaveChangesAsync();

        var archiveService = new Mock<IBukuArchiveService>(MockBehavior.Strict);
        archiveService
            .Setup(service => service.GetDocxArchiveRelativePath("05111740000123", 34))
            .Returns("buku/05111740000123/34/docx/buku-docx.zip");
        archiveService
            .Setup(service => service.GetPdfArchiveRelativePath("05111740000123", 34))
            .Returns("buku/05111740000123/34/pdf/buku-pdf.zip");
        archiveService
            .Setup(service => service.TryResolveStorageFilePath(It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Returns((string _, out string fullPath) =>
            {
                fullPath = string.Empty;
                return false;
            });

        var controller = CreateController(
            db,
            sttsDb,
            Mock.Of<IBukuService>(),
            Mock.Of<IWebSocketService>(),
            archiveService.Object);

        var result = await controller.GetBukuById(34);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.Equal("tidak_lolos", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("has_failed_bab").GetBoolean());
        Assert.Equal(
            "Konversi legacy gagal",
            root.GetProperty("bab")[0].GetProperty("error_message").GetString());
    }

    private static BukuController CreateController(
        KorektorBukuDbContext db,
        SttsDbContext sttsDb,
        IBukuService bukuService,
        IWebSocketService wsService,
        IBukuArchiveService? archiveService = null)
    {
        var controller = new BukuController(
            db,
            sttsDb,
            bukuService,
            wsService,
            Mock.Of<IValidationReportService>(),
            archiveService ?? Mock.Of<IBukuArchiveService>())
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

    private static IFormFile CreateFormFile(string fileName)
    {
        var stream = new MemoryStream([1, 2, 3]);
        return new FormFile(stream, 0, stream.Length, "files", fileName);
    }
}

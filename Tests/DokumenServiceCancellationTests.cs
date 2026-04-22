using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class DokumenServiceCancellationTests
{
    [Fact]
    public async Task BatalDokumen_ShouldCancelDokumenAndClearOnlyActiveQueueStages()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 21,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab1.docx",
            DokumenStatus = "diproses"
        });
        db.Antrians.Add(new Antrian
        {
            AntrianId = 211,
            AntrianTipe = "dokumen",
            DokumenId = 21,
            AntrianExtractionStatus = "completed",
            AntrianLabelingStatus = "processing",
            AntrianValidationStatus = "failed",
            AntrianErrorMessage = "Error lama"
        });
        await db.SaveChangesAsync();

        var wsService = new Mock<IWebSocketService>(MockBehavior.Strict);
        wsService
            .Setup(service => service.NotifyDokumenCancelled("05111740000123", 21))
            .Returns(Task.CompletedTask);

        var service = new DokumenService(
            Mock.Of<IFileService>(),
            db,
            Mock.Of<ILogger<DokumenService>>(),
            wsService.Object);

        await service.BatalDokumen("05111740000123", 21);

        var dokumen = await db.Dokumens.SingleAsync();
        var queue = await db.Antrians.SingleAsync();

        Assert.Equal("dibatalkan", dokumen.DokumenStatus);
        Assert.NotNull(dokumen.DokumenUpdatedAt);
        Assert.Equal("completed", queue.AntrianExtractionStatus);
        Assert.Null(queue.AntrianLabelingStatus);
        Assert.Equal("failed", queue.AntrianValidationStatus);
        Assert.Equal(QueueCancellationHelper.CancelledByUserMessage, queue.AntrianErrorMessage);
        Assert.NotNull(queue.AntrianUpdatedAt);

        wsService.Verify(service => service.NotifyDokumenCancelled("05111740000123", 21), Times.Once);
        wsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CanUpload_ShouldReturnTrueWhenDokumenIsDiproses()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 22,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab2.docx",
            DokumenStatus = "diproses"
        });
        await db.SaveChangesAsync();

        var service = new DokumenService(
            Mock.Of<IFileService>(),
            db,
            Mock.Of<ILogger<DokumenService>>(),
            Mock.Of<IWebSocketService>());

        Assert.False(service.HasDokumenInQueue("05111740000123"));
        Assert.True(service.CanUpload("05111740000123"));
    }

    [Fact]
    public async Task CanUpload_ShouldReturnFalseWhenDokumenIsDalamAntrian()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 23,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab3.docx",
            DokumenStatus = "dalam_antrian"
        });
        await db.SaveChangesAsync();

        var service = new DokumenService(
            Mock.Of<IFileService>(),
            db,
            Mock.Of<ILogger<DokumenService>>(),
            Mock.Of<IWebSocketService>());

        Assert.True(service.HasDokumenInQueue("05111740000123"));
        Assert.False(service.CanUpload("05111740000123"));
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class BukuServiceTests
{
    [Fact]
    public async Task UploadBuku_ShouldRejectFilesThatLookLikeNonChapterSections()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        var fileService = new Mock<IFileService>(MockBehavior.Strict);
        var archiveService = new Mock<IBukuArchiveService>(MockBehavior.Strict);
        var wsService = new Mock<IWebSocketService>(MockBehavior.Strict);
        var service = new BukuService(
            fileService.Object,
            archiveService.Object,
            db,
            Mock.Of<ILogger<BukuService>>(),
            wsService.Object);

        var files = new List<IFormFile>
        {
            CreateFormFile("BAB 1 Pendahuluan.docx"),
            CreateFormFile("Daftar_Pustaka.docx")
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadBuku("05111740000123", "Judul TA", files));

        Assert.Contains("hanya menerima file isi buku per BAB", exception.Message);
        Assert.Contains("Daftar_Pustaka.docx", exception.Message);
        Assert.Equal(0, await db.Bukus.CountAsync());
        Assert.Equal(0, await db.Babs.CountAsync());

        fileService.VerifyNoOtherCalls();
        archiveService.VerifyNoOtherCalls();
        wsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadBuku_ShouldSaveOnlyChapterFilesInOrder()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        var fileService = new Mock<IFileService>(MockBehavior.Strict);
        var archiveService = new Mock<IBukuArchiveService>(MockBehavior.Strict);
        var wsService = new Mock<IWebSocketService>(MockBehavior.Strict);

        fileService.Setup(service => service.ValidateExtension(It.IsAny<string>()));
        fileService
            .Setup(service => service.ValidateDocumentSource(It.IsAny<IFormFile>()))
            .Returns(Task.CompletedTask);
        fileService
            .Setup(service => service.SaveFile(It.IsAny<IFormFile>(), "05111740000123", It.IsAny<int>(), "buku"))
            .ReturnsAsync((IFormFile file, string _, int bukuId, string __) => $"buku/05111740000123/{bukuId}/docx/{file.FileName}");

        archiveService
            .Setup(service => service.RefreshDocxArchiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("buku/05111740000123/1/docx/buku-docx.zip");

        wsService
            .Setup(service => service.NotifyBukuArchiveReady("05111740000123", It.IsAny<int>(), true, false))
            .Returns(Task.CompletedTask);

        var service = new BukuService(
            fileService.Object,
            archiveService.Object,
            db,
            Mock.Of<ILogger<BukuService>>(),
            wsService.Object);

        var files = new List<IFormFile>
        {
            CreateFormFile("BAB 1 Pendahuluan.docx"),
            CreateFormFile("BAB 2 Tinjauan Pustaka.docx")
        };

        var buku = await service.UploadBuku("05111740000123", "Judul TA", files);

        Assert.True(buku.BukuId > 0);

        var savedBuku = await db.Bukus.SingleAsync();
        var savedBabs = await db.Babs.OrderBy(b => b.BabOrder).ToListAsync();
        var savedQueues = await db.Antrians.OrderBy(a => a.AntrianId).ToListAsync();

        Assert.Equal(2, savedBuku.BukuJumlahBab);
        Assert.Equal(2, savedBabs.Count);
        Assert.Equal((byte)1, savedBabs[0].BabOrder);
        Assert.Equal((byte)2, savedBabs[1].BabOrder);
        Assert.Equal("BAB 1 Pendahuluan.docx", savedBabs[0].BabFilename);
        Assert.Equal("BAB 2 Tinjauan Pustaka.docx", savedBabs[1].BabFilename);
        Assert.Equal(2, savedQueues.Count);
        Assert.All(savedQueues, queue => Assert.Equal("buku", queue.AntrianTipe));

        fileService.Verify(service => service.ValidateExtension(It.IsAny<string>()), Times.Exactly(2));
        fileService.Verify(service => service.ValidateDocumentSource(It.IsAny<IFormFile>()), Times.Exactly(2));
        fileService.Verify(service => service.SaveFile(It.IsAny<IFormFile>(), "05111740000123", buku.BukuId, "buku"), Times.Exactly(2));
        archiveService.Verify(service => service.RefreshDocxArchiveAsync(buku.BukuId, It.IsAny<CancellationToken>()), Times.Once);
        wsService.Verify(service => service.NotifyBukuArchiveReady("05111740000123", buku.BukuId, true, false), Times.Once);
    }

    private static IFormFile CreateFormFile(string fileName)
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        return new FormFile(stream, 0, stream.Length, "files", fileName);
    }
}

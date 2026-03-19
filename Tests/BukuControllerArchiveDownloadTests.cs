using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class BukuControllerArchiveDownloadTests
{
    [Theory]
    [InlineData("docx", "buku-docx.zip")]
    [InlineData("pdf", "buku-pdf.zip")]
    public async Task DownloadBukuArchive_ShouldUseNrpAndTimestampAsDownloadName(string archiveKind, string storedFileName)
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 1,
            MhsNrp = "05111740000123",
            BukuJudul = "Test Buku",
            BukuStatus = "lolos",
            BukuDocxZipPath = "buku/05111740000123/1/docx/buku-docx.zip",
            BukuPdfZipPath = "buku/05111740000123/1/pdf/buku-pdf.zip"
        });
        await db.SaveChangesAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), $"buku-archive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var fullPath = Path.Combine(tempDir, "buku", "05111740000123", "1", archiveKind, storedFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, new byte[] { 1, 2, 3 });

            var timestamp = new DateTime(2026, 3, 16, 9, 8, 7, DateTimeKind.Local);
            File.SetLastWriteTime(fullPath, timestamp);

            var archiveService = new Mock<IBukuArchiveService>();
            var resolvedPath = fullPath;
            archiveService
                .Setup(service => service.TryResolveStorageFilePath(It.IsAny<string>(), out resolvedPath))
                .Returns(true);

            var controller = new BukuController(
                db,
                null!,
                Mock.Of<IBukuService>(),
                Mock.Of<IWebSocketService>(),
                Mock.Of<IValidationReportService>(),
                archiveService.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            controller.HttpContext.Items["Nrp"] = "05111740000123";
            controller.HttpContext.Items["Role"] = "mahasiswa";

            var result = archiveKind == "docx"
                ? await controller.DownloadBukuDocxArchive(1)
                : await controller.DownloadBukuPdfArchive(1);

            var fileResult = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal($"05111740000123_{archiveKind}_20260316090807.zip", fileResult.FileDownloadName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

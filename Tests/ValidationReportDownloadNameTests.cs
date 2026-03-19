using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ValidationReportDownloadNameTests
{
    [Fact]
    public async Task GenerateDokumenReportAsync_ShouldUseNrpAndFileTimestampForDownloadName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dokumen-report-test-{Guid.NewGuid():N}");
        var previousStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);

        try
        {
            await using var db = ControllerTestHelpers.CreateDbContext();
            await using var sttsDb = CreateSttsDbContext();

            db.Dokumens.Add(new Dokumen
            {
                DokumenId = 7,
                MhsNrp = "05111740000123",
                DokumenFilename = "dokumen.docx",
                DokumenStatus = "lolos",
                DokumenJumlahKesalahan = 0
            });
            await db.SaveChangesAsync();

            var reportPath = Path.Combine(tempDir, "dokumen", "05111740000123", "7", "report", "report_validasi_7.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllBytesAsync(reportPath, new byte[] { 1, 2, 3, 4 });
            File.SetLastWriteTime(reportPath, new DateTime(2026, 3, 16, 10, 11, 12, DateTimeKind.Local));

            var service = new ValidationReportService(db, sttsDb, Mock.Of<ILogger<ValidationReportService>>());

            var result = await service.GenerateDokumenReportAsync(7, "05111740000123", "mahasiswa", refresh: false, CancellationToken.None);

            Assert.Equal("05111740000123_report_dokumen_20260316101112.pdf", result.FileName);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", previousStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateBukuReportAsync_ShouldUseNrpAndFileTimestampForDownloadName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"buku-report-test-{Guid.NewGuid():N}");
        var previousStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);

        try
        {
            await using var db = ControllerTestHelpers.CreateDbContext();
            await using var sttsDb = CreateSttsDbContext();

            db.Bukus.Add(new Buku
            {
                BukuId = 9,
                MhsNrp = "05111740000123",
                BukuJudul = "Laporan Buku",
                BukuStatus = "lolos",
                BukuJumlahKesalahan = 0
            });
            await db.SaveChangesAsync();

            var reportPath = Path.Combine(tempDir, "buku", "05111740000123", "9", "report", "report_validasi_buku_9.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllBytesAsync(reportPath, new byte[] { 9, 8, 7 });
            File.SetLastWriteTime(reportPath, new DateTime(2026, 3, 16, 13, 14, 15, DateTimeKind.Local));

            var service = new ValidationReportService(db, sttsDb, Mock.Of<ILogger<ValidationReportService>>());

            var result = await service.GenerateBukuReportAsync(9, "05111740000123", "mahasiswa", refresh: false, CancellationToken.None);

            Assert.Equal("05111740000123_report_buku_20260316131415.pdf", result.FileName);
            Assert.Equal(new byte[] { 9, 8, 7 }, result.Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", previousStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static SttsDbContext CreateSttsDbContext()
    {
        var options = new DbContextOptionsBuilder<SttsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SttsDbContext(options);
    }
}

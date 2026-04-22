using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

[Collection("storage-path")]
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
            var expectedTimestamp = File.GetLastWriteTime(reportPath).ToString("yyyyMMddHHmmss");

            var service = new ValidationReportService(db, sttsDb, Mock.Of<ILogger<ValidationReportService>>());

            var result = await service.GenerateDokumenReportAsync(7, "05111740000123", "mahasiswa", refresh: false, CancellationToken.None);

            Assert.Equal($"05111740000123_report_dokumen_{expectedTimestamp}.pdf", result.FileName);
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
            var expectedTimestamp = File.GetLastWriteTime(reportPath).ToString("yyyyMMddHHmmss");

            var service = new ValidationReportService(db, sttsDb, Mock.Of<ILogger<ValidationReportService>>());

            var result = await service.GenerateBukuReportAsync(9, "05111740000123", "mahasiswa", refresh: false, CancellationToken.None);

            Assert.Equal($"05111740000123_report_buku_{expectedTimestamp}.pdf", result.FileName);
            Assert.Equal(new byte[] { 9, 8, 7 }, result.Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", previousStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateDokumenReportAsync_ShouldGeneratePdfAndPersistReportPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dokumen-report-generate-{Guid.NewGuid():N}");
        var previousStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);

        try
        {
            await using var db = ControllerTestHelpers.CreateDbContext();
            await using var sttsDb = CreateSttsDbContext();

            db.Dokumens.Add(new Dokumen
            {
                DokumenId = 11,
                MhsNrp = "05111740000123",
                DokumenFilename = "bab-2.docx",
                DokumenStatus = "tidak_lolos",
                DokumenSkor = 72,
                DokumenJumlahKesalahan = 1
            });

            db.Kesalahans.Add(new Kesalahan
            {
                KesalahanId = 101,
                KesalahanKategori = "Paragraf",
                KesalahanRefTipe = KesalahanRefTipe.dokumen,
                KesalahanRefId = 11,
                KesalahanLokasi = "[{\"halaman_ke\":2,\"bbox\":{\"y0\":128.5},\"evidence\":\"Paragraf contoh\"}]",
                Details = new List<KesalahanDetail>
                {
                    new()
                    {
                        KesalahanDetailId = 1001,
                        KesalahanId = 101,
                        KesalahanDetailJudul = "Indentasi tidak sesuai",
                        KesalahanDetailPenjelasan = "Indentasi paragraf harus 1 cm.",
                        KesalahanIsHardConstraint = true
                    }
                }
            });
            await db.SaveChangesAsync();

            var service = new ValidationReportService(db, sttsDb, Mock.Of<ILogger<ValidationReportService>>());

            var result = await service.GenerateDokumenReportAsync(11, "05111740000123", "mahasiswa", refresh: true, CancellationToken.None);

            var dokumen = await db.Dokumens.FindAsync(11);
            Assert.NotNull(dokumen);
            Assert.NotNull(dokumen!.DokumenReportPath);
            Assert.EndsWith(".pdf", result.FileName);
            Assert.NotEmpty(result.Content);
            Assert.True(File.Exists(Path.Combine(tempDir, dokumen.DokumenReportPath!.Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", previousStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateBukuReportAsync_ShouldGeneratePdfAndPersistReportPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"buku-report-generate-{Guid.NewGuid():N}");
        var previousStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);

        try
        {
            await using var db = ControllerTestHelpers.CreateDbContext();
            await using var sttsDb = CreateSttsDbContext();

            db.Bukus.Add(new Buku
            {
                BukuId = 12,
                MhsNrp = "05111740000123",
                BukuJudul = "Analisis Format Buku",
                BukuStatus = "tidak_lolos",
                BukuSkor = 80,
                BukuJumlahKesalahan = 1,
                BukuJumlahBab = 1
            });

            db.Babs.Add(new Bab
            {
                BabId = 120,
                BukuId = 12,
                BabOrder = 1,
                BabFilename = "Bab1.docx",
                BabSkor = 80,
                BabSkorMinimal = 75,
                BabJumlahKesalahan = 1
            });

            db.Kesalahans.Add(new Kesalahan
            {
                KesalahanId = 202,
                KesalahanKategori = "Margin",
                KesalahanRefTipe = KesalahanRefTipe.bab,
                KesalahanRefId = 120,
                KesalahanLokasi = "[{\"halaman_ke\":1,\"bbox\":{\"y0\":64.25},\"evidence\":\"Margin kiri\"}]",
                Details = new List<KesalahanDetail>
                {
                    new()
                    {
                        KesalahanDetailId = 2002,
                        KesalahanId = 202,
                        KesalahanDetailJudul = "Margin kiri tidak sesuai",
                        KesalahanDetailPenjelasan = "Margin kiri harus 4 cm.",
                        KesalahanIsHardConstraint = true
                    }
                }
            });

            sttsDb.Mahasiswas.Add(new Mahasiswa
            {
                MhsNrp = "05111740000123",
                MhsNama = "Mahasiswa Uji"
            });

            await db.SaveChangesAsync();
            await sttsDb.SaveChangesAsync();

            var service = new ValidationReportService(db, sttsDb, Mock.Of<ILogger<ValidationReportService>>());

            var result = await service.GenerateBukuReportAsync(12, "05111740000123", "mahasiswa", refresh: true, CancellationToken.None);

            var buku = await db.Bukus.FindAsync(12);
            Assert.NotNull(buku);
            Assert.NotNull(buku!.BukuReportPath);
            Assert.EndsWith(".pdf", result.FileName);
            Assert.NotEmpty(result.Content);
            Assert.True(File.Exists(Path.Combine(tempDir, buku.BukuReportPath!.Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", previousStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateBukuReportAsync_ShouldPersistValidationRuleVersion()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"buku-report-rule-version-{Guid.NewGuid():N}");
        var previousStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);

        try
        {
            await using var db = ControllerTestHelpers.CreateDbContext();
            await using var sttsDb = CreateSttsDbContext();

            db.Aturans.Add(new Aturan
            {
                AturanId = 77,
                AturanVersi = "Panduan Edisi 2026",
                AturanStatus = AturanStatusValues.Aktif
            });

            db.Bukus.Add(new Buku
            {
                BukuId = 14,
                MhsNrp = "05111740000123",
                BukuJudul = "Pra Validasi Buku",
                BukuStatus = "lolos",
                BukuSkor = 100,
                BukuJumlahKesalahan = 0,
                BukuJumlahBab = 1
            });

            db.Babs.Add(new Bab
            {
                BabId = 140,
                BukuId = 14,
                BabOrder = 1,
                BabFilename = "Bab1.docx",
                BabSkor = 100,
                BabSkorMinimal = 80,
                BabJumlahKesalahan = 0
            });

            sttsDb.Mahasiswas.Add(new Mahasiswa
            {
                MhsNrp = "05111740000123",
                MhsNama = "Mahasiswa Uji"
            });

            await db.SaveChangesAsync();
            await sttsDb.SaveChangesAsync();

            var service = new ValidationReportService(db, sttsDb, Mock.Of<ILogger<ValidationReportService>>());

            await service.GenerateBukuReportAsync(14, "05111740000123", "mahasiswa", refresh: true, CancellationToken.None);

            var buku = await db.Bukus.FindAsync(14);
            Assert.NotNull(buku);
            Assert.Equal("Panduan Edisi 2026", buku!.BukuAturanVersiValidasi);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", previousStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void BuildRows_ShouldMapHardConstraintFlagFromKesalahanDetail()
    {
        var method = typeof(ValidationReportService).GetMethod(
            "BuildRows",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var kesalahan = new Kesalahan
        {
            KesalahanId = 301,
            KesalahanKategori = "Paragraf",
            KesalahanRefTipe = KesalahanRefTipe.dokumen,
            KesalahanRefId = 11,
            Details = new List<KesalahanDetail>
            {
                new()
                {
                    KesalahanDetailId = 3001,
                    KesalahanId = 301,
                    KesalahanDetailJudul = "Indentasi tidak sesuai",
                    KesalahanDetailPenjelasan = "Penjelasan",
                    KesalahanIsHardConstraint = true
                }
            }
        };

        var rows = Assert.IsType<List<ValidationReportRow>>(method!.Invoke(null, [new List<Kesalahan> { kesalahan }]));
        var row = Assert.Single(rows);
        Assert.True(row.IsHardConstraint);
    }

    [Fact]
    public void BuildBukuRows_ShouldMapHardConstraintFlagFromKesalahanDetail()
    {
        var method = typeof(ValidationReportService).GetMethod(
            "BuildBukuRows",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var bab = new Bab
        {
            BabId = 401,
            BukuId = 12,
            BabOrder = 2,
            BabFilename = "Bab 2.docx"
        };

        var kesalahan = new Kesalahan
        {
            KesalahanId = 402,
            KesalahanKategori = "Margin",
            KesalahanRefTipe = KesalahanRefTipe.bab,
            KesalahanRefId = 401,
            Details = new List<KesalahanDetail>
            {
                new()
                {
                    KesalahanDetailId = 4002,
                    KesalahanId = 402,
                    KesalahanDetailJudul = "Margin kiri tidak sesuai",
                    KesalahanDetailPenjelasan = "Penjelasan",
                    KesalahanIsHardConstraint = true
                }
            }
        };

        var rows = Assert.IsType<List<BukuValidationReportRow>>(method!.Invoke(null, [new List<Kesalahan> { kesalahan }, new Dictionary<uint, Bab> { [bab.BabId] = bab }]));
        var row = Assert.Single(rows);
        Assert.True(row.IsHardConstraint);
    }

    [Fact]
    public void BuildSummary_ShouldReturnTotalDetailsOnly()
    {
        var method = typeof(ValidationReportService).GetMethod(
            "BuildSummary",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var rows = new List<ValidationReportRow>
        {
            new()
            {
                Title = "Hard constraint",
                IsHardConstraint = true
            },
            new()
            {
                Title = "Bukan hard constraint",
                IsHardConstraint = false
            }
        };

        var summary = Assert.IsType<ValidationReportSummary>(method!.Invoke(null, [rows]));
        Assert.Equal(2, summary.TotalDetails);
    }

    private static SttsDbContext CreateSttsDbContext()
    {
        var options = new DbContextOptionsBuilder<SttsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SttsDbContext(options);
    }
}

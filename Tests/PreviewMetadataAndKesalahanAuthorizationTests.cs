using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

[Collection("storage-path")]
public class PreviewMetadataAndKesalahanAuthorizationTests
{
    [Fact]
    public async Task GetDokumenById_ShouldReturnAvailablePagesFromStorageImages()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 1,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab1.docx",
            DokumenStatus = "lolos"
        });
        await db.SaveChangesAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), $"dokumen-preview-test-{Guid.NewGuid():N}");
        var originalStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);
            var imagesDir = Path.Combine(tempDir, "dokumen", "05111740000123", "1", "images");
            Directory.CreateDirectory(imagesDir);
            await File.WriteAllBytesAsync(Path.Combine(imagesDir, "1.jpg"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(imagesDir, "10.png"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(imagesDir, "2.jpeg"), [1]);

            var controller = new DokumenController(
                db,
                sttsDb,
                Mock.Of<IDokumenService>(),
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

            var result = await controller.GetDokumenById(1);

            var ok = Assert.IsType<OkObjectResult>(result);
            var value = ok.Value!;
            var valueType = value.GetType();
            Assert.Equal(3, valueType.GetProperty("total_halaman")!.GetValue(value));
            Assert.Equal([1, 2, 10], Assert.IsType<List<int>>(valueType.GetProperty("available_pages")!.GetValue(value)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", originalStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetDokumenById_ShouldReturnDocxAndPdfReadyFlagsBasedOnPhysicalFiles()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 2,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab2.docx",
            DokumenStatus = "diproses",
            DokumenDocxPath = "dokumen/05111740000123/2/docx/Bab2.docx",
            DokumenPdfPath = "dokumen/05111740000123/2/pdf/Bab2.pdf"
        });
        await db.SaveChangesAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), $"dokumen-ready-test-{Guid.NewGuid():N}");
        var originalStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);
            var docxFullPath = Path.Combine(tempDir, "dokumen", "05111740000123", "2", "docx", "Bab2.docx");
            Directory.CreateDirectory(Path.GetDirectoryName(docxFullPath)!);
            await File.WriteAllBytesAsync(docxFullPath, [1, 2, 3]);

            var controller = new DokumenController(
                db,
                sttsDb,
                Mock.Of<IDokumenService>(),
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

            var result = await controller.GetDokumenById(2);

            var ok = Assert.IsType<OkObjectResult>(result);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
            var root = json.RootElement;
            Assert.True(root.GetProperty("docx_ready").GetBoolean());
            Assert.False(root.GetProperty("pdf_ready").GetBoolean());
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", originalStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetKesalahanByBab_ShouldReturnAvailablePagesFromBabImageDirectory()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 1,
            MhsNrp = "05111740000123",
            BukuJudul = "Test Buku",
            BukuStatus = "lolos"
        });
        db.Babs.Add(new Bab
        {
            BabId = 9,
            BukuId = 1,
            BabOrder = 2,
            BabFilename = "Bab 2.docx",
            BabImagesPath = "buku/05111740000123/1/images/2"
        });
        await db.SaveChangesAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), $"bab-preview-test-{Guid.NewGuid():N}");
        var originalStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);
            var imagesDir = Path.Combine(tempDir, "buku", "05111740000123", "1", "images", "2");
            Directory.CreateDirectory(imagesDir);
            await File.WriteAllBytesAsync(Path.Combine(imagesDir, "3.jpg"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(imagesDir, "1.png"), [1]);

            var controller = new BabController(db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            controller.HttpContext.Items["Nrp"] = "05111740000123";
            controller.HttpContext.Items["Role"] = "mahasiswa";

            var result = await controller.GetKesalahanByBab(9);

            var ok = Assert.IsType<OkObjectResult>(result);
            var value = ok.Value!;
            var valueType = value.GetType();
            Assert.Equal(2, valueType.GetProperty("total_halaman")!.GetValue(value));
            Assert.Equal([1, 3], Assert.IsType<List<int>>(valueType.GetProperty("available_pages")!.GetValue(value)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", originalStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetBukuById_ShouldReturnArchiveReadyFlagsBasedOnPhysicalFiles()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 2,
            MhsNrp = "05111740000123",
            BukuJudul = "Test Buku Ready",
            BukuStatus = "diproses",
            BukuDocxZipPath = "buku/05111740000123/2/docx/buku-docx.zip",
            BukuPdfZipPath = "buku/05111740000123/2/pdf/buku-pdf.zip"
        });
        await db.SaveChangesAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), $"buku-ready-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var docxArchivePath = Path.Combine(tempDir, "buku", "05111740000123", "2", "docx", "buku-docx.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(docxArchivePath)!);
            await File.WriteAllBytesAsync(docxArchivePath, [1, 2, 3]);

            var archiveService = new Mock<IBukuArchiveService>();
            archiveService
                .Setup(service => service.TryResolveStorageFilePath(It.IsAny<string>(), out It.Ref<string>.IsAny))
                .Returns((string relativePath, out string fullPath) =>
                {
                    fullPath = Path.Combine(tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    return true;
                });
            archiveService
                .Setup(service => service.GetDocxArchiveRelativePath("05111740000123", 2))
                .Returns("buku/05111740000123/2/docx/buku-docx.zip");
            archiveService
                .Setup(service => service.GetPdfArchiveRelativePath("05111740000123", 2))
                .Returns("buku/05111740000123/2/pdf/buku-pdf.zip");

            var controller = new BukuController(
                db,
                sttsDb,
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

            var result = await controller.GetBukuById(2);

            var ok = Assert.IsType<OkObjectResult>(result);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
            var root = json.RootElement;
            Assert.True(root.GetProperty("docx_archive_ready").GetBoolean());
            Assert.False(root.GetProperty("pdf_archive_ready").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetBukuById_ShouldExposeFailedBabStateEvenWhenBookStatusIsStillDiproses()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();

        db.Bukus.Add(new Buku
        {
            BukuId = 3,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Gagal",
            BukuStatus = "diproses",
            BukuJumlahBab = 1
        });
        db.Babs.Add(new Bab
        {
            BabId = 31,
            BukuId = 3,
            BabOrder = 1,
            BabFilename = "BAB I.docx"
        });
        db.Antrians.Add(new Antrian
        {
            AntrianId = 301,
            AntrianTipe = "buku",
            BukuId = 3,
            BabId = 31,
            AntrianExtractionStatus = "failed",
            AntrianErrorMessage = "Konversi gagal untuk BAB I",
            AntrianUpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var archiveService = new Mock<IBukuArchiveService>(MockBehavior.Strict);
        archiveService
            .Setup(service => service.GetDocxArchiveRelativePath("05111740000123", 3))
            .Returns("buku/05111740000123/3/docx/buku-docx.zip");
        archiveService
            .Setup(service => service.GetPdfArchiveRelativePath("05111740000123", 3))
            .Returns("buku/05111740000123/3/pdf/buku-pdf.zip");
        archiveService
            .Setup(service => service.TryResolveStorageFilePath(It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Returns((string _, out string fullPath) =>
            {
                fullPath = string.Empty;
                return false;
            });

        var controller = new BukuController(
            db,
            sttsDb,
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

        var result = await controller.GetBukuById(3);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.Equal("tidak_lolos", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("has_failed_bab").GetBoolean());

        var bab = root.GetProperty("bab")[0];
        Assert.Equal("failed", bab.GetProperty("extraction_status").GetString());
        Assert.Equal("Konversi gagal untuk BAB I", bab.GetProperty("error_message").GetString());
    }

    [Fact]
    public async Task GetKesalahanById_ShouldForbidMahasiswaWhoDoesNotOwnDokumenError()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 1,
            MhsNrp = "05111740000123",
            DokumenFilename = "Test.docx",
            DokumenStatus = "lolos"
        });
        db.Kesalahans.Add(new Kesalahan
        {
            KesalahanId = 11,
            KesalahanKategori = "Paragraf",
            KesalahanRefTipe = KesalahanRefTipe.dokumen,
            KesalahanRefId = 1,
            Details =
            [
                new KesalahanDetail
                {
                    KesalahanDetailId = 21,
                    KesalahanId = 11,
                    KesalahanDetailJudul = "Judul",
                    KesalahanDetailPenjelasan = "Penjelasan"
                }
            ]
        });
        await db.SaveChangesAsync();

        var controller = new KesalahanController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["Nrp"] = "05111740000999";
        controller.HttpContext.Items["Role"] = "mahasiswa";

        var result = await controller.GetKesalahanById(11);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetKesalahanById_ShouldAllowOwnerForBabError()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 1,
            MhsNrp = "05111740000123",
            BukuJudul = "Test Buku",
            BukuStatus = "lolos"
        });
        db.Babs.Add(new Bab
        {
            BabId = 9,
            BukuId = 1,
            BabFilename = "Bab 2.docx"
        });
        db.Kesalahans.Add(new Kesalahan
        {
            KesalahanId = 12,
            KesalahanKategori = "Gambar",
            KesalahanRefTipe = KesalahanRefTipe.bab,
            KesalahanRefId = 9,
            Details =
            [
                new KesalahanDetail
                {
                    KesalahanDetailId = 22,
                    KesalahanId = 12,
                    KesalahanDetailJudul = "Caption gambar salah",
                    KesalahanDetailPenjelasan = "Penjelasan",
                    KesalahanIsHardConstraint = true
                }
            ]
        });
        await db.SaveChangesAsync();

        var controller = new KesalahanController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["Nrp"] = "05111740000123";
        controller.HttpContext.Items["Role"] = "mahasiswa";

        var result = await controller.GetKesalahanById(12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;
        Assert.Equal((uint)12, root.GetProperty("id").GetUInt32());
        var details = root.GetProperty("details");
        Assert.Single(details.EnumerateArray());
        Assert.False(details[0].TryGetProperty("is_required", out var ignoredIsRequired));
        Assert.True(details[0].GetProperty("is_hard_constraint").GetBoolean());
    }

    [Fact]
    public async Task GetKesalahanDetailsByDokumenPage_ShouldReturnOnlyRequestedPageItems()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 1,
            MhsNrp = "05111740000123",
            DokumenFilename = "Test.docx",
            DokumenStatus = "lolos"
        });
        db.Kesalahans.AddRange(
            new Kesalahan
            {
                KesalahanId = 31,
                KesalahanKategori = "Paragraf",
                KesalahanRefTipe = KesalahanRefTipe.dokumen,
                KesalahanRefId = 1,
                KesalahanLokasi = """[{ "halaman_ke": 2, "bbox": { "y0": 10 } }]""",
                Details =
                [
                    new KesalahanDetail
                    {
                        KesalahanDetailId = 41,
                        KesalahanId = 31,
                        KesalahanDetailJudul = "Detail halaman 2",
                        KesalahanDetailPenjelasan = "Penjelasan",
                        KesalahanIsHardConstraint = true
                    }
                ]
            },
            new Kesalahan
            {
                KesalahanId = 32,
                KesalahanKategori = "Gambar",
                KesalahanRefTipe = KesalahanRefTipe.dokumen,
                KesalahanRefId = 1,
                KesalahanLokasi = """[{ "halaman_ke": 3, "bbox": { "y0": 20 } }]""",
                Details =
                [
                    new KesalahanDetail
                    {
                        KesalahanDetailId = 42,
                        KesalahanId = 32,
                        KesalahanDetailJudul = "Detail halaman 3",
                        KesalahanDetailPenjelasan = "Penjelasan"
                    }
                ]
            });
        await db.SaveChangesAsync();

        var controller = new DokumenController(
            db,
            sttsDb,
            Mock.Of<IDokumenService>(),
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

        var result = await controller.GetKesalahanDetailsByDokumenPage(1, 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.Equal(2, root.GetProperty("halaman_ke").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(31, items[0].GetProperty("kesalahan_id").GetInt32());
        Assert.Equal("Paragraf", items[0].GetProperty("kategori").GetString());
        Assert.Equal(1, items[0].GetProperty("details").GetArrayLength());
        Assert.False(items[0].GetProperty("details")[0].TryGetProperty("is_required", out var ignoredDokumenIsRequired));
        Assert.True(items[0].GetProperty("details")[0].GetProperty("is_hard_constraint").GetBoolean());
    }

    [Fact]
    public async Task GetKesalahanDetailsByDokumenPage_ShouldForbidMahasiswaWhoDoesNotOwnDokumen()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 7,
            MhsNrp = "05111740000123",
            DokumenFilename = "Test.docx",
            DokumenStatus = "lolos"
        });
        await db.SaveChangesAsync();

        var controller = new DokumenController(
            db,
            sttsDb,
            Mock.Of<IDokumenService>(),
            Mock.Of<IDokumenImportService>(),
            Mock.Of<IDokumenHistoryPurgeService>(),
            Mock.Of<IValidationReportService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["Nrp"] = "05111740000999";
        controller.HttpContext.Items["Role"] = "mahasiswa";

        var result = await controller.GetKesalahanDetailsByDokumenPage(7, 1);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetKesalahanDetailsByBabPage_ShouldReturnOnlyRequestedPageItems()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 1,
            MhsNrp = "05111740000123",
            BukuJudul = "Test Buku",
            BukuStatus = "lolos"
        });
        db.Babs.Add(new Bab
        {
            BabId = 9,
            BukuId = 1,
            BabFilename = "Bab 2.docx"
        });
        db.Kesalahans.AddRange(
            new Kesalahan
            {
                KesalahanId = 51,
                KesalahanKategori = "Tabel",
                KesalahanRefTipe = KesalahanRefTipe.bab,
                KesalahanRefId = 9,
                KesalahanLokasi = """[{ "halaman_ke": 4, "bbox": { "y0": 12 } }]""",
                Details =
                [
                    new KesalahanDetail
                    {
                        KesalahanDetailId = 61,
                        KesalahanId = 51,
                        KesalahanDetailJudul = "Detail halaman 4",
                        KesalahanDetailPenjelasan = "Penjelasan",
                        KesalahanIsHardConstraint = true
                    }
                ]
            },
            new Kesalahan
            {
                KesalahanId = 52,
                KesalahanKategori = "List Item",
                KesalahanRefTipe = KesalahanRefTipe.bab,
                KesalahanRefId = 9,
                KesalahanLokasi = """[{ "halaman_ke": 6, "bbox": { "y0": 30 } }]""",
                Details =
                [
                    new KesalahanDetail
                    {
                        KesalahanDetailId = 62,
                        KesalahanId = 52,
                        KesalahanDetailJudul = "Detail halaman 6",
                        KesalahanDetailPenjelasan = "Penjelasan"
                    }
                ]
            });
        await db.SaveChangesAsync();

        var controller = new BabController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["Nrp"] = "05111740000123";
        controller.HttpContext.Items["Role"] = "mahasiswa";

        var result = await controller.GetKesalahanDetailsByBabPage(9, 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.Equal(4, root.GetProperty("halaman_ke").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(51, items[0].GetProperty("kesalahan_id").GetInt32());
        Assert.Equal("Tabel", items[0].GetProperty("kategori").GetString());
        Assert.Equal(1, items[0].GetProperty("details").GetArrayLength());
        Assert.False(items[0].GetProperty("details")[0].TryGetProperty("is_required", out var ignoredBabIsRequired));
        Assert.True(items[0].GetProperty("details")[0].GetProperty("is_hard_constraint").GetBoolean());
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class PreviewMetadataAndKesalahanAuthorizationTests
{
    [Fact]
    public async Task GetDokumenById_ShouldReturnAvailablePagesFromStorageImages()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
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

        controller.HttpContext.Items["Nrp"] = "05111740000123";
        controller.HttpContext.Items["Role"] = "mahasiswa";

        var result = await controller.GetKesalahanById(12);

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = ok.Value!;
        var valueType = value.GetType();
        Assert.Equal((uint)12, valueType.GetProperty("id")!.GetValue(value));
    }

    [Fact]
    public async Task GetKesalahanDetailsByDokumenPage_ShouldReturnOnlyRequestedPageItems()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
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
                        KesalahanDetailPenjelasan = "Penjelasan"
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
    }

    [Fact]
    public async Task GetKesalahanDetailsByDokumenPage_ShouldForbidMahasiswaWhoDoesNotOwnDokumen()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
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
                        KesalahanDetailPenjelasan = "Penjelasan"
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
    }
}

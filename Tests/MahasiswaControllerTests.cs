using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;
using _.Services;

namespace Tests;

[Collection("storage-path")]
public class MahasiswaControllerTests
{
    [Fact]
    public async Task GetNonaktifBuku_ShouldSupportMultiSelectFilters()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();

        sttsDb.Jurusans.AddRange(
            new Jurusan { JurKode = "IF", JurNama = "Informatika", JurSingkat = "IF" },
            new Jurusan { JurKode = "SI", JurNama = "Sistem Informasi", JurSingkat = "SI" });

        sttsDb.Mahasiswas.AddRange(
            new Mahasiswa
            {
                MhsNrp = "05111740000111",
                MhsNama = "Mahasiswa Nonaktif A",
                JurKode = "IF",
                MhsAngkatan = 2020,
                MhsStatus = 0
            },
            new Mahasiswa
            {
                MhsNrp = "05111740000222",
                MhsNama = "Mahasiswa Nonaktif B",
                JurKode = "SI",
                MhsAngkatan = 2021,
                MhsStatus = 4
            },
            new Mahasiswa
            {
                MhsNrp = "05111740000333",
                MhsNama = "Mahasiswa Aktif",
                JurKode = "IF",
                MhsAngkatan = 2020,
                MhsStatus = 1
            });

        db.Bukus.AddRange(
            new Buku { BukuId = 1, MhsNrp = "05111740000111", BukuJudul = "Buku A", BukuStatus = "lolos", BukuCreatedAt = DateTime.Now },
            new Buku { BukuId = 2, MhsNrp = "05111740000222", BukuJudul = "Buku B", BukuStatus = "tidak_lolos", BukuCreatedAt = DateTime.Now.AddMinutes(-5) },
            new Buku { BukuId = 3, MhsNrp = "05111740000333", BukuJudul = "Buku Aktif", BukuStatus = "lolos", BukuCreatedAt = DateTime.Now.AddMinutes(-10) });

        await sttsDb.SaveChangesAsync();
        await db.SaveChangesAsync();

        var controller = CreateController(db, sttsDb);

        var result = controller.GetNonaktifBuku("2020,2021", "IF,SI", null, 10, 0);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.Equal(2, root.GetProperty("total").GetInt32());
        var nrps = root.GetProperty("data")
            .EnumerateArray()
            .Select(item => item.GetProperty("nrp").GetString())
            .ToList();

        Assert.Contains("05111740000111", nrps);
        Assert.Contains("05111740000222", nrps);
        Assert.DoesNotContain("05111740000333", nrps);
    }

    [Fact]
    public async Task HapusBukuNonaktif_ShouldDeleteSelectedBooksAndArtifacts()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();

        sttsDb.Mahasiswas.AddRange(
            new Mahasiswa { MhsNrp = "05111740000111", MhsNama = "Mahasiswa Nonaktif", MhsStatus = 0 },
            new Mahasiswa { MhsNrp = "05111740000333", MhsNama = "Mahasiswa Aktif", MhsStatus = 1 });

        db.Bukus.AddRange(
            new Buku { BukuId = 1, MhsNrp = "05111740000111", BukuJudul = "Buku Nonaktif", BukuStatus = "lolos" },
            new Buku { BukuId = 2, MhsNrp = "05111740000333", BukuJudul = "Buku Aktif", BukuStatus = "lolos" });

        db.Babs.AddRange(
            new Bab { BabId = 11, BukuId = 1, BabOrder = 1, BabFilename = "Bab1.docx" },
            new Bab { BabId = 22, BukuId = 2, BabOrder = 1, BabFilename = "Bab1.docx" });

        db.Antrians.AddRange(
            new Antrian { AntrianId = 101, AntrianTipe = "buku", BukuId = 1, BabId = 11 },
            new Antrian { AntrianId = 202, AntrianTipe = "buku", BukuId = 2, BabId = 22 });

        db.Kesalahans.AddRange(
            new Kesalahan
            {
                KesalahanId = 301,
                KesalahanKategori = "paragraf",
                KesalahanRefTipe = KesalahanRefTipe.bab,
                KesalahanRefId = 11,
                Details =
                [
                    new KesalahanDetail
                    {
                        KesalahanDetailId = 401,
                        KesalahanDetailJudul = "Kesalahan Bab 1",
                        KesalahanDetailPenjelasan = "Penjelasan"
                    }
                ]
            },
            new Kesalahan
            {
                KesalahanId = 302,
                KesalahanKategori = "paragraf",
                KesalahanRefTipe = KesalahanRefTipe.bab,
                KesalahanRefId = 22,
                Details =
                [
                    new KesalahanDetail
                    {
                        KesalahanDetailId = 402,
                        KesalahanDetailJudul = "Kesalahan Bab 2",
                        KesalahanDetailPenjelasan = "Penjelasan"
                    }
                ]
            });

        db.DokumenSections.AddRange(
            new DokumenSection { DsecId = 501, DsecRefTipe = "bab", DsecRefId = 11, DsecIndex = 0 },
            new DokumenSection { DsecId = 502, DsecRefTipe = "bab", DsecRefId = 22, DsecIndex = 0 });

        db.DokumenParts.AddRange(
            new DokumenPart { DpartId = 601, DsecId = 501, DpartType = "body" },
            new DokumenPart { DpartId = 602, DsecId = 502, DpartType = "body" });

        db.DokumenElemens.AddRange(
            new DokumenElemen { DelemenId = 701, DpartId = 601, DelemenSequence = 1, DelemenType = "paragraph", DelemenXml = "<w:p />" },
            new DokumenElemen { DelemenId = 702, DpartId = 602, DelemenSequence = 1, DelemenType = "paragraph", DelemenXml = "<w:p />" });

        db.DokumenElemenVisuals.AddRange(
            new DokumenElemenVisual { DevId = 801, DevRefTipe = "bab", DevRefId = 11, DokumenElemenId = 701 },
            new DokumenElemenVisual { DevId = 802, DevRefTipe = "bab", DevRefId = 22, DokumenElemenId = 702 });

        await sttsDb.SaveChangesAsync();
        await db.SaveChangesAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), $"hapus-riwayat-test-{Guid.NewGuid():N}");
        var originalStoragePath = Environment.GetEnvironmentVariable("STORAGE_PATH");
        Directory.CreateDirectory(Path.Combine(tempDir, "buku", "05111740000111", "1", "docx"));
        Directory.CreateDirectory(Path.Combine(tempDir, "buku", "05111740000333", "2", "docx"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "buku", "05111740000111", "1", "docx", "Bab1.docx"), "test");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "buku", "05111740000333", "2", "docx", "Bab1.docx"), "test");

        try
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", tempDir);
            var controller = CreateController(db, sttsDb);

            var result = await controller.HapusBukuNonaktif(new HapusBukuRequest
            {
                mahasiswa =
                [
                    new MahasiswaBuku
                    {
                        nrp = "05111740000111",
                        buku_ids = [1]
                    }
                ]
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
            var root = json.RootElement;
            Assert.Equal(1, root.GetProperty("deleted").GetInt32());
            Assert.False(root.GetProperty("errors").EnumerateArray().Any());

            Assert.DoesNotContain(db.Bukus, item => item.BukuId == 1);
            Assert.Contains(db.Bukus, item => item.BukuId == 2);

            Assert.DoesNotContain(db.Babs, item => item.BabId == 11);
            Assert.Contains(db.Babs, item => item.BabId == 22);

            Assert.DoesNotContain(db.Antrians, item => item.BukuId == 1 || item.BabId == 11);
            Assert.Contains(db.Antrians, item => item.BukuId == 2 && item.BabId == 22);

            Assert.DoesNotContain(db.Kesalahans, item => item.KesalahanRefId == 11);
            Assert.Contains(db.Kesalahans, item => item.KesalahanRefId == 22);

            Assert.DoesNotContain(db.DokumenSections, item => item.DsecRefTipe == "bab" && item.DsecRefId == 11);
            Assert.Contains(db.DokumenSections, item => item.DsecRefTipe == "bab" && item.DsecRefId == 22);

            Assert.DoesNotContain(db.DokumenParts, item => item.DpartId == 601);
            Assert.Contains(db.DokumenParts, item => item.DpartId == 602);

            Assert.DoesNotContain(db.DokumenElemens, item => item.DelemenId == 701);
            Assert.Contains(db.DokumenElemens, item => item.DelemenId == 702);

            Assert.DoesNotContain(db.DokumenElemenVisuals, item => item.DevRefTipe == "bab" && item.DevRefId == 11);
            Assert.Contains(db.DokumenElemenVisuals, item => item.DevRefTipe == "bab" && item.DevRefId == 22);

            Assert.False(Directory.Exists(Path.Combine(tempDir, "buku", "05111740000111", "1")));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "buku", "05111740000333", "2")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_PATH", originalStoragePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task HapusBukuNonaktif_ShouldRejectActiveStudentPayload()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();

        sttsDb.Mahasiswas.Add(new Mahasiswa
        {
            MhsNrp = "05111740000333",
            MhsNama = "Mahasiswa Aktif",
            MhsStatus = 1
        });

        db.Bukus.Add(new Buku
        {
            BukuId = 2,
            MhsNrp = "05111740000333",
            BukuJudul = "Buku Aktif",
            BukuStatus = "lolos"
        });

        await sttsDb.SaveChangesAsync();
        await db.SaveChangesAsync();

        var controller = CreateController(db, sttsDb);

        var result = await controller.HapusBukuNonaktif(new HapusBukuRequest
        {
            mahasiswa =
            [
                new MahasiswaBuku
                {
                    nrp = "05111740000333",
                    buku_ids = [2]
                }
            ]
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.Equal(0, root.GetProperty("deleted").GetInt32());
        Assert.Contains(
            root.GetProperty("errors").EnumerateArray().Select(item => item.GetString()),
            item => item != null && item.Contains("masih aktif", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(db.Bukus, item => item.BukuId == 2);
    }

    private static MahasiswaController CreateController(KorektorBukuDbContext db, SttsDbContext sttsDb)
    {
        var controller = new MahasiswaController(
            Mock.Of<IMahasiswaService>(),
            db,
            sttsDb,
            new ExtractionArtifactCleanupService(db, NullLogger<ExtractionArtifactCleanupService>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["Role"] = "admin";
        controller.HttpContext.Items["Nrp"] = "admin";
        return controller;
    }
}

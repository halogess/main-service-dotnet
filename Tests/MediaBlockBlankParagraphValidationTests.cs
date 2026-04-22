using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class MediaBlockBlankParagraphValidationTests
{
    [Fact]
    public async Task ValidateImageAsync_ShouldSkipBlankParagraphBefore_WhenBlockStartsAtTopOfPage()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageRules();

        fixture.AddParagraphElement(1001, 1, "Gambar 1.1 Arsitektur Sistem", page: 1, y0: 100f, y1: 120f, label: "caption_gambar");
        fixture.AddImageElement(1002, 2, page: 1, y0: 130f, y1: 220f);
        fixture.AddBlankParagraphElement(1003, 3, page: 1, y0: 230f, y1: 240f);
        fixture.AddParagraphElement(1004, 4, "Penjelasan gambar dimulai di sini.", page: 1, y0: 250f, y1: 290f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateImageAsync", fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sebelum blok gambar tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sesudah blok gambar tidak sesuai");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldRequireBlankParagraphBefore_WhenBlockIsNotAtTopOfPage()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddTableRules();

        fixture.AddParagraphElement(2001, 1, "Paragraf sebelum tabel.", page: 1, y0: 100f, y1: 130f);
        fixture.AddParagraphElement(2002, 2, "Tabel 1.1 Hasil Pengujian", page: 1, y0: 145f, y1: 165f, label: "caption_tabel");
        fixture.AddTableElement(2003, 3, page: 1, y0: 175f, y1: 240f);
        fixture.AddBlankParagraphElement(2004, 4, page: 1, y0: 250f, y1: 260f);
        fixture.AddParagraphElement(2005, 5, "Paragraf sesudah tabel.", page: 1, y0: 270f, y1: 305f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateTableAsync", fixture.Db);

        var error = Assert.Single(result.Errors, item => item.Message == "Jumlah baris kosong sebelum blok tabel tidak sesuai");
        Assert.Equal("tabel", error.Field);
        Assert.Equal("Tepat 1 baris kosong", error.Expected);
        Assert.Equal("0 baris kosong", error.Actual);
        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sesudah blok tabel tidak sesuai");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldPreferVisibleBlankParagraphCount_WhenInvisibleBlankAlsoExistsBeforeBlock()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddTableRules();

        fixture.AddParagraphElement(2101, 1, "Paragraf sebelum tabel.", page: 1, y0: 100f, y1: 130f);
        fixture.AddInvisibleBlankParagraphElement(2102, 2);
        fixture.AddBlankParagraphElement(2103, 3, page: 1, y0: 138f, y1: 148f);
        fixture.AddParagraphElement(2104, 4, "Tabel 1.1 Hasil Pengujian", page: 1, y0: 158f, y1: 178f, label: "caption_tabel");
        fixture.AddTableElement(2105, 5, page: 1, y0: 188f, y1: 250f);
        fixture.AddBlankParagraphElement(2106, 6, page: 1, y0: 262f, y1: 272f);
        fixture.AddParagraphElement(2107, 7, "Paragraf sesudah tabel.", page: 1, y0: 284f, y1: 320f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateTableAsync", fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sebelum blok tabel tidak sesuai");
    }

    [Fact]
    public async Task ValidateCodeAsync_ShouldSkipBlankParagraphAfter_WhenBlockEndsAtBottomOfPage()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddCodeRules();

        fixture.AddParagraphElement(3001, 1, "Paragraf sebelum kode.", page: 1, y0: 500f, y1: 530f);
        fixture.AddBlankParagraphElement(3002, 2, page: 1, y0: 540f, y1: 548f);
        fixture.AddParagraphElement(3003, 3, "Algoritma 1.1 Bubble Sort", page: 1, y0: 560f, y1: 580f, label: "judul_kode");
        fixture.AddCodeElement(3004, 4, "for i in range(n):", page: 1, y0: 592f, y1: 700f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateCodeAsync", fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sebelum blok kode tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sesudah blok kode tidak sesuai");
    }

    [Fact]
    public async Task ValidateImageAsync_ShouldValidateBlankParagraphFormatsAgainstParagraphRule()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageRules();
        fixture.AddParagraphRule();
        fixture.AddBlankParagraphFormats();

        fixture.AddParagraphElement(4001, 1, "Paragraf sebelum gambar.", page: 1, y0: 100f, y1: 125f);
        fixture.AddFormattedBlankParagraphElement(4002, 2, paragraphFormatId: 501, textFormatId: 601, page: 1, y0: 135f, y1: 148f);
        fixture.AddParagraphElement(4003, 3, "Gambar 1.1 Arsitektur Sistem", page: 1, y0: 160f, y1: 180f, label: "caption_gambar");
        fixture.AddImageElement(4004, 4, page: 1, y0: 190f, y1: 260f);
        fixture.AddFormattedBlankParagraphElement(4005, 5, paragraphFormatId: 501, textFormatId: 601, page: 1, y0: 272f, y1: 285f);
        fixture.AddParagraphElement(4006, 6, "Paragraf sesudah gambar.", page: 1, y0: 296f, y1: 330f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateImageAsync", fixture.Db);

        Assert.Contains(result.Errors, item => item.Message == "Font baris kosong sebelum blok gambar tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Ukuran font baris kosong sebelum blok gambar tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Line spacing baris kosong sebelum blok gambar tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Font baris kosong sesudah blok gambar tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Line spacing baris kosong sesudah blok gambar tidak sesuai dengan aturan paragraf");
    }

    [Fact]
    public async Task ValidateImageAsync_ShouldCountInvisibleBlankParagraphBeforeBlock()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageRules();

        fixture.AddParagraphElement(4501, 1, "Paragraf sebelum gambar.", page: 1, y0: 100f, y1: 130f);
        fixture.AddInvisibleBlankParagraphElement(4502, 2);
        fixture.AddParagraphElement(4503, 3, "Gambar 1.1 Contoh", page: 1, y0: 160f, y1: 180f, label: "caption_gambar");
        fixture.AddImageElement(4504, 4, page: 1, y0: 190f, y1: 260f);
        fixture.AddParagraphElement(4505, 5, "Paragraf sesudah gambar.", page: 1, y0: 272f, y1: 300f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateImageAsync", fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sebelum blok gambar tidak sesuai");
    }

    [Fact]
    public async Task ValidateImageAsync_ShouldCountInvisibleBlankParagraphAfterBlock()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageRules();

        fixture.AddParagraphElement(4601, 1, "Paragraf sebelum gambar.", page: 1, y0: 100f, y1: 130f);
        fixture.AddParagraphElement(4602, 2, "Gambar 1.1 Contoh", page: 1, y0: 145f, y1: 165f, label: "caption_gambar");
        fixture.AddImageElement(4603, 3, page: 1, y0: 175f, y1: 240f);
        fixture.AddInvisibleBlankParagraphElement(4604, 4);
        fixture.AddParagraphElement(4605, 5, "Paragraf sesudah gambar.", page: 1, y0: 270f, y1: 305f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateImageAsync", fixture.Db);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sesudah blok gambar tidak sesuai");
    }

    [Fact]
    public async Task ValidateImageAsync_ShouldUseGapBboxForInvisibleBlankParagraphBeforeFormatErrors()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageRules();
        fixture.AddParagraphRule();
        fixture.AddBlankParagraphFormats();

        fixture.AddParagraphElement(4701, 1, "Paragraf sebelum gambar.", page: 1, y0: 100f, y1: 125f);
        fixture.AddInvisibleFormattedBlankParagraphElement(4702, 2, paragraphFormatId: 501, textFormatId: 601);
        fixture.AddParagraphElement(4703, 3, "Gambar 1.1 Contoh", page: 1, y0: 160f, y1: 180f, label: "caption_gambar");
        fixture.AddImageElement(4704, 4, page: 1, y0: 190f, y1: 260f);
        fixture.AddBlankParagraphElement(4705, 5, page: 1, y0: 272f, y1: 285f);
        fixture.AddParagraphElement(4706, 6, "Paragraf sesudah gambar.", page: 1, y0: 296f, y1: 330f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateImageAsync", fixture.Db);

        var error = Assert.Single(result.Errors, item => item.Message == "Font baris kosong sebelum blok gambar tidak sesuai dengan aturan paragraf");
        var location = Assert.Single(error.Locations);
        Assert.Equal(1, location.HalamanKe);
        Assert.NotNull(location.Bbox);
        Assert.Equal(113.39m, Math.Round(location.Bbox!.X0, 2));
        Assert.Equal(125m, Math.Round(location.Bbox.Y0, 2));
        Assert.Equal(510.24m, Math.Round(location.Bbox.X1, 2));
        Assert.Equal(160m, Math.Round(location.Bbox.Y1, 2));
    }

    [Fact]
    public async Task ValidateCodeAsync_ShouldUseGapBboxForInvisibleBlankParagraphAfterFormatErrors()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddCodeRules();
        fixture.AddParagraphRule();
        fixture.AddBlankParagraphFormats();

        fixture.AddParagraphElement(4801, 1, "Paragraf sebelum kode.", page: 1, y0: 100f, y1: 130f);
        fixture.AddBlankParagraphElement(4802, 2, page: 1, y0: 140f, y1: 152f);
        fixture.AddParagraphElement(4803, 3, "Algoritma 1.1 Bubble Sort", page: 1, y0: 164f, y1: 184f, label: "judul_kode");
        fixture.AddCodeElement(4804, 4, "for i in range(n):", page: 1, y0: 196f, y1: 270f);
        fixture.AddInvisibleFormattedBlankParagraphElement(4805, 5, paragraphFormatId: 501, textFormatId: 601);
        fixture.AddParagraphElement(4806, 6, "Paragraf sesudah kode.", page: 1, y0: 306f, y1: 340f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateCodeAsync", fixture.Db);

        var error = Assert.Single(result.Errors, item => item.Message == "Font baris kosong sesudah blok kode tidak sesuai dengan aturan paragraf");
        var location = Assert.Single(error.Locations);
        Assert.Equal(1, location.HalamanKe);
        Assert.NotNull(location.Bbox);
        Assert.Equal(113.39m, Math.Round(location.Bbox!.X0, 2));
        Assert.Equal(270m, Math.Round(location.Bbox.Y0, 2));
        Assert.Equal(510.24m, Math.Round(location.Bbox.X1, 2));
        Assert.Equal(306m, Math.Round(location.Bbox.Y1, 2));
    }

    [Fact]
    public async Task ValidateImageAsync_ShouldKeepVisibleBlankParagraphOwnBboxForFormatErrors()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageRules();
        fixture.AddParagraphRule();
        fixture.AddBlankParagraphFormats();

        fixture.AddParagraphElement(4901, 1, "Paragraf sebelum gambar.", page: 1, y0: 100f, y1: 125f);
        fixture.AddFormattedBlankParagraphElement(4902, 2, paragraphFormatId: 501, textFormatId: 601, page: 1, y0: 135f, y1: 148f);
        fixture.AddParagraphElement(4903, 3, "Gambar 1.1 Arsitektur Sistem", page: 1, y0: 160f, y1: 180f, label: "caption_gambar");
        fixture.AddImageElement(4904, 4, page: 1, y0: 190f, y1: 260f);
        fixture.AddBlankParagraphElement(4905, 5, page: 1, y0: 272f, y1: 285f);
        fixture.AddParagraphElement(4906, 6, "Paragraf sesudah gambar.", page: 1, y0: 296f, y1: 330f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateImageAsync", fixture.Db);

        var error = Assert.Single(result.Errors, item => item.Message == "Font baris kosong sebelum blok gambar tidak sesuai dengan aturan paragraf");
        var location = Assert.Single(error.Locations);
        Assert.Equal(1, location.HalamanKe);
        Assert.NotNull(location.Bbox);
        Assert.Equal(10m, Math.Round(location.Bbox!.X0, 2));
        Assert.Equal(135m, Math.Round(location.Bbox.Y0, 2));
        Assert.Equal(180m, Math.Round(location.Bbox.X1, 2));
        Assert.Equal(148m, Math.Round(location.Bbox.Y1, 2));
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldValidateBlankParagraphAfterAgainstParagraphRule()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddTableRules();
        fixture.AddParagraphRule();
        fixture.AddBlankParagraphFormats();

        fixture.AddParagraphElement(5001, 1, "Paragraf sebelum tabel.", page: 1, y0: 100f, y1: 130f);
        fixture.AddBlankParagraphElement(5002, 2, page: 1, y0: 138f, y1: 148f);
        fixture.AddParagraphElement(5003, 3, "Tabel 1.1 Hasil Pengujian", page: 1, y0: 158f, y1: 178f, label: "caption_tabel");
        fixture.AddTableElement(5004, 4, page: 1, y0: 188f, y1: 250f);
        fixture.AddFormattedBlankParagraphElement(5005, 5, paragraphFormatId: 501, textFormatId: 601, page: 1, y0: 262f, y1: 274f);
        fixture.AddParagraphElement(5006, 6, "Paragraf sesudah tabel.", page: 1, y0: 286f, y1: 320f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateTableAsync", fixture.Db);

        Assert.Contains(result.Errors, item => item.Message == "Font baris kosong sesudah blok tabel tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Ukuran font baris kosong sesudah blok tabel tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Line spacing baris kosong sesudah blok tabel tidak sesuai dengan aturan paragraf");
    }

    [Fact]
    public async Task ValidateCodeAsync_ShouldValidateBlankParagraphBeforeAgainstParagraphRule()
    {
        using var fixture = new SqliteMediaFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddCodeRules();
        fixture.AddParagraphRule();
        fixture.AddBlankParagraphFormats();

        fixture.AddParagraphElement(6001, 1, "Paragraf sebelum kode.", page: 1, y0: 100f, y1: 130f);
        fixture.AddFormattedBlankParagraphElement(6002, 2, paragraphFormatId: 501, textFormatId: 601, page: 1, y0: 140f, y1: 152f);
        fixture.AddParagraphElement(6003, 3, "Algoritma 1.1 Bubble Sort", page: 1, y0: 164f, y1: 184f, label: "judul_kode");
        fixture.AddCodeElement(6004, 4, "for i in range(n):", page: 1, y0: 196f, y1: 270f);
        fixture.AddBlankParagraphElement(6005, 5, page: 1, y0: 282f, y1: 294f);
        fixture.AddParagraphElement(6006, 6, "Paragraf sesudah kode.", page: 1, y0: 306f, y1: 340f);
        await fixture.Db.SaveChangesAsync();

        var result = await InvokeValidationAsync("ValidateCodeAsync", fixture.Db);

        Assert.Contains(result.Errors, item => item.Message == "Font baris kosong sebelum blok kode tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Ukuran font baris kosong sebelum blok kode tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Line spacing baris kosong sebelum blok kode tidak sesuai dengan aturan paragraf");
    }

    private static async Task<ValidationResult> InvokeValidationAsync(string methodName, KorektorBukuDbContext db)
    {
        var service = new ValidationService(db, NullLogger<ValidationService>.Instance);
        var method = typeof(ValidationService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, new object?[] { 10, CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task!.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<ValidationResult>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteMediaFixture : IDisposable
    {
        public SqliteMediaFixture()
        {
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
                .UseSqlite(Connection)
                .Options;

            Db = new KorektorBukuDbContext(options);
            Db.Database.EnsureCreated();
        }

        public SqliteConnection Connection { get; }

        public KorektorBukuDbContext Db { get; }

        public void AddDokumen()
        {
            Db.Dokumens.Add(new Dokumen
            {
                DokumenId = 10,
                MhsNrp = "1234567890",
                DokumenFilename = "media-blank-line.docx",
                DokumenStatus = "selesai"
            });
        }

        public void AddBodyStructure()
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = 1,
                DsecRefTipe = "dokumen",
                DsecRefId = 10,
                DsecIndex = 1,
                DsecPageWidthTwips = 11907,
                DsecPageHeightTwips = 16839,
                DsecOrientation = "portrait",
                DsecMarginTopTwips = 2268,
                DsecMarginBottomTwips = 1701,
                DsecMarginLeftTwips = 2268,
                DsecMarginRightTwips = 1701,
                DsecHeaderMarginTwips = 1417,
                DsecFooterMarginTwips = 850,
                DsecColumnCount = 1
            });

            Db.DokumenParts.Add(new DokumenPart
            {
                DpartId = 1,
                DsecId = 1,
                DpartType = "body"
            });
        }

        public void AddActiveAturan()
        {
            Db.Aturans.Add(new Aturan
            {
                AturanId = 1,
                AturanVersi = "test",
                AturanStatus = AturanStatusValues.Aktif,
                AturanCreatedAt = DateTime.UtcNow
            });
        }

        public void AddImageRules()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 1,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "gambar",
                AturanDetailJsonValue =
                    """
                    {
                      "gambar": {
                        "struktur_konten": {
                          "jumlah_baris_kosong_sebelum": { "value": 1, "is_editable": true },
                          "jumlah_baris_kosong_setelah": { "value": 1, "is_editable": true },
                          "abaikan_jika_di_awal_halaman": { "value": true, "is_editable": true }
                        }
                      },
                      "caption_gambar": {
                        "position": { "value": "before", "is_editable": true }
                      }
                    }
                    """
            });
        }

        public void AddTableRules()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 2,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "tabel",
                AturanDetailJsonValue =
                    """
                    {
                      "tabel": {
                        "struktur_konten": {
                          "jumlah_baris_kosong_sebelum": { "value": 1, "is_editable": true },
                          "jumlah_baris_kosong_setelah": { "value": 1, "is_editable": true },
                          "abaikan_jika_di_awal_halaman": { "value": true, "is_editable": true }
                        }
                      },
                      "caption_tabel": {
                        "position": { "value": "before", "is_editable": true }
                      }
                    }
                    """
            });
        }

        public void AddCodeRules()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 3,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "kode",
                AturanDetailJsonValue =
                    """
                    {
                      "kode": {
                        "struktur_konten": {
                          "jumlah_baris_kosong_sebelum": { "value": 1, "is_editable": true },
                          "jumlah_baris_kosong_setelah": { "value": 1, "is_editable": true },
                          "abaikan_jika_di_awal_halaman": { "value": true, "is_editable": true }
                        }
                      },
                      "judul_kode": {
                        "position": { "value": "before", "is_editable": true }
                      }
                    }
                    """
            });
        }

        public void AddParagraphRule()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 10,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "paragraf",
                AturanDetailJsonValue =
                    """
                    {
                      "font": {
                        "font_name": { "value": "Times New Roman", "is_editable": true },
                        "font_size": { "value": 12, "is_editable": true }
                      },
                      "paragraph": {
                        "spacing": {
                          "line_spacing": { "value": 1.5, "is_editable": true },
                          "before": { "value": 0, "is_editable": true },
                          "after": { "value": 0, "is_editable": true }
                        }
                      }
                    }
                    """
            });
        }

        public void AddBlankParagraphFormats()
        {
            Db.DokumenFormatParagrafs.AddRange(
                new DokumenFormatParagraf
                {
                    DfpId = 501,
                    DfpJc = "both",
                    DfpSpacingLineTwips = 240,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0
                },
                new DokumenFormatParagraf
                {
                    DfpId = 502,
                    DfpJc = "both",
                    DfpSpacingLineTwips = 360,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0
                });

            Db.DokumenFormatTexts.AddRange(
                new DokumenFormatText
                {
                    DftxId = 601,
                    DftxFontAscii = "Arial",
                    DftxSizeHalfpt = 20,
                    DftxBold = false,
                    DftxItalic = false,
                    DftxUnderline = "none"
                },
                new DokumenFormatText
                {
                    DftxId = 602,
                    DftxFontAscii = "Times New Roman",
                    DftxSizeHalfpt = 24,
                    DftxBold = false,
                    DftxItalic = false,
                    DftxUnderline = "none"
                });
        }

        public void AddParagraphElement(ulong elementId, uint sequence, string text, uint page, float y0, float y1, string label = "paragraf")
        {
            AddElement(elementId, sequence, "paragraph", $$"""{"text":"{{EscapeJson(text)}}"}""", page, y0, y1, label);
        }

        public void AddBlankParagraphElement(ulong elementId, uint sequence, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, "paragraph", """{"text":""}""", page, y0, y1, "paragraf");
        }

        public void AddFormattedBlankParagraphElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId, uint page, float y0, float y1)
        {
            AddElement(
                elementId,
                sequence,
                "paragraph",
                $$"""{"dfp_id":{{paragraphFormatId}},"content":[{"type":"text","dftx_id":{{textFormatId}},"value":""}]}""",
                page,
                y0,
                y1,
                "paragraf");
        }

        public void AddInvisibleBlankParagraphElement(ulong elementId, uint sequence)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = """{"text":""}""",
                DelemenXml = string.Empty
            });
        }

        public void AddInvisibleFormattedBlankParagraphElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = $$"""{"dfp_id":{{paragraphFormatId}},"content":[{"type":"text","dftx_id":{{textFormatId}},"value":""}]}""",
                DelemenXml = string.Empty
            });
        }

        public void AddImageElement(ulong elementId, uint sequence, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, "gambar", """{"content":[{"type":"image","rId":"rId-image"}]}""", page, y0, y1, "gambar");
        }

        public void AddTableElement(ulong elementId, uint sequence, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, "table", """{"content":{"rows":[]}}""", page, y0, y1, "tabel");
        }

        public void AddCodeElement(ulong elementId, uint sequence, string text, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, "paragraph", $$"""{"text":"{{EscapeJson(text)}}"}""", page, y0, y1, "kode");
        }

        private void AddElement(ulong elementId, uint sequence, string elementType, string json, uint page, float y0, float y1, string label)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = elementType,
                DelemenJsonTree = json,
                DelemenXml = string.Empty
            });

            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DokumenElemenId = elementId,
                DevBboxX0 = 10,
                DevBboxY0 = y0,
                DevBboxX1 = 180,
                DevBboxY1 = y1,
                DevLabel = label,
                DevLabelStruktural = label
            });
        }

        private static string EscapeJson(string text)
        {
            return (text ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class CaptionDrivenContentValidationTests
{
    [Fact]
    public async Task ValidateTableAsync_ShouldTreatCaptionGambarWithTableNumberingAsTableViolation()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddTableRules();
        fixture.AddElement(1001, 1, "paragraph", """{"text":"Tabel 1.1 Contoh Tabel"}""");
        fixture.AddElement(1002, 2, "gambar", """{"content":[{"type":"image","rId":"rId-table"}]}""");
        fixture.AddVisual(1, 1001, "caption_gambar");
        fixture.AddVisual(2, 1002, "gambar");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateTableAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "tabel" &&
            error.Message == "Tabel tidak boleh berupa gambar");
    }

    [Fact]
    public async Task ValidateCodeAsync_ShouldTreatCaptionGambarWithCodeNumberingAsCodeViolation()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddCodeRules();
        fixture.AddElement(2001, 1, "paragraph", """{"text":"Algoritma 1.1 Contoh Kode"}""");
        fixture.AddElement(2002, 2, "gambar", """{"content":[{"type":"image","rId":"rId-code"}]}""");
        fixture.AddVisual(1, 2001, "caption_gambar");
        fixture.AddVisual(2, 2002, "gambar");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateCodeAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "kode" &&
            error.Message == "Kode tidak boleh berupa gambar");
    }

    [Fact]
    public async Task ValidateImageAsync_ShouldIgnoreCaptionGambarThatMatchesTableCaption()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageCaptionRules();
        fixture.AddTableRules();
        fixture.AddElement(3001, 1, "paragraph", """{"text":"Tabel 1.1 Contoh Tabel"}""");
        fixture.AddElement(3002, 2, "gambar", """{"content":[{"type":"image","rId":"rId-ignore"}]}""");
        fixture.AddVisual(1, 3001, "caption_gambar");
        fixture.AddVisual(2, 3002, "gambar");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateImageAsync",
            10,
            CancellationToken.None);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldTreatSplitCaptionGambarWithTableNumberingAsTableViolation()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddTableRules();
        fixture.AddElement(4001, 1, "paragraph", """{"text":"Tabel 1.1"}""");
        fixture.AddElement(4002, 2, "paragraph", """{"text":"Contoh Tabel"}""");
        fixture.AddElement(4003, 3, "gambar", """{"content":[{"type":"image","rId":"rId-table-split"}]}""");
        fixture.AddVisual(10, 4001, "caption_gambar");
        fixture.AddVisual(11, 4002, "caption_gambar");
        fixture.AddVisual(12, 4003, "gambar");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateTableAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "tabel" &&
            error.Message == "Tabel tidak boleh berupa gambar");
    }

    [Fact]
    public async Task ValidateImageAsync_ShouldIgnoreSplitCaptionGambarThatMatchesTableCaption()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddImageCaptionRules();
        fixture.AddTableRules();
        fixture.AddElement(5001, 1, "paragraph", """{"text":"Tabel 1.1"}""");
        fixture.AddElement(5002, 2, "paragraph", """{"text":"Contoh Tabel"}""");
        fixture.AddElement(5003, 3, "gambar", """{"content":[{"type":"image","rId":"rId-ignore-split"}]}""");
        fixture.AddVisual(20, 5001, "caption_gambar");
        fixture.AddVisual(21, 5002, "caption_gambar");
        fixture.AddVisual(22, 5003, "gambar");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateImageAsync",
            10,
            CancellationToken.None);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateCodeAsync_ShouldRecognizeGenericCaptionWithSegmenAliasAsCodeTitle()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddCodeRules();
        fixture.AddElement(6001, 1, "paragraph", """{"text":"Segmen 2.1. Algoritma Menghapus Partikel"}""");
        fixture.AddElement(6002, 2, "paragraph", """{"text":"for i in range(n):"}""");
        fixture.AddVisual(30, 6001, "caption");
        fixture.AddVisual(31, 6002, "kode");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateCodeAsync",
            10,
            CancellationToken.None);

        Assert.DoesNotContain(result.Errors, error =>
            error.Field == "judul_kode" &&
            error.Message == "Judul kode tidak ditemukan");
        Assert.DoesNotContain(result.Errors, error =>
            error.Field == "judul_kode" &&
            error.Message == "Format nomor judul kode tidak sesuai");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldAllowLowercaseAffixExampleInQuotedTitle()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();

        fixture.Db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 10,
            AturanId = 1,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "tabel",
            AturanDetailJsonValue = """{"cegah_gambar_tabel":{"value":true,"is_editable":true}}""",
            AturanDetailStatus = 1
        });

        fixture.Db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 11,
            AturanId = 1,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "caption_tabel",
            AturanDetailJsonValue =
                """{"numbering":{"number_format":{"value":"Tabel [nomor_bab].[nomor_tabel]","is_editable":false},"case":{"value":"Title Case","is_editable":true},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"before","is_editable":true}}""",
            AturanDetailStatus = 1
        });

        fixture.AddElement(7001, 1, "paragraph", """{"text":"Tabel 2.8 Cara Menentukan Tipe Awalan untuk Kata yang Diawali \"te-\""}""");
        fixture.AddElement(7002, 2, "table", """{"content":{"rows":[]}}""");
        fixture.AddVisual(40, 7001, "caption_tabel");
        fixture.AddVisual(41, 7002, "tabel");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateTableAsync",
            10,
            CancellationToken.None);

        Assert.DoesNotContain(result.Errors, error =>
            error.Field == "caption_tabel" &&
            error.Message == "Judul caption tabel harus Title Case");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldIgnoreQuotedLowercasePhraseInCaptionTitle()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();

        fixture.Db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 12,
            AturanId = 1,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "tabel",
            AturanDetailJsonValue = """{"cegah_gambar_tabel":{"value":true,"is_editable":true}}""",
            AturanDetailStatus = 1
        });

        fixture.Db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 13,
            AturanId = 1,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "caption_tabel",
            AturanDetailJsonValue =
                """{"numbering":{"number_format":{"value":"Tabel [nomor_bab].[nomor_tabel]","is_editable":false},"case":{"value":"Title Case","is_editable":true},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"before","is_editable":true}}""",
            AturanDetailStatus = 1
        });

        fixture.AddElement(8001, 1, "paragraph", """{"text":"Tabel 2.9 Cara Menentukan Tipe \"contoh awalan\" untuk Kata"}""");
        fixture.AddElement(8002, 2, "table", """{"content":{"rows":[]}}""");
        fixture.AddVisual(50, 8001, "caption_tabel");
        fixture.AddVisual(51, 8002, "tabel");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateTableAsync",
            10,
            CancellationToken.None);

        Assert.DoesNotContain(result.Errors, error =>
            error.Field == "caption_tabel" &&
            error.Message == "Judul caption tabel harus Title Case");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldRequireContinuationCaption_WhenTableSpansMultiplePages()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddTableRules();
        fixture.AddElement(9001, 1, "paragraph", """{"text":"Tabel 1.1 Hasil Pengujian"}""");
        fixture.AddElement(9002, 2, "table", """{"content":{"rows":[]}}""");
        fixture.AddVisual(60, 9001, "caption_tabel", page: 1);
        fixture.AddVisual(61, 9002, "tabel", page: 1);
        fixture.AddVisual(62, 9002, "tabel", page: 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateTableAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "caption_tabel" &&
            error.Message == "Tabel lintas halaman harus memiliki caption lanjutan");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldDefaultContinuationCaptionRule_WhenLegacyEmbeddedCaptionRuleOmitsFlag()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();

        fixture.Db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 20,
            AturanId = 1,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "tabel",
            AturanDetailJsonValue =
                """{"tabel":{"cegah_gambar_tabel":{"value":true,"is_editable":true}},"caption_tabel":{"numbering":{"number_format":{"value":"Tabel [nomor_bab].[nomor_tabel]","is_editable":false},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"before","is_editable":true}}}""",
            AturanDetailStatus = 1
        });

        fixture.AddElement(9021, 1, "paragraph", """{"text":"Tabel 1.1 Hasil Pengujian"}""");
        fixture.AddElement(9022, 2, "table", """{"content":{"rows":[]}}""");
        fixture.AddVisual(63, 9021, "caption_tabel", page: 1);
        fixture.AddVisual(64, 9022, "tabel", page: 1);
        fixture.AddVisual(65, 9022, "tabel", page: 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateTableAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "caption_tabel" &&
            error.Message == "Tabel lintas halaman harus memiliki caption lanjutan");
    }

    [Fact]
    public async Task ValidateTableAsync_ShouldRequireContinuationCaption_WhenSplitTableContinuesOnNextPageWithoutCaption()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddTableRules();
        fixture.AddElement(9051, 1, "paragraph", """{"text":"Tabel 1.1 Hasil Pengujian"}""");
        fixture.AddElement(9052, 2, "table", """{"content":{"rows":[]}}""");
        fixture.AddElement(9053, 3, "table", """{"content":{"rows":[]}}""");
        fixture.AddElement(9054, 4, "paragraph", """{"text":"Tabel 1.2 Hasil Lanjutan"}""");
        fixture.AddElement(9055, 5, "table", """{"content":{"rows":[]}}""");
        fixture.AddVisual(65, 9051, "caption_tabel", page: 1);
        fixture.AddVisual(66, 9052, "tabel", page: 1);
        fixture.AddVisual(67, 9053, "tabel", page: 2);
        fixture.AddVisual(68, 9054, "caption_tabel", page: 2);
        fixture.AddVisual(69, 9055, "tabel", page: 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateTableAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "caption_tabel" &&
            error.Message == "Tabel lintas halaman harus memiliki caption lanjutan");
        Assert.DoesNotContain(result.Errors, error =>
            error.Field == "caption_tabel" &&
            error.Message == "Posisi caption tabel harus sebelum tabel");
    }

    [Fact]
    public async Task ValidateCodeAsync_ShouldRequireContinuationCaption_WhenCodeSpansMultiplePages()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();
        fixture.AddCodeRules();
        fixture.AddElement(9101, 1, "paragraph", """{"text":"Algoritma 1.1 Proses Data"}""");
        fixture.AddElement(9102, 2, "paragraph", """{"text":"for i in range(10):"}""");
        fixture.AddElement(9103, 3, "paragraph", """{"text":"    print(i)"}""");
        fixture.AddVisual(70, 9101, "judul_kode", page: 1);
        fixture.AddVisual(71, 9102, "kode", page: 1);
        fixture.AddVisual(72, 9103, "kode", page: 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateCodeAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "judul_kode" &&
            error.Message == "Kode lintas halaman harus memiliki caption lanjutan");
    }

    [Fact]
    public async Task ValidateCodeAsync_ShouldDefaultContinuationCaptionRule_WhenLegacyEmbeddedTitleRuleOmitsFlag()
    {
        using var fixture = new SqliteValidationFixture();
        fixture.AddDokumen();
        fixture.AddBasicBodyStructure();
        fixture.AddActiveAturan();

        fixture.Db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 21,
            AturanId = 1,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "kode",
            AturanDetailJsonValue =
                """{"kode":{"cegah_gambar_kode":{"value":true,"is_editable":true}},"judul_kode":{"numbering":{"number_format":{"value":["Algoritma [nomor_bab].[nomor_algo]","Segmen Program [nomor_bab].[nomor_segpro]"],"is_editable":false},"enter_after_numbering":{"value":false,"is_editable":true}},"position":{"value":"before","is_editable":true}}}""",
            AturanDetailStatus = 1
        });

        fixture.AddElement(9111, 1, "paragraph", """{"text":"Algoritma 1.1 Proses Data"}""");
        fixture.AddElement(9112, 2, "paragraph", """{"text":"for i in range(10):"}""");
        fixture.AddElement(9113, 3, "paragraph", """{"text":"    print(i)"}""");
        fixture.AddVisual(73, 9111, "judul_kode", page: 1);
        fixture.AddVisual(74, 9112, "kode", page: 1);
        fixture.AddVisual(75, 9113, "kode", page: 2);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeValidationServiceAsync(
            service,
            "ValidateCodeAsync",
            10,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "judul_kode" &&
            error.Message == "Kode lintas halaman harus memiliki caption lanjutan");
    }

    private static ValidationService CreateValidationService(KorektorBukuDbContext db)
        => new(db, NullLogger<ValidationService>.Instance);

    private static async Task<ValidationResult> InvokeValidationServiceAsync(
        ValidationService service,
        string methodName,
        params object[] args)
    {
        var method = typeof(ValidationService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, args) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<ValidationResult>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteValidationFixture : IDisposable
    {
        public SqliteValidationFixture()
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
                DokumenFilename = "uji.docx",
                DokumenStatus = "selesai"
            });
        }

        public void AddBasicBodyStructure()
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = 1,
                DsecRefTipe = "dokumen",
                DsecRefId = 10,
                DsecIndex = 1
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

        public void AddTableRules()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 1,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "tabel",
                AturanDetailJsonValue = """{"cegah_gambar_tabel":{"value":true,"is_editable":true}}""",
                AturanDetailStatus = 1
            });

            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 2,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "caption_tabel",
                AturanDetailJsonValue =
                    """{"numbering":{"number_format":{"value":"Tabel [nomor_bab].[nomor_tabel]","is_editable":false},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"before","is_editable":true},"wajib_caption_lanjutan_jika_lintas_halaman":{"value":true,"is_editable":true}}""",
                AturanDetailStatus = 1
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
                    """{"kode":{"cegah_gambar_kode":{"value":true,"is_editable":true}},"judul_kode":{"numbering":{"number_format":{"value":["Algoritma [nomor_bab].[nomor_algo]","Segmen Program [nomor_bab].[nomor_segpro]"],"is_editable":false},"enter_after_numbering":{"value":false,"is_editable":true}},"position":{"value":"before","is_editable":true},"wajib_caption_lanjutan_jika_lintas_halaman":{"value":true,"is_editable":true}}}""",
                AturanDetailStatus = 1
            });
        }

        public void AddImageCaptionRules()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 4,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "caption_gambar",
                AturanDetailJsonValue =
                    """{"numbering":{"number_format":{"value":"Gambar [nomor_bab].[nomor_gambar]","is_editable":false},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"before","is_editable":true}}""",
                AturanDetailStatus = 1
            });
        }

        public void AddElement(ulong elementId, uint sequence, string elementType, string json)
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
        }

        public void AddVisual(ulong visualId, ulong elementId, string structuralLabel, uint page = 1)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = visualId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DokumenElemenId = elementId,
                DevBboxX0 = 10,
                DevBboxY0 = 10,
                DevBboxX1 = 50,
                DevBboxY1 = 20,
                DevLabel = structuralLabel,
                DevLabelStruktural = structuralLabel
            });
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

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
                    """{"numbering":{"number_format":{"value":"Tabel [nomor_bab].[nomor_tabel]","is_editable":false},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"before","is_editable":true}}""",
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
                    """{"kode":{"cegah_gambar_kode":{"value":true,"is_editable":true}},"judul_kode":{"numbering":{"number_format":{"value":["Algoritma [nomor_bab].[nomor_algo]"],"is_editable":false},"enter_after_numbering":{"value":false,"is_editable":true}},"position":{"value":"before","is_editable":true}}}""",
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

        public void AddVisual(ulong visualId, ulong elementId, string structuralLabel)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = visualId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = 1,
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

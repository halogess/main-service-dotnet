using System.Reflection;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanCatalogContractTests
{
    private static readonly string[] ExpectedDetailKeys =
    [
        "page_settings",
        "nomor_halaman",
        "judul_bab",
        "judul_subbab",
        "paragraf",
        "item_daftar",
        "gambar",
        "tabel",
        "kode",
        "rumus",
        "footnote"
    ];

    private static readonly string[] ExpectedExportElements =
    [
        "page_settings",
        "nomor_halaman",
        "judul_bab",
        "judul_subbab",
        "paragraf",
        "item_daftar",
        "gambar",
        "caption_gambar",
        "tabel",
        "caption_tabel",
        "kode",
        "judul_kode",
        "rumus",
        "footnote"
    ];

    [Fact]
    public void CatalogTemplates_ShouldCanonicalizeIntoStableValidShapes()
    {
        var details = CreateCanonicalCatalogDetails();
        var detailKeys = details
            .Select(detail => detail.AturanDetailKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToArray();

        Assert.Equal(ExpectedDetailKeys, detailKeys);

        foreach (var detail in details)
        {
            Assert.True(
                AturanDetailCanonicalizer.TryCanonicalize(
                    detail.AturanDetailKey,
                    detail.AturanDetailJsonValue!,
                    out var roundTripJson,
                    out var changed,
                    out var errorMessage),
                errorMessage);

            Assert.False(changed);
            Assert.Equal(detail.AturanDetailJsonValue, roundTripJson);
            Assert.True(
                AturanDetailShapeValidator.TryValidate(detail.AturanDetailKey, roundTripJson!, out var shapeErrorMessage),
                shapeErrorMessage);
        }
    }

    [Fact]
    public void CatalogTemplates_ShouldExportWithoutLegacyCriteriaNames()
    {
        var rows = AturanExcelExportBuilder.BuildRows(CreateCanonicalCatalogDetails());
        var exportedElements = rows.Select(row => row.Elemen).Distinct().ToArray();

        Assert.NotEmpty(rows);
        Assert.Equal(ExpectedExportElements, exportedElements);

        Assert.DoesNotContain(rows, row =>
            row.Elemen == "judul_bab" &&
            row.Kriteria is "Satu Baris Kosong Setelah" or "Min Satu Paragraf Sebelum Subbab");

        Assert.DoesNotContain(rows, row =>
            row.Elemen == "judul_subbab" &&
            row.Kriteria is "Minimal Satu Paragraf Setelah" or "Cegah Subbab Tunggal");

        Assert.DoesNotContain(rows, row =>
            row.Elemen is "caption_gambar" or "caption_tabel" or "judul_kode" &&
            row.Kriteria == "Enter After Number");
    }

    [Fact]
    public async Task CatalogTemplates_ShouldRoundTripThroughWorkbookPreviewWithoutChanges()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 100,
            AturanVersi = "catalog-contract",
            AturanStatus = AturanStatusValues.TidakAktif
        });

        var details = CreateCanonicalCatalogDetails()
            .Select((detail, index) =>
            {
                detail.AturanId = 100;
                detail.AturanDetailId = (uint)(index + 1);
                return detail;
            })
            .ToList();

        db.AturanDetails.AddRange(details);
        await db.SaveChangesAsync();

        var workbookBytes = AturanExcelExportBuilder.BuildWorkbook("catalog-contract", details);
        using var workbookStream = new MemoryStream(workbookBytes);
        var file = new FormFile(workbookStream, 0, workbookStream.Length, "file", "catalog-contract.xlsx");

        var service = new AturanExcelImportPreviewService(
            db,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<AturanExcelImportPreviewService>>());

        var result = await service.PreviewAsync(100, file);

        Assert.True(result.TotalRows > 0);
        Assert.Equal(0, result.ChangedRows);
        Assert.Equal(0, result.ChangedDetails);
        Assert.Empty(result.Details);

        using var readStream = new MemoryStream(workbookBytes);
        using var workbook = new XLWorkbook(readStream);
        var worksheet = workbook.Worksheet(AturanExcelExportBuilder.WorksheetName);
        var rows = worksheet.RowsUsed()
            .Skip(1)
            .Select(row => new
            {
                Elemen = row.Cell(1).GetString(),
                Kriteria = row.Cell(4).GetString()
            })
            .ToList();

        Assert.DoesNotContain(rows, row => row.Elemen == "Judul Bab" && row.Kriteria == "Satu Baris Kosong Setelah");
        Assert.DoesNotContain(rows, row => row.Elemen == "Judul Subbab" && row.Kriteria == "Minimal Satu Paragraf Setelah");
        Assert.DoesNotContain(rows, row =>
            row.Elemen is "Caption Gambar" or "Caption Tabel" or "Judul Kode" &&
            row.Kriteria == "Enter After Number");
    }

    private static List<AturanDetail> CreateCanonicalCatalogDetails()
    {
        return LoadCatalogDetails()
            .Select(detail =>
            {
                Assert.True(
                    AturanDetailCanonicalizer.TryCanonicalize(
                        detail.AturanDetailKey,
                        detail.AturanDetailJsonValue!,
                        out var canonicalJson,
                        out var _,
                        out var errorMessage),
                    errorMessage);

                return new AturanDetail
                {
                    AturanDetailKategori = detail.AturanDetailKategori,
                    AturanDetailKey = detail.AturanDetailKey,
                    AturanDetailJsonValue = canonicalJson,
                    AturanDetailStatus = detail.AturanDetailStatus
                };
            })
            .ToList();
    }

    private static IReadOnlyList<AturanDetail> LoadCatalogDetails()
    {
        var assembly = typeof(AturanDetailCanonicalizer).Assembly;
        var catalogType = assembly.GetType("ValidasiTugasAkhir.MainService.Services.AturanExportCatalog");
        Assert.NotNull(catalogType);

        var method = catalogType!.GetMethod(
            "CreateDefaultDetails",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(uint), typeof(bool)],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [0u, false]);
        var details = Assert.IsAssignableFrom<IReadOnlyList<AturanDetail>>(result);
        return details;
    }
}

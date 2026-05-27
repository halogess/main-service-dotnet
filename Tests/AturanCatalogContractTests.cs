using System.Reflection;
using System.Text.Json.Nodes;
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

            Assert.Equal(detail.AturanDetailJsonValue, roundTripJson);
            Assert.True(
                AturanDetailShapeValidator.TryValidate(detail.AturanDetailKey, roundTripJson!, out var shapeErrorMessage),
                shapeErrorMessage);
        }

        var pageNumberRoot = JsonNode.Parse(details.Single(detail => detail.AturanDetailKey == "nomor_halaman").AturanDetailJsonValue!)!.AsObject();
        Assert.Null(pageNumberRoot["paragraph"]!["spacing"]!["line_spacing"]);
    }

    [Fact]
    public void CatalogTemplates_ShouldExposeExactEditablePolicy()
    {
        var details = CreateCanonicalCatalogDetails();

        foreach (var detail in details)
        {
            var lockedPaths = ExtractLockedPaths(JsonNode.Parse(detail.AturanDetailJsonValue!)!);
            var expectedLockedPaths = AturanDetailEditablePolicy
                .GetLockedPaths(detail.AturanDetailKey)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedLockedPaths, lockedPaths);
        }
    }

    [Fact]
    public void FrontendSeed_ShouldMatchBackendEditablePolicy()
    {
        var seedPath = FindFrontendSeedPath();
        var root = JsonNode.Parse(File.ReadAllText(seedPath))!.AsObject();
        var details = root["details"]!.AsArray();

        var detailKeys = details
            .Select(detail => detail?["key"]?.GetValue<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToArray();

        Assert.Equal(ExpectedDetailKeys, detailKeys);

        foreach (var detail in details)
        {
            var key = detail!["key"]!.GetValue<string>();
            var jsonValue = detail["json_value"]!;
            var lockedPaths = ExtractLockedPaths(jsonValue);
            var expectedLockedPaths = AturanDetailEditablePolicy
                .GetLockedPaths(key)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedLockedPaths, lockedPaths);
        }

        var pageNumberSeed = details.Single(detail => detail?["key"]?.GetValue<string>() == "nomor_halaman")!;
        Assert.Null(pageNumberSeed["json_value"]!["paragraph"]!["spacing"]!["line_spacing"]);
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

        Assert.DoesNotContain(rows, row =>
            row.Elemen == "nomor_halaman" &&
            row.Kriteria == "Line Spacing");
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
                    AturanDetailJsonValue = canonicalJson
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

    private static string[] ExtractLockedPaths(JsonNode node)
    {
        var lockedPaths = new List<string>();
        CollectLockedPaths(node, [], lockedPaths);
        return lockedPaths
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void CollectLockedPaths(JsonNode? node, IReadOnlyList<string> path, List<string> lockedPaths)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
                CollectLockedPaths(item, path, lockedPaths);
            return;
        }

        if (node is not JsonObject jsonObject)
            return;

        if (jsonObject["is_editable"] is JsonValue editableValue &&
            editableValue.TryGetValue<bool>(out var isEditable) &&
            !isEditable)
        {
            lockedPaths.Add(string.Join('.', path));
        }

        foreach (var property in jsonObject)
        {
            if (property.Key is "is_editable" or "is_hard_constraint")
                continue;

            CollectLockedPaths(
                property.Value,
                property.Key == "value" ? path : AppendPath(path, property.Key),
                lockedPaths);
        }
    }

    private static IReadOnlyList<string> AppendPath(IReadOnlyList<string> path, string segment)
    {
        if (path.Count == 0)
            return [segment];

        var result = new string[path.Count + 1];
        for (var index = 0; index < path.Count; index++)
            result[index] = path[index];
        result[^1] = segment;
        return result;
    }

    private static string FindFrontendSeedPath()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "cek-ta-react", "default-aturan-seed.json");
            if (File.Exists(candidate))
                return candidate;
        }

        var fallback = Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory) ?? string.Empty, "cek-ta-react", "default-aturan-seed.json");
        if (File.Exists(fallback))
            return fallback;

        throw new FileNotFoundException("Tidak menemukan default-aturan-seed.json pada repo FE.");
    }
}

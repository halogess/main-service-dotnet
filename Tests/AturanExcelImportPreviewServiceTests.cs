using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json.Nodes;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanExcelImportPreviewServiceTests
{
    [Fact]
    public async Task PreviewAsync_ShouldReturnPatchReadyDetailsForChangedValueAndHardConstraint()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 1,
            AturanVersi = "template-a",
            AturanStatus = AturanStatusValues.TidakAktif
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 10,
            AturanId = 1,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "judul_bab",
            AturanDetailJsonValue = """
                                    {
                                      "font": {
                                        "font_name": { "value": "Times New Roman", "is_editable": true, "is_hard_constraint": false }
                                      }
                                    }
                                    """
        });
        await db.SaveChangesAsync();

        var service = new AturanExcelImportPreviewService(
            db,
            Mock.Of<ILogger<AturanExcelImportPreviewService>>());

        var file = CreateWorkbookFile(await db.AturanDetails.Where(detail => detail.AturanId == 1).ToListAsync(), worksheet =>
        {
            var row = FindRow(worksheet, "Judul Bab", "Font", string.Empty, "Font Name");
            row.Cell(5).Value = "Arial";
            row.Cell(6).Value = true;
        });

        var result = await service.PreviewAsync(1, file);

        var changedDetail = Assert.Single(result.Details);
        Assert.True(result.TotalRows > 0);
        Assert.Equal(1, result.ChangedRows);
        Assert.Equal(1, result.ChangedDetails);
        Assert.Equal((uint)10, changedDetail.AturanDetailId);

        var root = JsonNode.Parse(changedDetail.JsonValue)!.AsObject();
        Assert.Equal("Arial", root["font"]?["font_name"]?["value"]?.GetValue<string>());
        Assert.True(root["font"]?["font_name"]?["is_hard_constraint"]?.GetValue<bool>());
    }

    [Fact]
    public async Task PreviewAsync_ShouldApplyChangeToNestedSplitRule()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 2,
            AturanVersi = "template-image",
            AturanStatus = AturanStatusValues.TidakAktif
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 20,
            AturanId = 2,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "gambar",
            AturanDetailJsonValue = """
                                    {
                                      "gambar": {
                                        "paragraph": {
                                          "alignment": { "value": "center", "is_editable": true, "is_hard_constraint": false }
                                        }
                                      },
                                      "caption_gambar": {
                                        "position": { "value": "after", "is_editable": true, "is_hard_constraint": false }
                                      }
                                    }
                                    """
        });
        await db.SaveChangesAsync();

        var service = new AturanExcelImportPreviewService(
            db,
            Mock.Of<ILogger<AturanExcelImportPreviewService>>());

        var file = CreateWorkbookFile(await db.AturanDetails.Where(detail => detail.AturanId == 2).ToListAsync(), worksheet =>
        {
            var row = FindRow(worksheet, "Caption Gambar", "Umum", string.Empty, "Position");
            row.Cell(5).Value = "before";
        });

        var result = await service.PreviewAsync(2, file);

        var changedDetail = Assert.Single(result.Details);
        var root = JsonNode.Parse(changedDetail.JsonValue)!.AsObject();
        Assert.Equal("before", root["caption_gambar"]?["position"]?["value"]?.GetValue<string>());
    }

    [Fact]
    public async Task PreviewAsync_ShouldRejectWorkbookWithMissingRows()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 3,
            AturanVersi = "template-missing",
            AturanStatus = AturanStatusValues.TidakAktif
        });
        db.AturanDetails.Add(new AturanDetail
        {
            AturanDetailId = 30,
            AturanId = 3,
            AturanDetailKategori = "Isi Buku",
            AturanDetailKey = "judul_bab",
            AturanDetailJsonValue = """
                                    {
                                      "font": {
                                        "font_name": { "value": "Times New Roman", "is_editable": true, "is_hard_constraint": false }
                                      }
                                    }
                                    """
        });
        await db.SaveChangesAsync();

        var service = new AturanExcelImportPreviewService(
            db,
            Mock.Of<ILogger<AturanExcelImportPreviewService>>());

        var file = CreateWorkbookFile(await db.AturanDetails.Where(detail => detail.AturanId == 3).ToListAsync(), worksheet =>
        {
            worksheet.Row(2).Delete();
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(3, file));

        Assert.Contains("Jumlah row workbook tidak cocok", error.Message);
    }

    private static IFormFile CreateWorkbookFile(
        IReadOnlyList<AturanDetail> details,
        Action<IXLWorksheet>? mutate = null)
    {
        var bytes = AturanExcelExportBuilder.BuildWorkbook("v-test", details);
        using var sourceStream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(sourceStream);
        var worksheet = workbook.Worksheet(AturanExcelExportBuilder.WorksheetName);
        mutate?.Invoke(worksheet);

        var outputStream = new MemoryStream();
        workbook.SaveAs(outputStream);
        outputStream.Position = 0;
        return new FormFile(outputStream, 0, outputStream.Length, "file", "aturan-import.xlsx");
    }

    private static IXLRow FindRow(
        IXLWorksheet worksheet,
        string elemen,
        string kategori,
        string subKategori,
        string kriteria)
    {
        return worksheet.RowsUsed()
            .Skip(1)
            .Single(row =>
                row.Cell(1).GetString() == elemen &&
                row.Cell(2).GetString() == kategori &&
                row.Cell(3).GetString() == subKategori &&
                row.Cell(4).GetString() == kriteria);
    }
}

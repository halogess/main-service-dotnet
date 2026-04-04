using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ValidasiTugasAkhir.MainService.Controllers;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanControllerExportTests
{
    [Fact]
    public async Task ExportAturan_ShouldReturnWorkbookFileForAdmin()
    {
        var aturanService = new Mock<IAturanService>();
        aturanService
            .Setup(service => service.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(new AturanWithDetails
            {
                Aturan = new Aturan
                {
                    AturanId = 1,
                    AturanVersi = "v1-export"
                },
                Details =
                [
                    new AturanDetail
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
                    }
                ]
            });

        await using var db = ControllerTestHelpers.CreateDbContext();
        var controller = new AturanController(aturanService.Object, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items["Role"] = "admin";

        var result = await controller.ExportAturan(1);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(AturanExcelExportBuilder.ContentType, fileResult.ContentType);
        Assert.Equal("v1-export.xlsx", fileResult.FileDownloadName);
        Assert.NotEmpty(fileResult.FileContents);
    }

    [Fact]
    public void BuildWorkbook_ShouldNotIncludeVersionAndExportedMetadataRows()
    {
        IReadOnlyList<AturanDetail> details =
        [
            new AturanDetail
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
            }
        ];

        var bytes = AturanExcelExportBuilder.BuildWorkbook("v1-export", details);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Aturan");
        var usedValues = worksheet.CellsUsed().Select(cell => cell.GetString()).ToList();

        Assert.DoesNotContain("Versi", usedValues);
        Assert.DoesNotContain("Diekspor", usedValues);
        Assert.Contains("Judul Bab", usedValues);
    }

    [Fact]
    public void BuildWorkbook_ShouldWriteNumericJsonValuesAsNumberCells()
    {
        IReadOnlyList<AturanDetail> details =
        [
            new AturanDetail
            {
                AturanDetailId = 11,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "judul_bab",
                AturanDetailJsonValue = """
                                        {
                                          "line_spacing": { "value": 1.5, "is_editable": true, "is_hard_constraint": false },
                                          "font_name": { "value": "Times New Roman", "is_editable": true, "is_hard_constraint": false }
                                        }
                                        """
            }
        ];

        var bytes = AturanExcelExportBuilder.BuildWorkbook("v1-export", details);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Aturan");
        var dataRows = worksheet.RowsUsed().Skip(1).ToList();

        var numericCell = dataRows
            .Single(row => row.Cell(1).GetString() == "Judul Bab" && row.Cell(4).GetString() == "Line Spacing")
            .Cell(5);
        var textCell = dataRows
            .Single(row => row.Cell(1).GetString() == "Judul Bab" && row.Cell(4).GetString() == "Font Name")
            .Cell(5);

        Assert.Equal(XLDataType.Number, numericCell.DataType);
        Assert.Equal(1.5, numericCell.GetDouble(), 6);
        Assert.Equal(XLDataType.Text, textCell.DataType);
        Assert.Equal("Times New Roman", textCell.GetString());
    }

    [Fact]
    public void BuildWorkbook_ShouldUseCompactWrappedValueColumn()
    {
        IReadOnlyList<AturanDetail> details =
        [
            new AturanDetail
            {
                AturanDetailId = 13,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "judul_bab",
                AturanDetailJsonValue = """
                                        {
                                          "number_format": {
                                            "value": "Format nomor yang cukup panjang untuk perlu wrap text di kolom value",
                                            "is_editable": false,
                                            "is_hard_constraint": false
                                          }
                                        }
                                        """
            }
        ];

        var bytes = AturanExcelExportBuilder.BuildWorkbook("v1-export", details);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Aturan");
        var valueColumn = worksheet.Column(5);

        Assert.Equal(14d, valueColumn.Width, 3);
        Assert.True(valueColumn.Style.Alignment.WrapText);
    }

    [Fact]
    public void BuildWorkbook_ShouldIncludeOnlyVisibleSyntheticValidationRulesOnAturanSheet()
    {
        IReadOnlyList<AturanDetail> details =
        [
            new AturanDetail
            {
                AturanDetailId = 12,
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
            }
        ];

        var bytes = AturanExcelExportBuilder.BuildWorkbook("v1-export", details);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var aturanWorksheet = workbook.Worksheet("Aturan");
        var aturanRows = aturanWorksheet.RowsUsed().Skip(1).ToList();

        Assert.Contains(aturanRows, row => row.Cell(1).GetString() == "Footnote");
        Assert.DoesNotContain(aturanRows, row => row.Cell(1).GetString() == "Daftar Pustaka");
        Assert.Equal(["Aturan"], workbook.Worksheets.Select(sheet => sheet.Name).ToList());
    }
}

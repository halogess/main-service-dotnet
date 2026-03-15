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
        var lastRow = worksheet.LastRowUsed();

        Assert.DoesNotContain("Versi", usedValues);
        Assert.DoesNotContain("Diekspor", usedValues);
        Assert.NotNull(lastRow);
        Assert.Equal(2, lastRow.RowNumber());
    }
}

using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanExcelExportBuilderTests
{
    [Fact]
    public void BuildRows_ShouldSplitCombinedImageAndCaptionIntoSeparateElements()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "gambar",
                AturanDetailJsonValue = """
                                        {
                                          "gambar": {
                                            "paragraph": {
                                              "alignment": { "value": "center", "is_editable": true, "is_hard_constraint": false },
                                              "indentation": {
                                                "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                                                "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": true }
                                              }
                                            }
                                          },
                                          "caption_gambar": {
                                            "paragraph": {
                                              "alignment": { "value": "center", "is_editable": false, "is_hard_constraint": false }
                                            },
                                            "position": { "value": "after", "is_editable": true, "is_hard_constraint": false }
                                          }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "gambar" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == "Indentation" &&
            row.Kriteria == "Right Indent (cm)" &&
            row.Value == "0" &&
            row.HardConstraint &&
            row.Note == "Angka desimal dalam cm");

        Assert.Contains(rows, row =>
            row.Elemen == "caption_gambar" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == string.Empty &&
            row.Kriteria == "Alignment" &&
            row.Value == "center" &&
            !row.HardConstraint &&
            row.Note.Contains("left", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(rows, row =>
            row.Elemen == "caption_gambar" &&
            row.Kategori == "Umum" &&
            row.Kriteria == "Position" &&
            row.Value == "after" &&
            !row.HardConstraint);
    }

    [Fact]
    public void BuildRows_ShouldPreferStandaloneCaptionDetailOverNestedCaption()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 1,
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
                                            "position": { "value": "before", "is_editable": true, "is_hard_constraint": false }
                                          }
                                        }
                                        """
            },
            new()
            {
                AturanDetailId = 2,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "caption_gambar",
                AturanDetailJsonValue = """
                                        {
                                          "position": { "value": "after", "is_editable": true, "is_hard_constraint": false }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        var captionPositionRow = Assert.Single(rows, row => row.Elemen == "caption_gambar" && row.Kriteria == "Position");
        Assert.Equal("after", captionPositionRow.Value);
        Assert.False(captionPositionRow.HardConstraint);
    }

    [Fact]
    public void BuildRows_ShouldPropagateHardConstraintFromWrapperObjectToLeafRows()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 3,
                AturanDetailKategori = "Pengaturan Halaman",
                AturanDetailKey = "paper",
                AturanDetailJsonValue = """
                                        {
                                          "section": {
                                            "isi": {
                                              "value": [
                                                { "size": "A4", "orientation": "PORTRAIT" }
                                              ],
                                              "is_editable": true,
                                              "is_hard_constraint": true
                                            }
                                          }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "paper" &&
            row.Kategori == "Section" &&
            row.SubKategori == "Isi [1]" &&
            row.Kriteria == "Size" &&
            row.Value == "A4" &&
            row.HardConstraint);

        Assert.Contains(rows, row =>
            row.Elemen == "paper" &&
            row.Kategori == "Section" &&
            row.SubKategori == "Isi [1]" &&
            row.Kriteria == "Orientation" &&
            row.Value == "PORTRAIT" &&
            row.HardConstraint);
    }
}

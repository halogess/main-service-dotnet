using ClosedXML.Excel;
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
            row.ValueText == "0" &&
            row.NumericValue == 0 &&
            row.HardConstraint &&
            row.Note == "Angka desimal dalam cm");

        Assert.Contains(rows, row =>
            row.Elemen == "caption_gambar" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == string.Empty &&
            row.Kriteria == "Alignment" &&
            row.ValueText == "center" &&
            !row.HardConstraint &&
            row.Note.Contains("left", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(rows, row =>
            row.Elemen == "caption_gambar" &&
            row.Kategori == "Umum" &&
            row.Kriteria == "Position" &&
            row.ValueText == "after" &&
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
        Assert.Equal("after", captionPositionRow.ValueText);
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
            row.ValueText == "A4" &&
            row.HardConstraint);

        Assert.Contains(rows, row =>
            row.Elemen == "paper" &&
            row.Kategori == "Section" &&
            row.SubKategori == "Isi [1]" &&
            row.Kriteria == "Orientation" &&
            row.ValueText == "PORTRAIT" &&
            row.HardConstraint);
    }

    [Fact]
    public void BuildRows_ShouldPreserveInputDetailOrder()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 20,
                AturanDetailKategori = "Zeta",
                AturanDetailKey = "zeta_elemen",
                AturanDetailJsonValue = """
                                        {
                                          "font_size": { "value": 12, "is_editable": true, "is_hard_constraint": false }
                                        }
                                        """
            },
            new()
            {
                AturanDetailId = 10,
                AturanDetailKategori = "Alpha",
                AturanDetailKey = "alpha_elemen",
                AturanDetailJsonValue = """
                                        {
                                          "font_size": { "value": 10, "is_editable": true, "is_hard_constraint": false }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);
        var orderedElements = rows.Select(row => row.Elemen).Distinct().ToList();

        Assert.Equal(["zeta_elemen", "alpha_elemen"], orderedElements);
    }

    [Fact]
    public void BuildRows_ShouldFormatCanonicalPageNumberValuesClearlyForExcel()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 30,
                AturanDetailKategori = "Nomor Halaman",
                AturanDetailKey = "nomor_halaman_isi",
                AturanDetailJsonValue = """
                                        {
                                          "different_first_page": { "value": true, "is_editable": false, "is_hard_constraint": false },
                                          "first_page": {
                                            "position": {
                                              "indentation": { "value": 0, "is_editable": false, "is_hard_constraint": false }
                                            },
                                            "number_format": {
                                              "prefix": { "value": "", "is_editable": false, "is_hard_constraint": false }
                                            },
                                            "text_style": {
                                              "spacing_after": { "value": 0, "is_editable": false, "is_hard_constraint": false }
                                            }
                                          }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman_isi" &&
            row.Kategori == "First Page" &&
            row.SubKategori == "Position" &&
            row.Kriteria == "Indentation" &&
            row.ValueText == "0" &&
            row.NumericValue == 0 &&
            row.Note == "Isi `none` atau angka 0 berarti tanpa indentasi");

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman_isi" &&
            row.Kategori == "First Page" &&
            row.SubKategori == "Number Format" &&
            row.Kriteria == "Prefix" &&
            row.ValueText == "(tanpa prefix)" &&
            row.Note == "Kosong = tanpa prefix");

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman_isi" &&
            row.Kategori == "First Page" &&
            row.SubKategori == "Text Style" &&
            row.Kriteria == "Spacing After" &&
            row.ValueText == "0" &&
            row.NumericValue == 0);
    }

    [Fact]
    public void BuildRows_ShouldUseCanonicalSyntheticTemplatesWithoutLegacyPageNumbering()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 40,
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
                                              "is_hard_constraint": false
                                            }
                                          }
                                        }
                                        """
            }
        };

        var workbookBytes = AturanExcelExportBuilder.BuildWorkbook("v-test", details);
        using var stream = new MemoryStream(workbookBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Aturan");
        var rows = worksheet.RowsUsed()
            .Skip(1)
            .Select(row => new
            {
                Elemen = row.Cell(1).GetString(),
                Kategori = row.Cell(2).GetString(),
                SubKategori = row.Cell(3).GetString(),
                Kriteria = row.Cell(4).GetString(),
                Value = row.Cell(5).GetString()
            })
            .ToList();

        Assert.DoesNotContain(rows, row => row.Elemen.Equals("Page Numbering", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rows, row => row.Value is "True" or "False" or "None");

        Assert.Contains(rows, row =>
            row.Elemen == "Nomor Halaman Awal" &&
            row.Kategori == "Default Page" &&
            row.SubKategori == "Number Format" &&
            row.Kriteria == "Prefix" &&
            row.Value == "(tanpa prefix)");

        Assert.Contains(rows, row =>
            row.Elemen == "Gambar" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == "Indentation" &&
            row.Kriteria == "Left Indent (cm)" &&
            row.Value == "0");
    }
}

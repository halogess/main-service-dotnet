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
                AturanDetailKey = "nomor_halaman",
                AturanDetailJsonValue = """
                                        {
                                          "numbering": {
                                            "number_format": { "value": "decimal", "is_editable": false, "is_hard_constraint": false }
                                          },
                                          "paragraph": {
                                            "indentation": {
                                              "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                                            },
                                            "spacing": {
                                              "after": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                                            }
                                          },
                                          "variation": {
                                            "different_first_page": {
                                              "enabled": { "value": true, "is_editable": true, "is_hard_constraint": false },
                                              "first": {
                                                "position": {
                                                  "location": { "value": "header", "is_editable": true, "is_hard_constraint": false },
                                                  "alignment": { "value": "right", "is_editable": true, "is_hard_constraint": false }
                                                }
                                              }
                                            }
                                          }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman" &&
            row.Kategori == "Numbering" &&
            row.SubKategori == string.Empty &&
            row.Kriteria == "Number Format" &&
            row.ValueText == "decimal" &&
            row.Note == "Nilai yang tersedia: decimal, lowerRoman, upperRoman, lowerLetter, upperLetter");

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == "Indentation" &&
            row.Kriteria == "Left Indent (cm)" &&
            row.ValueText == "0" &&
            row.NumericValue == 0);

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == "Spacing" &&
            row.Kriteria == "After" &&
            row.ValueText == "0" &&
            row.NumericValue == 0);

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman" &&
            row.Kategori == "Variation" &&
            row.SubKategori == "Different First Page / First / Position" &&
            row.Kriteria == "Location" &&
            row.ValueText == "header");
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
        Assert.DoesNotContain(rows, row => row.Elemen.StartsWith("Nomor Halaman ", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(rows, row =>
            row.Elemen == "Nomor Halaman" &&
            row.Kategori == "Numbering" &&
            row.SubKategori == string.Empty &&
            row.Kriteria == "Number Format" &&
            row.Value == "decimal");

        Assert.Contains(rows, row =>
            row.Elemen == "Gambar" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == "Indentation" &&
            row.Kriteria == "Left Indent (cm)" &&
            row.Value == "0");

    }

    [Fact]
    public void BuildRows_ShouldNotEmitFirstLineIndentForCanonicalListItemRule()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 41,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "item_daftar",
                AturanDetailJsonValue = """
                                        {
                                          "font": {
                                            "font_name": { "value": "Times New Roman", "is_editable": true, "is_hard_constraint": false },
                                            "font_size": { "value": 12, "is_editable": true, "is_hard_constraint": false }
                                          },
                                          "paragraph": {
                                            "alignment": { "value": "justify", "is_editable": true, "is_hard_constraint": false },
                                            "indentation": {
                                              "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                                              "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                                              "hanging": { "value": 0.75, "is_editable": true, "is_hard_constraint": false }
                                            }
                                          }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "item_daftar" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == "Indentation" &&
            row.Kriteria == "Hanging (cm)" &&
            row.ValueText == "0.75");

        Assert.DoesNotContain(rows, row =>
            row.Elemen == "item_daftar" &&
            row.Kategori == "Paragraph" &&
            row.SubKategori == "Indentation" &&
            row.Kriteria == "First Line Indent (cm)");
    }

    [Fact]
    public void BuildRows_ShouldDescribeAllowedValuesForEnumeratedFields()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 50,
                AturanDetailKategori = "Referensi",
                AturanDetailKey = "footnote",
                AturanDetailJsonValue = """
                                        {
                                          "numbering": {
                                            "number_format": { "value": "roman_lower", "is_editable": true, "is_hard_constraint": false },
                                            "type": { "value": "restart_each_page", "is_editable": true, "is_hard_constraint": false }
                                          }
                                        }
                                        """
            },
            new()
            {
                AturanDetailId = 51,
                AturanDetailKategori = "Nomor Halaman",
                AturanDetailKey = "nomor_halaman",
                AturanDetailJsonValue = """
                                        {
                                          "numbering": {
                                            "number_format": { "value": "upperRoman", "is_editable": false, "is_hard_constraint": false }
                                          }
                                        }
                                        """
            },
            new()
            {
                AturanDetailId = 52,
                AturanDetailKategori = "Pengaturan Halaman",
                AturanDetailKey = "page_settings",
                AturanDetailJsonValue = """
                                        {
                                          "gutter": {
                                            "position": { "value": "left", "is_editable": true, "is_hard_constraint": false }
                                          },
                                          "akhir_halaman": {
                                            "max_baris_kosong": { "value": 3, "is_editable": true, "is_hard_constraint": false },
                                            "cegah_halaman_kosong": { "value": true, "is_editable": true, "is_hard_constraint": false }
                                          }
                                        }
                                        """
            },
            new()
            {
                AturanDetailId = 53,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "kode",
                AturanDetailJsonValue = """
                                        {
                                          "numbering": {
                                            "number_format": { "value": "%01", "is_editable": true, "is_hard_constraint": false }
                                          }
                                        }
                                        """
            },
            new()
            {
                AturanDetailId = 54,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "caption_gambar",
                AturanDetailJsonValue = """
                                        {
                                          "position": { "value": "after", "is_editable": true, "is_hard_constraint": false },
                                          "numbering": {
                                            "case": { "value": "Sentence case", "is_editable": true, "is_hard_constraint": false }
                                          }
                                        }
                                        """
            },
            new()
            {
                AturanDetailId = 55,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "rumus",
                AturanDetailJsonValue = """
                                        {
                                          "tabs": {
                                            "left_tab": {
                                              "leader_style": { "value": "none", "is_editable": true, "is_hard_constraint": false }
                                            }
                                          }
                                        }
                                        """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "footnote" &&
            row.Kriteria == "Number Format" &&
            row.Note == "Nilai yang tersedia: arabic, roman_lower, roman_upper, letter_lower, letter_upper, symbol");

        Assert.Contains(rows, row =>
            row.Elemen == "footnote" &&
            row.Kriteria == "Type" &&
            row.Note == "Nilai yang tersedia: continuous, restart_each_page, restart_each_section");

        Assert.Contains(rows, row =>
            row.Elemen == "nomor_halaman" &&
            row.Kategori == "Numbering" &&
            row.Kriteria == "Number Format" &&
            row.Note == "Nilai yang tersedia: decimal, lowerRoman, upperRoman, lowerLetter, upperLetter");

        Assert.Contains(rows, row =>
            row.Elemen == "page_settings" &&
            row.Kategori == "Gutter" &&
            row.Kriteria == "Position" &&
            row.Note == "Nilai yang tersedia: left, top");

        Assert.Contains(rows, row =>
            row.Elemen == "page_settings" &&
            row.Kategori == "Akhir Halaman" &&
            row.Kriteria == "Max Baris Kosong (baris)" &&
            row.Note == "Angka bulat jumlah baris kosong di akhir halaman. Default: 3");

        Assert.Contains(rows, row =>
            row.Elemen == "page_settings" &&
            row.Kategori == "Akhir Halaman" &&
            row.Kriteria == "Cegah Halaman Kosong" &&
            row.Note == "Nilai yang tersedia: true, false");

        Assert.Contains(rows, row =>
            row.Elemen == "kode" &&
            row.Kriteria == "Number Format" &&
            row.Note == "Nilai yang tersedia: none, %1, %01");

        Assert.Contains(rows, row =>
            row.Elemen == "caption_gambar" &&
            row.Kriteria == "Case" &&
            row.Note == "Nilai yang tersedia: UPPERCASE, Title Case, Sentence case, lowercase");

        Assert.Contains(rows, row =>
            row.Elemen == "caption_gambar" &&
            row.Kriteria == "Position" &&
            row.Note == "Nilai yang tersedia: before, after");

        Assert.Contains(rows, row =>
            row.Elemen == "rumus" &&
            row.Kriteria == "Leader Style" &&
            row.Note == "Nilai yang tersedia: none, dots, dash, underline");
    }

    [Fact]
    public void BuildRows_ShouldDescribeMediaBlankParagraphStructureRulesClearly()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 60,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "gambar",
                AturanDetailJsonValue =
                    """
                    {
                      "gambar": {
                        "struktur_konten": {
                          "jumlah_baris_kosong_sebelum": { "value": 1, "is_editable": true, "is_hard_constraint": false },
                          "jumlah_baris_kosong_setelah": { "value": 1, "is_editable": true, "is_hard_constraint": false },
                          "abaikan_jika_di_awal_halaman": { "value": true, "is_editable": true, "is_hard_constraint": false }
                        }
                      }
                    }
                    """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "gambar" &&
            row.Kategori == "Struktur Konten" &&
            row.Kriteria == "Jumlah Baris Kosong Sebelum (baris)" &&
            row.Note == "Angka bulat jumlah baris kosong sebelum blok elemen. Default: 1");

        Assert.Contains(rows, row =>
            row.Elemen == "gambar" &&
            row.Kategori == "Struktur Konten" &&
            row.Kriteria == "Jumlah Baris Kosong Setelah (baris)" &&
            row.Note == "Angka bulat jumlah baris kosong sesudah blok elemen. Default: 1");

        Assert.Contains(rows, row =>
            row.Elemen == "gambar" &&
            row.Kategori == "Struktur Konten" &&
            row.Kriteria == "Abaikan Jika Di Awal Halaman" &&
            row.Note == "Nilai yang tersedia: true, false");
    }

    [Fact]
    public void BuildRows_ShouldNotEmitFontSizeForTableContentRule()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 61,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "tabel",
                AturanDetailJsonValue =
                    """
                    {
                      "tabel": {
                        "konten_tabel": {
                          "font": {
                            "font_name": { "value": "Times New Roman", "is_editable": true, "is_hard_constraint": false },
                            "font_size": { "value": 12, "is_editable": true, "is_hard_constraint": false }
                          }
                        }
                      }
                    }
                    """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "tabel" &&
            row.Kategori == "Konten Tabel" &&
            row.SubKategori == "Font" &&
            row.Kriteria == "Font Name" &&
            row.ValueText == "Times New Roman");

        Assert.DoesNotContain(rows, row =>
            row.Elemen == "tabel" &&
            row.Kategori == "Konten Tabel" &&
            row.SubKategori == "Font" &&
            row.Kriteria == "Font Size (pt)");
    }

    [Fact]
    public void BuildRows_ShouldCanonicalizeLegacyChapterAndSubchapterStructureFields()
    {
        var details = new List<AturanDetail>
        {
            new()
            {
                AturanDetailId = 70,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "judul_bab",
                AturanDetailJsonValue =
                    """
                    {
                      "struktur_konten": {
                        "satu_baris_kosong_setelah": { "value": true, "is_editable": true, "is_hard_constraint": false },
                        "min_satu_paragraf_sebelum_subbab": { "value": false, "is_editable": true, "is_hard_constraint": false }
                      }
                    }
                    """
            },
            new()
            {
                AturanDetailId = 71,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "judul_subbab",
                AturanDetailJsonValue =
                    """
                    {
                      "paragraph": {
                        "indentation": {
                          "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                          "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                        }
                      },
                      "struktur_konten": {
                        "minimal_satu_paragraf_setelah": { "value": true, "is_editable": true, "is_hard_constraint": false },
                        "cegah_subbab_tunggal": { "value": true, "is_editable": true, "is_hard_constraint": false }
                      }
                    }
                    """
            }
        };

        var rows = AturanExcelExportBuilder.BuildRows(details);

        Assert.Contains(rows, row =>
            row.Elemen == "judul_bab" &&
            row.Kategori == "Struktur Konten" &&
            row.Kriteria == "Jumlah Baris Kosong Setelah (baris)" &&
            row.ValueText == "1");

        Assert.Contains(rows, row =>
            row.Elemen == "judul_bab" &&
            row.Kategori == "Struktur Konten" &&
            row.Kriteria == "Minimal Paragraf Sebelum Subbab" &&
            row.ValueText == "0");

        Assert.Contains(rows, row =>
            row.Elemen == "judul_subbab" &&
            row.Kategori == "Struktur Konten" &&
            row.Kriteria == "Minimal Paragraf Setelah" &&
            row.ValueText == "1");

        Assert.Contains(rows, row =>
            row.Elemen == "judul_subbab" &&
            row.Kategori == "Struktur Konten" &&
            row.Kriteria == "Minimal Subbab Level Sama" &&
            row.ValueText == "2");

        Assert.DoesNotContain(rows, row => row.Kriteria == "Satu Baris Kosong Setelah");
        Assert.DoesNotContain(rows, row => row.Kriteria == "Min Satu Paragraf Sebelum Subbab");
        Assert.DoesNotContain(rows, row => row.Kriteria == "Minimal Satu Paragraf Setelah");
        Assert.DoesNotContain(rows, row => row.Kriteria == "Cegah Subbab Tunggal");
    }
}

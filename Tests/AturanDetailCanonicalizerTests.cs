using System.Text.Json.Nodes;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanDetailCanonicalizerTests
{
    [Fact]
    public void TryCanonicalize_ShouldFillMissingTableContinuationFlagAndRenameLegacyAlias()
    {
        const string rawJson = """
            {
              "caption_tabel": {
                "numbering": {
                  "enter_after_number": true
                }
              }
            }
            """;

        var result = AturanDetailCanonicalizer.TryCanonicalize("tabel", rawJson, out var canonicalJson, out var changed, out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.True(changed);

        var root = JsonNode.Parse(canonicalJson!)!.AsObject();
        Assert.True(root["caption_tabel"]!["numbering"]!["enter_after_numbering"]!["value"]!.GetValue<bool>());
        Assert.Null(root["caption_tabel"]!["numbering"]!["enter_after_number"]);
        Assert.True(root["caption_tabel"]!["wajib_caption_lanjutan_jika_lintas_halaman"]!["value"]!.GetValue<bool>());
    }

    [Fact]
    public void TryCanonicalize_ShouldPromoteLegacyParagraphIndentationIntoCanonicalShape()
    {
        const string rawJson = """
            {
              "paragraph": {
                "alignment": "justify",
                "left_indent": 0,
                "right_indent": 0,
                "first_line_indent": 1.5
              }
            }
            """;

        var result = AturanDetailCanonicalizer.TryCanonicalize("paragraf", rawJson, out var canonicalJson, out var changed, out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.True(changed);

        var root = JsonNode.Parse(canonicalJson!)!.AsObject();
        Assert.Equal(0m, root["paragraph"]!["indentation"]!["left_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(0m, root["paragraph"]!["indentation"]!["right_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(1.5m, root["paragraph"]!["indentation"]!["first_line_indent"]!["value"]!.GetValue<decimal>());
        Assert.Null(root["paragraph"]!["left_indent"]);
        Assert.Null(root["paragraph"]!["right_indent"]);
        Assert.Null(root["paragraph"]!["first_line_indent"]);
    }

    [Fact]
    public void TryCanonicalize_ShouldConvertLegacyChapterStructureBooleansIntoCanonicalNumericFields()
    {
        const string rawJson = """
            {
              "struktur_konten": {
                "satu_baris_kosong_setelah": {
                  "value": true,
                  "is_editable": false,
                  "is_hard_constraint": true
                },
                "min_satu_paragraf_sebelum_subbab": {
                  "value": false,
                  "is_editable": true,
                  "is_hard_constraint": false
                }
              }
            }
            """;

        var result = AturanDetailCanonicalizer.TryCanonicalize("judul_bab", rawJson, out var canonicalJson, out var changed, out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.True(changed);

        var root = JsonNode.Parse(canonicalJson!)!.AsObject();
        Assert.Equal(1m, root["struktur_konten"]!["jumlah_baris_kosong_setelah"]!["value"]!.GetValue<decimal>());
        Assert.True(root["struktur_konten"]!["jumlah_baris_kosong_setelah"]!["is_editable"]!.GetValue<bool>());
        Assert.True(root["struktur_konten"]!["jumlah_baris_kosong_setelah"]!["is_hard_constraint"]!.GetValue<bool>());
        Assert.Equal(0m, root["struktur_konten"]!["minimal_paragraf_sebelum_subbab"]!["value"]!.GetValue<decimal>());
        Assert.True(root["struktur_konten"]!["minimal_paragraf_sebelum_subbab"]!["is_editable"]!.GetValue<bool>());
        Assert.False(root["struktur_konten"]!["minimal_paragraf_sebelum_subbab"]!["is_hard_constraint"]!.GetValue<bool>());
        Assert.Null(root["struktur_konten"]!["satu_baris_kosong_setelah"]);
        Assert.Null(root["struktur_konten"]!["min_satu_paragraf_sebelum_subbab"]);
    }

    [Fact]
    public void TryCanonicalize_ShouldConvertLegacySubchapterStructureBooleansIntoCanonicalNumericFields()
    {
        const string rawJson = """
            {
              "paragraph": {
                "indentation": {
                  "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                  "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                }
              },
              "struktur_konten": {
                "minimal_satu_paragraf_setelah": {
                  "value": true,
                  "is_editable": true,
                  "is_hard_constraint": true
                },
                "cegah_subbab_tunggal": {
                  "value": false,
                  "is_editable": false,
                  "is_hard_constraint": false
                }
              }
            }
            """;

        var result = AturanDetailCanonicalizer.TryCanonicalize("judul_subbab", rawJson, out var canonicalJson, out var changed, out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.True(changed);

        var root = JsonNode.Parse(canonicalJson!)!.AsObject();
        Assert.Equal(1m, root["struktur_konten"]!["minimal_paragraf_setelah"]!["value"]!.GetValue<decimal>());
        Assert.True(root["struktur_konten"]!["minimal_paragraf_setelah"]!["is_hard_constraint"]!.GetValue<bool>());
        Assert.Equal(1m, root["struktur_konten"]!["minimal_subbab_level_sama"]!["value"]!.GetValue<decimal>());
        Assert.True(root["struktur_konten"]!["minimal_subbab_level_sama"]!["is_editable"]!.GetValue<bool>());
        Assert.Null(root["struktur_konten"]!["minimal_satu_paragraf_setelah"]);
        Assert.Null(root["struktur_konten"]!["cegah_subbab_tunggal"]);
    }

    [Fact]
    public void TryCanonicalize_ShouldRenameLegacyCaptionGambarEnterAfterAlias()
    {
        const string rawJson = """
            {
              "caption_gambar": {
                "numbering": {
                  "enter_after_number": {
                    "value": false,
                    "is_editable": false,
                    "is_hard_constraint": true
                  }
                }
              }
            }
            """;

        var result = AturanDetailCanonicalizer.TryCanonicalize("gambar", rawJson, out var canonicalJson, out var changed, out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.True(changed);

        var root = JsonNode.Parse(canonicalJson!)!.AsObject();
        Assert.False(root["caption_gambar"]!["numbering"]!["enter_after_numbering"]!["value"]!.GetValue<bool>());
        Assert.True(root["caption_gambar"]!["numbering"]!["enter_after_numbering"]!["is_editable"]!.GetValue<bool>());
        Assert.True(root["caption_gambar"]!["numbering"]!["enter_after_numbering"]!["is_hard_constraint"]!.GetValue<bool>());
        Assert.Null(root["caption_gambar"]!["numbering"]!["enter_after_number"]);
    }

    [Fact]
    public void TryCanonicalize_ShouldRenameLegacyJudulKodeEnterAfterAlias()
    {
        const string rawJson = """
            {
              "judul_kode": {
                "numbering": {
                  "enter_after_number": {
                    "value": true,
                    "is_editable": true,
                    "is_hard_constraint": false
                  }
                }
              }
            }
            """;

        var result = AturanDetailCanonicalizer.TryCanonicalize("kode", rawJson, out var canonicalJson, out var changed, out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.True(changed);

        var root = JsonNode.Parse(canonicalJson!)!.AsObject();
        Assert.True(root["judul_kode"]!["numbering"]!["enter_after_numbering"]!["value"]!.GetValue<bool>());
        Assert.True(root["judul_kode"]!["numbering"]!["enter_after_numbering"]!["is_editable"]!.GetValue<bool>());
        Assert.False(root["judul_kode"]!["numbering"]!["enter_after_numbering"]!["is_hard_constraint"]!.GetValue<bool>());
        Assert.Null(root["judul_kode"]!["numbering"]!["enter_after_number"]);
    }

    [Fact]
    public void TryCanonicalize_ShouldForceExactEditablePolicyForCanonicalRules()
    {
        const string rawJson = """
            {
              "paragraph": {
                "alignment": {
                  "value": "center",
                  "is_editable": false,
                  "is_hard_constraint": false
                }
              },
              "numbering": {
                "number_format": {
                  "value": "BAB I",
                  "is_editable": true,
                  "is_hard_constraint": false
                }
              }
            }
            """;

        var result = AturanDetailCanonicalizer.TryCanonicalize("judul_bab", rawJson, out var canonicalJson, out var changed, out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.True(changed);

        var root = JsonNode.Parse(canonicalJson!)!.AsObject();
        Assert.True(root["paragraph"]!["alignment"]!["is_editable"]!.GetValue<bool>());
        Assert.False(root["numbering"]!["number_format"]!["is_editable"]!.GetValue<bool>());
    }
}

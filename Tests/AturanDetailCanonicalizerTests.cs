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
}

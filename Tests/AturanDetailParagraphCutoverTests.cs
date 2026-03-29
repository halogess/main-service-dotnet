using System.Text.Json.Nodes;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanDetailParagraphCutoverTests
{
    [Fact]
    public void TryTransform_ShouldAddDefaultSubchapterIndentationWrappers()
    {
        var json = """
                   {
                     "paragraph": {
                       "alignment": "justify",
                       "hanging_min_cm": 1.27,
                       "hanging_max_cm": 2.5,
                       "spacing": {
                         "line_spacing": 1.5,
                         "before": 0,
                         "after": 0
                       }
                     }
                   }
                   """;

        var result = AturanDetailParagraphCutover.TryTransform("judul_subbab", json, out var transformedJson, out var changed, out var errorMessage);

        Assert.True(result);
        Assert.True(changed);
        Assert.Null(errorMessage);

        var root = JsonNode.Parse(transformedJson!)!.AsObject();
        var paragraph = root["paragraph"]!.AsObject();
        var indentation = paragraph["indentation"]!.AsObject();

        Assert.Equal(0m, indentation["left_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(0m, indentation["right_indent"]!["value"]!.GetValue<decimal>());
        Assert.NotNull(paragraph["hanging_min_cm"]);
        Assert.NotNull(paragraph["hanging_max_cm"]);
    }

    [Fact]
    public void TryTransform_ShouldMoveParagraphFlatIndentationIntoContainer()
    {
        var json = """
                   {
                     "paragraph": {
                       "alignment": "justify",
                       "left_indent": 0.5,
                       "right_indent": 0.25,
                       "first_line_indent": 1.5,
                       "spacing": {
                         "line_spacing": 1.5,
                         "before": 0,
                         "after": 0
                       }
                     }
                   }
                   """;

        var result = AturanDetailParagraphCutover.TryTransform("paragraf", json, out var transformedJson, out var changed, out var errorMessage);

        Assert.True(result);
        Assert.True(changed);
        Assert.Null(errorMessage);

        var root = JsonNode.Parse(transformedJson!)!.AsObject();
        var paragraph = root["paragraph"]!.AsObject();
        var indentation = paragraph["indentation"]!.AsObject();

        Assert.False(paragraph.ContainsKey("left_indent"));
        Assert.False(paragraph.ContainsKey("right_indent"));
        Assert.False(paragraph.ContainsKey("first_line_indent"));
        Assert.Equal(0.5m, indentation["left_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(0.25m, indentation["right_indent"]!["value"]!.GetValue<decimal>());
        Assert.Equal(1.5m, indentation["first_line_indent"]!["value"]!.GetValue<decimal>());
    }
}

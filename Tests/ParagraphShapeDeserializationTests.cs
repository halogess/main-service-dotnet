using System.Text.Json;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ParagraphShapeDeserializationTests
{
    [Fact]
    public void SubchapterTitleRule_ShouldDeserializeIndentationContainer()
    {
        var rawJson = """
                      {
                        "paragraph": {
                          "alignment": { "value": "justify", "is_editable": true, "is_hard_constraint": false },
                          "indentation": {
                            "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                            "right_indent": { "value": 0.25, "is_editable": true, "is_hard_constraint": false }
                          },
                          "hanging_min_cm": { "value": 1.27, "is_editable": true, "is_hard_constraint": false },
                          "hanging_max_cm": { "value": 2.5, "is_editable": true, "is_hard_constraint": false }
                        }
                      }
                      """;

        var rule = JsonSerializer.Deserialize<SubchapterTitleRule>(rawJson);

        Assert.NotNull(rule);
        Assert.NotNull(rule!.Paragraph);
        Assert.NotNull(rule.Paragraph!.Indentation);
        Assert.Equal(0m, rule.Paragraph.Indentation!.LeftIndent!.Value);
        Assert.Equal(0.25m, rule.Paragraph.Indentation.RightIndent!.Value);
        Assert.Equal(1.27m, rule.Paragraph.HangingMinCm!.Value);
        Assert.Equal(2.5m, rule.Paragraph.HangingMaxCm!.Value);
    }

    [Fact]
    public void ParagraphRule_ShouldDeserializeIndentationContainer()
    {
        var rawJson = """
                      {
                        "paragraph": {
                          "alignment": { "value": "justify", "is_editable": true, "is_hard_constraint": false },
                          "indentation": {
                            "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                            "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                            "first_line_indent": { "value": 1.27, "is_editable": true, "is_hard_constraint": false }
                          }
                        }
                      }
                      """;

        var rule = JsonSerializer.Deserialize<ParagraphRule>(rawJson);

        Assert.NotNull(rule);
        Assert.NotNull(rule!.Paragraph);
        Assert.NotNull(rule.Paragraph!.Indentation);
        Assert.Equal(0m, rule.Paragraph.Indentation!.LeftIndent!.Value);
        Assert.Equal(0m, rule.Paragraph.Indentation.RightIndent!.Value);
        Assert.Equal(1.27m, rule.Paragraph.Indentation.FirstLineIndent!.Value);
    }
}

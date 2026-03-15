using System.Text.Json;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class TitleParagraphIndentationRuleTests
{
    [Fact]
    public void ChapterTitleRule_ShouldDeserializeLegacyNoneIndentation()
    {
        var rawJson = """
                      {
                        "paragraph": {
                          "indentation": {
                            "value": "none",
                            "is_editable": false,
                            "is_hard_constraint": false
                          }
                        }
                      }
                      """;

        var rule = JsonSerializer.Deserialize<ChapterTitleRule>(rawJson);

        Assert.NotNull(rule);
        Assert.NotNull(rule!.Paragraph);
        Assert.NotNull(rule.Paragraph!.Indentation);
        Assert.Equal("none", rule.Paragraph.Indentation!.Value);
        Assert.Null(rule.Paragraph.Indentation.LeftIndent);
        Assert.Null(rule.Paragraph.Indentation.RightIndent);
        Assert.Null(rule.Paragraph.Indentation.FirstLineIndent);
        Assert.Null(rule.Paragraph.Indentation.Hanging);
    }

    [Fact]
    public void ChapterTitleRule_ShouldDeserializeExplicitIndentationObject()
    {
        var rawJson = """
                      {
                        "paragraph": {
                          "indentation": {
                            "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                            "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                            "first_line_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                          }
                        }
                      }
                      """;

        var rule = JsonSerializer.Deserialize<ChapterTitleRule>(rawJson);

        Assert.NotNull(rule);
        Assert.NotNull(rule!.Paragraph);
        Assert.NotNull(rule.Paragraph!.Indentation);
        Assert.Null(rule.Paragraph.Indentation!.Value);
        Assert.Equal(0m, rule.Paragraph.Indentation.LeftIndent!.Value);
        Assert.Equal(0m, rule.Paragraph.Indentation.RightIndent!.Value);
        Assert.Equal(0m, rule.Paragraph.Indentation.FirstLineIndent!.Value);
        Assert.Null(rule.Paragraph.Indentation.Hanging);
    }
}

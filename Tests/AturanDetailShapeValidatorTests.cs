using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanDetailShapeValidatorTests
{
    [Fact]
    public void TryValidate_ShouldAcceptJudulSubbabWithIndentationContainer()
    {
        var json = """
                   {
                     "paragraph": {
                       "alignment": { "value": "justify", "is_editable": true, "is_hard_constraint": false },
                       "indentation": {
                         "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                         "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                       },
                       "hanging_min_cm": { "value": 1.27, "is_editable": true, "is_hard_constraint": false },
                       "hanging_max_cm": { "value": 2.5, "is_editable": true, "is_hard_constraint": false },
                       "spacing": {
                         "line_spacing": { "value": 1.5, "is_editable": true, "is_hard_constraint": false },
                         "before": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                         "after": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                       }
                     }
                   }
                   """;

        var result = AturanDetailShapeValidator.TryValidate("judul_subbab", json, out var errorMessage);

        Assert.True(result);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryValidate_ShouldRejectLegacyJudulSubbabFlatIndent()
    {
        var json = """
                   {
                     "paragraph": {
                       "alignment": { "value": "justify", "is_editable": true, "is_hard_constraint": false },
                       "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                       "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                     }
                   }
                   """;

        var result = AturanDetailShapeValidator.TryValidate("judul_subbab", json, out var errorMessage);

        Assert.False(result);
        Assert.Contains("paragraph.indentation.left_indent/right_indent", errorMessage);
    }

    [Fact]
    public void TryValidate_ShouldAcceptParagrafWithIndentationContainer()
    {
        var json = """
                   {
                     "paragraph": {
                       "alignment": { "value": "justify", "is_editable": true, "is_hard_constraint": false },
                       "indentation": {
                         "left_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                         "right_indent": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                         "first_line_indent": { "value": 1.27, "is_editable": true, "is_hard_constraint": false }
                       },
                       "spacing": {
                         "line_spacing": { "value": 1.5, "is_editable": true, "is_hard_constraint": false },
                         "before": { "value": 0, "is_editable": true, "is_hard_constraint": false },
                         "after": { "value": 0, "is_editable": true, "is_hard_constraint": false }
                       }
                     }
                   }
                   """;

        var result = AturanDetailShapeValidator.TryValidate("paragraf", json, out var errorMessage);

        Assert.True(result);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryValidate_ShouldRejectLegacyParagrafFlatIndent()
    {
        var json = """
                   {
                     "paragraph": {
                       "alignment": { "value": "justify", "is_editable": true, "is_hard_constraint": false },
                       "first_line_indent": { "value": 1.27, "is_editable": true, "is_hard_constraint": false }
                     }
                   }
                   """;

        var result = AturanDetailShapeValidator.TryValidate("paragraf", json, out var errorMessage);

        Assert.False(result);
        Assert.Contains("paragraph.indentation.left_indent/right_indent/first_line_indent", errorMessage);
    }
}

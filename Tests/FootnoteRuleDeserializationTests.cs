using System.Text.Json;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class FootnoteRuleDeserializationTests
{
    [Fact]
    public void FootnoteRule_ShouldDeserializeProvidedStructure()
    {
        var json = """
                   {
                     "numbering": {
                       "number_format": { "value": "arabic", "is_editable": true },
                       "type": { "value": "continuous", "is_editable": true }
                     },
                     "separator": {
                       "paragraph": {
                         "alignment": { "value": "left", "is_editable": false },
                         "indentation": {
                           "left_indent": { "value": 0, "is_editable": false },
                           "first_line_indent": { "value": 0, "is_editable": false }
                         },
                         "spacing": {
                           "before": { "value": 0, "is_editable": false },
                           "after": { "value": 0, "is_editable": false },
                           "line_spacing": { "value": 1, "is_editable": true }
                         }
                       },
                       "cegah_tab_awal": { "value": true, "is_editable": true }
                     },
                     "footnote_text": {
                       "font": {
                         "font_name": { "value": "Times New Roman", "is_editable": true },
                         "font_size": { "value": 10, "is_editable": true }
                       },
                       "paragraph": {
                         "alignment": { "value": "left", "is_editable": true },
                         "spacing": {
                           "line_spacing": { "value": 1, "is_editable": true },
                           "before": { "value": 0, "is_editable": true },
                           "after": { "value": 0, "is_editable": true }
                         }
                       },
                       "struktur_konten": {
                         "satu_enter_sebelum": { "value": true, "is_editable": true }
                       }
                     },
                     "sumber": {
                       "wajib_berisi_sumber": { "value": true, "is_editable": false },
                       "format_penulisan": {
                         "value": [
                           { "keterangan": "", "format": "", "contoh": "" }
                         ],
                         "is_editable": false
                       }
                     }
                   }
                   """;

        var rule = JsonSerializer.Deserialize<FootnoteRule>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(rule);
        Assert.Equal("arabic", rule!.Numbering?.NumberFormat?.Value);
        Assert.Equal("continuous", rule.Numbering?.Type?.Value);
        Assert.Equal("left", rule.Separator?.Paragraph?.Alignment?.Value);
        Assert.True(rule.Separator?.CegahTabAwal?.Value);
        Assert.Equal("Times New Roman", rule.FootnoteText?.Font?.FontName?.Value);
        Assert.Equal(10m, rule.FootnoteText?.Font?.FontSize?.Value);
        Assert.True(rule.FootnoteText?.StrukturKonten?.SatuEnterSebelum?.Value);
        Assert.True(rule.Sumber?.WajibBerisiSumber?.Value);
        Assert.Single(rule.Sumber?.FormatPenulisan?.Value ?? []);
    }
}

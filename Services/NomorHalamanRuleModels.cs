using System.Text.Json.Serialization;

namespace ValidasiTugasAkhir.MainService.Services;

public class NomorHalamanRule
{
    [JsonPropertyName("numbering")]
    public TitleNumberingRule? Numbering { get; set; }

    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("struktur_konten")]
    public NomorHalamanContentStructureRule? StrukturKonten { get; set; }

    [JsonPropertyName("variation")]
    public NomorHalamanVariationRule? Variation { get; set; }
}

public class NomorHalamanContentStructureRule
{
    [JsonPropertyName("cegah_baris_tambahan")]
    public RuleValue<bool>? CegahBarisTambahan { get; set; }
}

public class NomorHalamanVariationRule
{
    [JsonPropertyName("default")]
    public NomorHalamanSlotRule? Default { get; set; }

    [JsonPropertyName("different_first_page")]
    public NomorHalamanFirstPageVariationRule? DifferentFirstPage { get; set; }

    [JsonPropertyName("different_odd_even")]
    public NomorHalamanOddEvenVariationRule? DifferentOddEven { get; set; }
}

public class NomorHalamanFirstPageVariationRule
{
    [JsonPropertyName("enabled")]
    public RuleValue<bool>? Enabled { get; set; }

    [JsonPropertyName("first")]
    public NomorHalamanSlotRule? First { get; set; }
}

public class NomorHalamanOddEvenVariationRule
{
    [JsonPropertyName("enabled")]
    public RuleValue<bool>? Enabled { get; set; }

    [JsonPropertyName("even")]
    public NomorHalamanSlotRule? Even { get; set; }
}

public class NomorHalamanSlotRule
{
    [JsonPropertyName("position")]
    public NomorHalamanPositionRule? Position { get; set; }
}

public class NomorHalamanPositionRule
{
    [JsonPropertyName("location")]
    public RuleValue<string>? Location { get; set; }

    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }
}

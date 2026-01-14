using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

#region DTOs for Aturan JSON Values

public class RuleValue<T>
{
    [JsonPropertyName("value")]
    public T? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }
}

public class DecimalRuleValue
{
    [JsonPropertyName("value")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }
}

public class FlexibleDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                raw = raw.Trim().Replace(',', '.');
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }

        throw new JsonException("Invalid decimal value.");
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

// Paper Size Rule
public class PaperSectionRule
{
    [JsonPropertyName("section")]
    public SectionRules? Section { get; set; }
}

public class SectionRules
{
    [JsonPropertyName("awal")]
    public RuleValue<List<PaperSpec>>? Awal { get; set; }

    [JsonPropertyName("isi")]
    public RuleValue<List<PaperSpec>>? Isi { get; set; }

    [JsonPropertyName("akhir")]
    public RuleValue<List<PaperSpec>>? Akhir { get; set; }

    [JsonPropertyName("lampiran")]
    public RuleValue<List<PaperSpec>>? Lampiran { get; set; }
}

public class PaperSpec
{
    [JsonPropertyName("size")]
    public string? Size { get; set; } // "A4", "A3"

    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; } // "PORTRAIT", "LANDSCAPE"
}

// Margin Rule
public class MarginRule
{
    [JsonPropertyName("paper")]
    public PaperMargins? Paper { get; set; }
}

public class PaperMargins
{
    [JsonPropertyName("a4_portrait")]
    public RuleValue<MarginSpec>? A4Portrait { get; set; }

    [JsonPropertyName("a4_landscape")]
    public RuleValue<MarginSpec>? A4Landscape { get; set; }

    [JsonPropertyName("a3_landscape")]
    public RuleValue<MarginSpec>? A3Landscape { get; set; }
}

public class MarginSpec
{
    [JsonPropertyName("top")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Top { get; set; }

    [JsonPropertyName("bottom")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Bottom { get; set; }

    [JsonPropertyName("left")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Left { get; set; }

    [JsonPropertyName("right")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Right { get; set; }
}

// Header Footer Rule
public class HeaderFooterRule
{
    [JsonPropertyName("header_from_top")]
    public DecimalRuleValue? HeaderFromTop { get; set; }

    [JsonPropertyName("footer_from_bottom")]
    public DecimalRuleValue? FooterFromBottom { get; set; }
}

// Gutter Rule (for binding margin)
public class GutterRule
{
    [JsonPropertyName("gutter")]
    public decimal Gutter { get; set; } // in cm

    [JsonPropertyName("position")]
    public string? Position { get; set; } // "left" or "top"
}

// Column Rule
public class ColumnRule
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}

// Page Numbering Rule
public class PageNumberingRule
{
    [JsonPropertyName("section")]
    public PageNumberingSectionRules? Section { get; set; }
}

public class PageNumberingSectionRules
{
    [JsonPropertyName("awal")]
    public PageNumberingSpec? Awal { get; set; }

    [JsonPropertyName("isi")]
    public PageNumberingSpec? Isi { get; set; }

    [JsonPropertyName("akhir")]
    public PageNumberingSpec? Akhir { get; set; }

    [JsonPropertyName("lampiran")]
    public PageNumberingSpec? Lampiran { get; set; }
}

public class PageNumberingSpec
{
    [JsonPropertyName("format")]
    public string? Format { get; set; } // "decimal", "lowerRoman", "upperRoman", "lowerLetter", "upperLetter"

    [JsonPropertyName("start")]
    public int? Start { get; set; } // Starting page number (null means continue)
}

// Chapter Title Rule (judul_bab)
public class ChapterTitleRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public TitleNumberingRule? Numbering { get; set; }
}

public class TitleFontRule
{
    [JsonPropertyName("font_name")]
    public RuleValue<string>? FontName { get; set; }

    [JsonPropertyName("font_size")]
    public DecimalRuleValue? FontSize { get; set; }

    [JsonPropertyName("font_style")]
    public TitleFontStyleRule? FontStyle { get; set; }
}

public class TitleFontStyleRule
{
    [JsonPropertyName("bold")]
    public RuleValue<bool>? Bold { get; set; }

    [JsonPropertyName("italic")]
    public RuleValue<bool>? Italic { get; set; }

    [JsonPropertyName("underline")]
    public RuleValue<bool>? Underline { get; set; }
}

public class TitleParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public RuleValue<string>? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class TitleParagraphSpacingRule
{
    [JsonPropertyName("line_spacing")]
    public DecimalRuleValue? LineSpacing { get; set; }

    [JsonPropertyName("before")]
    public DecimalRuleValue? Before { get; set; }

    [JsonPropertyName("after")]
    public DecimalRuleValue? After { get; set; }
}

public class TitleNumberingRule
{
    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }

    [JsonPropertyName("case")]
    public RuleValue<string>? Case { get; set; }

    [JsonPropertyName("enter_after_number")]
    public RuleValue<bool>? EnterAfterNumber { get; set; }
}

#endregion

#region Validation Result DTOs

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; set; } = new();
    public int TotalChecks { get; set; }
    public int PassedChecks { get; set; }
    public decimal Score => TotalChecks > 0 ? (decimal)PassedChecks / TotalChecks * 100 : 100;
}

public class ValidationError
{
    public string Category { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public int? SectionIndex { get; set; }
    public int? PageNumber { get; set; }
    public string? DiffType { get; set; }
    public string? Cause { get; set; }
    public bool? HasNumbering { get; set; }
    public string? StyleName { get; set; }
    public string? StyleId { get; set; }
    public string? Evidence { get; set; }
    public string? ToolRequirement { get; set; }
    public string? FeatureName { get; set; }
    public List<string>? AllowedActions { get; set; }
    public List<string>? DisallowedActions { get; set; }
    public string? ScopeHint { get; set; }
    public string? PageRange { get; set; }
}

#endregion

public interface IValidationService
{
    Task<ValidationResult> ValidateDokumenAsync(int dokumenId, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePageSettingsAsync(int dokumenId, CancellationToken cancellationToken = default);
}

public partial class ValidationService : IValidationService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<ValidationService> _logger;

    // Conversion constants
    private const decimal TwipsPerCm = 566.929m; // 1 cm = 566.929 twips
    private const decimal TwipsTolerance = 28.35m; // ~0.5mm tolerance

    // Standard paper sizes in twips (width x height for portrait)
    private static readonly Dictionary<string, (uint Width, uint Height)> PaperSizes = new()
    {
        { "A4", (11906, 16838) },  // 210mm x 297mm
        { "A3", (16838, 23811) },  // 297mm x 420mm
        { "LETTER", (12240, 15840) }, // 8.5" x 11"
        { "LEGAL", (12240, 20160) }  // 8.5" x 14"
    };

    public ValidationService(KorektorBukuDbContext db, ILogger<ValidationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateDokumenAsync(int dokumenId, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        // Validate page settings
        var pageResult = await ValidatePageSettingsAsync(dokumenId, cancellationToken);
        result.Errors.AddRange(pageResult.Errors);
        result.TotalChecks += pageResult.TotalChecks;
        result.PassedChecks += pageResult.PassedChecks;

        // Validate chapter title
        var titleResult = await ValidateChapterTitleAsync(dokumenId, cancellationToken);
        result.Errors.AddRange(titleResult.Errors);
        result.TotalChecks += titleResult.TotalChecks;
        result.PassedChecks += titleResult.PassedChecks;

        // TODO: Add more validation categories here
        // - Font validation
        // - Paragraph formatting validation
        // - Table validation
        // - Image validation
        // etc.

        return result;
    }

}

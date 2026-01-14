using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Resolves theme font languages from settings.xml (w:themeFontLang).
/// </summary>
public class ThemeFontLangResolver
{
    public string? LatinLang { get; }
    public string? EastAsiaLang { get; }
    public string? BidiLang { get; }

    public bool HasEastAsia => !string.IsNullOrWhiteSpace(EastAsiaLang);
    public bool HasBidi => !string.IsNullOrWhiteSpace(BidiLang);

    private ThemeFontLangResolver(string? latinLang, string? eastAsiaLang, string? bidiLang)
    {
        LatinLang = latinLang;
        EastAsiaLang = eastAsiaLang;
        BidiLang = bidiLang;
    }

    public static ThemeFontLangResolver? FromSettingsPart(DocumentSettingsPart? settingsPart)
    {
        var themeLang = settingsPart?.Settings?.GetFirstChild<ThemeFontLanguages>();
        if (themeLang == null)
            return null;

        var latin = GetAttributeValue(themeLang, "val");
        var eastAsia = GetAttributeValue(themeLang, "eastAsia");
        var bidi = GetAttributeValue(themeLang, "bidi");

        return new ThemeFontLangResolver(latin, eastAsia, bidi);
    }

    private static string? GetAttributeValue(OpenXmlElement element, string localName)
    {
        var attr = element.GetAttributes().FirstOrDefault(a => a.LocalName == localName);
        return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
    }
}

using DocumentFormat.OpenXml.Packaging;
using System.Xml.Linq;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Resolves theme font keys (major/minor) to actual typeface names from theme1.xml.
/// </summary>
public class ThemeFontResolver
{
    private readonly Dictionary<string, string> _themeFonts;

    private ThemeFontResolver(Dictionary<string, string> themeFonts)
    {
        _themeFonts = themeFonts;
    }

    /// <summary>
    /// Build a resolver from the document's ThemePart (word/theme/theme1.xml).
    /// Returns null if no theme is available.
    /// </summary>
    public static ThemeFontResolver? FromThemePart(ThemePart? themePart)
    {
        if (themePart == null)
            return null;

        try
        {
            using var stream = themePart.GetStream();
            var xdoc = XDocument.Load(stream);

            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var fontScheme = xdoc.Descendants(a + "fontScheme").FirstOrDefault();
            if (fontScheme == null)
                return new ThemeFontResolver(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var major = fontScheme.Element(a + "majorFont");
            var minor = fontScheme.Element(a + "minorFont");

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var majorLatin = major?.Element(a + "latin")?.Attribute("typeface")?.Value;
            var minorLatin = minor?.Element(a + "latin")?.Attribute("typeface")?.Value;
            var majorEa = major?.Element(a + "ea")?.Attribute("typeface")?.Value;
            var minorEa = minor?.Element(a + "ea")?.Attribute("typeface")?.Value;
            var majorCs = major?.Element(a + "cs")?.Attribute("typeface")?.Value;
            var minorCs = minor?.Element(a + "cs")?.Attribute("typeface")?.Value;

            AddThemeFont(map, "majorHAnsi", majorLatin);
            AddThemeFont(map, "majorAscii", majorLatin);
            AddThemeFont(map, "minorHAnsi", minorLatin);
            AddThemeFont(map, "minorAscii", minorLatin);
            AddThemeFont(map, "majorEastAsia", majorEa);
            AddThemeFont(map, "minorEastAsia", minorEa);
            AddThemeFont(map, "majorBidi", majorCs);
            AddThemeFont(map, "minorBidi", minorCs);

            return new ThemeFontResolver(map);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve a theme key (e.g., majorHAnsi) to an actual font name.
    /// </summary>
    public string? ResolveThemeFont(string? themeValue)
    {
        if (string.IsNullOrWhiteSpace(themeValue))
            return null;

        return _themeFonts.TryGetValue(themeValue.Trim(), out var font) ? font : null;
    }

    private static void AddThemeFont(Dictionary<string, string> map, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        map[key] = value.Trim();
    }
}

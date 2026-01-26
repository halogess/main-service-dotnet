using DocumentFormat.OpenXml.Packaging;
using System.Xml.Linq;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Resolves theme font keys (major/minor) to actual typeface names from theme1.xml.
/// </summary>
public class ThemeFontResolver
{
    private readonly Dictionary<string, string> _themeFonts;
    private readonly Dictionary<string, string> _scriptThemeFonts;

    private ThemeFontResolver(Dictionary<string, string> themeFonts, Dictionary<string, string> scriptThemeFonts)
    {
        _themeFonts = themeFonts;
        _scriptThemeFonts = scriptThemeFonts;
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
                return new ThemeFontResolver(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var major = fontScheme.Element(a + "majorFont");
            var minor = fontScheme.Element(a + "minorFont");

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var scriptMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

            AddScriptFonts(scriptMap, "major", major);
            AddScriptFonts(scriptMap, "minor", minor);

            return new ThemeFontResolver(map, scriptMap);
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
        return ResolveThemeFont(themeValue, null);
    }

    /// <summary>
    /// Resolve a theme key to an actual font name with optional language-aware script mapping.
    /// </summary>
    public string? ResolveThemeFont(string? themeValue, string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(themeValue))
            return null;

        var normalized = themeValue.Trim();
        var scheme = normalized.StartsWith("major", StringComparison.OrdinalIgnoreCase) ? "major"
            : normalized.StartsWith("minor", StringComparison.OrdinalIgnoreCase) ? "minor"
            : null;

        if (scheme != null && !string.IsNullOrWhiteSpace(languageTag))
        {
            var script = MapLanguageToScript(languageTag);
            if (!string.IsNullOrWhiteSpace(script))
            {
                var scriptKey = $"{scheme}:{script}";
                if (_scriptThemeFonts.TryGetValue(scriptKey, out var scriptFont))
                    return scriptFont;
            }
        }

        return _themeFonts.TryGetValue(normalized, out var font) ? font : null;
    }

    private static void AddThemeFont(Dictionary<string, string> map, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        map[key] = value.Trim();
    }

    private static void AddScriptFonts(Dictionary<string, string> map, string scheme, XElement? fontSet)
    {
        if (fontSet == null)
            return;

        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        foreach (var font in fontSet.Elements(a + "font"))
        {
            var script = font.Attribute("script")?.Value;
            var typeface = font.Attribute("typeface")?.Value;
            if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(typeface))
                continue;

            map[$"{scheme}:{script.Trim()}"] = typeface.Trim();
        }
    }

    private static string? MapLanguageToScript(string languageTag)
    {
        var normalized = languageTag.Trim().Replace('_', '-');
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        if (parts.Length >= 2 && parts[1].Length == 4)
            return NormalizeScriptTag(parts[1]);

        var lang = parts[0].ToLowerInvariant();
        var region = parts.Length >= 2 ? parts[1].ToUpperInvariant() : string.Empty;

        if (lang == "ja") return "Jpan";
        if (lang == "ko") return "Hang";
        if (lang == "zh")
        {
            if (region is "TW" or "HK" or "MO" or "HANT" or "CHT")
                return "Hant";
            return "Hans";
        }

        if (lang is "ar" or "fa" or "ur" or "ug" or "ps")
            return "Arab";
        if (lang is "he" or "iw" or "yi" or "ji")
            return "Hebr";
        if (lang == "dv") return "Thaa";
        if (lang == "syr") return "Syrc";
        if (lang == "th") return "Thai";

        return null;
    }

    private static string NormalizeScriptTag(string script)
    {
        if (script.Length == 0)
            return script;

        if (script.Length == 1)
            return script.ToUpperInvariant();

        return char.ToUpperInvariant(script[0]) + script.Substring(1).ToLowerInvariant();
    }
}

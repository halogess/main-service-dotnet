using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles numbering-specific property resolution from numbering.xml
/// </summary>
public class NumberingResolver
{
    /// <summary>
    /// Gets the Level element from numbering.xml for a given numId and ilvl
    /// </summary>
    public static Level? GetNumberingLevel(NumberingDefinitionsPart numberingPart, int numId, int ilvl)
    {
        var numInstance = numberingPart.Numbering?.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId);
        if (numInstance == null) return null;
        
        int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
        if (abstractNumId < 0) return null;
        
        // Check for level override first
        var levelOverride = numInstance.Elements<LevelOverride>()
            .FirstOrDefault(lo => lo.LevelIndex?.Value == ilvl);
        if (levelOverride?.Level != null)
            return levelOverride.Level;
        
        // Fall back to abstract numbering definition
        var abstractNum = numberingPart.Numbering?.Elements<AbstractNum>()
            .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
        
        return abstractNum?.Elements<Level>()
            .FirstOrDefault(l => l.LevelIndex?.Value == ilvl);
    }
    
    /// <summary>
    /// Merge PreviousParagraphProperties (w:lvl/w:pPr) into effective properties
    /// </summary>
    public static void MergeNumberingLevelParagraphProperties(
        EffectiveParagraphProperties effective, 
        PreviousParagraphProperties pPr)
    {
        // Indentation - this is the key property that list levels override
        var ind = pPr.GetFirstChild<Indentation>();
        if (ind != null)
        {
            if (ind.Left?.Value != null) effective.IndentLeft = int.Parse(ind.Left.Value);
            if (ind.Right?.Value != null) effective.IndentRight = int.Parse(ind.Right.Value);
            if (ind.FirstLine?.Value != null) effective.IndentFirstLine = int.Parse(ind.FirstLine.Value);
            if (ind.Hanging?.Value != null) effective.IndentHanging = int.Parse(ind.Hanging.Value);
        }
        
        // Spacing
        var spacing = pPr.GetFirstChild<SpacingBetweenLines>();
        if (spacing != null)
        {
            if (spacing.Before?.Value != null) effective.SpaceBefore = int.Parse(spacing.Before.Value);
            if (spacing.After?.Value != null) effective.SpaceAfter = int.Parse(spacing.After.Value);
            if (spacing.Line?.Value != null) effective.LineSpacing = int.Parse(spacing.Line.Value);
            if (spacing.LineRule?.Value != null) effective.LineRule = StyleResolverHelpers.ConvertLineRule(spacing.LineRule.Value);
        }
        
        // Justification
        var jc = pPr.GetFirstChild<Justification>();
        if (jc?.Val?.Value != null)
            effective.Justification = StyleResolverHelpers.ConvertJustification(jc.Val.Value);
    }
    
    /// <summary>
    /// Merge NumberingSymbolRunProperties (w:lvl/w:rPr) into effective properties
    /// This applies to the bullet/number character, not the paragraph text
    /// </summary>
    public static void MergeNumberingLevelRunProperties(
        EffectiveRunProperties effective, 
        NumberingSymbolRunProperties rPr, 
        string source,
        ThemeFontResolver? themeFontResolver = null)
    {
        effective.ResolvedFromStyle = source;
        
        var fonts = rPr.GetFirstChild<RunFonts>();
        if (fonts != null)
        {
            var asciiTheme = GetRunFontsAttributeValue(fonts, "asciiTheme");
            var highAnsiTheme = GetRunFontsAttributeValue(fonts, "hAnsiTheme");
            var eastAsiaTheme = GetRunFontsAttributeValue(fonts, "eastAsiaTheme");
            var complexTheme = GetRunFontsAttributeValue(fonts, "cstheme");
            var hint = GetRunFontsAttributeValue(fonts, "hint");

            effective.FontAscii = ResolveFont(effective.FontAscii, fonts.Ascii?.Value, asciiTheme, themeFontResolver);
            effective.FontHighAnsi = ResolveFont(effective.FontHighAnsi, fonts.HighAnsi?.Value, highAnsiTheme, themeFontResolver);
            effective.FontEastAsia = ResolveFont(effective.FontEastAsia, fonts.EastAsia?.Value, eastAsiaTheme, themeFontResolver);
            effective.FontComplexScript = ResolveFont(effective.FontComplexScript, fonts.ComplexScript?.Value, complexTheme, themeFontResolver);
            if (!string.IsNullOrWhiteSpace(hint))
                effective.FontHint = hint;
        }

        ApplyLanguages(effective, rPr.GetFirstChild<Languages>());
        
        var fontSize = rPr.GetFirstChild<FontSize>();
        if (fontSize?.Val?.Value != null && int.TryParse(fontSize.Val.Value, out int sz))
            effective.FontSize = sz;
        
        var fontSizeCs = rPr.GetFirstChild<FontSizeComplexScript>();
        if (fontSizeCs?.Val?.Value != null && int.TryParse(fontSizeCs.Val.Value, out int szCs))
            effective.FontSizeCs = szCs;
        
        var bold = rPr.GetFirstChild<Bold>();
        if (bold != null)
            effective.Bold = bold.Val?.Value ?? true;
        
        var italic = rPr.GetFirstChild<Italic>();
        if (italic != null)
            effective.Italic = italic.Val?.Value ?? true;
        
        var color = rPr.GetFirstChild<Color>();
        if (color?.Val?.Value != null)
            effective.Color = color.Val.Value;
        
        var underline = rPr.GetFirstChild<Underline>();
        if (underline != null)
        {
            var val = underline.Val?.Value;
            effective.Underline = val != null && val != UnderlineValues.None;
            effective.UnderlineStyle = val?.ToString()?.ToLower();
        }
        
        var strike = rPr.GetFirstChild<Strike>();
        if (strike != null)
            effective.Strike = strike.Val?.Value ?? true;
    }

    private static string? ResolveFont(string? current, string? explicitFont, string? themeValue, ThemeFontResolver? themeFontResolver)
    {
        if (!string.IsNullOrWhiteSpace(explicitFont))
            return explicitFont.Trim();

        if (!string.IsNullOrWhiteSpace(themeValue))
        {
            var resolved = themeFontResolver?.ResolveThemeFont(themeValue);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return current;
    }

    private static void ApplyLanguages(EffectiveRunProperties effective, Languages? languages)
    {
        if (languages == null)
            return;

        var latin = GetAttributeValue(languages, "val");
        var eastAsia = GetAttributeValue(languages, "eastAsia");
        var bidi = GetAttributeValue(languages, "bidi");

        if (!string.IsNullOrWhiteSpace(latin))
            effective.LangLatin = latin;
        if (!string.IsNullOrWhiteSpace(eastAsia))
            effective.LangEastAsia = eastAsia;
        if (!string.IsNullOrWhiteSpace(bidi))
            effective.LangBidi = bidi;
    }

    private static string? GetAttributeValue(OpenXmlElement element, string localName)
    {
        var attr = element.GetAttributes().FirstOrDefault(a => a.LocalName == localName);
        return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
    }

    private static string? GetRunFontsAttributeValue(RunFonts fonts, string localName)
    {
        var attr = fonts.GetAttributes().FirstOrDefault(a => a.LocalName == localName);
        return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
    }
}

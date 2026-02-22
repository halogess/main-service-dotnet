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
    /// Gets abstractNumId from numbering.xml for a given numId
    /// </summary>
    public static int? GetAbstractNumberId(NumberingDefinitionsPart numberingPart, int numId)
    {
        return numberingPart.Numbering?.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId)?
            .AbstractNumId?.Val?.Value;
    }

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
            var hasAnyIndent = ind.Left?.Value != null ||
                               ind.Right?.Value != null ||
                               ind.FirstLine?.Value != null ||
                               ind.Hanging?.Value != null ||
                               ind.Start?.Value != null ||
                               ind.End?.Value != null ||
                               ind.LeftChars?.Value != null ||
                               ind.RightChars?.Value != null;

            if (hasAnyIndent)
            {
                // Numbering-level indentation overrides the entire indent group.
                effective.IndentLeft = null;
                effective.IndentRight = null;
                effective.IndentFirstLine = null;
                effective.IndentHanging = null;
                effective.IndentStart = null;
                effective.IndentEnd = null;
                effective.IndentLeftChars = null;
                effective.IndentRightChars = null;
            }

            if (ind.Left?.Value != null) effective.IndentLeft = int.Parse(ind.Left.Value);
            if (ind.Right?.Value != null) effective.IndentRight = int.Parse(ind.Right.Value);
            if (ind.FirstLine?.Value != null) effective.IndentFirstLine = int.Parse(ind.FirstLine.Value);
            if (ind.Hanging?.Value != null) effective.IndentHanging = int.Parse(ind.Hanging.Value);
            if (ind.Start?.Value != null) effective.IndentStart = int.Parse(ind.Start.Value);
            if (ind.End?.Value != null) effective.IndentEnd = int.Parse(ind.End.Value);
            if (ind.LeftChars?.Value != null) effective.IndentLeftChars = ind.LeftChars.Value;
            if (ind.RightChars?.Value != null) effective.IndentRightChars = ind.RightChars.Value;
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
        string source)
    {
        effective.ResolvedFromStyle = source;
        
        var fonts = rPr.GetFirstChild<RunFonts>();
        if (fonts != null)
        {
            var asciiTheme = GetRunFontsAttributeValue(fonts, "asciiTheme");
            var highAnsiTheme = GetRunFontsAttributeValue(fonts, "hAnsiTheme");
            var eastAsiaTheme = GetRunFontsAttributeValue(fonts, "eastAsiaTheme");
            var complexTheme = GetRunFontsAttributeValue(fonts, "csTheme");
            var hint = GetRunFontsAttributeValue(fonts, "hint");

            var ascii = fonts.Ascii?.Value;
            if (!string.IsNullOrWhiteSpace(ascii))
            {
                effective.FontAscii = ascii.Trim();
                effective.FontAsciiTheme = null;
            }
            else if (!string.IsNullOrWhiteSpace(asciiTheme))
            {
                effective.FontAscii = null;
                effective.FontAsciiTheme = asciiTheme.Trim();
            }

            var highAnsi = fonts.HighAnsi?.Value;
            if (!string.IsNullOrWhiteSpace(highAnsi))
            {
                effective.FontHighAnsi = highAnsi.Trim();
                effective.FontHighAnsiTheme = null;
            }
            else if (!string.IsNullOrWhiteSpace(highAnsiTheme))
            {
                effective.FontHighAnsi = null;
                effective.FontHighAnsiTheme = highAnsiTheme.Trim();
            }

            var eastAsia = fonts.EastAsia?.Value;
            if (!string.IsNullOrWhiteSpace(eastAsia))
            {
                effective.FontEastAsia = eastAsia.Trim();
                effective.FontEastAsiaTheme = null;
            }
            else if (!string.IsNullOrWhiteSpace(eastAsiaTheme))
            {
                effective.FontEastAsia = null;
                effective.FontEastAsiaTheme = eastAsiaTheme.Trim();
            }

            var complex = fonts.ComplexScript?.Value;
            if (!string.IsNullOrWhiteSpace(complex))
            {
                effective.FontComplexScript = complex.Trim();
                effective.FontComplexScriptTheme = null;
            }
            else if (!string.IsNullOrWhiteSpace(complexTheme))
            {
                effective.FontComplexScript = null;
                effective.FontComplexScriptTheme = complexTheme.Trim();
            }
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

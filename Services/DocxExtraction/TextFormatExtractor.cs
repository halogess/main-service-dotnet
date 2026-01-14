using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts text (run) formatting properties from OpenXML w:rPr into DokumenFormatText.
/// Supports full style inheritance via StyleResolver.
/// </summary>
public class TextFormatExtractor
{
    /// <summary>
    /// Extract run properties from a Run element (direct formatting only, no inheritance).
    /// Use ExtractEffectiveFormat for full style inheritance.
    /// </summary>
    public DokumenFormatText ExtractFormat(Run run)
    {
        var format = new DokumenFormatText();
        var rPr = run.RunProperties;
        
        // Store raw XML for debugging
        if (rPr != null)
            format.DftxRawRprXml = rPr.OuterXml;
        
        if (rPr == null)
            return format;
        
        // Font ASCII (w:rFonts/@w:ascii)
        var fonts = rPr.RunFonts;
        if (fonts?.Ascii?.Value != null)
            format.DftxFontAscii = fonts.Ascii.Value;
        else if (fonts?.HighAnsi?.Value != null)
            format.DftxFontAscii = fonts.HighAnsi.Value;
        
        // Font Size in half-points (w:sz/@w:val)
        var fontSize = rPr.FontSize;
        if (fontSize?.Val?.Value != null)
        {
            if (ushort.TryParse(fontSize.Val.Value, out ushort size))
                format.DftxSizeHalfpt = size;
        }
        
        // Bold (w:b) - tri-state handling
        var bold = rPr.Bold;
        if (bold != null)
            format.DftxBold = bold.Val == null || bold.Val.Value; // no val = ON, val=false = OFF
        
        // Italic (w:i) - tri-state handling
        var italic = rPr.Italic;
        if (italic != null)
            format.DftxItalic = italic.Val == null || italic.Val.Value;
        
        // Underline (w:u/@w:val)
        var underline = rPr.Underline;
        if (underline?.Val?.Value != null)
        {
            var ulVal = underline.Val.Value;
            if (ulVal == UnderlineValues.None)
                format.DftxUnderline = "none";
            else if (ulVal == UnderlineValues.Single)
                format.DftxUnderline = "single";
            else if (ulVal == UnderlineValues.Double)
                format.DftxUnderline = "double";
            else if (ulVal == UnderlineValues.Dotted || ulVal == UnderlineValues.DottedHeavy)
                format.DftxUnderline = "dotted";
            else if (ulVal == UnderlineValues.Dash || ulVal == UnderlineValues.DashedHeavy || ulVal == UnderlineValues.DashLong || ulVal == UnderlineValues.DashLongHeavy)
                format.DftxUnderline = "dash";
            else if (ulVal == UnderlineValues.Wave || ulVal == UnderlineValues.WavyDouble || ulVal == UnderlineValues.WavyHeavy)
                format.DftxUnderline = "wavy";
            else
                format.DftxUnderline = "single"; // default to single for other types
        }
        
        return format;
    }
    
    /// <summary>
    /// Extract EFFECTIVE run properties using full style inheritance via StyleResolver.
    /// Precedence: docDefaults → paragraphStyle.rPr → characterStyle.rPr → direct rPr
    /// </summary>
    public DokumenFormatText ExtractEffectiveFormat(
        Run run,
        StyleResolver styleResolver,
        ParagraphProperties? paragraphProps = null,
        ThemeFontLangResolver? themeFontLangResolver = null)
    {
        var format = new DokumenFormatText();
        
        // Get effective properties with full inheritance
        var effective = styleResolver.GetEffectiveRunProperties(run, paragraphProps);
        
        // Store raw XML of direct formatting for debugging
        if (run.RunProperties != null)
            format.DftxRawRprXml = run.RunProperties.OuterXml;
        
        // Map effective properties to format model
        format.DftxFontAscii = ResolvePreferredFont(effective, run, themeFontLangResolver);
        format.DftxSizeHalfpt = effective.FontSize.HasValue ? (ushort)effective.FontSize.Value : null;
        format.DftxBold = effective.Bold;
        format.DftxItalic = effective.Italic;
        
        // Map underline
        if (effective.Underline == true && !string.IsNullOrEmpty(effective.UnderlineStyle))
        {
            var ulStyle = effective.UnderlineStyle.ToLower();
            if (ulStyle.Contains("single") || ulStyle == "words")
                format.DftxUnderline = "single";
            else if (ulStyle.Contains("double"))
                format.DftxUnderline = "double";
            else if (ulStyle.Contains("dot"))
                format.DftxUnderline = "dotted";
            else if (ulStyle.Contains("dash"))
                format.DftxUnderline = "dash";
            else if (ulStyle.Contains("wav"))
                format.DftxUnderline = "wavy";
            else
                format.DftxUnderline = "single";
        }
        else if (effective.Underline == false)
        {
            format.DftxUnderline = "none";
        }
        
        return format;
    }

    public static string? ResolvePreferredFont(
        EffectiveRunProperties effective,
        Run run,
        ThemeFontLangResolver? themeFontLangResolver = null)
    {
        var hint = effective.FontHint;
        var text = run.InnerText;
        return ResolvePreferredFont(effective, hint, text, themeFontLangResolver);
    }

    private static string? ResolvePreferredFont(
        EffectiveRunProperties effective,
        string? hint,
        string? text,
        ThemeFontLangResolver? themeFontLangResolver)
    {
        var normalizedHint = hint?.Trim().ToLowerInvariant();
        if (normalizedHint == "eastasia")
            return PickEastAsia(effective);
        if (normalizedHint == "cs" || normalizedHint == "bidi")
            return PickComplexScript(effective);

        if (!string.IsNullOrWhiteSpace(effective.LangBidi))
            return PickComplexScript(effective);
        if (!string.IsNullOrWhiteSpace(effective.LangEastAsia))
            return PickEastAsia(effective);

        if (!string.IsNullOrEmpty(text))
        {
            if (ContainsComplexScript(text))
                return PickComplexScript(effective);
            if (ContainsEastAsian(text))
                return PickEastAsia(effective);
        }

        if (themeFontLangResolver?.HasBidi == true)
            return PickComplexScript(effective);
        if (themeFontLangResolver?.HasEastAsia == true)
            return PickEastAsia(effective);

        return PickLatin(effective);
    }

    private static string? PickLatin(EffectiveRunProperties effective)
    {
        return effective.FontAscii
            ?? effective.FontHighAnsi
            ?? effective.FontEastAsia
            ?? effective.FontComplexScript;
    }

    private static string? PickEastAsia(EffectiveRunProperties effective)
    {
        return effective.FontEastAsia
            ?? effective.FontAscii
            ?? effective.FontHighAnsi
            ?? effective.FontComplexScript;
    }

    private static string? PickComplexScript(EffectiveRunProperties effective)
    {
        return effective.FontComplexScript
            ?? effective.FontAscii
            ?? effective.FontHighAnsi
            ?? effective.FontEastAsia;
    }

    private static bool ContainsEastAsian(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = char.ConvertToUtf32(text, i);
            if (codePoint > 0xFFFF)
                i++;

            if ((codePoint >= 0x1100 && codePoint <= 0x11FF) || // Hangul Jamo
                (codePoint >= 0x2E80 && codePoint <= 0x2EFF) || // CJK Radicals Supplement
                (codePoint >= 0x3000 && codePoint <= 0x303F) || // CJK Symbols and Punctuation
                (codePoint >= 0x3040 && codePoint <= 0x309F) || // Hiragana
                (codePoint >= 0x30A0 && codePoint <= 0x30FF) || // Katakana
                (codePoint >= 0x31F0 && codePoint <= 0x31FF) || // Katakana Extensions
                (codePoint >= 0x3400 && codePoint <= 0x4DBF) || // CJK Ext A
                (codePoint >= 0x4E00 && codePoint <= 0x9FFF) || // CJK Unified Ideographs
                (codePoint >= 0xAC00 && codePoint <= 0xD7AF) || // Hangul Syllables
                (codePoint >= 0xF900 && codePoint <= 0xFAFF) || // CJK Compatibility Ideographs
                (codePoint >= 0x20000 && codePoint <= 0x2A6DF) || // CJK Ext B
                (codePoint >= 0x2A700 && codePoint <= 0x2B73F) || // CJK Ext C
                (codePoint >= 0x2B740 && codePoint <= 0x2B81F) || // CJK Ext D
                (codePoint >= 0x2B820 && codePoint <= 0x2CEAF) || // CJK Ext E
                (codePoint >= 0x2CEB0 && codePoint <= 0x2EBEF) || // CJK Ext F
                (codePoint >= 0x30000 && codePoint <= 0x3134F))   // CJK Ext G
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsComplexScript(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = char.ConvertToUtf32(text, i);
            if (codePoint > 0xFFFF)
                i++;

            if ((codePoint >= 0x0590 && codePoint <= 0x05FF) || // Hebrew
                (codePoint >= 0x0600 && codePoint <= 0x06FF) || // Arabic
                (codePoint >= 0x0700 && codePoint <= 0x074F) || // Syriac
                (codePoint >= 0x0750 && codePoint <= 0x077F) || // Arabic Supplement
                (codePoint >= 0x0780 && codePoint <= 0x07BF) || // Thaana
                (codePoint >= 0x07C0 && codePoint <= 0x07FF) || // NKo
                (codePoint >= 0x0800 && codePoint <= 0x083F) || // Samaritan
                (codePoint >= 0x0840 && codePoint <= 0x085F) || // Mandaic
                (codePoint >= 0x0860 && codePoint <= 0x086F) || // Syriac Supplement
                (codePoint >= 0x08A0 && codePoint <= 0x08FF) || // Arabic Extended-A
                (codePoint >= 0x0900 && codePoint <= 0x0DFF) || // Indic scripts (broad)
                (codePoint >= 0xFB50 && codePoint <= 0xFDFF) || // Arabic Presentation Forms-A
                (codePoint >= 0xFE70 && codePoint <= 0xFEFF) || // Arabic Presentation Forms-B
                (codePoint >= 0x1EE00 && codePoint <= 0x1EEFF))  // Arabic Mathematical Symbols
            {
                return true;
            }
        }

        return false;
    }
    
    /// <summary>
    /// Check if run has any significant formatting that warrants saving
    /// </summary>
    public bool HasSignificantFormatting(Run run)
    {
        var rPr = run.RunProperties;
        if (rPr == null) return false;
        
        // Check if any formatting properties exist
        return rPr.Bold != null ||
               rPr.Italic != null ||
               rPr.Underline != null ||
               rPr.FontSize != null ||
               rPr.RunFonts != null ||
               rPr.RunStyle != null; // Character style reference
    }
    
    /// <summary>
    /// Check if effective properties differ from document defaults (worth saving)
    /// </summary>
    public bool HasSignificantEffectiveFormatting(EffectiveRunProperties effective)
    {
        // Check if any formatting is explicitly set
        return effective.Bold == true ||
               effective.Italic == true ||
               effective.Underline == true ||
               effective.Strike == true ||
               effective.DoubleStrike == true ||
               effective.Caps == true ||
               effective.SmallCaps == true ||
               !string.IsNullOrEmpty(effective.Color) ||
               !string.IsNullOrEmpty(effective.HighlightColor);
    }
}

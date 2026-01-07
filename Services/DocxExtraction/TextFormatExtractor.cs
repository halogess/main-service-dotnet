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
    public DokumenFormatText ExtractEffectiveFormat(Run run, StyleResolver styleResolver, ParagraphProperties? paragraphProps = null)
    {
        var format = new DokumenFormatText();
        
        // Get effective properties with full inheritance
        var effective = styleResolver.GetEffectiveRunProperties(run, paragraphProps);
        
        // Store raw XML of direct formatting for debugging
        if (run.RunProperties != null)
            format.DftxRawRprXml = run.RunProperties.OuterXml;
        
        // Map effective properties to format model
        format.DftxFontAscii = effective.FontAscii;
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

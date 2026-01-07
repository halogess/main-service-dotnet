using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Static helper methods for style resolution
/// </summary>
public static class StyleResolverHelpers
{
    /// <summary>
    /// Check if an OnOffType toggle is ON (presence without Val means true, Val=true means true)
    /// </summary>
    public static bool IsToggleOn(OnOffType? toggle)
    {
        if (toggle == null) return false;
        if (toggle.Val == null) return true; // Presence without value = true
        return toggle.Val.Value;
    }
    
    /// <summary>
    /// Check if an OnOffType toggle is explicitly OFF (Val=false)
    /// </summary>
    public static bool IsToggleOff(OnOffType? toggle)
    {
        if (toggle == null) return false;
        if (toggle.Val == null) return false; // Presence without value = true, not off
        return !toggle.Val.Value;
    }
    
    /// <summary>
    /// Convert OpenXML LineSpacingRuleValues to camelCase string for database enum.
    /// DB expects: 'auto', 'atLeast', 'exact'
    /// </summary>
    public static string ConvertLineRule(LineSpacingRuleValues value)
    {
        if (value == LineSpacingRuleValues.Auto) return "auto";
        if (value == LineSpacingRuleValues.AtLeast) return "atLeast";
        if (value == LineSpacingRuleValues.Exact) return "exact";
        return value.ToString();
    }

    /// <summary>
    /// Convert OpenXML JustificationValues to database enum.
    /// DB expects: 'left', 'right', 'center', 'both', 'distribute', 'start', 'end'
    /// </summary>
    public static string ConvertJustification(JustificationValues value)
    {
        if (value == JustificationValues.Left) return "left";
        if (value == JustificationValues.Start) return "start";
        if (value == JustificationValues.Right) return "right";
        if (value == JustificationValues.End) return "end";
        if (value == JustificationValues.Center) return "center";
        if (value == JustificationValues.Both) return "both";
        if (value == JustificationValues.Distribute) return "distribute";
        
        // Default fallback
        return "left";
    }

    /// <summary>
    /// Convert OpenXML VerticalTextAlignmentValues to database enum.
    /// DB expects: 'auto', 'baseline', 'top', 'center', 'bottom'
    /// </summary>
    public static string ConvertTextAlignment(VerticalTextAlignmentValues value)
    {
        if (value == VerticalTextAlignmentValues.Auto) return "auto";
        if (value == VerticalTextAlignmentValues.Baseline) return "baseline";
        if (value == VerticalTextAlignmentValues.Top) return "top";
        if (value == VerticalTextAlignmentValues.Center) return "center";
        if (value == VerticalTextAlignmentValues.Bottom) return "bottom";
        
        // Default fallback
        return "auto";
    }
}

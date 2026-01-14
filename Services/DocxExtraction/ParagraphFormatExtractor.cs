using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts EFFECTIVE paragraph formatting properties from OpenXML w:pPr into DokumenFormatParagraf.
/// Resolves full inheritance chain: docDefaults → style basedOn → w:lvl/w:pPr → direct pPr
/// </summary>
public class ParagraphFormatExtractor
{
    private readonly StyleResolver? _styleResolver;
    private readonly NumberingDefinitionsPart? _numberingPart;
    private const uint DefaultLineSpacingTwips = 240; // Single line (auto) in WordprocessingML.
    
    public ParagraphFormatExtractor(StyleResolver? styleResolver, NumberingDefinitionsPart? numberingPart)
    {
        _styleResolver = styleResolver;
        _numberingPart = numberingPart;
    }
    
    /// <summary>
    /// Extract EFFECTIVE paragraph properties from a Paragraph element.
    /// This resolves the full inheritance chain, not just direct formatting.
    /// </summary>
    public DokumenFormatParagraf ExtractFormat(Paragraph paragraph)
    {
        var format = new DokumenFormatParagraf();
        var pPr = paragraph.ParagraphProperties;
        
        // Store raw XML for debugging
        if (pPr != null)
            format.DfpRawPprXml = pPr.OuterXml;
        
        // Style ID (direct reference)
        format.DfpPStyleId = pPr?.ParagraphStyleId?.Val?.Value;
        
        // Get numbering info for resolved properties
        int? numId = null;
        int ilvl = 0;
        
        if (_styleResolver != null)
        {
            (numId, ilvl) = _styleResolver.GetEffectiveNumberingProperties(paragraph);
        }
        else if (pPr?.NumberingProperties != null)
        {
            numId = pPr.NumberingProperties.NumberingId?.Val?.Value;
            ilvl = pPr.NumberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
        }
        
        format.DfpIsList = numId.HasValue && numId.Value > 0;
        format.DfpListNumId = numId.HasValue && numId.Value > 0 ? (uint?)numId.Value : null;
        format.DfpListIlvl = format.DfpIsList ? (uint?)ilvl : null;
        
        // ============================================================
        // GET EFFECTIVE/RESOLVED PROPERTIES (full inheritance chain)
        // Order: docDefaults → style chain → w:lvl/w:pPr → direct pPr
        // ============================================================
        
        if (_styleResolver != null)
        {
            var effective = _styleResolver.GetEffectiveParagraphPropertiesWithNumbering(
                paragraph, _numberingPart, numId, ilvl);
            
            // Map effective properties to model
            MapEffectivePropertiesToFormat(format, effective);
        }
        else
        {
            // Fallback: extract only direct properties if no StyleResolver
            ExtractDirectPropertiesOnly(format, pPr);
        }
        
        // ============================================================
        // NESTED DETAILS (JSON) - Always from direct pPr for reference
        // ============================================================
        
        if (pPr != null)
        {
            // Numbering Properties (JSON)
            var numPr = pPr.NumberingProperties;
            if (numPr != null)
                format.DfpNumprJson = SerializeElementToJson(numPr);
            
            // Paragraph Borders (JSON)
            var pBdr = pPr.ParagraphBorders;
            if (pBdr != null)
                format.DfpPbdrJson = SerializeElementToJson(pBdr);
            
            // Shading (JSON)
            var shd = pPr.Shading;
            if (shd != null)
                format.DfpShdJson = SerializeShadingToJson(shd);
            
            // Tabs (JSON)
            var tabs = pPr.Tabs;
            if (tabs != null)
                format.DfpTabsJson = SerializeTabsToJson(tabs);
            
            // Conditional Style (cnfStyle)
            var cnfStyle = pPr.GetFirstChild<ConditionalFormatStyle>();
            if (cnfStyle != null)
                format.DfpCnfStyleJson = SerializeElementToJson(cnfStyle);
            
            // Paragraph Mark Run Properties
            var rPr = pPr.ParagraphMarkRunProperties;
            if (rPr != null)
                format.DfpParaMarkRprJson = SerializeElementToJson(rPr);
            
            // Paragraph Properties Change (revision tracking)
            var pPrChange = pPr.ParagraphPropertiesChange;
            if (pPrChange != null)
                format.DfpPprChangeJson = SerializeElementToJson(pPrChange);
        }

        ApplyFallbackDefaults(format);
        
        return format;
    }
    
    /// <summary>
    /// Map EffectiveParagraphProperties to DokumenFormatParagraf model
    /// </summary>
    private void MapEffectivePropertiesToFormat(DokumenFormatParagraf format, EffectiveParagraphProperties effective)
    {
        // Alignment
        format.DfpJc = effective.Justification;
        format.DfpTextAlignment = effective.TextAlignment;
        
        // Indentation (RESOLVED from docDefaults → style → numbering → direct)
        format.DfpIndLeftTwips = effective.IndentLeft.HasValue ? (uint?)effective.IndentLeft.Value : null;
        format.DfpIndRightTwips = effective.IndentRight.HasValue ? (uint?)effective.IndentRight.Value : null;
        format.DfpIndFirstLineTwips = effective.IndentFirstLine.HasValue ? (uint?)effective.IndentFirstLine.Value : null;
        format.DfpIndHangingTwips = effective.IndentHanging.HasValue ? (uint?)effective.IndentHanging.Value : null;
        format.DfpIndStartTwips = effective.IndentStart.HasValue ? (uint?)effective.IndentStart.Value : null;
        format.DfpIndEndTwips = effective.IndentEnd.HasValue ? (uint?)effective.IndentEnd.Value : null;
        format.DfpIndLeftChars = effective.IndentLeftChars.HasValue ? (uint?)effective.IndentLeftChars.Value : null;
        format.DfpIndRightChars = effective.IndentRightChars.HasValue ? (uint?)effective.IndentRightChars.Value : null;
        
        // Spacing (RESOLVED)
        format.DfpSpacingBeforeTwips = effective.SpaceBefore.HasValue ? (uint?)effective.SpaceBefore.Value : null;
        format.DfpSpacingAfterTwips = effective.SpaceAfter.HasValue ? (uint?)effective.SpaceAfter.Value : null;
        format.DfpSpacingLineTwips = effective.LineSpacing.HasValue ? (uint?)effective.LineSpacing.Value : null;
        format.DfpSpacingLineRule = effective.LineRule;
        format.DfpSpacingBeforeAutospacing = effective.SpaceBeforeAuto;
        format.DfpSpacingAfterAutospacing = effective.SpaceAfterAuto;
        format.DfpSpacingBeforeLines = effective.SpaceBeforeLines.HasValue ? (uint?)effective.SpaceBeforeLines.Value : null;
        format.DfpSpacingAfterLines = effective.SpaceAfterLines.HasValue ? (uint?)effective.SpaceAfterLines.Value : null;
        
        // Pagination/Layout (RESOLVED)
        format.DfpKeepNext = effective.KeepNext;
        format.DfpKeepLines = effective.KeepLines;
        format.DfpPageBreakBefore = effective.PageBreakBefore;
        format.DfpWidowControl = effective.WidowControl;
        format.DfpSuppressLineNumbers = effective.SuppressLineNumbers;
        format.DfpSuppressAutoHyphens = effective.SuppressAutoHyphens;
        
        // Layout Toggles (RESOLVED)
        format.DfpSnapToGrid = effective.SnapToGrid;
        format.DfpAdjustRightInd = effective.AdjustRightIndent;
        format.DfpMirrorIndents = effective.MirrorIndents;
        format.DfpSuppressOverlap = effective.SuppressOverlap;
        format.DfpContextualSpacing = effective.ContextualSpacing;
        format.DfpWordWrap = effective.WordWrap;
        
        // Outline level
        format.DfpOutlineLvl = effective.OutlineLevel.HasValue ? (byte?)effective.OutlineLevel.Value : null;
    }
    
    /// <summary>
    /// Extract only direct properties (fallback when no StyleResolver)
    /// </summary>
    private void ExtractDirectPropertiesOnly(DokumenFormatParagraf format, ParagraphProperties? pPr)
    {
        if (pPr == null) return;
        
        // Pagination / Layout toggles
        format.DfpKeepNext = IsToggleOn(pPr.KeepNext);
        format.DfpKeepLines = IsToggleOn(pPr.KeepLines);
        format.DfpPageBreakBefore = IsToggleOn(pPr.PageBreakBefore);
        format.DfpWidowControl = !IsToggleOff(pPr.WidowControl);
        format.DfpSuppressLineNumbers = IsToggleOn(pPr.SuppressLineNumbers);
        format.DfpSuppressAutoHyphens = IsToggleOn(pPr.SuppressAutoHyphens);
        format.DfpSnapToGrid = IsToggleOn(pPr.SnapToGrid);
        format.DfpAdjustRightInd = IsToggleOn(pPr.AdjustRightIndent);
        format.DfpMirrorIndents = IsToggleOn(pPr.MirrorIndents);
        format.DfpSuppressOverlap = IsToggleOn(pPr.SuppressOverlap);
        format.DfpContextualSpacing = IsToggleOn(pPr.ContextualSpacing);
        format.DfpWordWrap = IsToggleOn(pPr.WordWrap);
        
        // Spacing
        var spacing = pPr.SpacingBetweenLines;
        if (spacing != null)
        {
            if (spacing.Before?.HasValue == true)
                format.DfpSpacingBeforeTwips = ParseUint(spacing.Before.Value?.ToString() ?? "");
            if (spacing.After?.HasValue == true)
                format.DfpSpacingAfterTwips = ParseUint(spacing.After.Value?.ToString() ?? "");
            if (spacing.Line?.HasValue == true)
                format.DfpSpacingLineTwips = ParseUint(spacing.Line.Value?.ToString() ?? "");
            if (spacing.LineRule?.HasValue == true)
                format.DfpSpacingLineRule = ConvertLineRuleValue(spacing.LineRule.Value);
            format.DfpSpacingAfterAutospacing = IsOnOffValueOn(spacing.AfterAutoSpacing);
            format.DfpSpacingBeforeAutospacing = IsOnOffValueOn(spacing.BeforeAutoSpacing);
            if (spacing.BeforeLines?.HasValue == true)
                format.DfpSpacingBeforeLines = (uint)spacing.BeforeLines.Value;
            if (spacing.AfterLines?.HasValue == true)
                format.DfpSpacingAfterLines = (uint)spacing.AfterLines.Value;
        }
        
        // Indentation
        var ind = pPr.Indentation;
        if (ind != null)
        {
            if (ind.Left?.HasValue == true)
                format.DfpIndLeftTwips = ParseUint(ind.Left.Value?.ToString() ?? "");
            if (ind.Right?.HasValue == true)
                format.DfpIndRightTwips = ParseUint(ind.Right.Value?.ToString() ?? "");
            if (ind.FirstLine?.HasValue == true)
                format.DfpIndFirstLineTwips = ParseUint(ind.FirstLine.Value?.ToString() ?? "");
            if (ind.Hanging?.HasValue == true)
                format.DfpIndHangingTwips = ParseUint(ind.Hanging.Value?.ToString() ?? "");
            if (ind.Start?.HasValue == true)
                format.DfpIndStartTwips = ParseUint(ind.Start.Value?.ToString() ?? "");
            if (ind.End?.HasValue == true)
                format.DfpIndEndTwips = ParseUint(ind.End.Value?.ToString() ?? "");
            if (ind.LeftChars?.HasValue == true)
                format.DfpIndLeftChars = (uint)ind.LeftChars.Value;
            if (ind.RightChars?.HasValue == true)
                format.DfpIndRightChars = (uint)ind.RightChars.Value;
        }
        
        // Alignment
        if (pPr.Justification?.Val?.HasValue == true)
            format.DfpJc = ConvertJustification(pPr.Justification.Val.Value);
        if (pPr.TextAlignment?.Val?.HasValue == true)
            format.DfpTextAlignment = ConvertTextAlignment(pPr.TextAlignment.Val.Value);
        
        // Outline level
        if (pPr.OutlineLevel?.Val?.HasValue == true)
            format.DfpOutlineLvl = (byte)pPr.OutlineLevel.Val.Value;
    }
    
    private static uint? ParseUint(string value)
    {
        if (int.TryParse(value, out int result))
            return result >= 0 ? (uint)result : null;
        return null;
    }
    
    private static bool IsToggleOn(OnOffType? toggle)
    {
        if (toggle == null) return false;
        if (toggle.Val == null) return true;
        return toggle.Val.Value;
    }
    
    private static bool IsToggleOff(OnOffType? toggle)
    {
        if (toggle == null) return false;
        if (toggle.Val == null) return false;
        return !toggle.Val.Value;
    }
    
    private static bool IsOnOffValueOn(OnOffValue? value)
    {
        if (value == null) return false;
        if (!value.HasValue) return true;
        return value.Value;
    }
    
    private static string SerializeElementToJson(OpenXmlElement element)
    {
        var obj = new JObject();
        foreach (var attr in element.GetAttributes())
            obj[attr.LocalName] = attr.Value;
        foreach (var child in element.ChildElements)
        {
            var childObj = new JObject();
            foreach (var attr in child.GetAttributes())
                childObj[attr.LocalName] = attr.Value;
            if (childObj.Count > 0 || child.HasChildren)
            {
                if (child.HasChildren && child.ChildElements.Count > 0)
                    childObj["_children"] = SerializeChildrenToJson(child);
                obj[child.LocalName] = childObj;
            }
        }
        return obj.ToString(Formatting.None);
    }
    
    private static JArray SerializeChildrenToJson(OpenXmlElement parent)
    {
        var arr = new JArray();
        foreach (var child in parent.ChildElements)
        {
            var obj = new JObject { ["_type"] = child.LocalName };
            foreach (var attr in child.GetAttributes())
                obj[attr.LocalName] = attr.Value;
            arr.Add(obj);
        }
        return arr;
    }
    
    private static string SerializeShadingToJson(Shading shd)
    {
        var obj = new JObject();
        if (shd.Val?.HasValue == true) obj["val"] = shd.Val.Value.ToString();
        if (shd.Color?.HasValue == true) obj["color"] = shd.Color.Value;
        if (shd.Fill?.HasValue == true) obj["fill"] = shd.Fill.Value;
        if (shd.ThemeColor?.HasValue == true) obj["themeColor"] = shd.ThemeColor.Value.ToString();
        if (shd.ThemeFill?.HasValue == true) obj["themeFill"] = shd.ThemeFill.Value.ToString();
        return obj.ToString(Formatting.None);
    }
    
    private static string SerializeTabsToJson(Tabs tabs)
    {
        var arr = new JArray();
        foreach (var tab in tabs.Elements<TabStop>())
        {
            var obj = new JObject();
            if (tab.Val?.HasValue == true) obj["val"] = tab.Val.Value.ToString();
            if (tab.Position?.HasValue == true) obj["pos"] = tab.Position.Value;
            if (tab.Leader?.HasValue == true) obj["leader"] = tab.Leader.Value.ToString();
            arr.Add(obj);
        }
        return arr.ToString(Formatting.None);
    }
    
    /// <summary>
    /// Convert OpenXML LineSpacingRuleValues to camelCase string for database enum.
    /// DB expects: 'auto', 'atLeast', 'exact'
    /// </summary>
    private static string ConvertLineRuleValue(LineSpacingRuleValues value)
    {
        if (value == LineSpacingRuleValues.Auto) return "auto";
        if (value == LineSpacingRuleValues.AtLeast) return "atLeast";
        if (value == LineSpacingRuleValues.Exact) return "exact";
        return value.ToString();
    }

    private static void ApplyFallbackDefaults(DokumenFormatParagraf format)
    {
        // WordprocessingML defaults when explicit values are absent (ISO/IEC 29500).
        if (string.IsNullOrWhiteSpace(format.DfpJc))
            format.DfpJc = "left";
        if (string.IsNullOrWhiteSpace(format.DfpTextAlignment))
            format.DfpTextAlignment = "auto";

        if (!format.DfpSpacingBeforeTwips.HasValue && !format.DfpSpacingBeforeAutospacing)
            format.DfpSpacingBeforeTwips = 0;
        if (!format.DfpSpacingAfterTwips.HasValue && !format.DfpSpacingAfterAutospacing)
            format.DfpSpacingAfterTwips = 0;
        if (!format.DfpSpacingBeforeLines.HasValue)
            format.DfpSpacingBeforeLines = 0;
        if (!format.DfpSpacingAfterLines.HasValue)
            format.DfpSpacingAfterLines = 0;

        if (string.IsNullOrWhiteSpace(format.DfpSpacingLineRule))
            format.DfpSpacingLineRule = "auto";
        if (!format.DfpSpacingLineTwips.HasValue &&
            string.Equals(format.DfpSpacingLineRule, "auto", StringComparison.OrdinalIgnoreCase))
            format.DfpSpacingLineTwips = DefaultLineSpacingTwips;

        if (!format.DfpIndLeftTwips.HasValue)
            format.DfpIndLeftTwips = 0;
        if (!format.DfpIndRightTwips.HasValue)
            format.DfpIndRightTwips = 0;
        if (!format.DfpIndFirstLineTwips.HasValue)
            format.DfpIndFirstLineTwips = 0;
        if (!format.DfpIndHangingTwips.HasValue)
            format.DfpIndHangingTwips = 0;
        if (!format.DfpIndStartTwips.HasValue)
            format.DfpIndStartTwips = 0;
        if (!format.DfpIndEndTwips.HasValue)
            format.DfpIndEndTwips = 0;
        if (!format.DfpIndLeftChars.HasValue)
            format.DfpIndLeftChars = 0;
        if (!format.DfpIndRightChars.HasValue)
            format.DfpIndRightChars = 0;
    }

    /// <summary>
    /// Convert OpenXML JustificationValues to database enum.
    /// DB expects: 'left', 'right', 'center', 'both', 'distribute'
    /// </summary>
    private static string ConvertJustification(JustificationValues value)
    {
        // Safe mapping to allowed DB enum values
        if (value == JustificationValues.Left) return "left";
        if (value == JustificationValues.Start) return "start";
        if (value == JustificationValues.Right) return "right";
        if (value == JustificationValues.End) return "end";
        if (value == JustificationValues.Center) return "center";
        if (value == JustificationValues.Both) return "both";
        if (value == JustificationValues.Distribute) return "distribute";
        
        // Default fallback for unsupported values
        return "left";
    }

    /// <summary>
    /// Convert OpenXML VerticalTextAlignmentValues to database enum.
    /// DB expects: 'auto', 'baseline', 'top', 'center', 'bottom'
    /// </summary>
    private static string ConvertTextAlignment(VerticalTextAlignmentValues value)
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

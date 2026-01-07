using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts drawing formatting properties from OpenXML w:drawing into DokumenFormatDrawing.
/// </summary>
public class DrawingFormatExtractor
{
    /// <summary>
    /// Extract drawing properties from a Drawing element.
    /// </summary>
    public DokumenFormatDrawing ExtractFormat(Drawing drawing)
    {
        var format = new DokumenFormatDrawing();
        
        // Store raw XML for debugging
        format.DfdrRawDrawingXml = drawing.OuterXml;
        
        // Check if inline or anchor
        var inline = drawing.Inline;
        var anchor = drawing.Anchor;
        
        if (inline != null)
        {
            format.DfdrIsInline = true;
            ExtractInlineProperties(inline, format);
        }
        else if (anchor != null)
        {
            format.DfdrIsInline = false;
            ExtractAnchorProperties(anchor, format);
        }
        
        return format;
    }
    
    private void ExtractInlineProperties(Inline inline, DokumenFormatDrawing format)
    {
        // Extent (size in EMUs)
        var extent = inline.Extent;
        if (extent != null)
        {
            format.DfdrCxEmu = (ulong?)extent.Cx?.Value;
            format.DfdrCyEmu = (ulong?)extent.Cy?.Value;
        }
        
        // Graphic type and relationship ID
        ExtractGraphicInfo(inline.Graphic, format);
    }
    
    private void ExtractAnchorProperties(Anchor anchor, DokumenFormatDrawing format)
    {
        // Extent (size in EMUs)
        var extent = anchor.Extent;
        if (extent != null)
        {
            format.DfdrCxEmu = (ulong?)extent.Cx?.Value;
            format.DfdrCyEmu = (ulong?)extent.Cy?.Value;
        }
        
        // Anchor positioning as JSON
        var anchorJson = new JObject();
        
        // Position
        var posH = anchor.HorizontalPosition;
        var posV = anchor.VerticalPosition;
        if (posH != null)
        {
            anchorJson["horizontalPosition"] = new JObject
            {
                ["relativeFrom"] = posH.RelativeFrom?.ToString(),
                ["posOffset"] = posH.PositionOffset?.Text
            };
        }
        if (posV != null)
        {
            anchorJson["verticalPosition"] = new JObject
            {
                ["relativeFrom"] = posV.RelativeFrom?.ToString(),
                ["posOffset"] = posV.PositionOffset?.Text
            };
        }
        
        // Simple positioning
        var simplePos = anchor.SimplePosition;
        if (simplePos != null)
        {
            anchorJson["simplePos"] = new JObject
            {
                ["x"] = simplePos.X?.Value,
                ["y"] = simplePos.Y?.Value
            };
        }
        
        // Distance from text
        anchorJson["distT"] = anchor.DistanceFromTop?.Value;
        anchorJson["distB"] = anchor.DistanceFromBottom?.Value;
        anchorJson["distL"] = anchor.DistanceFromLeft?.Value;
        anchorJson["distR"] = anchor.DistanceFromRight?.Value;
        
        // Behavior flags
        anchorJson["behindDoc"] = anchor.BehindDoc?.Value;
        anchorJson["locked"] = anchor.Locked?.Value;
        anchorJson["layoutInCell"] = anchor.LayoutInCell?.Value;
        anchorJson["allowOverlap"] = anchor.AllowOverlap?.Value;
        
        format.DfdrAnchorJson = anchorJson.ToString(Formatting.None);
        
        // Text wrapping
        ExtractWrapInfo(anchor, format);
        
        // Graphic type and relationship ID - use Descendants to find Graphic
        var graphic = anchor.Descendants<DocumentFormat.OpenXml.Drawing.Graphic>().FirstOrDefault();
        ExtractGraphicInfo(graphic, format);
    }
    
    private void ExtractWrapInfo(Anchor anchor, DokumenFormatDrawing format)
    {
        var wrapJson = new JObject();
        var xml = anchor.OuterXml;
        
        // Detect wrap type from XML content
        if (xml.Contains("<wp:wrapNone") || xml.Contains("wrapNone"))
            wrapJson["type"] = "none";
        else if (xml.Contains("<wp:wrapSquare") || xml.Contains("wrapSquare"))
            wrapJson["type"] = "square";
        else if (xml.Contains("<wp:wrapTight") || xml.Contains("wrapTight"))
            wrapJson["type"] = "tight";
        else if (xml.Contains("<wp:wrapThrough") || xml.Contains("wrapThrough"))
            wrapJson["type"] = "through";
        else if (xml.Contains("<wp:wrapTopAndBottom") || xml.Contains("wrapTopAndBottom"))
            wrapJson["type"] = "topAndBottom";
        
        if (wrapJson.HasValues)
            format.DfdrWrapJson = wrapJson.ToString(Formatting.None);
    }
    
    private void ExtractGraphicInfo(DocumentFormat.OpenXml.Drawing.Graphic? graphic, DokumenFormatDrawing format)
    {
        if (graphic?.GraphicData == null)
        {
            format.DfdrGraphicType = "unknown";
            return;
        }
        
        var graphicData = graphic.GraphicData;
        var uri = graphicData.Uri?.Value ?? "";
        
        // Determine graphic type based on URI
        if (uri.Contains("picture"))
        {
            format.DfdrGraphicType = "picture";
            
            // Get relationship ID from picture
            var blip = graphicData.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value != null)
                format.DfdrRelId = blip.Embed.Value;
        }
        else if (uri.Contains("chart"))
            format.DfdrGraphicType = "chart";
        else if (uri.Contains("diagram") || uri.Contains("smartArt"))
            format.DfdrGraphicType = "smartart";
        else if (uri.Contains("ole"))
            format.DfdrGraphicType = "ole";
        else if (uri.Contains("wordprocessingShape") || uri.Contains("wsp"))
        {
            // Check if it's a textbox or regular shape
            if (graphicData.OuterXml.Contains("txbx") || graphicData.OuterXml.Contains("<wsp:txbx"))
                format.DfdrGraphicType = "textbox";
            else
                format.DfdrGraphicType = "shape";
            
            // Extract preset shape type from a:prstGeom
            ExtractPresetShape(graphicData, format);
        }
        else if (uri.Contains("textbox") || graphicData.OuterXml.Contains("txbx"))
            format.DfdrGraphicType = "textbox";
        else
            format.DfdrGraphicType = "unknown";
    }
    
    /// <summary>
    /// Extract preset shape geometry type from a:prstGeom element
    /// </summary>
    private void ExtractPresetShape(DocumentFormat.OpenXml.Drawing.GraphicData graphicData, DokumenFormatDrawing format)
    {
        // Look for preset geometry in the graphic data
        // Pattern: <a:prstGeom prst="rect">
        var xml = graphicData.OuterXml;
        
        // Find a:prstGeom prst="..."
        var prstGeomMatch = System.Text.RegularExpressions.Regex.Match(
            xml, 
            @"<a:prstGeom[^>]+prst=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (prstGeomMatch.Success)
        {
            format.DfdrPresetShape = prstGeomMatch.Groups[1].Value;
            return;
        }
        
        // Also try without namespace prefix
        var prstGeomMatch2 = System.Text.RegularExpressions.Regex.Match(
            xml, 
            @"prst=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (prstGeomMatch2.Success)
        {
            format.DfdrPresetShape = prstGeomMatch2.Groups[1].Value;
        }
    }
    
    /// <summary>
    /// Extract properties from VML Picture element (legacy format w:pict)
    /// </summary>
    public DokumenFormatDrawing ExtractVmlFormat(Picture picture)
    {
        var format = new DokumenFormatDrawing();
        
        // Store raw XML for debugging
        format.DfdrRawDrawingXml = picture.OuterXml;
        
        // VML pictures are inline by default
        format.DfdrIsInline = true;
        format.DfdrGraphicType = "picture";
        
        // Try to extract shape type from VML
        var xml = picture.OuterXml;
        
        // Extract relationship ID from v:imagedata r:id
        var ridMatch = System.Text.RegularExpressions.Regex.Match(
            xml,
            @"r:id=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (ridMatch.Success)
            format.DfdrRelId = ridMatch.Groups[1].Value;
        
        // Try to extract size from style attribute (width:XXpt;height:YYpt)
        var styleMatch = System.Text.RegularExpressions.Regex.Match(
            xml,
            @"style=""[^""]*width:([0-9.]+)(pt|in|cm|px)[^""]*height:([0-9.]+)(pt|in|cm|px)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (styleMatch.Success)
        {
            // Convert to EMUs (1 point = 12700 EMUs)
            if (double.TryParse(styleMatch.Groups[1].Value, out double width))
            {
                format.DfdrCxEmu = (ulong)(width * 12700);
            }
            if (double.TryParse(styleMatch.Groups[3].Value, out double height))
            {
                format.DfdrCyEmu = (ulong)(height * 12700);
            }
        }
        
        // Check for shape type
        var typeMatch = System.Text.RegularExpressions.Regex.Match(
            xml,
            @"type=""#([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (typeMatch.Success)
            format.DfdrPresetShape = typeMatch.Groups[1].Value;
        
        return format;
    }
}

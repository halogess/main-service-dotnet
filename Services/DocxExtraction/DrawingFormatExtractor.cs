using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml.Vml;
using DocumentFormat.OpenXml.Wordprocessing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts drawing formatting properties from OpenXML w:drawing into DokumenFormatDrawing.
/// </summary>
public class DrawingFormatExtractor
{
    private const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string DiagramUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private const string OleUri = "http://schemas.openxmlformats.org/drawingml/2006/ole";
    private const string WpsUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string WpgUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
    private const string WpcUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas";

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

        // Inline layout distances and effect extent (per wp:inline)
        ExtractInlineLayout(inline, format);
        
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
            var posHJson = new JObject
            {
                ["relativeFrom"] = posH.RelativeFrom?.Value.ToString()
            };
            var posOffset = posH.PositionOffset?.Text;
            if (!string.IsNullOrWhiteSpace(posOffset))
                posHJson["posOffset"] = posOffset;
            var align = posH.HorizontalAlignment?.Text;
            if (!string.IsNullOrWhiteSpace(align))
                posHJson["align"] = align;
            var pctOffset = ReadPercentOffset(posH, "pctPosHOffset");
            if (!string.IsNullOrWhiteSpace(pctOffset))
                posHJson["pctOffset"] = pctOffset;
            anchorJson["horizontalPosition"] = posHJson;
        }
        if (posV != null)
        {
            var posVJson = new JObject
            {
                ["relativeFrom"] = posV.RelativeFrom?.Value.ToString()
            };
            var posOffset = posV.PositionOffset?.Text;
            if (!string.IsNullOrWhiteSpace(posOffset))
                posVJson["posOffset"] = posOffset;
            var align = posV.VerticalAlignment?.Text;
            if (!string.IsNullOrWhiteSpace(align))
                posVJson["align"] = align;
            var pctOffset = ReadPercentOffset(posV, "pctPosVOffset");
            if (!string.IsNullOrWhiteSpace(pctOffset))
                posVJson["pctOffset"] = pctOffset;
            anchorJson["verticalPosition"] = posVJson;
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
        anchorJson["distT"] = anchor.DistanceFromTop?.Value ?? 0U;
        anchorJson["distB"] = anchor.DistanceFromBottom?.Value ?? 0U;
        anchorJson["distL"] = anchor.DistanceFromLeft?.Value ?? 0U;
        anchorJson["distR"] = anchor.DistanceFromRight?.Value ?? 0U;

        // Effect extent (defaults to 0 when omitted)
        var effectExtentJson = BuildEffectExtentJson(anchor.EffectExtent, includeDefault: true);
        if (effectExtentJson != null)
            anchorJson["effectExtent"] = effectExtentJson;
        
        // Behavior flags
        anchorJson["simplePosFlag"] = anchor.SimplePos?.Value ?? false;
        anchorJson["relativeHeight"] = anchor.RelativeHeight?.Value ?? 0U;
        anchorJson["behindDoc"] = anchor.BehindDoc?.Value ?? false;
        anchorJson["locked"] = anchor.Locked?.Value ?? false;
        anchorJson["layoutInCell"] = anchor.LayoutInCell?.Value ?? true;
        anchorJson["allowOverlap"] = anchor.AllowOverlap?.Value ?? true;
        anchorJson["hidden"] = anchor.Hidden?.Value ?? false;
        
        format.DfdrAnchorJson = anchorJson.ToString(Formatting.None);
        
        // Text wrapping
        ExtractWrapInfo(anchor, format);
        
        // Graphic type and relationship ID
        var graphic = anchor.GetFirstChild<DocumentFormat.OpenXml.Drawing.Graphic>();
        ExtractGraphicInfo(graphic, format);
    }
    
    private void ExtractWrapInfo(Anchor anchor, DokumenFormatDrawing format)
    {
        var wrapJson = new JObject();
        
        if (anchor.GetFirstChild<WrapNone>() != null)
        {
            wrapJson["type"] = "none";
        }
        else if (anchor.GetFirstChild<WrapSquare>() is WrapSquare wrapSquare)
        {
            wrapJson["type"] = "square";
            AddWrapText(wrapJson, wrapSquare.WrapText, WrapTextValues.BothSides);
            AddWrapDistance(wrapJson, "distT", wrapSquare.DistanceFromTop);
            AddWrapDistance(wrapJson, "distB", wrapSquare.DistanceFromBottom);
            AddWrapDistance(wrapJson, "distL", wrapSquare.DistanceFromLeft);
            AddWrapDistance(wrapJson, "distR", wrapSquare.DistanceFromRight);
            var squareEffect = BuildEffectExtentJson(wrapSquare.EffectExtent, includeDefault: true);
            if (squareEffect != null)
                wrapJson["effectExtent"] = squareEffect;
        }
        else if (anchor.GetFirstChild<WrapTight>() is WrapTight wrapTight)
        {
            wrapJson["type"] = "tight";
            AddWrapText(wrapJson, wrapTight.WrapText, WrapTextValues.BothSides);
            AddWrapDistance(wrapJson, "distL", wrapTight.DistanceFromLeft);
            AddWrapDistance(wrapJson, "distR", wrapTight.DistanceFromRight);
            AddWrapPolygon(wrapJson, wrapTight.WrapPolygon);
        }
        else if (anchor.GetFirstChild<WrapThrough>() is WrapThrough wrapThrough)
        {
            wrapJson["type"] = "through";
            AddWrapText(wrapJson, wrapThrough.WrapText, WrapTextValues.BothSides);
            AddWrapDistance(wrapJson, "distL", wrapThrough.DistanceFromLeft);
            AddWrapDistance(wrapJson, "distR", wrapThrough.DistanceFromRight);
            AddWrapPolygon(wrapJson, wrapThrough.WrapPolygon);
        }
        else if (anchor.GetFirstChild<WrapTopBottom>() is WrapTopBottom wrapTopBottom)
        {
            wrapJson["type"] = "topAndBottom";
            AddWrapDistance(wrapJson, "distT", wrapTopBottom.DistanceFromTop);
            AddWrapDistance(wrapJson, "distB", wrapTopBottom.DistanceFromBottom);
        }
        
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
        var uri = graphicData.Uri?.Value;
        
        // Determine graphic type based on URI (per DrawingML spec)
        if (string.Equals(uri, PictureUri, StringComparison.OrdinalIgnoreCase))
        {
            format.DfdrGraphicType = "picture";
            
            // Get relationship ID from picture
            var blip = graphicData.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            format.DfdrRelId = blip?.Embed?.Value ?? blip?.Link?.Value;
        }
        else if (string.Equals(uri, ChartUri, StringComparison.OrdinalIgnoreCase))
            format.DfdrGraphicType = "chart";
        else if (string.Equals(uri, DiagramUri, StringComparison.OrdinalIgnoreCase))
            format.DfdrGraphicType = "smartart";
        else if (string.Equals(uri, OleUri, StringComparison.OrdinalIgnoreCase))
            format.DfdrGraphicType = "ole";
        else if (IsWordprocessingShapeUri(uri))
        {
            // Check if it's a textbox or regular shape
            format.DfdrGraphicType = graphicData.Descendants<TextBoxContent>().Any() ? "textbox" : "shape";
            
            // Extract preset shape type from a:prstGeom
            ExtractPresetShape(graphicData, format);
        }
        else if (graphicData.Descendants<TextBoxContent>().Any())
            format.DfdrGraphicType = "textbox";
        else
            format.DfdrGraphicType = "unknown";
    }
    
    /// <summary>
    /// Extract preset shape geometry type from a:prstGeom element
    /// </summary>
    private void ExtractPresetShape(DocumentFormat.OpenXml.Drawing.GraphicData graphicData, DokumenFormatDrawing format)
    {
        var presetGeometry = graphicData.Descendants<DocumentFormat.OpenXml.Drawing.PresetGeometry>().FirstOrDefault();
        var preset = presetGeometry?.Preset?.Value;
        if (preset != null)
            format.DfdrPresetShape = preset.ToString();
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
        
        var imageData = picture.Descendants<ImageData>().FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(imageData?.RelationshipId?.Value))
        {
            format.DfdrGraphicType = "picture";
            format.DfdrRelId = imageData.RelationshipId!.Value;
        }
        else
        {
            format.DfdrGraphicType = "shape";
        }
        
        var shape = picture.Descendants<Shape>().FirstOrDefault();
        if (shape != null)
        {
            if (!string.IsNullOrWhiteSpace(shape.Type?.Value))
                format.DfdrPresetShape = shape.Type!.Value.TrimStart('#');
            
            var style = shape.Style?.Value;
            if (!string.IsNullOrWhiteSpace(style))
            {
                var (cxEmu, cyEmu) = ParseVmlStyleSize(style);
                if (cxEmu.HasValue)
                    format.DfdrCxEmu = cxEmu.Value;
                if (cyEmu.HasValue)
                    format.DfdrCyEmu = cyEmu.Value;
            }
        }
        
        return format;
    }

    private static string? ReadPercentOffset(OpenXmlElement container, string localName)
    {
        var element = container.ChildElements.FirstOrDefault(e => e.LocalName == localName);
        var text = element?.InnerText;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void AddWrapText(JObject wrapJson, EnumValue<WrapTextValues>? wrapText, WrapTextValues? defaultValue = null)
    {
        if (wrapText?.Value != null)
            wrapJson["wrapText"] = wrapText.Value.ToString();
        else if (defaultValue.HasValue)
            wrapJson["wrapText"] = defaultValue.Value.ToString();
    }

    private static void AddWrapDistance(JObject wrapJson, string name, UInt32Value? value)
    {
        wrapJson[name] = value?.Value ?? 0U;
    }

    private static void AddWrapPolygon(JObject wrapJson, WrapPolygon? wrapPolygon)
    {
        if (wrapPolygon == null)
            return;

        var polygonJson = new JObject
        {
            ["edited"] = wrapPolygon.Edited?.Value ?? false
        };

        var start = wrapPolygon.StartPoint;
        if (start != null)
        {
            polygonJson["start"] = new JObject
            {
                ["x"] = start.X?.Value ?? 0L,
                ["y"] = start.Y?.Value ?? 0L
            };
        }

        var lines = new JArray();
        foreach (var line in wrapPolygon.Elements<LineTo>())
        {
            lines.Add(new JObject
            {
                ["x"] = line.X?.Value ?? 0L,
                ["y"] = line.Y?.Value ?? 0L
            });
        }

        if (lines.Count > 0)
            polygonJson["lines"] = lines;

        wrapJson["polygon"] = polygonJson;
    }

    private static JObject? BuildEffectExtentJson(EffectExtent? effectExtent, bool includeDefault)
    {
        if (effectExtent == null)
        {
            if (!includeDefault)
                return null;

            return new JObject
            {
                ["l"] = 0L,
                ["t"] = 0L,
                ["r"] = 0L,
                ["b"] = 0L
            };
        }

        return new JObject
        {
            ["l"] = effectExtent.LeftEdge?.Value ?? 0L,
            ["t"] = effectExtent.TopEdge?.Value ?? 0L,
            ["r"] = effectExtent.RightEdge?.Value ?? 0L,
            ["b"] = effectExtent.BottomEdge?.Value ?? 0L
        };
    }

    private static void ExtractInlineLayout(Inline inline, DokumenFormatDrawing format)
    {
        var wrapJson = new JObject
        {
            ["type"] = "inline",
            ["distT"] = inline.DistanceFromTop?.Value ?? 0U,
            ["distB"] = inline.DistanceFromBottom?.Value ?? 0U,
            ["distL"] = inline.DistanceFromLeft?.Value ?? 0U,
            ["distR"] = inline.DistanceFromRight?.Value ?? 0U
        };

        var effectExtentJson = BuildEffectExtentJson(inline.EffectExtent, includeDefault: true);
        if (effectExtentJson != null)
            wrapJson["effectExtent"] = effectExtentJson;

        format.DfdrWrapJson = wrapJson.ToString(Formatting.None);
    }

    private static bool IsWordprocessingShapeUri(string? uri)
    {
        return string.Equals(uri, WpsUri, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri, WpgUri, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri, WpcUri, StringComparison.OrdinalIgnoreCase);
    }

    private static (ulong? cxEmu, ulong? cyEmu) ParseVmlStyleSize(string style)
    {
        var width = ParseVmlStyleLength(style, "width");
        var height = ParseVmlStyleLength(style, "height");
        return (width.HasValue ? (ulong)width.Value : null, height.HasValue ? (ulong)height.Value : null);
    }

    private static double? ParseVmlStyleLength(string style, string name)
    {
        var match = Regex.Match(
            style,
            $@"(?:^|;)\s*{Regex.Escape(name)}\s*:\s*([0-9.]+)\s*(pt|in|cm|mm|px)\s*(?:;|$)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        var unit = match.Groups[2].Value.ToLowerInvariant();
        return unit switch
        {
            "pt" => value * 12700d,
            "in" => value * 914400d,
            "cm" => value * 360000d,
            "mm" => value * 36000d,
            "px" => value * 9525d,
            _ => null
        };
    }
}

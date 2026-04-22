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
        }
    }
    
    private void ExtractAnchorProperties(Anchor anchor, DokumenFormatDrawing format)
    {
        // Extent (size in EMUs)
        var extent = anchor.Extent;
        if (extent != null)
        {
            format.DfdrCxEmu = (ulong?)extent.Cx?.Value;
        }
    }
    
    /// <summary>
    /// Extract properties from VML Picture element (legacy format w:pict)
    /// </summary>
    public DokumenFormatDrawing ExtractVmlFormat(Picture picture)
    {
        var format = new DokumenFormatDrawing();

        // VML pictures are inline by default
        format.DfdrIsInline = true;

        var shape = picture.Descendants<Shape>().FirstOrDefault();
        if (shape != null)
        {
            var style = shape.Style?.Value;
            if (!string.IsNullOrWhiteSpace(style))
            {
                var (cxEmu, cyEmu) = ParseVmlStyleSize(style);
                if (cxEmu.HasValue)
                    format.DfdrCxEmu = cxEmu.Value;
            }
        }
        
        return format;
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

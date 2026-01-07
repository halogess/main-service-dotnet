using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles extraction of drawing content (images, shapes, textboxes, charts)
/// </summary>
public class DrawingExtractor
{
    private readonly ILogger _logger;

    public DrawingExtractor(ILogger logger)
    {
        _logger = logger;
    }

    public JObject? ExtractDrawingContent(
        Drawing drawing,
        Func<OpenXmlElement, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, JArray> extractTextBoxAsItems,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var (shapeId, shapeName) = GetShapeIdentity(drawing);
        
        if (shapeName?.StartsWith("Group ") == true)
            return null;
        
        int sortYPosition = 0;
        
        var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
        if (anchor != null)
        {
            var positionV = anchor.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.VerticalPosition>();
            if (positionV != null)
            {
                var posOffset = positionV.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.PositionOffset>();
                if (posOffset != null && int.TryParse(posOffset.Text, out int yPos))
                    sortYPosition = yPos;
            }
        }

        var blips = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>()
            .Where(b => b.Embed?.Value != null)
            .Select(b => b.Embed!.Value)
            .Distinct()
            .ToList();
        
        var chartRefs = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Charts.ChartReference>()
            .Where(c => c.Id?.Value != null)
            .Select(c => c.Id!.Value)
            .Distinct()
            .ToList();

        if (!chartRefs.Any())
        {
            chartRefs = drawing.Descendants()
                .Where(e => e.LocalName == "chart" && e.NamespaceUri == "http://schemas.openxmlformats.org/drawingml/2006/chart")
                .Select(e => e.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships").Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList()!;
        }
        
        var txbxContents = drawing.Descendants<TextBoxContent>().ToList();
        
        var txbxContent = drawing.Descendants<TextBoxContent>().FirstOrDefault();
        if (txbxContent != null)
        {
            var content = extractTextBoxAsItems(txbxContent, numberingPart, numberingCounters);
            return new JObject { 
                ["type"] = "shape", 
                ["content"] = content, 
                ["_sortY"] = sortYPosition
            };
        }

        if (blips.Count == 1 && !txbxContents.Any())
            return new JObject { ["type"] = "image", ["rId"] = blips[0], ["_sortY"] = sortYPosition };
        
        if (blips.Count > 1 && !txbxContents.Any())
        {
            var images = new JArray();
            foreach (var rId in blips)
                images.Add(new JObject { ["type"] = "image", ["rId"] = rId });
            return new JObject { ["type"] = "composite", ["content"] = images, ["_sortY"] = sortYPosition };
        }
        
        if (chartRefs.Count == 1 && !txbxContents.Any() && blips.Count == 0)
            return new JObject { ["type"] = "chart", ["rId"] = chartRefs[0], ["_sortY"] = sortYPosition };
        
        if (blips.Count > 0 || chartRefs.Count > 0 || txbxContents.Any())
        {
            var content = new JArray();
            
            foreach (var rId in blips)
                content.Add(new JObject { ["type"] = "image", ["rId"] = rId });
            
            foreach (var rId in chartRefs)
                content.Add(new JObject { ["type"] = "chart", ["rId"] = rId });
            
            // Each textbox gets its own shape object for better structure
            foreach (var txbx in txbxContents)
            {
                var textItems = extractTextBoxAsItems(txbx, numberingPart, numberingCounters);
                if (textItems.Count > 0)
                {
                    content.Add(new JObject { 
                        ["type"] = "textbox", 
                        ["content"] = textItems 
                    });
                }
            }
            
            var result = new JObject { 
                ["type"] = "shape", 
                ["_sortY"] = sortYPosition
            };
            if (content.Count > 0)
                result["content"] = content;
            return result;
        }
        
        var drawingTexts = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        
        if (drawingTexts.Any())
        {
            var content = new JArray();
            foreach (var txt in drawingTexts)
                content.Add(new JObject { ["type"] = "text", ["value"] = txt.Trim() });
            
            return new JObject { 
                ["type"] = "shape", 
                ["content"] = content, 
                ["_sortY"] = sortYPosition
            };
        }
        
        _logger.LogWarning("Empty shape detected: id={Id}, name={Name}", shapeId, shapeName);
        
        return new JObject { 
            ["type"] = "shape", 
            ["_sortY"] = sortYPosition
        };
    }

    public JObject? ExtractVmlPicture(Picture pict)
    {
        var imageData = pict.Descendants<DocumentFormat.OpenXml.Vml.ImageData>().FirstOrDefault();
        if (imageData?.RelationshipId?.Value != null)
            return new JObject { ["type"] = "image", ["rId"] = imageData.RelationshipId.Value };
        
        return new JObject { ["type"] = "shape" };
    }

    public (string? id, string? name) GetShapeIdentity(Drawing drawing)
    {
        var docPr = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().FirstOrDefault();
        if (docPr != null)
            return (docPr.Id?.ToString(), docPr.Name?.Value);
        
        return (null, null);
    }

    public (string? id, string? name) GetVmlShapeIdentity(OpenXmlElement? parent)
    {
        if (parent == null)
            return (null, null);
        
        var xml = parent.OuterXml;
        var idMatch = System.Text.RegularExpressions.Regex.Match(xml, @"id=[""']([^""']+)[""']");
        var nameMatch = System.Text.RegularExpressions.Regex.Match(xml, @"o:spid=[""']([^""']+)[""']");
        
        return (idMatch.Success ? idMatch.Groups[1].Value : null, 
                nameMatch.Success ? nameMatch.Groups[1].Value : null);
    }
}

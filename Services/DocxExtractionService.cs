using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IDocxExtractionService
{
    Task ExtractDocxToDatabase(string docxPath, int dokumenId);
}

public class DocxExtractionService : IDocxExtractionService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<DocxExtractionService> _logger;
    private readonly string _storagePath;

    public DocxExtractionService(KorektorBukuDbContext db, ILogger<DocxExtractionService> logger)
    {
        _db = db;
        _logger = logger;
        _storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
    }

    public async Task ExtractDocxToDatabase(string docxPath, int dokumenId)
    {
        try
        {
            _logger.LogInformation("Starting extraction for dokumen {DokumenId}, path: {Path}", dokumenId, docxPath);
            
            using var doc = WordprocessingDocument.Open(docxPath, false);
            
            await ExtractAllMedia(doc, dokumenId);
            
            var body = doc.MainDocumentPart!.Document.Body!;
            
            // Collect all elements with their floating info
            var elementsWithPosition = new List<(OpenXmlElement element, bool isFloating, int floatYPosition, int originalIndex)>();
            int idx = 0;
            
            foreach (var elem in body.Elements())
            {
                var (isFloating, yPos) = DetectFloatingElement(elem);
                elementsWithPosition.Add((elem, isFloating, yPos, idx++));
            }
            
            // Reorder: floating elements should be placed after the non-floating element that precedes them
            // based on their Y position offset
            var reorderedElements = ReorderFloatingElements(elementsWithPosition);
            
            int seq = 1;
            foreach (var elem in reorderedElements)
            {
                foreach (var (type, json) in ConvertBodyElementToItems(elem))
                {
                    _db.DokumenElemens.Add(new DokumenElemen
                    {
                        DokumenId = dokumenId,
                        DokumenElemenSequence = seq++,
                        DokumenElemenType = type,
                        DokumenElemenJsonTree = json.ToString(Formatting.None),
                        DokumenElemenXml = elem.OuterXml
                    });
                }
            }
            
            await _db.SaveChangesAsync();
            _logger.LogInformation("Extraction completed for dokumen {DokumenId}, {Count} elements", dokumenId, seq - 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for dokumen {DokumenId}", dokumenId);
            throw;
        }
    }

    /// <summary>
    /// Detects if an element is floating (positioned) and extracts its Y position.
    /// Floating elements include:
    /// - Tables with w:tblpPr (table positioning properties)
    /// - Paragraphs containing only anchored drawings (wp:anchor)
    /// </summary>
    private (bool isFloating, int yPosition) DetectFloatingElement(OpenXmlElement elem)
    {
        // Check for floating table
        if (elem is Table table)
        {
            var tblPr = table.GetFirstChild<TableProperties>();
            if (tblPr != null)
            {
                var tblpPr = tblPr.GetFirstChild<TablePositionProperties>();
                if (tblpPr != null)
                {
                    // Table is floating - extract Y position
                    // tblpY is in twips (1/20 of a point, 1440 twips = 1 inch)
                    var yPos = tblpPr.TablePositionY?.Value ?? 0;
                    _logger.LogInformation("Detected floating table with Y position: {Y} twips", yPos);
                    return (true, yPos);
                }
            }
        }
        
        // Check for paragraph containing anchored drawings
        if (elem is Paragraph para)
        {
            var drawings = para.Descendants<Drawing>().ToList();
            if (drawings.Count > 0)
            {
                // Check if any drawing is anchored (floating)
                foreach (var drawing in drawings)
                {
                    var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
                    if (anchor != null)
                    {
                        // Try to get Y position from positionV
                        var positionV = anchor.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.VerticalPosition>();
                        if (positionV != null)
                        {
                            var posOffset = positionV.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.PositionOffset>();
                            if (posOffset != null && int.TryParse(posOffset.Text, out int yPos))
                            {
                                // posOffset is in EMUs (914400 EMUs = 1 inch)
                                // Convert to twips for consistency (1440 twips = 1 inch)
                                var yTwips = (int)(yPos / 635.0); // EMU to twips
                                _logger.LogInformation("Detected floating drawing with Y position: {Y} EMUs ({Twips} twips)", yPos, yTwips);
                                return (true, yTwips);
                            }
                        }
                    }
                }
            }
        }
        
        return (false, 0);
    }

    /// <summary>
    /// Reorders floating elements locally within their clusters.
    /// Strategy: 
    /// - Identify clusters of consecutive floating elements.
    /// - Sort each cluster by Y position.
    /// - Preserve the relative order of clusters and non-floating elements.
    /// This prevents floating elements from jumping across pages (separated by text).
    /// </summary>
    private List<OpenXmlElement> ReorderFloatingElements(List<(OpenXmlElement element, bool isFloating, int floatYPosition, int originalIndex)> elements)
    {
        var result = new List<OpenXmlElement>();
        var cluster = new List<(OpenXmlElement element, int yPos, int origIdx)>();

        foreach (var (element, isFloating, floatY, origIdx) in elements)
        {
            // Check if this is a floating element with a valid Y position
            if (isFloating && floatY > 0)
            {
                // Add to current cluster
                cluster.Add((element, floatY, origIdx));
            }
            else
            {
                // Non-floating element encountered (or floating with no Y).
                // 1. Flush any pending cluster of floating elements
                if (cluster.Count > 0)
                {
                    // Sort cluster by Y, then by original index
                    var sortedCluster = cluster
                        .OrderBy(c => c.yPos)
                        .ThenBy(c => c.origIdx)
                        .Select(c => c.element);
                    
                    result.AddRange(sortedCluster);
                    
                    if (cluster.Count > 1) 
                    {
                         _logger.LogInformation("Sorted local cluster of {Count} floating elements around index {Idx}", cluster.Count, cluster[0].origIdx);
                    }
                    
                    cluster.Clear();
                }

                // 2. Add the non-floating element
                result.Add(element);
            }
        }

        // Flush remaining cluster at the end
        if (cluster.Count > 0)
        {
             var sortedCluster = cluster
                .OrderBy(c => c.yPos)
                .ThenBy(c => c.origIdx)
                .Select(c => c.element);
            result.AddRange(sortedCluster);
            
            if (cluster.Count > 1) 
            {
                 _logger.LogInformation("Sorted final cluster of {Count} floating elements", cluster.Count);
            }
        }

        return result;
    }

    private async Task ExtractAllMedia(WordprocessingDocument doc, int dokumenId)
    {
        var main = doc.MainDocumentPart!;
        
        foreach (var imgPart in main.ImageParts)
        {
            string rId = main.GetIdOfPart(imgPart);
            
            using var stream = imgPart.GetStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            byte[] bytes = ms.ToArray();
            
            string folder = Path.Combine(_storagePath, "dokumen_images", dokumenId.ToString());
            Directory.CreateDirectory(folder);
            
            string ext = imgPart.ContentType.Split('/').Last();
            string filename = $"{Guid.NewGuid()}.{ext}";
            string filepath = Path.Combine(folder, filename);
            
            await File.WriteAllBytesAsync(filepath, bytes);
            
            var dokumenMedia = new DokumenMedia
            {
                DokumenId = dokumenId,
                DokumenMediaRid = rId,
                DokumenMediaFilename = filename,
                DokumenMediaFilepath = filepath,
                DokumenMediaContentType = imgPart.ContentType
            };
            
            _db.DokumenMedias.Add(dokumenMedia);
        }
        
        await _db.SaveChangesAsync();
    }

    private IEnumerable<(string type, JObject json)> ConvertBodyElementToItems(OpenXmlElement elem)
    {
        if (elem is Paragraph p)
            return FlattenParagraph(p);

        if (elem is Table t)
            return new[] { ("table", new JObject { ["content"] = new JObject { ["rows"] = ConvertTableRows(t, null!) } }) };

        if (elem is SectionProperties)
            return new[] { ("sectionBreak", new JObject()) };

        if (elem is DocumentFormat.OpenXml.Math.OfficeMath math)
            return new[] { ("math", new JObject { ["content"] = new JArray { new JObject { ["type"] = "math", ["text"] = ExtractMathText(math) } } }) };

        if (elem is BookmarkStart || elem is BookmarkEnd)
            return Array.Empty<(string, JObject)>();

        return new[] { (elem.LocalName, new JObject { ["xml"] = elem.OuterXml }) };
    }

    private IEnumerable<(string type, JObject json)> FlattenParagraph(Paragraph p)
    {
        var paragraphType = DetectParagraphType(p);
        var content = ExtractParagraphContentSorted(p);

        if (content.Count == 0) yield break;

        yield return (paragraphType, new JObject { ["content"] = content });
    }

    /// <summary>
    /// Extracts paragraph content and sorts items by _sortY position.
    /// Items without _sortY (or _sortY=0) appear first in their original order,
    /// then anchored shapes with _sortY > 0 are appended sorted by their Y position.
    /// </summary>
    private JArray ExtractParagraphContentSorted(Paragraph p)
    {
        var regularItems = new List<(JToken item, int originalIndex)>();
        var anchoredItems = new List<(JObject item, int sortY, int originalIndex)>();
        var sb = new System.Text.StringBuilder();
        int itemIndex = 0;

        void FlushText()
        {
            var text = sb.ToString();
            sb.Clear();
            if (!string.IsNullOrWhiteSpace(text))
                regularItems.Add((new JObject { ["type"] = "text", ["value"] = text }, itemIndex++));
        }

        void ProcessElement(OpenXmlElement elem, bool skipTextBox = false)
        {
            if (elem is DocumentFormat.OpenXml.Math.OfficeMath om)
            {
                FlushText();
                var result = om.InnerText?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(result))
                    regularItems.Add((new JObject { ["type"] = "math", ["text"] = result }, itemIndex++));
            }
            else if (elem is DocumentFormat.OpenXml.Math.Paragraph mathPara)
            {
                FlushText();
                foreach (var oMath in mathPara.Elements<DocumentFormat.OpenXml.Math.OfficeMath>())
                {
                    var result = string.Join("", oMath.Descendants<DocumentFormat.OpenXml.Math.Text>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                    if (!string.IsNullOrWhiteSpace(result))
                        regularItems.Add((new JObject { ["type"] = "math", ["text"] = result }, itemIndex++));
                }
            }
            else if (elem is Text t)
            {
                sb.Append(t.Text);
            }
            else if (elem is TabChar)
            {
                sb.Append('\t');
            }
            else if (elem is Break)
            {
                sb.Append('\n');
            }
            else if (elem is Drawing drawing)
            {
                FlushText();
                
                // Check if this drawing is anchored (floating)
                var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
                var drawingItem = ExtractDrawingContent(drawing);
                
                if (drawingItem != null)
                {
                    if (anchor != null)
                    {
                        // This is an anchored/floating drawing - get sortY for ordering
                        int sortY = drawingItem["_sortY"]?.Value<int>() ?? 0;
                        _logger.LogInformation("Anchored drawing detected: type={Type}, sortY={SortY}", 
                            drawingItem["type"]?.ToString(), sortY);
                        if (sortY > 0)
                        {
                            anchoredItems.Add((drawingItem, sortY, itemIndex++));
                            _logger.LogInformation("Added to anchoredItems (will appear after regular items)");
                        }
                        else
                        {
                            regularItems.Add((drawingItem, itemIndex++));
                            _logger.LogInformation("Added to regularItems (sortY was 0)");
                        }
                    }
                    else
                    {
                        // This is an inline drawing - keep in regular items
                        regularItems.Add((drawingItem, itemIndex++));
                    }
                }
            }
            else if (elem is Picture pict)
            {
                FlushText();
                var pictItem = ExtractVmlPicture(pict);
                if (pictItem != null)
                    regularItems.Add((pictItem, itemIndex++));
            }
            else if (elem is DocumentFormat.OpenXml.Wordprocessing.TextBoxContent txbx && !skipTextBox)
            {
                FlushText();
                var shapeData = ExtractTextBoxAsShape(txbx, elem.Parent);
                if (shapeData != null)
                    regularItems.Add((shapeData, itemIndex++));
            }
            else if (elem is not DocumentFormat.OpenXml.Wordprocessing.TextBoxContent)
            {
                foreach (var child in elem.ChildElements)
                    ProcessElement(child, skipTextBox);
            }
        }

        foreach (var child in p.ChildElements)
            ProcessElement(child);

        FlushText();

        // Build result: regular items first (in original order), then anchored items sorted by Y
        var result = new JArray();
        
        foreach (var (item, _) in regularItems.OrderBy(x => x.originalIndex))
        {
            // Remove internal _sortY property before output
            if (item is JObject jObj && jObj["_sortY"] != null)
                jObj.Remove("_sortY");
            result.Add(item);
        }
        
        foreach (var (item, sortY, _) in anchoredItems.OrderBy(x => x.sortY))
        {
            // Remove internal _sortY property before output
            item.Remove("_sortY");
            result.Add(item);
        }
        
        return result;
    }

    /// <summary>
    /// Legacy method for table cell extraction - uses sorted extraction
    /// </summary>
    private JArray ExtractParagraphContent(Paragraph p)
    {
        return ExtractParagraphContentSorted(p);
    }

    private JObject? ExtractDrawingContent(Drawing drawing)
    {
        var (shapeId, shapeName) = GetShapeIdentity(drawing);
        
        // Skip group shapes - hanya proses individual shapes
        if (shapeName?.StartsWith("Group ") == true)
            return null;
        
        // Extract positioning and z-order properties from anchor
        int sortYPosition = 0;
        long zIndex = 0;
        bool behindDoc = false;
        
        var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
        if (anchor != null)
        {
            // Extract Y position for sorting
            var positionV = anchor.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.VerticalPosition>();
            if (positionV != null)
            {
                var posOffset = positionV.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.PositionOffset>();
                if (posOffset != null && int.TryParse(posOffset.Text, out int yPos))
                {
                    sortYPosition = yPos;
                }
            }
            
            // Extract z-index (relativeHeight attribute)
            if (anchor.RelativeHeight != null)
            {
                zIndex = anchor.RelativeHeight.Value;
            }
            
            // Extract behindDoc attribute
            if (anchor.BehindDoc != null)
            {
                behindDoc = anchor.BehindDoc.Value;
            }
        }

        
        var blips = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>()
            .Where(b => b.Embed?.Value != null)
            .Select(b => b.Embed!.Value)
            .Distinct()
            .ToList();
        
        // Detect chart relationships
        var chartRefs = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Charts.ChartReference>()
            .Where(c => c.Id?.Value != null)
            .Select(c => c.Id!.Value)
            .Distinct()
            .ToList();

        // Fallback: manual XML search if strongly typed class misses it
        if (!chartRefs.Any())
        {
            chartRefs = drawing.Descendants()
                .Where(e => e.LocalName == "chart" && e.NamespaceUri == "http://schemas.openxmlformats.org/drawingml/2006/chart")
                .Select(e => e.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships").Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList()!;
        }
        
        var txbxContents = drawing.Descendants<DocumentFormat.OpenXml.Wordprocessing.TextBoxContent>().ToList();
        
        // Pure image (no textbox)
        if (blips.Count == 1 && !txbxContents.Any())
            return new JObject { ["type"] = "image", ["rId"] = blips[0] };
        
        // Multiple images (no textbox)
        if (blips.Count > 1 && !txbxContents.Any())
        {
            var images = new JArray();
            foreach (var rId in blips)
                images.Add(new JObject { ["type"] = "image", ["rId"] = rId });
            return new JObject { ["type"] = "composite", ["content"] = images };
        }
        
        // Pure chart (no textbox)
        if (chartRefs.Count == 1 && !txbxContents.Any() && blips.Count == 0)
        {
            return new JObject { ["type"] = "image", ["rId"] = chartRefs[0] };
        }
        
        // Shape with content (image/chart + textbox or textbox only)
        if (blips.Count > 0 || chartRefs.Count > 0 || txbxContents.Any())
        {
            var content = new JArray();
            
            foreach (var rId in blips)
                content.Add(new JObject { ["type"] = "image", ["rId"] = rId });
            
            foreach (var rId in chartRefs)
                content.Add(new JObject { ["type"] = "image", ["rId"] = rId });
            
            foreach (var txbx in txbxContents)
            {
                var textItems = ExtractTextBoxAsItems(txbx);
                foreach (var item in textItems)
                    content.Add(item);
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
        
        // Empty shape - log untuk debugging
        _logger.LogWarning("Empty shape detected: id={Id}, name={Name}, xml={Xml}", 
            shapeId, shapeName, drawing.OuterXml.Substring(0, Math.Min(200, drawing.OuterXml.Length)));
        
        return new JObject { 
            ["type"] = "shape", 
            ["_sortY"] = sortYPosition
        };
    }

    private JArray ExtractTextBoxContent(OpenXmlElement container)
    {
        var content = new JArray();
        
        foreach (var elem in container.ChildElements)
        {
            if (elem is Paragraph para)
            {
                var paraContent = ExtractParagraphContent(para);
                foreach (var item in paraContent)
                    content.Add(item);
            }
            else if (elem is Table table)
            {
                var tableRows = ConvertTableRows(table, null!);
                content.Add(new JObject { ["type"] = "table", ["rows"] = tableRows });
            }
        }
        
        return content;
    }

    private JArray ExtractTextBoxAsItems(OpenXmlElement container)
    {
        var items = new JArray();
        
        foreach (var elem in container.ChildElements)
        {
            if (elem is Paragraph para)
            {
                var paraContent = ExtractParagraphContent(para);
                foreach (var item in paraContent)
                    items.Add(item);
            }
            else if (elem is Table table)
            {
                var tableRows = ConvertTableRows(table, null!);
                items.Add(new JObject { ["type"] = "table", ["rows"] = tableRows });
            }
        }
        
        return items;
    }

    private JObject? ExtractTextBoxAsShape(OpenXmlElement txbxContent, OpenXmlElement? parent)
    {
        var (shapeId, shapeName) = GetVmlShapeIdentity(parent);
        var content = ExtractTextBoxAsItems(txbxContent);
        
        var result = new JObject { ["type"] = "shape" };
        if (content.Count > 0)
            result["content"] = content;
        
        return result;
    }

    private JObject? ExtractVmlPicture(Picture pict)
    {
        var imageData = pict.Descendants<DocumentFormat.OpenXml.Vml.ImageData>().FirstOrDefault();
        if (imageData?.RelationshipId?.Value != null)
        {
            return new JObject { ["type"] = "image", ["rId"] = imageData.RelationshipId.Value };
        }
        
        return new JObject { ["type"] = "shape" };
    }

    private (string? id, string? name) GetShapeIdentity(Drawing drawing)
    {
        var docPr = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().FirstOrDefault();
        if (docPr != null)
            return (docPr.Id?.ToString(), docPr.Name?.Value);
        
        return (null, null);
    }

    private (string? id, string? name) GetVmlShapeIdentity(OpenXmlElement? parent)
    {
        if (parent == null)
            return (null, null);
        
        var xml = parent.OuterXml;
        var idMatch = System.Text.RegularExpressions.Regex.Match(xml, @"id=[""']([^""']+)[""']");
        var nameMatch = System.Text.RegularExpressions.Regex.Match(xml, @"o:spid=[""']([^""']+)[""']");
        
        return (idMatch.Success ? idMatch.Groups[1].Value : null, 
                nameMatch.Success ? nameMatch.Groups[1].Value : null);
    }

    private string DetectParagraphType(Paragraph p)
    {
        var pPr = p.ParagraphProperties;
        
        if (pPr == null)
            return "paragraph";

        // Heading (h1-h9)
        if (pPr.ParagraphStyleId?.Val?.Value != null)
        {
            var styleId = pPr.ParagraphStyleId.Val.Value.ToLower();
            if (styleId.StartsWith("heading"))
            {
                var level = styleId.Replace("heading", "");
                return $"h{level}";
            }
            if (styleId.StartsWith("title"))
                return "title";
            if (styleId.StartsWith("subtitle"))
                return "subtitle";
        }

        // Numbering (ordered/unordered list)
        if (pPr.NumberingProperties?.NumberingId?.Val?.Value != null)
        {
            var numId = pPr.NumberingProperties.NumberingId.Val.Value;
            var ilvl = pPr.NumberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
            return $"list-item-{numId}-{ilvl}";
        }

        return "paragraph";
    }

    private string ExtractMathText(OpenXmlElement mathElement)
    {
        // Try InnerText first
        var innerText = mathElement.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(innerText))
        {
            _logger.LogInformation("Math InnerText: '{Text}'", innerText);
            return innerText;
        }
        
        // Fallback: extract from all Text descendants
        var texts = mathElement.Descendants<DocumentFormat.OpenXml.Math.Text>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        
        var result = string.Join("", texts);
        _logger.LogInformation("Math from descendants: '{Text}'", result);
        
        return string.IsNullOrWhiteSpace(result) ? "[formula]" : result;
    }



    private JArray ConvertTableRows(Table t, MainDocumentPart mainPart)
    {
        var rows = new JArray();

        foreach (var row in t.Elements<TableRow>())
        {
            var cells = new JArray();

            foreach (var cell in row.Elements<TableCell>())
            {
                var cellInlines = new JArray();
                
                // Collect all items with their Y positions for sorting
                var itemsWithPosition = new List<(JToken item, int yPosition, int originalIndex)>();
                int itemIndex = 0;
                
                foreach (var elem in cell.ChildElements)
                {
                    if (elem is Paragraph para)
                    {
                        var paraContent = ExtractParagraphContent(para);
                        foreach (var item in paraContent)
                        {
                            // Get Y position from _sortY property if it exists
                            int yPos = 0;
                            if (item is JObject jObj && jObj["_sortY"] != null)
                            {
                                yPos = jObj["_sortY"]!.Value<int>();
                            }
                            itemsWithPosition.Add((item, yPos, itemIndex++));
                        }
                    }
                    else if (elem is Table nestedTable)
                    {
                        var nestedRows = ConvertTableRows(nestedTable, null!);
                        var tableItem = new JObject { ["type"] = "table", ["rows"] = nestedRows };
                        itemsWithPosition.Add((tableItem, 0, itemIndex++));
                    }
                }
                
                // Sort items by Y position (shapes with Y > 0 should be in visual order)
                var sortedItems = itemsWithPosition
                    .OrderBy(x => x.yPosition > 0 ? x.yPosition : int.MaxValue) // Items with Y position first
                    .ThenBy(x => x.originalIndex) // Then by original order for items without Y position
                    .ToList();
                
                foreach (var (item, yPos, idx) in sortedItems)
                {
                    // Remove _sortY property before adding to result (internal use only)
                    if (item is JObject jObj && jObj["_sortY"] != null)
                    {
                        jObj.Remove("_sortY");
                    }
                    cellInlines.Add(item);
                }
                
                cells.Add(cellInlines);
            }

            rows.Add(new JObject { ["cells"] = cells });
        }

        return rows;
    }

}

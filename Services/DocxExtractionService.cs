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
            var numberingPart = doc.MainDocumentPart.NumberingDefinitionsPart;
            // Map: numId -> level -> counter
            var numberingCounters = new Dictionary<int, Dictionary<int, int>>();
            
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
                foreach (var (type, json) in ConvertBodyElementToItems(elem, numberingPart, numberingCounters))
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
            
            string ext = Path.GetExtension(imgPart.Uri.OriginalString).TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "png";
            
            string filename = $"{dokumenId}_{rId}.{ext}";
            string fullPath = Path.Combine(_storagePath, filename);
            
            await File.WriteAllBytesAsync(fullPath, ms.ToArray());
            _logger.LogInformation("Saved media: {Filename}", filename);
        }
    }

    private IEnumerable<(string type, JObject json)> ConvertBodyElementToItems(
        OpenXmlElement elem, 
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        if (elem is Paragraph p)
            return FlattenParagraph(p, numberingPart, numberingCounters);

        if (elem is Table t)
            return new[] { ("table", new JObject { ["content"] = new JObject { ["rows"] = ConvertTableRows(t, numberingPart, numberingCounters) } }) };

        if (elem is SectionProperties)
            return new[] { ("sectionBreak", new JObject()) };

        if (elem is DocumentFormat.OpenXml.Math.OfficeMath math)
            return new[] { ("math", new JObject { ["content"] = new JArray { new JObject { ["type"] = "math", ["text"] = ExtractMathText(math) } } }) };

        if (elem is BookmarkStart || elem is BookmarkEnd)
            return Array.Empty<(string, JObject)>();

        return new[] { (elem.LocalName, new JObject { ["xml"] = elem.OuterXml }) };
    }

    private IEnumerable<(string type, JObject json)> FlattenParagraph(
        Paragraph p,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var paragraphType = DetectParagraphType(p);
        var content = ExtractParagraphContentSorted(p, numberingPart, numberingCounters);

        if (content.Count == 0) yield break;

        yield return (paragraphType, new JObject { ["content"] = content });
    }

    /// <summary>
    /// Extracts paragraph content and sorts items by _sortY position.
    /// Items without _sortY (or _sortY=0) appear first in their original order,
    /// then anchored shapes with _sortY > 0 are appended sorted by their Y position.
    /// </summary>
    private JArray ExtractParagraphContentSorted(
        Paragraph p,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var regularItems = new List<(JToken item, int originalIndex)>();
        var anchoredItems = new List<(JObject item, int sortY, int originalIndex)>();
        
        int itemIndex = 0;
        var helper = new ParagraphContentHelper();
        
        // --- Numbering Extraction Logic ---
        string numberingText = "";
        
        if (p.ParagraphProperties?.NumberingProperties != null && numberingPart != null && numberingCounters != null)
        {
            try 
            {
                var numPr = p.ParagraphProperties.NumberingProperties;
                var numId = numPr.NumberingId?.Val;
                var ilvl = numPr.NumberingLevelReference?.Val ?? 0;
                
                if (numId != null)
                {
                    string label = GetNumberingText(numberingPart, numId.Value, ilvl.Value, numberingCounters);
                    if (!string.IsNullOrEmpty(label))
                    {
                        numberingText = label + " "; // Add space for separation
                    }
                }
            }
            catch (Exception ex)
            {
                 _logger.LogWarning(ex, "Failed to resolve numbering for paragraph");
            }
        }
        
        // Add numbering as the VERY FIRST item if valid
        if (!string.IsNullOrEmpty(numberingText))
        {
             regularItems.Add((new JObject { ["type"] = "text", ["value"] = numberingText }, itemIndex++));
        }
        // ----------------------------------

        void FlushText()
        {
            var text = helper.FlushAndClear();
            if (!string.IsNullOrEmpty(text))
            {
                regularItems.Add((new JObject { ["type"] = "text", ["value"] = text }, itemIndex++));
            }
        }
        
        void ProcessElement(OpenXmlElement elem, bool skipTextBox = false)
        {
            var sb = helper.StringBuilder;
            
            if (elem is Run or DocumentFormat.OpenXml.Math.Run) 
            {
                foreach (var child in elem.ChildElements)
                {
                    ProcessElement(child, skipTextBox);
                }
            }
            else if (elem is DocumentFormat.OpenXml.Math.OfficeMath om)
            {
                FlushText();
                var result = string.Join("", om.Descendants<DocumentFormat.OpenXml.Math.Text>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
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
                var drawingItem = ExtractDrawingContent(drawing, numberingPart, numberingCounters);
                
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
                var shapeData = ExtractTextBoxAsShape(txbx, elem.Parent, numberingPart, numberingCounters);
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
    
    private string GetNumberingText(NumberingDefinitionsPart numberingPart, int numId, int ilvl, Dictionary<int, Dictionary<int, int>> counters)
    {
        // 1. Find the Num instance
        var numInstance = numberingPart.Numbering.Elements<DocumentFormat.OpenXml.Wordprocessing.NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        if (numInstance == null) return "?";
        
        int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
        
        // 2. Find AbstractNum
        var abstractNum = numberingPart.Numbering.Elements<DocumentFormat.OpenXml.Wordprocessing.AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
        if (abstractNum == null) return "?";
        
        // 3. Find Level definition
        var level = abstractNum.Elements<DocumentFormat.OpenXml.Wordprocessing.Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == ilvl);
        if (level == null) return "?";
        
        // 4. Update Counter
        if (!counters.ContainsKey(numId))
            counters[numId] = new Dictionary<int, int>();
            
        if (!counters[numId].ContainsKey(ilvl))
        {
             // Initialize counter (default to StartingIndex or 1)
             int start = level.StartNumberingValue?.Val ?? 1;
             counters[numId][ilvl] = start;
        }
        else
        {
             // Increment
             counters[numId][ilvl]++;
        }
        
        // Reset deeper levels? usually implicit in Word, but simple approx:
        // When level N increments, reset N+1, N+2, etc.
        foreach(var key in counters[numId].Keys.ToList())
        {
            if (key > ilvl) counters[numId].Remove(key);
        }
        
        int currentVal = counters[numId][ilvl];
        
        // 5. Format Text
        // lvlText e.g. "%1." or "(%1)"
        // numFmt e.g. "decimal", "bullet", "lowerLetter"
        
        string lvlText = level.LevelText?.Val?.ToString() ?? "";
        string numFmt = level.NumberingFormat?.Val?.ToString() ?? "decimal";
        
        if (numFmt == "bullet")
        {
             // Ignore counter for bullets, use the actual char if present in LevelText or fallback
             return lvlText.Length > 0 ? lvlText : "•"; 
        }
        
        // Convert integer to requested format string
        string valStr = currentVal.ToString();
        
        if (numFmt == "lowerLetter") 
        {
            // 1->a, 2->b...
            valStr = GetLetter(currentVal, true);
        }
        else if (numFmt == "upperLetter")
        {
            valStr = GetLetter(currentVal, false);
        }
        else if (numFmt == "lowerRoman")
        {
            valStr = ToRoman(currentVal).ToLowerInvariant();
        }
        else if (numFmt == "upperRoman")
        {
            valStr = ToRoman(currentVal);
        }
        
        // Replace placeholders %1, %2 etc. with values from higher levels if needed
        // For simplicity, we mostly handle %1 for current level. 
        // A robust system needs to pull counters[numId][0], counters[numId][1] etc.
        
        // Replace the place holder for this level (level index is 0-based, placeholder is 1-based usually)
        // e.g. for Level 0, placeholder is %1. For Level 1, might be %1.%2
        
        // Simple replacement algorithm:
        // Iterate levels 0 to ilvl, get their current counter values, format them, and replace %1..%(ilvl+1)
        
        string formatted = lvlText;
        for (int i = 0; i <= ilvl; i++)
        {
             int cVal = counters[numId].ContainsKey(i) ? counters[numId][i] : (abstractNum.Elements<DocumentFormat.OpenXml.Wordprocessing.Level>().FirstOrDefault(l => l.LevelIndex==i)?.StartNumberingValue?.Val ?? 1);
             
             // We need the format of THAT level too
             var subLevel = abstractNum.Elements<DocumentFormat.OpenXml.Wordprocessing.Level>().FirstOrDefault(l => l.LevelIndex==i);
             string subFmt = subLevel?.NumberingFormat?.Val?.ToString() ?? "decimal";
             
             string subValStr = cVal.ToString();
             if (subFmt == "lowerLetter") subValStr = GetLetter(cVal, true);
             else if (subFmt == "upperLetter") subValStr = GetLetter(cVal, false);
             else if (subFmt == "lowerRoman") subValStr = ToRoman(cVal).ToLowerInvariant();
             else if (subFmt == "upperRoman") subValStr = ToRoman(cVal);
             else if (subFmt == "bullet") subValStr = subLevel?.LevelText?.Val ?? "";

             formatted = formatted.Replace($"%{i+1}", subValStr);
        }
        
        return formatted;
    }
    
    private string GetLetter(int val, bool lower)
    {
        // 1->A, 26->Z, 27->AA...
        if (val <= 0) return "?";
        val--; 
        string s = "";
        do {
            s = (char)('A' + (val % 26)) + s;
            val /= 26;
            val--;
        } while (val >= 0);
        return lower ? s.ToLowerInvariant() : s;
    }
    
    private string ToRoman(int number) 
    {
        if (number < 1) return string.Empty;
        if (number >= 1000) return "M" + ToRoman(number - 1000);
        if (number >= 900) return "CM" + ToRoman(number - 900);
        if (number >= 500) return "D" + ToRoman(number - 500);
        if (number >= 400) return "CD" + ToRoman(number - 400);
        if (number >= 100) return "C" + ToRoman(number - 100);
        if (number >= 90) return "XC" + ToRoman(number - 90);
        if (number >= 50) return "L" + ToRoman(number - 50);
        if (number >= 40) return "XL" + ToRoman(number - 40);
        if (number >= 10) return "X" + ToRoman(number - 10);
        if (number >= 9) return "IX" + ToRoman(number - 9);
        if (number >= 5) return "V" + ToRoman(number - 5);
        if (number >= 4) return "IV" + ToRoman(number - 4);
        return "I" + ToRoman(number - 1);
    }

    /// <summary>
    /// Legacy method for table cell extraction - uses sorted extraction
    /// </summary>
    private JArray ExtractParagraphContent(
        Paragraph p,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        return ExtractParagraphContentSorted(p, numberingPart, numberingCounters);
    }

    private JObject? ExtractDrawingContent(
        Drawing drawing,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
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
        
        // Check for TextBox content
        var txbxContent = drawing.Descendants<DocumentFormat.OpenXml.Wordprocessing.TextBoxContent>().FirstOrDefault();
        if (txbxContent != null)
        {
            // Extract content from text box
            var content = ExtractTextBoxAsItems(txbxContent, numberingPart, numberingCounters);
            return new JObject { 
                ["type"] = "shape", 
                ["content"] = content, 
                ["_sortY"] = sortYPosition
            };
        }

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

    private JArray ExtractTextBoxContent(
        OpenXmlElement container,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var content = new JArray();
        
        foreach (var elem in container.ChildElements)
        {
            if (elem is Paragraph para)
            {
                var paraContent = ExtractParagraphContent(para, numberingPart, numberingCounters);
                foreach (var item in paraContent)
                    content.Add(item);
            }
            else if (elem is Table table)
            {
                var tableRows = ConvertTableRows(table, numberingPart, numberingCounters);
                content.Add(new JObject { ["type"] = "table", ["rows"] = tableRows });
            }
        }
        
        return content;
    }

    private JArray ExtractTextBoxAsItems(
        OpenXmlElement container,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var items = new JArray();
        
        foreach (var elem in container.ChildElements)
        {
            if (elem is Paragraph para)
            {
                var paraContent = ExtractParagraphContent(para, numberingPart, numberingCounters);
                foreach (var item in paraContent)
                    items.Add(item);
            }
            else if (elem is Table table)
            {
                var tableRows = ConvertTableRows(table, numberingPart, numberingCounters);
                items.Add(new JObject { ["type"] = "table", ["rows"] = tableRows });
            }
        }
        
        return items;
    }

    private JObject? ExtractTextBoxAsShape(
        OpenXmlElement txbxContent, 
        OpenXmlElement? parent,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var (shapeId, shapeName) = GetVmlShapeIdentity(parent);
        var content = ExtractTextBoxAsItems(txbxContent, numberingPart, numberingCounters);
        
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



    private JArray ConvertTableRows(
        Table table, 
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var rows = new JArray();
        
        foreach (var row in table.Descendants<TableRow>())
        {
            var rowJson = new JObject { ["cells"] = new JArray() };
            foreach (var cell in row.Descendants<TableCell>())
            {
                var cellContent = new JArray();
                
                foreach (var element in cell.Elements())
                {
                     if (element is Paragraph p)
                     {
                         var pType = DetectParagraphType(p);
                         // Use Sorted here
                         var pContent = ExtractParagraphContentSorted(p, numberingPart, numberingCounters);
                         if (pContent.Count > 0)
                            cellContent.Add(new JObject { ["type"] = pType, ["content"] = pContent });
                     }
                     else if (element is Table nestedTable)
                     {
                         // Recursive table processing
                         var nestedRows = ConvertTableRows(nestedTable, numberingPart, numberingCounters);
                         cellContent.Add(new JObject { ["type"] = "table", ["content"] = new JObject { ["rows"] = nestedRows } });
                     }
                }
                
                ((JArray)rowJson["cells"]!).Add(new JObject { ["content"] = cellContent });
            }
            rows.Add(rowJson);
        }
        return rows;
    }

}

/// <summary>
/// Helper class for paragraph content extraction
/// </summary>
internal class ParagraphContentHelper
{
    public System.Text.StringBuilder StringBuilder { get; } = new System.Text.StringBuilder();
    
    public string FlushAndClear()
    {
        var text = StringBuilder.ToString();
        StringBuilder.Clear();
        return text;
    }
}

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Helper class for paragraph content extraction
/// </summary>
internal class ParagraphContentHelper
{
    public StringBuilder StringBuilder { get; } = new StringBuilder();
    
    public string FlushAndClear()
    {
        var text = StringBuilder.ToString();
        StringBuilder.Clear();
        return text;
    }
}

/// <summary>
/// Handles extraction of paragraph content including numbering, text, math, and inline elements.
/// Delegates math extraction to MathExtractor and numbering to NumberingExtractor.
/// </summary>
public class ParagraphExtractor
{
    private readonly ILogger _logger;
    private readonly DrawingExtractor _drawingExtractor;
    private StyleResolver? _styleResolver;

    public ParagraphExtractor(ILogger logger, DrawingExtractor drawingExtractor)
    {
        _logger = logger;
        _drawingExtractor = drawingExtractor;
    }
    
    /// <summary>
    /// Sets the StyleResolver for resolving numbering from style chain
    /// </summary>
    public void SetStyleResolver(StyleResolver? styleResolver)
    {
        _styleResolver = styleResolver;
    }

    public string DetectParagraphType(Paragraph p)
    {
        var pPr = p.ParagraphProperties;
        
        if (pPr == null)
            return "paragraph";

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

        if (pPr.NumberingProperties?.NumberingId?.Val?.Value != null)
        {
            var numId = pPr.NumberingProperties.NumberingId.Val.Value;
            var ilvl = pPr.NumberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
            return $"list-item-{numId}-{ilvl}";
        }

        return "paragraph";
    }

    public IEnumerable<(string type, JObject json)> FlattenParagraph(
        Paragraph p,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var paragraphType = DetectParagraphType(p);
        var content = ExtractParagraphContentSorted(p, numberingPart, numberingCounters);

        if (content.Count == 0) yield break;

        yield return (paragraphType, new JObject { ["content"] = content });
    }


    public JArray ExtractParagraphContentSorted(
        Paragraph p,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var regularItems = new List<(JToken item, int originalIndex)>();
        var anchoredItems = new List<(JObject item, int sortY, int originalIndex)>();
        
        int itemIndex = 0;
        var helper = new ParagraphContentHelper();
        
        // --- Numbering Extraction Logic (delegated to NumberingExtractor) ---
        string numberingText = "";
        
        if (numberingPart != null && numberingCounters != null)
        {
            try 
            {
                int? numId = null;
                int ilvl = 0;
                string source = "none";
                
                var directNumPr = p.ParagraphProperties?.NumberingProperties;
                if (directNumPr?.NumberingId?.Val != null)
                {
                    int directNumId = directNumPr.NumberingId.Val.Value;
                    if (directNumId == 0)
                    {
                        numId = null;
                        source = "disabled";
                    }
                    else
                    {
                        numId = directNumId;
                        ilvl = directNumPr.NumberingLevelReference?.Val?.Value ?? 0;
                        source = "direct";
                    }
                }
                else if (_styleResolver != null)
                {
                    var (styleNumId, styleIlvl) = _styleResolver.GetEffectiveNumberingProperties(p);
                    if (styleNumId != null && styleNumId.Value != 0)
                    {
                        numId = styleNumId;
                        ilvl = styleIlvl;
                        source = "style";
                    }
                }
                
                if (numId != null && numId.Value > 0)
                {
                    string label = NumberingExtractor.GetNumberingText(numberingPart, numId.Value, ilvl, numberingCounters);
                    if (!string.IsNullOrEmpty(label))
                        numberingText = label + " ";
                    _logger.LogInformation("Numbering: source={Source}, numId={NumId}, ilvl={Ilvl}, label={Label}", 
                        source, numId, ilvl, label);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve numbering for paragraph");
            }
        }
        
        if (!string.IsNullOrEmpty(numberingText))
            regularItems.Add((new JObject { ["type"] = "text", ["value"] = numberingText }, itemIndex++));

        void FlushText()
        {
            var text = helper.FlushAndClear();
            if (!string.IsNullOrEmpty(text))
                regularItems.Add((new JObject { ["type"] = "text", ["value"] = text }, itemIndex++));
        }
        
        void ProcessElement(OpenXmlElement elem, bool skipTextBox = false)
        {
            var sb = helper.StringBuilder;
            
            if (elem is Run or DocumentFormat.OpenXml.Math.Run)
            {
                foreach (var child in elem.ChildElements)
                    ProcessElement(child, skipTextBox);
            }
            // Math extraction delegated to MathExtractor
            else if (elem is DocumentFormat.OpenXml.Math.OfficeMath om)
            {
                FlushText();
                var result = MathExtractor.ExtractMathContent(om);
                if (!string.IsNullOrWhiteSpace(result))
                    regularItems.Add((new JObject { ["type"] = "math", ["text"] = result }, itemIndex++));
            }
            else if (elem is DocumentFormat.OpenXml.Math.Paragraph mathPara)
            {
                FlushText();
                foreach (var oMath in mathPara.Elements<DocumentFormat.OpenXml.Math.OfficeMath>())
                {
                    var result = MathExtractor.ExtractMathContent(oMath);
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
                
                var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
                var drawingItem = _drawingExtractor.ExtractDrawingContent(drawing, ExtractTextBoxAsItems, numberingPart, numberingCounters);
                
                if (drawingItem != null)
                {
                    if (anchor != null)
                    {
                        int sortY = drawingItem["_sortY"]?.Value<int>() ?? 0;
                        if (sortY > 0)
                            anchoredItems.Add((drawingItem, sortY, itemIndex++));
                        else
                            regularItems.Add((drawingItem, itemIndex++));
                    }
                    else
                    {
                        regularItems.Add((drawingItem, itemIndex++));
                    }
                }
            }
            else if (elem is Picture pict)
            {
                FlushText();
                var pictItem = _drawingExtractor.ExtractVmlPicture(pict);
                if (pictItem != null)
                    regularItems.Add((pictItem, itemIndex++));
            }
            else if (elem is TextBoxContent txbx && !skipTextBox)
            {
                FlushText();
                var shapeData = ExtractTextBoxAsShape(txbx, elem.Parent, numberingPart, numberingCounters);
                if (shapeData != null)
                    regularItems.Add((shapeData, itemIndex++));
            }
            else if (elem is not TextBoxContent)
            {
                foreach (var child in elem.ChildElements)
                    ProcessElement(child, skipTextBox);
            }
        }

        foreach (var child in p.ChildElements)
            ProcessElement(child);

        FlushText();

        var result = new JArray();
        
        foreach (var (item, _) in regularItems.OrderBy(x => x.originalIndex))
        {
            if (item is JObject jObj && jObj["_sortY"] != null)
                jObj.Remove("_sortY");
            result.Add(item);
        }
        
        foreach (var (item, sortY, _) in anchoredItems.OrderBy(x => x.sortY))
        {
            item.Remove("_sortY");
            result.Add(item);
        }
        
        return result;
    }

    public JArray ExtractTextBoxAsItems(
        OpenXmlElement container,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var items = new JArray();
        
        foreach (var elem in container.ChildElements)
        {
            if (elem is Paragraph para)
            {
                var paraContent = ExtractParagraphContentSorted(para, numberingPart, numberingCounters);
                foreach (var item in paraContent)
                    items.Add(item);
            }
            else if (elem is Table table)
            {
                items.Add(new JObject { ["type"] = "table" });
            }
        }
        
        return items;
    }

    public JObject? ExtractTextBoxAsShape(
        OpenXmlElement txbxContent, 
        OpenXmlElement? parent,
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var content = ExtractTextBoxAsItems(txbxContent, numberingPart, numberingCounters);
        
        var result = new JObject { ["type"] = "shape" };
        if (content.Count > 0)
            result["content"] = content;
        
        return result;
    }
}

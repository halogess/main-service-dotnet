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
/// Handles extraction of paragraph content including numbering, text, math, and inline elements
/// </summary>
public class ParagraphExtractor
{
    private readonly ILogger _logger;
    private readonly DrawingExtractor _drawingExtractor;

    public ParagraphExtractor(ILogger logger, DrawingExtractor drawingExtractor)
    {
        _logger = logger;
        _drawingExtractor = drawingExtractor;
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
                        numberingText = label + " ";
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
                // Tables inside textboxes - simplified extraction
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

    public string ExtractMathText(OpenXmlElement mathElement)
    {
        var innerText = mathElement.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(innerText))
            return innerText;
        
        var texts = mathElement.Descendants<DocumentFormat.OpenXml.Math.Text>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        
        var result = string.Join("", texts);
        return string.IsNullOrWhiteSpace(result) ? "[formula]" : result;
    }

    // Numbering helper methods
    public string GetNumberingText(NumberingDefinitionsPart numberingPart, int numId, int ilvl, Dictionary<int, Dictionary<int, int>> counters)
    {
        var numInstance = numberingPart.Numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        if (numInstance == null) return "?";
        
        int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
        
        var abstractNum = numberingPart.Numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
        if (abstractNum == null) return "?";
        
        var level = abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == ilvl);
        if (level == null) return "?";
        
        if (!counters.ContainsKey(numId))
            counters[numId] = new Dictionary<int, int>();
            
        if (!counters[numId].ContainsKey(ilvl))
        {
            int start = level.StartNumberingValue?.Val ?? 1;
            counters[numId][ilvl] = start;
        }
        else
        {
            counters[numId][ilvl]++;
        }
        
        foreach (var key in counters[numId].Keys.ToList())
        {
            if (key > ilvl) counters[numId].Remove(key);
        }
        
        int currentVal = counters[numId][ilvl];
        
        string lvlText = level.LevelText?.Val?.ToString() ?? "";
        string numFmt = level.NumberingFormat?.Val?.ToString() ?? "decimal";
        
        if (numFmt == "bullet")
            return lvlText.Length > 0 ? lvlText : "•";
        
        string formatted = lvlText;
        for (int i = 0; i <= ilvl; i++)
        {
            int cVal = counters[numId].ContainsKey(i) ? counters[numId][i] : (int)(abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == i)?.StartNumberingValue?.Val ?? 1);
             
            var subLevel = abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == i);
            string subFmt = subLevel?.NumberingFormat?.Val?.ToString() ?? "decimal";
             
            string subValStr = cVal.ToString();
            if (subFmt == "lowerLetter") subValStr = GetLetter(cVal, true);
            else if (subFmt == "upperLetter") subValStr = GetLetter(cVal, false);
            else if (subFmt == "lowerRoman") subValStr = ToRoman(cVal).ToLowerInvariant();
            else if (subFmt == "upperRoman") subValStr = ToRoman(cVal);
            else if (subFmt == "bullet") subValStr = subLevel?.LevelText?.Val?.Value ?? "";

            formatted = formatted.Replace($"%{i + 1}", subValStr);
        }
        
        return formatted;
    }
    
    private string GetLetter(int val, bool lower)
    {
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
}

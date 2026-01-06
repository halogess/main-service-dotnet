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
        
        // --- Numbering Extraction Logic ---
        string numberingText = "";
        
        if (numberingPart != null && numberingCounters != null)
        {
            try 
            {
                // Try to get numbering from direct pPr or style chain
                int? numId = null;
                int ilvl = 0;
                string source = "none";
                
                // Check direct numPr first
                var directNumPr = p.ParagraphProperties?.NumberingProperties;
                if (directNumPr?.NumberingId?.Val != null)
                {
                    int directNumId = directNumPr.NumberingId.Val.Value;
                    // numId=0 means "disable numbering from style" - don't process further
                    if (directNumId == 0)
                    {
                        // Explicitly disabled, skip numbering entirely
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
                // Fallback to style chain (only if no direct numPr)
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
                    string label = GetNumberingText(numberingPart, numId.Value, ilvl, numberingCounters);
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
            else if (elem is DocumentFormat.OpenXml.Math.OfficeMath om)
            {
                FlushText();
                var result = ExtractMathContent(om);
                if (!string.IsNullOrWhiteSpace(result))
                    regularItems.Add((new JObject { ["type"] = "math", ["text"] = result }, itemIndex++));
            }
            else if (elem is DocumentFormat.OpenXml.Math.Paragraph mathPara)
            {
                FlushText();
                foreach (var oMath in mathPara.Elements<DocumentFormat.OpenXml.Math.OfficeMath>())
                {
                    var result = ExtractMathContent(oMath);
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

    /// <summary>
    /// Comprehensive extraction of math content including all special elements
    /// </summary>
    public string ExtractMathContent(OpenXmlElement mathElement)
    {
        var sb = new StringBuilder();
        ExtractMathElementRecursive(mathElement, sb);
        return sb.ToString();
    }
    
    private void ExtractMathElementRecursive(OpenXmlElement element, StringBuilder sb)
    {
        // Process children in order
        foreach (var child in element.ChildElements)
        {
            // Math Text - the basic text content
            if (child is DocumentFormat.OpenXml.Math.Text t)
            {
                sb.Append(t.Text);
            }
            // Delimiter - handles (), [], {}, ||, etc.
            else if (child is DocumentFormat.OpenXml.Math.Delimiter d)
            {
                // Get delimiter properties
                var dPr = d.DelimiterProperties;
                string beginChar = dPr?.BeginChar?.Val?.Value ?? "(";
                string endChar = dPr?.EndChar?.Val?.Value ?? ")";
                
                sb.Append(beginChar);
                
                // Process delimiter elements (m:e inside delimiter)
                foreach (var dElem in d.Elements<DocumentFormat.OpenXml.Math.Base>())
                {
                    ExtractMathElementRecursive(dElem, sb);
                }
                
                sb.Append(endChar);
            }
            // Nary - summation, product, integral (∑, ∏, ∫)
            else if (child is DocumentFormat.OpenXml.Math.Nary nary)
            {
                var naryPr = nary.NaryProperties;
                string chr = naryPr?.AccentChar?.Val?.Value ?? "∑";
                sb.Append(chr);
                
                // Subscript (lower limit)
                var sub = nary.SubArgument;
                if (sub != null)
                {
                    sb.Append("_");
                    ExtractMathElementRecursive(sub, sb);
                }
                
                // Superscript (upper limit)  
                var sup = nary.SuperArgument;
                if (sup != null)
                {
                    sb.Append("^");
                    ExtractMathElementRecursive(sup, sb);
                }
                
                // Base/argument
                var baseElem = nary.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null)
                {
                    ExtractMathElementRecursive(baseElem, sb);
                }
            }
            // Radical (square root, nth root)
            else if (child is DocumentFormat.OpenXml.Math.Radical rad)
            {
                var radPr = rad.RadicalProperties;
                var hideDegreeVal = radPr?.HideDegree?.Val?.Value;
                bool hideDegree = hideDegreeVal == null || hideDegreeVal == DocumentFormat.OpenXml.Math.BooleanValues.True || hideDegreeVal == DocumentFormat.OpenXml.Math.BooleanValues.On;
                
                sb.Append("√");
                
                // Degree (for nth root)
                if (!hideDegree)
                {
                    var degree = rad.Degree;
                    if (degree != null)
                    {
                        sb.Append("[");
                        ExtractMathElementRecursive(degree, sb);
                        sb.Append("]");
                    }
                }
                
                sb.Append("(");
                var baseElem = rad.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null)
                {
                    ExtractMathElementRecursive(baseElem, sb);
                }
                sb.Append(")");
            }
            // Fraction
            else if (child is DocumentFormat.OpenXml.Math.Fraction frac)
            {
                sb.Append("(");
                var num = frac.Numerator;
                if (num != null) ExtractMathElementRecursive(num, sb);
                sb.Append("/");
                var den = frac.Denominator;
                if (den != null) ExtractMathElementRecursive(den, sb);
                sb.Append(")");
            }
            // Superscript
            else if (child is DocumentFormat.OpenXml.Math.Superscript sSup)
            {
                var baseElem = sSup.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("^");
                var supArg = sSup.SuperArgument;
                if (supArg != null) ExtractMathElementRecursive(supArg, sb);
            }
            // Subscript
            else if (child is DocumentFormat.OpenXml.Math.Subscript sSub)
            {
                var baseElem = sSub.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("_");
                var subArg = sSub.SubArgument;
                if (subArg != null) ExtractMathElementRecursive(subArg, sb);
            }
            // SubSuperscript (both sub and super)
            else if (child is DocumentFormat.OpenXml.Math.SubSuperscript sSubSup)
            {
                var baseElem = sSubSup.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("_");
                var subArg = sSubSup.SubArgument;
                if (subArg != null) ExtractMathElementRecursive(subArg, sb);
                sb.Append("^");
                var supArg = sSubSup.SuperArgument;
                if (supArg != null) ExtractMathElementRecursive(supArg, sb);
            }
            // Accent (overline, hat, arrow, etc.)
            else if (child is DocumentFormat.OpenXml.Math.Accent acc)
            {
                var accPr = acc.AccentProperties;
                string accChar = accPr?.AccentChar?.Val?.Value ?? "";
                
                var baseElem = acc.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                if (!string.IsNullOrEmpty(accChar)) sb.Append(accChar);
            }
            // Bar (overbar, underbar)
            else if (child is DocumentFormat.OpenXml.Math.Bar bar)
            {
                var baseElem = bar.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("̄"); // combining overline
            }
            // Function (sin, cos, lim, etc.)
            else if (child is DocumentFormat.OpenXml.Math.MathFunction func)
            {
                var funcName = func.FunctionName;
                if (funcName != null) ExtractMathElementRecursive(funcName, sb);
                
                var baseElem = func.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
            }
            // Limit Lower (e.g., lim with subscript)
            else if (child is DocumentFormat.OpenXml.Math.LimitLower limLow)
            {
                var baseElem = limLow.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("_");
                var limit = limLow.Limit;
                if (limit != null) ExtractMathElementRecursive(limit, sb);
            }
            // Limit Upper
            else if (child is DocumentFormat.OpenXml.Math.LimitUpper limUp)
            {
                var baseElem = limUp.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("^");
                var limit = limUp.Limit;
                if (limit != null) ExtractMathElementRecursive(limit, sb);
            }
            // Matrix
            else if (child is DocumentFormat.OpenXml.Math.Matrix matrix)
            {
                sb.Append("[");
                bool firstRow = true;
                foreach (var row in matrix.Elements<DocumentFormat.OpenXml.Math.MatrixRow>())
                {
                    if (!firstRow) sb.Append("; ");
                    firstRow = false;
                    
                    bool firstCol = true;
                    foreach (var col in row.Elements<DocumentFormat.OpenXml.Math.Base>())
                    {
                        if (!firstCol) sb.Append(", ");
                        firstCol = false;
                        ExtractMathElementRecursive(col, sb);
                    }
                }
                sb.Append("]");
            }
            // GroupChar (underbrace, overbrace, etc.)
            else if (child is DocumentFormat.OpenXml.Math.GroupChar grpChr)
            {
                var grpPr = grpChr.GroupCharProperties;
                string chr = grpPr?.AccentChar?.Val?.Value ?? "";
                
                var baseElem = grpChr.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                if (!string.IsNullOrEmpty(chr)) sb.Append(chr);
            }
            // Box, BorderBox, Equation Array - just recurse
            else if (child is DocumentFormat.OpenXml.Math.Box 
                  || child is DocumentFormat.OpenXml.Math.BorderBox
                  || child is DocumentFormat.OpenXml.Math.EquationArray
                  || child is DocumentFormat.OpenXml.Math.Phantom
                  || child is DocumentFormat.OpenXml.Math.Run
                  || child is DocumentFormat.OpenXml.Math.OfficeMath
                  || child is DocumentFormat.OpenXml.Math.Base
                  || child is DocumentFormat.OpenXml.Math.FunctionName
                  || child is DocumentFormat.OpenXml.Math.Numerator
                  || child is DocumentFormat.OpenXml.Math.Denominator
                  || child is DocumentFormat.OpenXml.Math.Degree
                  || child is DocumentFormat.OpenXml.Math.SubArgument
                  || child is DocumentFormat.OpenXml.Math.SuperArgument
                  || child is DocumentFormat.OpenXml.Math.Limit)
            {
                ExtractMathElementRecursive(child, sb);
            }
            // Skip properties and other non-content elements
            else if (child.LocalName.EndsWith("Pr") || child is DocumentFormat.OpenXml.Math.ControlProperties)
            {
                // Skip properties
            }
            else
            {
                // For unknown elements, try to recurse if they have children
                if (child.HasChildren)
                {
                    ExtractMathElementRecursive(child, sb);
                }
            }
        }
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
    // Note: counters are keyed by abstractNumId (not numId) because multiple numIds can share the same abstractNumId
    // When a numId has startOverride, it resets the counter for that abstractNumId
    public string GetNumberingText(NumberingDefinitionsPart numberingPart, int numId, int ilvl, Dictionary<int, Dictionary<int, int>> counters)
    {
        var numInstance = numberingPart.Numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        if (numInstance == null) return "?";
        
        int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
        
        var abstractNum = numberingPart.Numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
        if (abstractNum == null) return "?";
        
        var level = abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == ilvl);
        if (level == null) return "?";
        
        // Check for lvlOverride with startOverride in num instance
        // This allows restart of numbering at specific values
        int? startOverride = null;
        var lvlOverride = numInstance.Elements<LevelOverride>().FirstOrDefault(lo => lo.LevelIndex?.Value == ilvl);
        if (lvlOverride != null)
        {
            startOverride = lvlOverride.StartOverrideNumberingValue?.Val?.Value;
        }
        
        // Use abstractNumId as key (not numId) so that different numIds sharing same abstractNumId share counter
        if (!counters.ContainsKey(abstractNumId))
            counters[abstractNumId] = new Dictionary<int, int>();
        
        // Track applied startOverrides using negative numId as key in a special entry
        // Key -1 stores a set of applied numIds (encoded as bit positions would be complex, so we use negative keys)
        int appliedKey = -numId - 1; // Use negative numId as key to track if startOverride was applied
        bool startOverrideApplied = counters.ContainsKey(appliedKey);
        
        if (startOverride.HasValue && !startOverrideApplied)
        {
            // First time seeing this numId with startOverride - apply it and mark as applied
            counters[appliedKey] = new Dictionary<int, int>(); // Just mark that this numId's startOverride was applied
            counters[abstractNumId][ilvl] = startOverride.Value;
        }
        else if (!counters[abstractNumId].ContainsKey(ilvl))
        {
            // First time seeing this level for this abstractNumId (no startOverride or already applied)
            int start = level.StartNumberingValue?.Val ?? 1;
            counters[abstractNumId][ilvl] = start;
        }
        else
        {
            counters[abstractNumId][ilvl]++;
        }
        
        // Reset lower levels when we move to a higher level
        foreach (var key in counters[abstractNumId].Keys.ToList())
        {
            if (key > ilvl) counters[abstractNumId].Remove(key);
        }
        
        int currentVal = counters[abstractNumId][ilvl];
        
        string lvlText = level.LevelText?.Val?.Value ?? "";
        string numFmt = level.NumberingFormat?.Val?.ToString() ?? "decimal";
        
        if (numFmt == "bullet")
        {
            // Normalize bullet character - Word often uses Symbol/Wingdings font characters
            string normalizedBullet = NormalizeBulletChar(lvlText);
            return normalizedBullet.Length > 0 ? normalizedBullet : "•";
        }
        
        string formatted = lvlText;
        for (int i = 0; i <= ilvl; i++)
        {
            int cVal = counters[abstractNumId].ContainsKey(i) ? counters[abstractNumId][i] : (int)(abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == i)?.StartNumberingValue?.Val ?? 1);
             
            var subLevel = abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == i);
            string subFmt = subLevel?.NumberingFormat?.Val?.ToString() ?? "decimal";
             
            string subValStr = cVal.ToString();
            if (subFmt == "decimalZero") subValStr = cVal.ToString("D2"); // 01, 02, 03...
            else if (subFmt == "lowerLetter") subValStr = GetLetter(cVal, true);
            else if (subFmt == "upperLetter") subValStr = GetLetter(cVal, false);
            else if (subFmt == "lowerRoman") subValStr = ToRoman(cVal).ToLowerInvariant();
            else if (subFmt == "upperRoman") subValStr = ToRoman(cVal);
            else if (subFmt == "bullet") subValStr = NormalizeBulletChar(subLevel?.LevelText?.Val?.Value ?? "");

            formatted = formatted.Replace($"%{i + 1}", subValStr);
        }
        
        return formatted;
    }
    
    /// <summary>
    /// Normalize bullet characters from Symbol/Wingdings fonts to proper Unicode
    /// </summary>
    private string NormalizeBulletChar(string bulletChar)
    {
        if (string.IsNullOrEmpty(bulletChar)) return "•";
        
        // Symbol font character mappings (Private Use Area -> Unicode)
        // These are common bullet characters in Symbol font
        var symbolMappings = new Dictionary<char, char>
        {
            { '\uF0B7', '•' },  // Bullet
            { '\uF0A7', '•' },  // Bullet variant
            { '\uF076', '•' },  // Another bullet
            { '\uF0D8', '◆' },  // Diamond
            { '\uF0FC', '✓' },  // Check mark
            { '\uF0A8', '■' },  // Square bullet
            { '\uF0E0', '→' },  // Arrow
            { '\uF0E8', '⮕' },  // Arrow variant
        };
        
        // Wingdings font character mappings
        var wingdingsMappings = new Dictionary<char, char>
        {
            { '\u006C', '●' },  // Circle (Wingdings 'l')
            { '\u006E', '■' },  // Square (Wingdings 'n')
            { '\u0075', '◆' },  // Diamond (Wingdings 'u')
            { '\u00A8', '➢' },  // Arrow (Wingdings)
            { '\u00FC', '✓' },  // Check mark (Wingdings)
            { '\u0076', '✔' },  // Check (Wingdings 'v')
            { '\u00D8', '➔' },  // Arrow (Wingdings)
            { '\u0077', '✗' },  // Cross (Wingdings 'w')
        };
        
        // Check if it's a single character that needs mapping
        if (bulletChar.Length == 1)
        {
            char c = bulletChar[0];
            
            // Try Symbol font mapping first (characters in Private Use Area)
            if (symbolMappings.TryGetValue(c, out char symbol))
                return symbol.ToString();
                
            // Try Wingdings mapping
            if (wingdingsMappings.TryGetValue(c, out char wingding))
                return wingding.ToString();
                
            // Common standalone bullet characters that are fine as-is
            if (c == '•' || c == '●' || c == '○' || c == '■' || c == '□' || 
                c == '◆' || c == '◇' || c == '▪' || c == '▫' || c == '►' ||
                c == '➢' || c == '➤' || c == '✓' || c == '✔' || c == '✗' ||
                c == '-' || c == '–' || c == '—' || c == '*')
                return bulletChar;
                
            // If it's in Private Use Area but not mapped, use default bullet
            if (c >= '\uE000' && c <= '\uF8FF')
                return "•";
                
            // Check for common problematic characters that become '?' or '.'
            // These are usually font-encoded symbols
            if (c == '?' || c < 32)
                return "•";
        }
        
        // Return as-is if nothing matched
        return bulletChar;
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

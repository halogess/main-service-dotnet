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
    
    // Inline format extractors
    private readonly TextFormatExtractor _textFormatExtractor;
    private readonly DrawingFormatExtractor _drawingFormatExtractor;
    private readonly FieldFormatExtractor _fieldFormatExtractor;
    
    // Database context for saving formats (optional - set via SetDbContext)
    private KorektorBukuDbContext? _db;
    
    // Table extraction callback (to handle nested tables in textboxes)
    private Func<Table, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, Task<JObject>>? _tableExtractor;

    public ParagraphExtractor(ILogger logger, DrawingExtractor drawingExtractor)
    {
        _logger = logger;
        _drawingExtractor = drawingExtractor;
        _textFormatExtractor = new TextFormatExtractor();
        _drawingFormatExtractor = new DrawingFormatExtractor();
        _fieldFormatExtractor = new FieldFormatExtractor();
    }
    
    /// <summary>
    /// Sets the database context for saving inline format records
    /// </summary>
    public void SetDbContext(KorektorBukuDbContext? db)
    {
        _db = db;
    }
    
    /// <summary>
    /// Sets the StyleResolver for resolving numbering from style chain
    /// </summary>
    public void SetStyleResolver(StyleResolver? styleResolver)
    {
        _styleResolver = styleResolver;
    }
    
    /// <summary>
    /// Sets the table extraction callback for handling nested tables in textboxes
    /// </summary>
    public void SetTableExtractor(Func<Table, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, Task<JObject>>? tableExtractor)
    {
        _tableExtractor = tableExtractor;
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

        // Always yield a result, even for empty paragraphs, to maintain element count consistency
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
        
        // Capture paragraph properties for style resolution
        var paragraphProps = p.ParagraphProperties;
        
        // === RUN ACCUMULATION STATE (to combine consecutive runs with same format) ===
        string currentFormatSignature = "";
        EffectiveRunProperties? currentEffectiveProps = null;
        Run? currentRun = null;
        var accumulatedRunText = new System.Text.StringBuilder();
        
        void FlushAccumulatedRun()
        {
            if (accumulatedRunText.Length == 0) return;
            
            var textContent = accumulatedRunText.ToString();
            accumulatedRunText.Clear();
            
            // Save format record to database
            uint? textFormatId = null;
            if (_db != null && currentRun != null && _styleResolver != null)
            {
                var textFormat = _textFormatExtractor.ExtractEffectiveFormat(currentRun, _styleResolver, paragraphProps);
                _db.DokumenFormatTexts.Add(textFormat);
                _db.SaveChanges();
                textFormatId = textFormat.DftxId;
            }
            else if (_db != null && currentRun != null)
            {
                var textFormat = _textFormatExtractor.ExtractFormat(currentRun);
                _db.DokumenFormatTexts.Add(textFormat);
                _db.SaveChanges();
                textFormatId = textFormat.DftxId;
            }
            
            // Create JSON item
            if (textFormatId.HasValue)
            {
                regularItems.Add((new JObject { 
                    ["type"] = "text", 
                    ["dftx_id"] = textFormatId.Value,
                    ["value"] = textContent
                }, itemIndex++));
            }
            else
            {
                regularItems.Add((new JObject { ["type"] = "text", ["value"] = textContent }, itemIndex++));
            }
            
            // Reset state
            currentFormatSignature = "";
            currentEffectiveProps = null;
            currentRun = null;
        }
        
        // === FIELD TRACKING STATE (for complex fields spanning multiple runs) ===
        bool inComplexField = false;
        bool inFieldInstr = false;
        bool inFieldResult = false;
        var fieldInstrBuilder = new System.Text.StringBuilder();
        var fieldResultBuilder = new System.Text.StringBuilder();
        bool fieldIsLocked = false;
        bool fieldIsDirty = false;
        Run? firstResultRun = null; // To capture formatting of result
        
        void FlushComplexField()
        {
            if (!inComplexField) return;
            
            var instrText = fieldInstrBuilder.ToString().Trim();
            var resultText = fieldResultBuilder.ToString();
            
            // Save field format to database
            ulong? fieldFormatId = null;
            if (_db != null && !string.IsNullOrEmpty(instrText))
            {
                var fieldFormat = _fieldFormatExtractor.ExtractFormat(instrText, resultText, fieldIsLocked, fieldIsDirty);
                _db.DokumenFormatFields.Add(fieldFormat);
                _db.SaveChanges();
                fieldFormatId = fieldFormat.DffdId;
            }
            
            // Create field JSON item - format IDs first, then content
            FlushText();
            var fieldItem = new JObject
            {
                ["type"] = "field",
                ["field_type"] = _fieldFormatExtractor.DetectFieldType(instrText)
            };
            if (fieldFormatId.HasValue)
                fieldItem["dffd_id"] = fieldFormatId.Value;
            
            // Add result format ID if we captured first result run (save ALL, not just significant)
            if (firstResultRun != null && _db != null && _styleResolver != null)
            {
                var textFormat = _textFormatExtractor.ExtractEffectiveFormat(firstResultRun, _styleResolver, paragraphProps);
                _db.DokumenFormatTexts.Add(textFormat);
                _db.SaveChanges();
                fieldItem["result_dftx_id"] = textFormat.DftxId;
            }
            
            // Add value after IDs
            fieldItem["value"] = resultText;
            
            regularItems.Add((fieldItem, itemIndex++));
            
            // Reset state
            inComplexField = false;
            inFieldInstr = false;
            inFieldResult = false;
            fieldInstrBuilder.Clear();
            fieldResultBuilder.Clear();
            fieldIsLocked = false;
            fieldIsDirty = false;
            firstResultRun = null;
        }
        
        void ProcessElement(OpenXmlElement elem, bool skipTextBox = false)
        {
            var sb = helper.StringBuilder;
            
            if (elem is Run run)
            {
                // Get effective format properties
                EffectiveRunProperties? effective = null;
                string formatSignature = "";
                
                if (_styleResolver != null)
                {
                    effective = _styleResolver.GetEffectiveRunProperties(run, paragraphProps);
                    // Create format signature from key properties: font|size|bold|italic|underline
                    formatSignature = $"{effective.FontAscii ?? ""}|{effective.FontSize ?? 0}|{(effective.Bold == true ? "B" : "")}|{(effective.Italic == true ? "I" : "")}|{(effective.Underline == true ? "U" : "")}|{effective.UnderlineStyle ?? ""}";
                }
                
                var runText = new StringBuilder();

                void FlushRunTextSegment()
                {
                    if (runText.Length == 0) return;

                    var textContent = runText.ToString();
                    runText.Clear();

                    if (formatSignature == currentFormatSignature && !string.IsNullOrEmpty(currentFormatSignature))
                    {
                        accumulatedRunText.Append(textContent);
                    }
                    else
                    {
                        FlushAccumulatedRun();
                        currentFormatSignature = formatSignature;
                        currentEffectiveProps = effective;
                        currentRun = run;
                        accumulatedRunText.Append(textContent);
                    }
                }
                
                foreach (var child in run.ChildElements)
                {
                    if (child is FieldChar fldChar)
                    {
                        FlushRunTextSegment();
                        FlushAccumulatedRun();
                        FlushText();
                        
                        var fldType = fldChar.FieldCharType?.Value;
                        
                        if (fldType == FieldCharValues.Begin)
                        {
                            inComplexField = true;
                            inFieldInstr = true;
                            inFieldResult = false;
                            fieldIsLocked = fldChar.FieldLock?.Value ?? false;
                            fieldIsDirty = fldChar.Dirty?.Value ?? false;
                            fieldInstrBuilder.Clear();
                            fieldResultBuilder.Clear();
                            firstResultRun = null;
                        }
                        else if (fldType == FieldCharValues.Separate)
                        {
                            inFieldInstr = false;
                            inFieldResult = true;
                        }
                        else if (fldType == FieldCharValues.End)
                        {
                            FlushComplexField();
                        }
                        continue;
                    }

                    if (child is FieldCode fieldCode)
                    {
                        if (inFieldInstr)
                            fieldInstrBuilder.Append(fieldCode.Text);
                        continue;
                    }

                    if (child is Text t)
                    {
                        if (inFieldResult)
                        {
                            if (firstResultRun == null)
                                firstResultRun = run;
                            fieldResultBuilder.Append(t.Text);
                        }
                        else if (!inComplexField)
                        {
                            runText.Append(t.Text);
                        }
                        continue;
                    }

                    if (child is TabChar)
                    {
                        if (inFieldResult)
                        {
                            if (firstResultRun == null)
                                firstResultRun = run;
                            fieldResultBuilder.Append('\t');
                        }
                        else if (!inComplexField)
                        {
                            runText.Append('\t');
                        }
                        continue;
                    }

                    if (child is Break)
                    {
                        if (inFieldResult)
                        {
                            if (firstResultRun == null)
                                firstResultRun = run;
                            fieldResultBuilder.Append('\n');
                        }
                        else if (!inComplexField)
                        {
                            runText.Append('\n');
                        }
                        continue;
                    }

                    if (child is Drawing || child is Picture)
                    {
                        FlushRunTextSegment();
                        FlushAccumulatedRun();
                        ProcessElement(child, skipTextBox);
                        continue;
                    }

                    if (child is DocumentFormat.OpenXml.AlternateContent altContent)
                    {
                        FlushRunTextSegment();
                        FlushAccumulatedRun();
                        ProcessElement(child, skipTextBox);
                        continue;
                    }

                    if (!inComplexField)
                    {
                        FlushRunTextSegment();
                        ProcessElement(child, skipTextBox);
                    }
                }

                FlushRunTextSegment();
            }
            else if (elem is DocumentFormat.OpenXml.Math.Run mathRun)
            {
                foreach (var child in mathRun.ChildElements)
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
            // Handle mc:AlternateContent - extract content from mc:Choice
            else if (elem is DocumentFormat.OpenXml.AlternateContent altContent)
            {
                // Process the preferred Choice content (usually contains Drawing)
                var choice = altContent.GetFirstChild<DocumentFormat.OpenXml.AlternateContentChoice>();
                if (choice != null)
                {
                    foreach (var child in choice.ChildElements)
                        ProcessElement(child, skipTextBox);
                }
            }
            else if (elem is Drawing drawing)
            {
                FlushText();
                
                // Extract drawing format and save
                ulong? drawingFormatId = null;
                if (_db != null)
                {
                    var drawingFormat = _drawingFormatExtractor.ExtractFormat(drawing);
                    _db.DokumenFormatDrawings.Add(drawingFormat);
                    _db.SaveChanges(); // Sync save to get ID
                    drawingFormatId = drawingFormat.DfdrId;
                }
                
                var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
                var drawingItem = _drawingExtractor.ExtractDrawingContent(drawing, ExtractTextBoxAsItems, numberingPart, numberingCounters);
                
                if (drawingItem != null)
                {
                    // Reconstruct JObject so format ID comes right after type
                    if (drawingFormatId.HasValue)
                    {
                        var reordered = new JObject { ["type"] = drawingItem["type"], ["dfdr_id"] = drawingFormatId.Value };
                        foreach (var prop in drawingItem.Properties())
                        {
                            if (prop.Name != "type") // type already added
                                reordered[prop.Name] = prop.Value;
                        }
                        drawingItem = reordered;
                    }
                    
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
                
                // Extract VML picture format and save
                ulong? pictFormatId = null;
                if (_db != null)
                {
                    var pictFormat = _drawingFormatExtractor.ExtractVmlFormat(pict);
                    _db.DokumenFormatDrawings.Add(pictFormat);
                    _db.SaveChanges();
                    pictFormatId = pictFormat.DfdrId;
                }
                
                var pictItem = _drawingExtractor.ExtractVmlPicture(pict);
                if (pictItem != null)
                {
                    // Reconstruct JObject so format ID comes right after type
                    if (pictFormatId.HasValue)
                    {
                        var reordered = new JObject { ["type"] = pictItem["type"], ["dfdr_id"] = pictFormatId.Value };
                        foreach (var prop in pictItem.Properties())
                        {
                            if (prop.Name != "type")
                                reordered[prop.Name] = prop.Value;
                        }
                        pictItem = reordered;
                    }
                    regularItems.Add((pictItem, itemIndex++));
                }
            }
            else if (elem is TextBoxContent txbx && !skipTextBox)
            {
                FlushText();
                var shapeData = ExtractTextBoxAsShape(txbx, elem.Parent, numberingPart, numberingCounters);
                if (shapeData != null)
                    regularItems.Add((shapeData, itemIndex++));
            }
            // === SIMPLE FIELD (w:fldSimple) ===
            else if (elem is SimpleField simpleField)
            {
                FlushText();
                
                var instrText = simpleField.Instruction?.Value ?? "";
                var resultText = simpleField.InnerText;
                
                // Save field format
                ulong? fieldFormatId = null;
                if (_db != null && !string.IsNullOrEmpty(instrText))
                {
                    var fieldFormat = _fieldFormatExtractor.ExtractFromSimpleField(simpleField);
                    _db.DokumenFormatFields.Add(fieldFormat);
                    _db.SaveChanges();
                    fieldFormatId = fieldFormat.DffdId;
                }
                
                // Create field JSON item - format IDs first
                var fieldItem = new JObject
                {
                    ["type"] = "field",
                    ["field_type"] = _fieldFormatExtractor.DetectFieldType(instrText)
                };
                if (fieldFormatId.HasValue)
                    fieldItem["dffd_id"] = fieldFormatId.Value;
                
                // Get result run format (save ALL, not just significant)
                var resultRun = simpleField.Descendants<Run>().FirstOrDefault();
                if (resultRun != null && _db != null && _styleResolver != null)
                {
                    var textFormat = _textFormatExtractor.ExtractEffectiveFormat(resultRun, _styleResolver, paragraphProps);
                    _db.DokumenFormatTexts.Add(textFormat);
                    _db.SaveChanges();
                    fieldItem["result_dftx_id"] = textFormat.DftxId;
                }
                
                // Add value after IDs
                fieldItem["value"] = resultText;
                
                regularItems.Add((fieldItem, itemIndex++));
            }
            // === FIELD CHAR (complex field markers) ===
            else if (elem is FieldChar fieldChar)
            {
                var fldType = fieldChar.FieldCharType?.Value;
                
                if (fldType == FieldCharValues.Begin)
                {
                    // Start of complex field
                    FlushText();
                    inComplexField = true;
                    inFieldInstr = true;
                    inFieldResult = false;
                    fieldIsLocked = fieldChar.FieldLock?.Value ?? false;
                    fieldIsDirty = fieldChar.Dirty?.Value ?? false;
                    fieldInstrBuilder.Clear();
                    fieldResultBuilder.Clear();
                    firstResultRun = null;
                }
                else if (fldType == FieldCharValues.Separate)
                {
                    // Switch from instruction to result
                    inFieldInstr = false;
                    inFieldResult = true;
                }
                else if (fldType == FieldCharValues.End)
                {
                    // End of complex field - flush it
                    FlushComplexField();
                }
            }
            // === FIELD CODE (w:instrText) - instruction text inside complex field ===
            else if (elem is FieldCode fieldCode && inFieldInstr)
            {
                fieldInstrBuilder.Append(fieldCode.Text);
            }
            else if (elem is not TextBoxContent)
            {
                // Handle text inside complex field result
                if (inFieldResult && elem is Run resultRun)
                {
                    // Capture first result run for formatting
                    if (firstResultRun == null)
                        firstResultRun = resultRun;
                    
                    // Collect result text
                    foreach (var child in resultRun.ChildElements)
                    {
                        if (child is Text txt)
                            fieldResultBuilder.Append(txt.Text);
                    }
                }
                else if (!inComplexField)
                {
                    // Normal processing when not in a complex field
                    foreach (var child in elem.ChildElements)
                        ProcessElement(child, skipTextBox);
                }
            }
        }

        foreach (var child in p.ChildElements)
            ProcessElement(child);

        FlushAccumulatedRun();  // Flush any remaining accumulated run text
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
                // Use table extraction callback if available
                if (_tableExtractor != null)
                {
                    try
                    {
                        var tableJson = _tableExtractor(table, numberingPart, numberingCounters).GetAwaiter().GetResult();
                        tableJson["type"] = "table";
                        items.Add(tableJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to extract nested table in textbox");
                        items.Add(new JObject { ["type"] = "table", ["error"] = "extraction_failed" });
                    }
                }
                else
                {
                    // Fallback to placeholder if no callback
                    items.Add(new JObject { ["type"] = "table" });
                }
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

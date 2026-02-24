using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
    private ThemeFontResolver? _themeFontResolver;
    private ThemeFontLangResolver? _themeFontLangResolver;
    
    // Table extraction callback (to handle nested tables in textboxes)
    private Func<Table, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, Task<JObject>>? _tableExtractor;

    private sealed class FieldContext
    {
        public StringBuilder InstrBuilder { get; } = new StringBuilder();
        public StringBuilder ResultBuilder { get; } = new StringBuilder();
        public bool InInstruction { get; set; } = true;
        public bool InResult { get; set; }
        public bool IsLocked { get; set; }
        public bool IsDirty { get; set; }
        public Run? FirstResultRun { get; set; }
    }

    private sealed class NumberingContinuationState
    {
        public string? StyleId { get; set; }
        public int? NumId { get; set; }
        public int Ilvl { get; set; }
        public int? AbstractNumId { get; set; }
        public bool FromDirectOrContinuation { get; set; }
    }

    private readonly NumberingContinuationState _numberingContinuation = new();

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
    /// Sets the ThemeFontLangResolver for script-aware font selection
    /// </summary>
    public void SetThemeFontLangResolver(ThemeFontLangResolver? themeFontLangResolver)
    {
        _themeFontLangResolver = themeFontLangResolver;
    }

    /// <summary>
    /// Sets the ThemeFontResolver for theme font resolution
    /// </summary>
    public void SetThemeFontResolver(ThemeFontResolver? themeFontResolver)
    {
        _themeFontResolver = themeFontResolver;
    }
    
    /// <summary>
    /// Sets the table extraction callback for handling nested tables in textboxes
    /// </summary>
    public void SetTableExtractor(Func<Table, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, Task<JObject>>? tableExtractor)
    {
        _tableExtractor = tableExtractor;
    }

    /// <summary>
    /// Resets state used to continue restarted list numbering across paragraphs.
    /// Call this when entering a new extraction flow/part.
    /// </summary>
    public void ResetNumberingState()
    {
        ClearNumberingContinuationState();
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

        if (_styleResolver != null)
        {
            var (numId, ilvl) = _styleResolver.GetEffectiveNumberingProperties(p);
            if (numId.HasValue && numId.Value > 0)
                return $"list-item-{numId.Value}-{ilvl}";
        }

        if (pPr.NumberingProperties?.NumberingId?.Val?.Value != null &&
            pPr.NumberingProperties.NumberingId.Val.Value > 0)
        {
            var numId = pPr.NumberingProperties.NumberingId.Val.Value;
            var ilvl = pPr.NumberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
            return $"list-item-{numId}-{ilvl}";
        }

        return "paragraph";
    }

    private void ClearNumberingContinuationState()
    {
        _numberingContinuation.StyleId = null;
        _numberingContinuation.NumId = null;
        _numberingContinuation.Ilvl = 0;
        _numberingContinuation.AbstractNumId = null;
        _numberingContinuation.FromDirectOrContinuation = false;
    }

    private bool TryResolveContinuationNumId(
        NumberingDefinitionsPart numberingPart,
        string? styleId,
        int styleNumId,
        int ilvl,
        out int continuedNumId)
    {
        continuedNumId = 0;

        if (string.IsNullOrWhiteSpace(styleId))
            return false;

        if (!_numberingContinuation.FromDirectOrContinuation ||
            !_numberingContinuation.NumId.HasValue ||
            _numberingContinuation.NumId.Value <= 0)
            return false;

        if (!string.Equals(_numberingContinuation.StyleId, styleId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_numberingContinuation.Ilvl != ilvl)
            return false;

        if (_numberingContinuation.NumId.Value == styleNumId)
            return false;

        var previousAbstractId = _numberingContinuation.AbstractNumId
            ?? NumberingResolver.GetAbstractNumberId(numberingPart, _numberingContinuation.NumId.Value);
        var styleAbstractId = NumberingResolver.GetAbstractNumberId(numberingPart, styleNumId);

        if (!previousAbstractId.HasValue || !styleAbstractId.HasValue)
            return false;

        if (previousAbstractId.Value != styleAbstractId.Value)
            return false;

        continuedNumId = _numberingContinuation.NumId.Value;
        return true;
    }

    private void UpdateNumberingContinuationState(
        NumberingDefinitionsPart numberingPart,
        string? styleId,
        int numId,
        int ilvl,
        bool fromDirectOrContinuation)
    {
        _numberingContinuation.StyleId = styleId;
        _numberingContinuation.NumId = numId;
        _numberingContinuation.Ilvl = ilvl;
        _numberingContinuation.AbstractNumId = NumberingResolver.GetAbstractNumberId(numberingPart, numId);
        _numberingContinuation.FromDirectOrContinuation = fromDirectOrContinuation;
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

    private static bool IsCaptionParagraphStyle(string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
            return false;

        return styleId.Equals("STTSGambar", StringComparison.OrdinalIgnoreCase) ||
               styleId.Equals("STTSTabel", StringComparison.OrdinalIgnoreCase) ||
               styleId.Equals("Caption", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCaptionSequenceInstruction(string? instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
            return false;

        return Regex.IsMatch(
            instruction,
            "\\bSEQ\\s+(Gambar|Tabel)(?:[_\\w\\.-]*)?\\b",
            RegexOptions.IgnoreCase);
    }

    private static bool HasCaptionSequenceField(Paragraph paragraph)
    {
        foreach (var simpleField in paragraph.Descendants<SimpleField>())
        {
            if (IsCaptionSequenceInstruction(simpleField.Instruction?.Value))
                return true;
        }

        foreach (var fieldCode in paragraph.Descendants<FieldCode>())
        {
            if (IsCaptionSequenceInstruction(fieldCode.Text))
                return true;
        }

        return false;
    }

    private static bool IsCaptionParagraph(Paragraph paragraph, string? styleId)
    {
        if (IsCaptionParagraphStyle(styleId))
            return true;

        return HasCaptionSequenceField(paragraph);
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
        var paragraphStyleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var paragraphPlainText = Regex.Replace(p.InnerText ?? string.Empty, "\\s+", " ").Trim();
        var isCaptionParagraph = IsCaptionParagraph(p, paragraphStyleId);
        
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
                        if (TryResolveContinuationNumId(numberingPart, paragraphStyleId, styleNumId.Value, styleIlvl, out var continuedNumId))
                        {
                            numId = continuedNumId;
                            source = "continuation";
                        }
                        else
                        {
                            numId = styleNumId;
                            source = "style";
                        }
                        ilvl = styleIlvl;
                    }
                }
                
                if (numId != null && numId.Value > 0)
                {
                    string label = NumberingExtractor.GetNumberingText(numberingPart, numId.Value, ilvl, numberingCounters);
                    if (!string.IsNullOrEmpty(label))
                        numberingText = label;

                    UpdateNumberingContinuationState(
                        numberingPart,
                        paragraphStyleId,
                        numId.Value,
                        ilvl,
                        source == "direct" || source == "continuation");

                    _logger.LogInformation("Numbering: source={Source}, numId={NumId}, ilvl={Ilvl}, label={Label}", 
                        source, numId, ilvl, label);
                }
                else
                {
                    // XML-driven behavior:
                    // - Paragraph without numbering (source=none) is neutral and should not
                    //   reset continuation candidate by itself.
                    // - Explicit numId=0 on empty placeholder paragraph is also treated
                    //   as neutral (common in generated docs).
                    var preserveContinuation =
                        source == "none" ||
                        (source == "disabled" && string.IsNullOrWhiteSpace(paragraphPlainText));

                    if (!preserveContinuation)
                        ClearNumberingContinuationState();
                }
            }
            catch (Exception ex)
            {
                ClearNumberingContinuationState();
                _logger.LogWarning(ex, "Failed to resolve numbering for paragraph");
            }
        }
        else
        {
            ClearNumberingContinuationState();
        }
        
        if (!string.IsNullOrEmpty(numberingText) && !isCaptionParagraph)
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
                var textFormat = _textFormatExtractor.ExtractEffectiveFormat(
                    currentRun, _styleResolver, paragraphProps, _themeFontLangResolver, _themeFontResolver);
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
        
        // === FIELD TRACKING STATE (supports nested fields) ===
        var fieldStack = new Stack<FieldContext>();

        FieldContext? CurrentField()
        {
            return fieldStack.Count > 0 ? fieldStack.Peek() : null;
        }

        void CloseField(FieldContext context)
        {
            var instrTextRaw = context.InstrBuilder.ToString();
            var resultText = context.ResultBuilder.ToString();

            if (fieldStack.Count > 0)
            {
                var parent = fieldStack.Peek();
                if (parent.InResult)
                    parent.ResultBuilder.Append(resultText);
                else if (parent.InInstruction)
                    parent.InstrBuilder.Append(instrTextRaw);
                return;
            }

            // Save field format to database (top-level only)
            ulong? fieldFormatId = null;
            if (_db != null && !string.IsNullOrEmpty(instrTextRaw))
            {
                var fieldFormat = _fieldFormatExtractor.ExtractFormat(instrTextRaw, resultText, context.IsLocked, context.IsDirty);
                _db.DokumenFormatFields.Add(fieldFormat);
                _db.SaveChanges();
                fieldFormatId = fieldFormat.DffdId;
            }

            // Create field JSON item - format IDs first, then content
            FlushText();
            var fieldItem = new JObject
            {
                ["type"] = "field",
                ["field_type"] = _fieldFormatExtractor.DetectFieldType(instrTextRaw)
            };
            if (fieldFormatId.HasValue)
                fieldItem["dffd_id"] = fieldFormatId.Value;

            // Add result format ID if we captured first result run (save ALL, not just significant)
            if (context.FirstResultRun != null && _db != null && _styleResolver != null)
            {
                var textFormat = _textFormatExtractor.ExtractEffectiveFormat(
                    context.FirstResultRun, _styleResolver, paragraphProps, _themeFontLangResolver, _themeFontResolver);
                _db.DokumenFormatTexts.Add(textFormat);
                _db.SaveChanges();
                fieldItem["result_dftx_id"] = textFormat.DftxId;
            }

            // Add value after IDs
            fieldItem["value"] = resultText;

            regularItems.Add((fieldItem, itemIndex++));
        }

        void StartField(FieldChar fldChar)
        {
            var ctx = new FieldContext
            {
                IsLocked = fldChar.FieldLock?.Value ?? false,
                IsDirty = fldChar.Dirty?.Value ?? false
            };
            fieldStack.Push(ctx);
        }

        void SeparateField()
        {
            var current = CurrentField();
            if (current == null)
                return;

            current.InInstruction = false;
            current.InResult = true;
        }

        void EndField()
        {
            if (fieldStack.Count == 0)
                return;

            var ctx = fieldStack.Pop();
            CloseField(ctx);
        }

        void AppendFieldInstruction(string text)
        {
            var current = CurrentField();
            if (current?.InInstruction == true)
                current.InstrBuilder.Append(text);
        }

        void AppendFieldResult(string text, Run? run)
        {
            var current = CurrentField();
            if (current?.InResult != true)
                return;

            if (current.FirstResultRun == null && run != null)
                current.FirstResultRun = run;
            current.ResultBuilder.Append(text);
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
                    var fontKey = TextFormatExtractor.ResolvePreferredFont(
                        effective, run, _themeFontLangResolver, _themeFontResolver) ?? "";
                    var sizeKey = TextFormatExtractor.ResolvePreferredFontSize(effective, run, _themeFontLangResolver) ?? 0;
                    formatSignature = $"{fontKey}|{sizeKey}|{(effective.Bold == true ? "B" : "")}|{(effective.Italic == true ? "I" : "")}|{(effective.Underline == true ? "U" : "")}|{effective.UnderlineStyle ?? ""}";
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
                            StartField(fldChar);
                        }
                        else if (fldType == FieldCharValues.Separate)
                        {
                            SeparateField();
                        }
                        else if (fldType == FieldCharValues.End)
                        {
                            EndField();
                        }
                        continue;
                    }

                    if (child is FieldCode fieldCode)
                    {
                        AppendFieldInstruction(fieldCode.Text);
                        continue;
                    }

                    if (child is Text t)
                    {
                        if (fieldStack.Count > 0)
                            AppendFieldResult(t.Text, run);
                        else
                            runText.Append(t.Text);
                        continue;
                    }

                    if (child is TabChar)
                    {
                        if (fieldStack.Count > 0)
                            AppendFieldResult("\t", run);
                        else
                            runText.Append('\t');
                        continue;
                    }

                    if (child is Break)
                    {
                        if (fieldStack.Count > 0)
                            AppendFieldResult("\n", run);
                        else
                            runText.Append('\n');
                        continue;
                    }

                    if (child is Drawing || child is Picture)
                    {
                        FlushRunTextSegment();
                        FlushAccumulatedRun();
                        if (fieldStack.Count == 0)
                            ProcessElement(child, skipTextBox);
                        continue;
                    }

                    if (child is DocumentFormat.OpenXml.AlternateContent altContent)
                    {
                        FlushRunTextSegment();
                        FlushAccumulatedRun();
                        if (fieldStack.Count == 0)
                            ProcessElement(child, skipTextBox);
                        continue;
                    }

                    if (fieldStack.Count == 0)
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
                if (fieldStack.Count > 0)
                    return;

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
                if (fieldStack.Count > 0)
                    return;

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
                if (fieldStack.Count > 0)
                    return;

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

                if (fieldStack.Count > 0)
                {
                    var fieldResultRun = simpleField.Descendants<Run>().FirstOrDefault();
                    AppendFieldResult(resultText, fieldResultRun);
                    return;
                }
                
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
                    var textFormat = _textFormatExtractor.ExtractEffectiveFormat(
                        resultRun, _styleResolver, paragraphProps, _themeFontLangResolver, _themeFontResolver);
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
                    StartField(fieldChar);
                }
                else if (fldType == FieldCharValues.Separate)
                {
                    // Switch from instruction to result
                    SeparateField();
                }
                else if (fldType == FieldCharValues.End)
                {
                    // End of complex field - flush it
                    EndField();
                }
            }
            // === FIELD CODE (w:instrText) - instruction text inside complex field ===
            else if (elem is FieldCode fieldCode)
            {
                AppendFieldInstruction(fieldCode.Text);
            }
            else if (elem is not TextBoxContent)
            {
                if (fieldStack.Count == 0)
                {
                    // Normal processing when not in a complex field
                    foreach (var child in elem.ChildElements)
                        ProcessElement(child, skipTextBox);
                }
            }
        }

        foreach (var child in p.ChildElements)
            ProcessElement(child);

        while (fieldStack.Count > 0)
            CloseField(fieldStack.Pop());

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

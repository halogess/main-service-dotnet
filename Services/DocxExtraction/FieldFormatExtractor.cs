using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Models;
using System.Text;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts field formatting properties from OpenXML complex fields (w:fldChar, w:instrText).
/// </summary>
public class FieldFormatExtractor
{
    // Common field type mappings
    private static readonly Dictionary<string, string> FieldTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        { "PAGE", "PAGE" },
        { "NUMPAGES", "NUMPAGES" },
        { "SECTION", "SECTION" },
        { "SECTIONPAGES", "SECTIONPAGES" },
        { "SEQ", "SEQ" },
        { "REF", "REF" },
        { "PAGEREF", "PAGEREF" },
        { "HYPERLINK", "HYPERLINK" },
        { "TOC", "TOC" },
        { "DATE", "DATE" },
        { "TIME", "TIME" }
    };
    
    /// <summary>
    /// Extract field properties from instruction text and result.
    /// </summary>
    public DokumenFormatField ExtractFormat(string instrText, string? resultText = null, bool isLocked = false, bool isDirty = false)
    {
        var format = new DokumenFormatField
        {
            DffdInstrText = instrText,
            DffdResultText = resultText,
            DffdIsLocked = isLocked,
            DffdIsDirty = isDirty,
            DffdFieldType = DetectFieldType(instrText)
        };
        
        return format;
    }
    
    /// <summary>
    /// Extract field from a SimpleField element (w:fldSimple)
    /// </summary>
    public DokumenFormatField ExtractFromSimpleField(SimpleField simpleField)
    {
        var instrText = simpleField.Instruction?.Value ?? "";
        var resultText = simpleField.InnerText;
        
        return ExtractFormat(instrText, resultText);
    }
    
    /// <summary>
    /// Detect field type from instruction text
    /// </summary>
    public string DetectFieldType(string instrText)
    {
        if (string.IsNullOrWhiteSpace(instrText))
            return "UNKNOWN";
        
        // Trim and get first word (field type)
        var trimmed = instrText.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var fieldName = (firstSpace > 0 ? trimmed.Substring(0, firstSpace) : trimmed).ToUpperInvariant();
        
        // Check if it's a known field type
        if (FieldTypeMappings.TryGetValue(fieldName, out var mappedType))
            return mappedType;
        
        return "UNKNOWN";
    }
    
    /// <summary>
    /// Collects field instruction text from a sequence of runs between fldChar begin and end.
    /// Returns instruction text, result text, locked status, and dirty status.
    /// </summary>
    public (string instrText, string resultText, bool isLocked, bool isDirty) CollectFieldParts(
        IEnumerable<Run> runs, 
        ref int currentIndex)
    {
        var instrBuilder = new StringBuilder();
        var resultBuilder = new StringBuilder();
        bool inInstr = false;
        bool inResult = false;
        bool isLocked = false;
        bool isDirty = false;
        
        var runList = runs.ToList();
        
        for (int i = currentIndex; i < runList.Count; i++)
        {
            var run = runList[i];
            
            foreach (var child in run.ChildElements)
            {
                if (child is FieldChar fldChar)
                {
                    var fldType = fldChar.FieldCharType?.Value;
                    
                    if (fldType == FieldCharValues.Begin)
                    {
                        inInstr = true;
                        isLocked = fldChar.FieldLock?.Value ?? false;
                        isDirty = fldChar.Dirty?.Value ?? false;
                    }
                    else if (fldType == FieldCharValues.Separate)
                    {
                        inInstr = false;
                        inResult = true;
                    }
                    else if (fldType == FieldCharValues.End)
                    {
                        currentIndex = i + 1;
                        return (instrBuilder.ToString(), resultBuilder.ToString(), isLocked, isDirty);
                    }
                }
                else if (child is FieldCode instrText && inInstr)
                {
                    instrBuilder.Append(instrText.Text);
                }
                else if (child is Text text && inResult)
                {
                    resultBuilder.Append(text.Text);
                }
            }
        }
        
        currentIndex = runList.Count;
        return (instrBuilder.ToString(), resultBuilder.ToString(), isLocked, isDirty);
    }
}

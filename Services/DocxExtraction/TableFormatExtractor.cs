using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts table formatting properties from OpenXML w:tblPr into DokumenFormatTable.
/// Uses TableStyleResolver to get effective properties after style inheritance.
/// </summary>
public class TableFormatExtractor
{
    private readonly TableStyleResolver? _styleResolver;
    
    public TableFormatExtractor(TableStyleResolver? styleResolver = null)
    {
        _styleResolver = styleResolver;
    }
    
    /// <summary>
    /// Extract table properties from a Table element.
    /// </summary>
    public DokumenFormatTable ExtractFormat(Table table)
    {
        var format = new DokumenFormatTable();
        
        // Get effective (resolved) table properties if resolver is available
        var effectiveTblPr = _styleResolver?.ResolveEffectiveTableProperties(table) 
                             ?? table.GetFirstChild<TableProperties>();
        
        var directTblPr = table.GetFirstChild<TableProperties>();

        if (effectiveTblPr == null)
            return format;
        
        // Style ID (from original, not effective)
        format.DftTblStyleId = directTblPr?.TableStyle?.Val?.Value;
        
        // Table Width (w:tblW)
        var tblW = effectiveTblPr.TableWidth;
        if (tblW != null)
        {
            if (tblW.Type?.HasValue == true)
                format.DftTblWType = ConvertTableWidthType(tblW.Type.Value);
            
            if (tblW.Width?.HasValue == true)
            {
                // TableWidth.Width is StringValue, need to parse
                if (int.TryParse(tblW.Width.Value?.ToString() ?? "", out int widthValue))
                {
                    // Store based on type
                    if (format.DftTblWType == "pct")
                        format.DftTblWPct50 = widthValue >= 0 ? (uint)widthValue : null;
                    else if (format.DftTblWType == "dxa")
                        format.DftTblWTwips = widthValue >= 0 ? (uint)widthValue : null;
                }
            }
        }
        
        // Table Justification/Alignment (w:jc)
        if (effectiveTblPr.TableJustification?.Val?.HasValue == true)
            format.DftJc = ConvertTableJustification(effectiveTblPr.TableJustification.Val.Value);
        
        // Table Indentation (w:tblInd)
        var tblInd = effectiveTblPr.TableIndentation;
        if (tblInd != null)
        {
            if (tblInd.Type?.HasValue == true)
                format.DftTblIndType = ConvertTableIndentationType(tblInd.Type.Value);
            
            if (tblInd.Width?.HasValue == true)
            {
                // TableIndentation.Width is StringValue, need to parse
                if (int.TryParse(tblInd.Width.Value.ToString(), out int indentValue))
                {
                    // Store based on type
                    if (format.DftTblIndType == "dxa")
                        format.DftTblIndTwips = indentValue; // Signed integer (can be negative)
                    // Note: pct is ignored per OpenXML spec, but we store it anyway if present
                    else if (tblInd.Type?.Value == TableWidthUnitValues.Pct)
                        format.DftTblIndPct50 = indentValue >= 0 ? (uint)indentValue : null;
                }
            }
        }
        
        // Table Layout (w:tblLayout)
        if (effectiveTblPr.TableLayout?.Type?.HasValue == true)
            format.DftTblLayoutType = ConvertTableLayout(effectiveTblPr.TableLayout.Type.Value);
        
        ApplyFallbackDefaults(format);
        
        return format;
    }
    
    /// <summary>
    /// Convert OpenXML TableWidthUnitValues to database enum.
    /// DB expects: 'auto', 'dxa', 'pct', 'nil'
    /// </summary>
    private static string ConvertTableWidthType(TableWidthUnitValues value)
    {
        if (value == TableWidthUnitValues.Auto) return "auto";
        if (value == TableWidthUnitValues.Dxa) return "dxa";
        if (value == TableWidthUnitValues.Pct) return "pct";
        if (value == TableWidthUnitValues.Nil) return "nil";
        
        // Default fallback
        return "auto";
    }
    
    /// <summary>
    /// Convert OpenXML TableWidthUnitValues for indentation to database enum.
    /// DB expects: 'dxa', 'nil'
    /// Note: Per OpenXML spec, only 'dxa' and 'nil' are valid for table indentation.
    /// </summary>
    private static string ConvertTableIndentationType(TableWidthUnitValues value)
    {
        if (value == TableWidthUnitValues.Dxa) return "dxa";
        if (value == TableWidthUnitValues.Nil) return "nil";
        
        // Default fallback (pct and auto are ignored per spec)
        return "dxa";
    }
    
    /// <summary>
    /// Convert OpenXML TableRowAlignmentValues to database enum.
    /// DB expects: 'left', 'center', 'right', 'start', 'end'
    /// Note: TableRowAlignmentValues only has Left, Center, Right (no Start/End)
    /// </summary>
    private static string ConvertTableJustification(TableRowAlignmentValues value)
    {
        if (value == TableRowAlignmentValues.Left) return "left";
        if (value == TableRowAlignmentValues.Center) return "center";
        if (value == TableRowAlignmentValues.Right) return "right";
        
        // Default fallback
        return "left";
    }
    
    /// <summary>
    /// Convert OpenXML TableLayoutValues to database enum.
    /// DB expects: 'fixed', 'autofit'
    /// </summary>
    private static string ConvertTableLayout(TableLayoutValues value)
    {
        if (value == TableLayoutValues.Fixed) return "fixed";
        if (value == TableLayoutValues.Autofit) return "autofit";
        
        // Default fallback
        return "autofit";
    }
    
    private static void ApplyFallbackDefaults(DokumenFormatTable format)
    {
        if (string.IsNullOrWhiteSpace(format.DftTblWType))
            format.DftTblWType = "auto";

        if (string.IsNullOrWhiteSpace(format.DftTblLayoutType))
            format.DftTblLayoutType = "autofit";

        if (string.IsNullOrWhiteSpace(format.DftJc))
            format.DftJc = "left";

        if (string.IsNullOrWhiteSpace(format.DftTblIndType) &&
            !format.DftTblIndTwips.HasValue &&
            !format.DftTblIndPct50.HasValue)
        {
            format.DftTblIndType = "dxa";
            format.DftTblIndTwips = 0;
        }
    }
}

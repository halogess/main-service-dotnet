using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts table cell formatting properties from OpenXML w:tcPr into DokumenFormatTableCell.
/// Uses TableStyleResolver to get effective properties after style inheritance and conditional formatting.
/// </summary>
public class TableCellFormatExtractor
{
    private readonly TableStyleResolver? _styleResolver;
    
    public TableCellFormatExtractor(TableStyleResolver? styleResolver = null)
    {
        _styleResolver = styleResolver;
    }
    
    /// <summary>
    /// Extract cell properties from a TableCell element.
    /// Requires position context for conditional style resolution.
    /// </summary>
    public DokumenFormatTableCell ExtractFormat(
        TableCell cell,
        int rowIndex = 0,
        int colIndex = 0,
        int totalRows = 1,
        int totalCols = 1,
        Table? table = null)
    {
        var format = new DokumenFormatTableCell();
        
        // Get effective (resolved) cell properties if resolver and table are available
        var effectiveTcPr = (_styleResolver != null && table != null)
            ? _styleResolver.ResolveEffectiveCellProperties(cell, rowIndex, colIndex, totalRows, totalCols, table)
            : cell.GetFirstChild<TableCellProperties>();
        
        // Store raw XML for debugging (original direct formatting)
        var directTcPr = cell.GetFirstChild<TableCellProperties>();
        if (directTcPr != null)
            format.DftcRawTcprXml = directTcPr.OuterXml;
        
        if (effectiveTcPr == null)
            return format;
        
        // Grid Span (w:gridSpan)
        if (effectiveTcPr.GridSpan?.Val?.HasValue == true)
        {
            var gridSpanValue = effectiveTcPr.GridSpan.Val.Value;
            if (gridSpanValue > 0)
                format.DftcGridSpan = (ushort)gridSpanValue;
        }
        
        // Vertical Merge (w:vMerge)
        var vMerge = effectiveTcPr.VerticalMerge;
        if (vMerge != null)
        {
            // If w:vMerge exists without @w:val, it means "continue"
            if (vMerge.Val?.HasValue == true)
                format.DftcVMerge = ConvertVerticalMerge(vMerge.Val.Value);
            else
                format.DftcVMerge = "continue"; // Default when w:vMerge is present without value
        }
        
        // Vertical Alignment (w:vAlign)
        if (effectiveTcPr.TableCellVerticalAlignment?.Val?.HasValue == true)
            format.DftcVAlign = ConvertVerticalAlignment(effectiveTcPr.TableCellVerticalAlignment.Val.Value);
        
        return format;
    }
    
    /// <summary>
    /// Convert OpenXML MergedCellValues to database enum.
    /// DB expects: 'restart', 'continue'
    /// </summary>
    private static string ConvertVerticalMerge(MergedCellValues value)
    {
        if (value == MergedCellValues.Restart) return "restart";
        if (value == MergedCellValues.Continue) return "continue";
        
        // Default fallback
        return "continue";
    }
    
    /// <summary>
    /// Convert OpenXML TableVerticalAlignmentValues to database enum.
    /// DB expects: 'top', 'center', 'bottom'
    /// </summary>
    private static string ConvertVerticalAlignment(TableVerticalAlignmentValues value)
    {
        if (value == TableVerticalAlignmentValues.Top) return "top";
        if (value == TableVerticalAlignmentValues.Center) return "center";
        if (value == TableVerticalAlignmentValues.Bottom) return "bottom";
        
        // Default fallback
        return "top";
    }
}

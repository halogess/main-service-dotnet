using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts table row formatting properties from OpenXML w:trPr into DokumenFormatTableRow.
/// Uses TableStyleResolver to get effective properties after style inheritance and conditional formatting.
/// </summary>
public class TableRowFormatExtractor
{
    private readonly TableStyleResolver? _styleResolver;
    
    public TableRowFormatExtractor(TableStyleResolver? styleResolver = null)
    {
        _styleResolver = styleResolver;
    }
    
    /// <summary>
    /// Extract row properties from a TableRow element.
    /// Requires position context for conditional style resolution.
    /// </summary>
    public DokumenFormatTableRow ExtractFormat(
        TableRow row,
        int rowIndex = 0,
        int totalRows = 1,
        Table? table = null)
    {
        var format = new DokumenFormatTableRow();
        
        // Get effective (resolved) row properties if resolver and table are available
        var effectiveTrPr = (_styleResolver != null && table != null)
            ? _styleResolver.ResolveEffectiveRowProperties(row, rowIndex, totalRows, table)
            : row.GetFirstChild<TableRowProperties>();
        
        // Store raw XML for debugging (original direct formatting)
        var directTrPr = row.GetFirstChild<TableRowProperties>();
        if (directTrPr != null)
            format.DftrRawTrprXml = directTrPr.OuterXml;
        
        if (effectiveTrPr == null)
            return format;
        
        // Table Header Row (w:tblHeader)
        // Specifies that this row should be repeated at the top of each page
        var tableHeader = effectiveTrPr.GetFirstChild<TableHeader>();
        if (tableHeader != null)
        {
            // OnOffOnlyValues: if element exists, it's true; if Val is present, use it
            format.DftrTblHeader = tableHeader.Val?.HasValue == true ? (tableHeader.Val.Value == OnOffOnlyValues.On) : true;
        }
        
        // Can't Split Row (w:cantSplit)
        // Specifies that the contents of this row cannot be split across multiple pages
        var cantSplit = effectiveTrPr.GetFirstChild<CantSplit>();
        if (cantSplit != null)
        {
            format.DftrCantSplit = cantSplit.Val?.HasValue == true ? (cantSplit.Val.Value == OnOffOnlyValues.On) : true;
        }
        
        return format;
    }
}

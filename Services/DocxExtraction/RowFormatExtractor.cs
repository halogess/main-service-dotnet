using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts table row formatting properties from OpenXML w:trPr into DokumenFormatRow.
/// </summary>
public class RowFormatExtractor
{
    /// <summary>
    /// Extract table row properties from a TableRow element.
    /// </summary>
    public DokumenFormatRow ExtractFormat(TableRow row)
    {
        var format = new DokumenFormatRow();
        var trPr = row.GetFirstChild<TableRowProperties>();
        
        // Store raw XML for debugging
        if (trPr != null)
            format.DfrRawTrprXml = trPr.OuterXml;
        
        if (trPr == null)
            return format;
        
        // Table Header Row (w:tblHeader)
        // If present, this row is repeated at the top of each page
        // OnOffOnlyType: element presence means true
        var tblHeader = trPr.GetFirstChild<TableHeader>();
        if (tblHeader != null)
        {
            format.DfrTblHeader = true;
        }
        
        // Can't Split Row (w:cantSplit)
        // If present, prevents row from splitting across pages
        // OnOffOnlyType: element presence means true
        var cantSplit = trPr.GetFirstChild<CantSplit>();
        if (cantSplit != null)
        {
            format.DfrCantSplit = true;
        }
        
        return format;
    }
}

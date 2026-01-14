using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Utility class for merging table properties with proper override semantics.
/// Implements "last non-null wins" for scalar properties and per-side merging for complex properties.
/// </summary>
public static class TablePropertyMerger
{
    // ========================================================================
    // TABLE PROPERTIES MERGING
    // ========================================================================
    
    /// <summary>
    /// Merge TableProperties (overlay overrides base).
    /// Returns a new TableProperties with merged values.
    /// </summary>
    public static TableProperties MergeTableProperties(TableProperties? baseProps, TableProperties? overlay)
    {
        var result = new TableProperties();
        
        if (baseProps != null)
            result = (TableProperties)baseProps.CloneNode(true);
        
        if (overlay == null)
            return result;
        
        // Scalar properties - last non-null wins
        if (overlay.TableStyle != null)
            SetOrReplace(result, overlay.TableStyle.CloneNode(true));
        
        if (overlay.TableWidth != null)
            SetOrReplace(result, overlay.TableWidth.CloneNode(true));
        
        if (overlay.TableJustification != null)
            SetOrReplace(result, overlay.TableJustification.CloneNode(true));
        
        if (overlay.TableIndentation != null)
            SetOrReplace(result, overlay.TableIndentation.CloneNode(true));
        
        if (overlay.TableLayout != null)
            SetOrReplace(result, overlay.TableLayout.CloneNode(true));
        
        if (overlay.TableCellSpacing != null)
            SetOrReplace(result, overlay.TableCellSpacing.CloneNode(true));
        
        if (overlay.TableLook != null)
            SetOrReplace(result, overlay.TableLook.CloneNode(true));

        var rowBandSize = overlay.GetFirstChild<TableStyleRowBandSize>();
        if (rowBandSize != null)
            SetOrReplace(result, rowBandSize.CloneNode(true));

        var colBandSize = overlay.GetFirstChild<TableStyleColumnBandSize>();
        if (colBandSize != null)
            SetOrReplace(result, colBandSize.CloneNode(true));
        
        // Complex properties - per-side merging
        if (overlay.TableBorders != null)
        {
            var baseBorders = result.GetFirstChild<TableBorders>();
            var mergedBorders = MergeTableBorders(baseBorders, overlay.TableBorders);
            SetOrReplace(result, mergedBorders);
        }
        
        if (overlay.Shading != null)
            SetOrReplace(result, overlay.Shading.CloneNode(true));
        
        if (overlay.TableCellMarginDefault != null)
        {
            var baseMargins = result.GetFirstChild<TableCellMarginDefault>();
            var mergedMargins = MergeCellMargins(baseMargins, overlay.TableCellMarginDefault);
            SetOrReplace(result, mergedMargins);
        }
        
        return result;
    }
    
    // ========================================================================
    // TABLE ROW PROPERTIES MERGING
    // ========================================================================
    
    /// <summary>
    /// Merge TableRowProperties (overlay overrides base).
    /// </summary>
    public static TableRowProperties MergeTableRowProperties(TableRowProperties? baseProps, TableRowProperties? overlay)
    {
        var result = new TableRowProperties();
        
        if (baseProps != null)
            result = (TableRowProperties)baseProps.CloneNode(true);
        
        if (overlay == null)
            return result;
        
        // Scalar properties - access as child elements
        var tableHeader = overlay.GetFirstChild<TableHeader>();
        if (tableHeader != null)
            SetOrReplace(result, tableHeader.CloneNode(true));
        
        var cantSplit = overlay.GetFirstChild<CantSplit>();
        if (cantSplit != null)
            SetOrReplace(result, cantSplit.CloneNode(true));
        
        var tableRowHeight = overlay.GetFirstChild<TableRowHeight>();
        if (tableRowHeight != null)
            SetOrReplace(result, tableRowHeight.CloneNode(true));
        
        var hidden = overlay.GetFirstChild<Hidden>();
        if (hidden != null)
            SetOrReplace(result, hidden.CloneNode(true));
        
        var divId = overlay.GetFirstChild<DivId>();
        if (divId != null)
            SetOrReplace(result, divId.CloneNode(true));
        
        var gridBefore = overlay.GetFirstChild<GridBefore>();
        if (gridBefore != null)
            SetOrReplace(result, gridBefore.CloneNode(true));
        
        var gridAfter = overlay.GetFirstChild<GridAfter>();
        if (gridAfter != null)
            SetOrReplace(result, gridAfter.CloneNode(true));
        
        var widthBefore = overlay.GetFirstChild<WidthBeforeTableRow>();
        if (widthBefore != null)
            SetOrReplace(result, widthBefore.CloneNode(true));
        
        var widthAfter = overlay.GetFirstChild<WidthAfterTableRow>();
        if (widthAfter != null)
            SetOrReplace(result, widthAfter.CloneNode(true));
        
        return result;
    }
    
    // ========================================================================
    // TABLE CELL PROPERTIES MERGING
    // ========================================================================
    
    /// <summary>
    /// Merge TableCellProperties (overlay overrides base).
    /// </summary>
    public static TableCellProperties MergeTableCellProperties(TableCellProperties? baseProps, TableCellProperties? overlay)
    {
        var result = new TableCellProperties();
        
        if (baseProps != null)
            result = (TableCellProperties)baseProps.CloneNode(true);
        
        if (overlay == null)
            return result;
        
        // Scalar properties
        if (overlay.TableCellWidth != null)
            SetOrReplace(result, overlay.TableCellWidth.CloneNode(true));
        
        if (overlay.GridSpan != null)
            SetOrReplace(result, overlay.GridSpan.CloneNode(true));
        
        if (overlay.VerticalMerge != null)
            SetOrReplace(result, overlay.VerticalMerge.CloneNode(true));
        
        if (overlay.TableCellVerticalAlignment != null)
            SetOrReplace(result, overlay.TableCellVerticalAlignment.CloneNode(true));
        
        if (overlay.HideMark != null)
            SetOrReplace(result, overlay.HideMark.CloneNode(true));
        
        if (overlay.NoWrap != null)
            SetOrReplace(result, overlay.NoWrap.CloneNode(true));
        
        if (overlay.TextDirection != null)
            SetOrReplace(result, overlay.TextDirection.CloneNode(true));
        
        if (overlay.TableCellFitText != null)
            SetOrReplace(result, overlay.TableCellFitText.CloneNode(true));
        
        // Complex properties - per-side merging
        if (overlay.TableCellBorders != null)
        {
            var baseBorders = result.GetFirstChild<TableCellBorders>();
            var mergedBorders = MergeCellBorders(baseBorders, overlay.TableCellBorders);
            SetOrReplace(result, mergedBorders);
        }
        
        if (overlay.Shading != null)
            SetOrReplace(result, overlay.Shading.CloneNode(true));
        
        if (overlay.TableCellMargin != null)
        {
            var baseMargins = result.GetFirstChild<TableCellMargin>();
            var mergedMargins = MergeCellMarginsIndividual(baseMargins, overlay.TableCellMargin);
            SetOrReplace(result, mergedMargins);
        }
        
        return result;
    }
    
    // ========================================================================
    // COMPLEX PROPERTY MERGING - BORDERS
    // ========================================================================
    
    /// <summary>
    /// Merge TableBorders per-side (overlay overrides base for each side).
    /// </summary>
    private static TableBorders MergeTableBorders(TableBorders? baseBorders, TableBorders overlay)
    {
        var result = new TableBorders();
        
        if (baseBorders != null)
            result = (TableBorders)baseBorders.CloneNode(true);
        
        // Merge each border side independently
        if (overlay.TopBorder != null)
            SetOrReplace(result, overlay.TopBorder.CloneNode(true));
        
        if (overlay.LeftBorder != null)
            SetOrReplace(result, overlay.LeftBorder.CloneNode(true));
        
        if (overlay.BottomBorder != null)
            SetOrReplace(result, overlay.BottomBorder.CloneNode(true));
        
        if (overlay.RightBorder != null)
            SetOrReplace(result, overlay.RightBorder.CloneNode(true));
        
        if (overlay.InsideHorizontalBorder != null)
            SetOrReplace(result, overlay.InsideHorizontalBorder.CloneNode(true));
        
        if (overlay.InsideVerticalBorder != null)
            SetOrReplace(result, overlay.InsideVerticalBorder.CloneNode(true));
        
        return result;
    }
    
    /// <summary>
    /// Merge TableCellBorders per-side.
    /// </summary>
    private static TableCellBorders MergeCellBorders(TableCellBorders? baseBorders, TableCellBorders overlay)
    {
        var result = new TableCellBorders();
        
        if (baseBorders != null)
            result = (TableCellBorders)baseBorders.CloneNode(true);
        
        if (overlay.TopBorder != null)
            SetOrReplace(result, overlay.TopBorder.CloneNode(true));
        
        if (overlay.LeftBorder != null)
            SetOrReplace(result, overlay.LeftBorder.CloneNode(true));
        
        if (overlay.BottomBorder != null)
            SetOrReplace(result, overlay.BottomBorder.CloneNode(true));
        
        if (overlay.RightBorder != null)
            SetOrReplace(result, overlay.RightBorder.CloneNode(true));
        
        if (overlay.InsideHorizontalBorder != null)
            SetOrReplace(result, overlay.InsideHorizontalBorder.CloneNode(true));
        
        if (overlay.InsideVerticalBorder != null)
            SetOrReplace(result, overlay.InsideVerticalBorder.CloneNode(true));
        
        if (overlay.TopLeftToBottomRightCellBorder != null)
            SetOrReplace(result, overlay.TopLeftToBottomRightCellBorder.CloneNode(true));
        
        if (overlay.TopRightToBottomLeftCellBorder != null)
            SetOrReplace(result, overlay.TopRightToBottomLeftCellBorder.CloneNode(true));
        
        return result;
    }
    
    // ========================================================================
    // COMPLEX PROPERTY MERGING - MARGINS
    // ========================================================================
    
    /// <summary>
    /// Merge TableCellMarginDefault per-side.
    /// Note: TableCellMarginDefault uses StartMargin/EndMargin (not LeftMargin/RightMargin)
    /// </summary>
    private static TableCellMarginDefault MergeCellMargins(TableCellMarginDefault? baseMargins, TableCellMarginDefault overlay)
    {
        var result = new TableCellMarginDefault();
        
        if (baseMargins != null)
            result = (TableCellMarginDefault)baseMargins.CloneNode(true);
        
        if (overlay.TopMargin != null)
            SetOrReplace(result, overlay.TopMargin.CloneNode(true));
        
        if (overlay.StartMargin != null)
            SetOrReplace(result, overlay.StartMargin.CloneNode(true));
        
        if (overlay.BottomMargin != null)
            SetOrReplace(result, overlay.BottomMargin.CloneNode(true));
        
        if (overlay.EndMargin != null)
            SetOrReplace(result, overlay.EndMargin.CloneNode(true));
        
        return result;
    }
    
    /// <summary>
    /// Merge TableCellMargin (individual cell margins) per-side.
    /// </summary>
    private static TableCellMargin MergeCellMarginsIndividual(TableCellMargin? baseMargins, TableCellMargin overlay)
    {
        var result = new TableCellMargin();
        
        if (baseMargins != null)
            result = (TableCellMargin)baseMargins.CloneNode(true);
        
        if (overlay.TopMargin != null)
            SetOrReplace(result, overlay.TopMargin.CloneNode(true));
        
        if (overlay.LeftMargin != null)
            SetOrReplace(result, overlay.LeftMargin.CloneNode(true));
        
        if (overlay.BottomMargin != null)
            SetOrReplace(result, overlay.BottomMargin.CloneNode(true));
        
        if (overlay.RightMargin != null)
            SetOrReplace(result, overlay.RightMargin.CloneNode(true));
        
        if (overlay.StartMargin != null)
            SetOrReplace(result, overlay.StartMargin.CloneNode(true));
        
        if (overlay.EndMargin != null)
            SetOrReplace(result, overlay.EndMargin.CloneNode(true));
        
        return result;
    }
    
    // ========================================================================
    // HELPER METHODS
    // ========================================================================
    
    /// <summary>
    /// Replace or add a child element in the parent.
    /// If an element of the same type exists, remove it first.
    /// </summary>
    private static void SetOrReplace<T>(OpenXmlCompositeElement parent, OpenXmlElement newChild) where T : OpenXmlElement
    {
        var existing = parent.GetFirstChild<T>();
        if (existing != null)
            existing.Remove();
        
        parent.Append(newChild);
    }
    
    /// <summary>
    /// Generic version that infers type from newChild.
    /// </summary>
    private static void SetOrReplace(OpenXmlCompositeElement parent, OpenXmlElement newChild)
    {
        var existing = parent.Elements().FirstOrDefault(e => e.GetType() == newChild.GetType());
        if (existing != null)
            existing.Remove();
        
        parent.Append(newChild);
    }
}

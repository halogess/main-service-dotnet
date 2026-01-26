using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Resolves table formatting with full inheritance:
/// Defaults → Table Style (basedOn chain) → Conditional Styles (position-based) → Direct Formatting
/// </summary>
public class TableStyleResolver
{
    private readonly Dictionary<string, Style> _stylesById = new();
    private readonly Dictionary<string, TableStyleDefinition> _tableStylesCache = new();
    private readonly Styles? _stylesRoot;
    private readonly string? _defaultTableStyleId;
    
    public TableStyleResolver(StylesPart? stylesPart, StylesWithEffectsPart? stylesWithEffectsPart = null)
    {
        _stylesRoot = stylesWithEffectsPart?.Styles ?? stylesPart?.Styles;
        if (_stylesRoot != null)
        {
            // Cache all styles by ID
            foreach (var style in _stylesRoot.Elements<Style>())
            {
                var styleId = style.StyleId?.Value;
                if (!string.IsNullOrEmpty(styleId))
                    _stylesById[styleId] = style;
            }

            _defaultTableStyleId = _stylesRoot.Elements<Style>()
                .FirstOrDefault(s => s.Type?.Value == StyleValues.Table && (s.Default?.Value ?? false))
                ?.StyleId?.Value;
        }
    }
    
    // ========================================================================
    // PUBLIC API - EFFECTIVE PROPERTY RESOLUTION
    // ========================================================================
    
    /// <summary>
    /// Resolve effective TableProperties for a table.
    /// Order: defaults → style chain (wholeTable) → direct tblPr
    /// </summary>
    public TableProperties ResolveEffectiveTableProperties(Table table)
    {
        var effective = new TableProperties();
        
        var tblPr = table.GetFirstChild<TableProperties>();

        // 1. Get table style ID (fallback to default table style if missing)
        var styleId = tblPr?.TableStyle?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
            styleId = _defaultTableStyleId;
        
        // 2. Apply style chain (wholeTable only for table-level properties)
        if (!string.IsNullOrEmpty(styleId))
        {
            var styleChain = GetTableStyleChain(styleId);
            foreach (var styleDef in styleChain)
            {
                if (styleDef.WholeTableTblPr != null)
                    effective = TablePropertyMerger.MergeTableProperties(effective, styleDef.WholeTableTblPr);
            }
        }
        
        // 3. Apply direct formatting (highest priority)
        if (tblPr != null)
            effective = TablePropertyMerger.MergeTableProperties(effective, tblPr);
        
        return effective;
    }
    
    /// <summary>
    /// Resolve effective TableRowProperties for a row.
    /// Order: defaults → style chain (wholeTable + conditional) → direct trPr → tblPrEx
    /// </summary>
    public TableRowProperties ResolveEffectiveRowProperties(
        TableRow row, 
        int rowIndex, 
        int totalRows, 
        Table table)
    {
        var effective = new TableRowProperties();
        var effectiveTblPr = ResolveEffectiveTableProperties(table);
        var styleId = effectiveTblPr.TableStyle?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
            styleId = _defaultTableStyleId;
        var tableLook = TableLookFlags.FromTableLook(effectiveTblPr.TableLook);
        var rowBandSize = effectiveTblPr.GetFirstChild<TableStyleRowBandSize>()?.Val?.Value ?? 1;
        
        // 1. Apply style chain with conditional formatting
        if (!string.IsNullOrEmpty(styleId))
        {
            var styleChain = GetTableStyleChain(styleId);
            
            // Calculate row conditions (we need column count for corners)
            var totalCols = GetTableColumnCount(table);
            var rowConditions = CalculateRowConditions(rowIndex, totalRows, totalCols, tableLook, rowBandSize);
            
            foreach (var styleDef in styleChain)
            {
                // Apply wholeTable trPr
                if (styleDef.WholeTableTrPr != null)
                    effective = TablePropertyMerger.MergeTableRowProperties(effective, styleDef.WholeTableTrPr);
                
                // Apply conditional styles in order
                effective = ApplyConditionalRowStyles(effective, styleDef, rowConditions, tableLook);
            }
        }
        
        // 2. Apply direct trPr
        var trPr = row.GetFirstChild<TableRowProperties>();
        if (trPr != null)
            effective = TablePropertyMerger.MergeTableRowProperties(effective, trPr);
        
        // 3. Apply tblPrEx (table property exceptions for this row)
        var tblPrEx = row.GetFirstChild<TablePropertyExceptions>();
        if (tblPrEx != null)
        {
            // tblPrEx can contain some table-level overrides for this specific row
            // For now, we'll handle the common properties
            // Note: tblPrEx doesn't directly affect trPr, but we note its presence
        }
        
        return effective;
    }
    
    /// <summary>
    /// Resolve effective TableCellProperties for a cell.
    /// Order: defaults → style chain (wholeTable + conditional based on position) → direct tcPr
    /// </summary>
    public TableCellProperties ResolveEffectiveCellProperties(
        TableCell cell,
        int rowIndex,
        int colIndex,
        int totalRows,
        int totalCols,
        Table table)
    {
        var effective = new TableCellProperties();
        var effectiveTblPr = ResolveEffectiveTableProperties(table);
        var styleId = effectiveTblPr.TableStyle?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
            styleId = _defaultTableStyleId;
        var tableLook = TableLookFlags.FromTableLook(effectiveTblPr.TableLook);
        var rowBandSize = effectiveTblPr.GetFirstChild<TableStyleRowBandSize>()?.Val?.Value ?? 1;
        var colBandSize = effectiveTblPr.GetFirstChild<TableStyleColumnBandSize>()?.Val?.Value ?? 1;
        
        // 1. Apply style chain with conditional formatting
        if (!string.IsNullOrEmpty(styleId))
        {
            var styleChain = GetTableStyleChain(styleId);
            
            // Calculate cell conditions
            var cellConditions = CalculateCellConditions(
                rowIndex, colIndex, totalRows, totalCols, 
                tableLook, rowBandSize, colBandSize);
            
            foreach (var styleDef in styleChain)
            {
                // Apply wholeTable tcPr
                if (styleDef.WholeTableTcPr != null)
                    effective = TablePropertyMerger.MergeTableCellProperties(effective, styleDef.WholeTableTcPr);
                
                // Apply conditional styles in order (general → specific)
                effective = ApplyConditionalCellStyles(effective, styleDef, cellConditions, tableLook);
            }
        }
        
        // 2. Apply direct tcPr (highest priority)
        var tcPr = cell.GetFirstChild<TableCellProperties>();
        if (tcPr != null)
            effective = TablePropertyMerger.MergeTableCellProperties(effective, tcPr);
        
        return effective;
    }
    
    // ========================================================================
    // STYLE CHAIN RESOLUTION
    // ========================================================================
    
    /// <summary>
    /// Get table style chain from basedOn relationships (parent → child order).
    /// </summary>
    private List<TableStyleDefinition> GetTableStyleChain(string? styleId)
    {
        var chain = new List<TableStyleDefinition>();
        var visited = new HashSet<string>();
        
        while (!string.IsNullOrEmpty(styleId) && !visited.Contains(styleId))
        {
            visited.Add(styleId);
            
            var styleDef = GetOrLoadTableStyle(styleId);
            if (styleDef != null)
            {
                chain.Insert(0, styleDef); // Insert at beginning for parent → child order
                styleId = styleDef.BasedOnStyleId;
            }
            else
            {
                break;
            }
        }
        
        return chain;
    }
    
    /// <summary>
    /// Get or load a table style definition from cache.
    /// </summary>
    private TableStyleDefinition? GetOrLoadTableStyle(string styleId)
    {
        if (_tableStylesCache.TryGetValue(styleId, out var cached))
            return cached;
        
        if (!_stylesById.TryGetValue(styleId, out var style))
            return null;
        
        // Only process table styles
        if (style.Type?.Value != StyleValues.Table)
            return null;
        
        var styleDef = new TableStyleDefinition
        {
            StyleId = styleId,
            BasedOnStyleId = style.BasedOn?.Val?.Value
        };
        
        // Extract wholeTable properties
        // Note: Table styles only have StyleTableProperties (tblPr) and StyleTableCellProperties (tcPr)
        // Convert StyleTableProperties to TableProperties
        styleDef.WholeTableTblPr = ConvertStyleTablePropertiesToTableProperties(style.StyleTableProperties);
        // Convert style row properties (CT_TrPrBaseStyleable) to TableRowProperties
        var styleRowProps = style.GetFirstChild<TableStyleConditionalFormattingTableRowProperties>();
        styleDef.WholeTableTrPr = ConvertStyleTableRowPropertiesToTableRowProperties(styleRowProps);
        // Convert StyleTableCellProperties to TableCellProperties
        styleDef.WholeTableTcPr = ConvertStyleTableCellPropertiesToTableCellProperties(style.StyleTableCellProperties);
        
        // Extract conditional styles (w:tblStylePr)
        foreach (var tblStylePr in style.Elements<TableStyleProperties>())
        {
            var type = tblStylePr.Type?.Value.ToString();
            if (string.IsNullOrEmpty(type))
                continue;
            
            var conditionalStyle = new ConditionalTableStyle
            {
                TblPr = ConvertConditionalTablePropertiesToTableProperties(
                    tblStylePr.GetFirstChild<TableStyleConditionalFormattingTableProperties>()),
                TrPr = ConvertStyleTableRowPropertiesToTableRowProperties(
                    tblStylePr.GetFirstChild<TableStyleConditionalFormattingTableRowProperties>()),
                TcPr = ConvertConditionalTableCellPropertiesToTableCellProperties(
                    tblStylePr.GetFirstChild<TableStyleConditionalFormattingTableCellProperties>())
            };
            
            styleDef.ConditionalStyles[type] = conditionalStyle;
        }
        
        _tableStylesCache[styleId] = styleDef;
        return styleDef;
    }
    
    // ========================================================================
    // CELL POSITION CONDITION CALCULATION
    // ========================================================================
    
    /// <summary>
    /// Calculate cell position conditions for conditional styling.
    /// </summary>
    private CellConditions CalculateCellConditions(
        int rowIndex, 
        int colIndex, 
        int totalRows, 
        int totalCols,
        TableLookFlags tableLook,
        int rowBandSize,
        int colBandSize)
    {
        return new CellConditions
        {
            IsFirstRow = rowIndex == 0,
            IsLastRow = rowIndex == totalRows - 1,
            IsFirstCol = colIndex == 0,
            IsLastCol = colIndex == totalCols - 1,
            IsBand1Horz = (rowIndex / rowBandSize) % 2 == 0,
            IsBand2Horz = (rowIndex / rowBandSize) % 2 == 1,
            IsBand1Vert = (colIndex / colBandSize) % 2 == 0,
            IsBand2Vert = (colIndex / colBandSize) % 2 == 1,
            IsNwCell = rowIndex == 0 && colIndex == 0,
            IsNeCell = rowIndex == 0 && colIndex == totalCols - 1,
            IsSwCell = rowIndex == totalRows - 1 && colIndex == 0,
            IsSeCell = rowIndex == totalRows - 1 && colIndex == totalCols - 1
        };
    }
    
    /// <summary>
    /// Calculate row-level conditions (simplified version of cell conditions).
    /// </summary>
    private CellConditions CalculateRowConditions(
        int rowIndex,
        int totalRows,
        int totalCols,
        TableLookFlags tableLook,
        int rowBandSize)
    {
        return new CellConditions
        {
            IsFirstRow = rowIndex == 0,
            IsLastRow = rowIndex == totalRows - 1,
            IsBand1Horz = (rowIndex / rowBandSize) % 2 == 0,
            IsBand2Horz = (rowIndex / rowBandSize) % 2 == 1
        };
    }
    
    // ========================================================================
    // CONDITIONAL STYLE APPLICATION
    // ========================================================================
    
    /// <summary>
    /// Apply conditional cell styles in the correct order (general → specific).
    /// Order: wholeTable → banding → firstRow/lastRow → firstCol/lastCol → corners
    /// </summary>
    private TableCellProperties ApplyConditionalCellStyles(
        TableCellProperties effective,
        TableStyleDefinition styleDef,
        CellConditions conditions,
        TableLookFlags tableLook)
    {
        // Order matters: more specific conditions override general ones
        
        // 1. Banding (if enabled)
        if (!tableLook.NoHBand)
        {
            if (conditions.IsBand1Horz && styleDef.ConditionalStyles.TryGetValue("band1Horz", out var band1H))
                if (band1H.TcPr != null)
                    effective = TablePropertyMerger.MergeTableCellProperties(effective, band1H.TcPr);
            
            if (conditions.IsBand2Horz && styleDef.ConditionalStyles.TryGetValue("band2Horz", out var band2H))
                if (band2H.TcPr != null)
                    effective = TablePropertyMerger.MergeTableCellProperties(effective, band2H.TcPr);
        }
        
        if (!tableLook.NoVBand)
        {
            if (conditions.IsBand1Vert && styleDef.ConditionalStyles.TryGetValue("band1Vert", out var band1V))
                if (band1V.TcPr != null)
                    effective = TablePropertyMerger.MergeTableCellProperties(effective, band1V.TcPr);
            
            if (conditions.IsBand2Vert && styleDef.ConditionalStyles.TryGetValue("band2Vert", out var band2V))
                if (band2V.TcPr != null)
                    effective = TablePropertyMerger.MergeTableCellProperties(effective, band2V.TcPr);
        }
        
        // 2. First/Last Row (if enabled)
        if (tableLook.FirstRow && conditions.IsFirstRow && styleDef.ConditionalStyles.TryGetValue("firstRow", out var firstRow))
            if (firstRow.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, firstRow.TcPr);
        
        if (tableLook.LastRow && conditions.IsLastRow && styleDef.ConditionalStyles.TryGetValue("lastRow", out var lastRow))
            if (lastRow.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, lastRow.TcPr);
        
        // 3. First/Last Column (if enabled)
        if (tableLook.FirstColumn && conditions.IsFirstCol && styleDef.ConditionalStyles.TryGetValue("firstCol", out var firstCol))
            if (firstCol.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, firstCol.TcPr);
        
        if (tableLook.LastColumn && conditions.IsLastCol && styleDef.ConditionalStyles.TryGetValue("lastCol", out var lastCol))
            if (lastCol.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, lastCol.TcPr);
        
        // 4. Corners (most specific - override everything)
        if (conditions.IsNwCell && styleDef.ConditionalStyles.TryGetValue("nwCell", out var nwCell))
            if (nwCell.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, nwCell.TcPr);
        
        if (conditions.IsNeCell && styleDef.ConditionalStyles.TryGetValue("neCell", out var neCell))
            if (neCell.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, neCell.TcPr);
        
        if (conditions.IsSwCell && styleDef.ConditionalStyles.TryGetValue("swCell", out var swCell))
            if (swCell.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, swCell.TcPr);
        
        if (conditions.IsSeCell && styleDef.ConditionalStyles.TryGetValue("seCell", out var seCell))
            if (seCell.TcPr != null)
                effective = TablePropertyMerger.MergeTableCellProperties(effective, seCell.TcPr);
        
        return effective;
    }
    
    /// <summary>
    /// Apply conditional row styles.
    /// </summary>
    private TableRowProperties ApplyConditionalRowStyles(
        TableRowProperties effective,
        TableStyleDefinition styleDef,
        CellConditions conditions,
        TableLookFlags tableLook)
    {
        // Banding
        if (!tableLook.NoHBand)
        {
            if (conditions.IsBand1Horz && styleDef.ConditionalStyles.TryGetValue("band1Horz", out var band1H))
                if (band1H.TrPr != null)
                    effective = TablePropertyMerger.MergeTableRowProperties(effective, band1H.TrPr);
            
            if (conditions.IsBand2Horz && styleDef.ConditionalStyles.TryGetValue("band2Horz", out var band2H))
                if (band2H.TrPr != null)
                    effective = TablePropertyMerger.MergeTableRowProperties(effective, band2H.TrPr);
        }
        
        // First/Last Row
        if (tableLook.FirstRow && conditions.IsFirstRow && styleDef.ConditionalStyles.TryGetValue("firstRow", out var firstRow))
            if (firstRow.TrPr != null)
                effective = TablePropertyMerger.MergeTableRowProperties(effective, firstRow.TrPr);
        
        if (tableLook.LastRow && conditions.IsLastRow && styleDef.ConditionalStyles.TryGetValue("lastRow", out var lastRow))
            if (lastRow.TrPr != null)
                effective = TablePropertyMerger.MergeTableRowProperties(effective, lastRow.TrPr);
        
        return effective;
    }
    
    // ========================================================================
    // HELPER METHODS
    // ========================================================================
    
    /// <summary>
    /// Convert StyleTableProperties to TableProperties by cloning all child elements.
    /// StyleTableProperties and TableProperties have the same structure, just different types.
    /// </summary>
    private static TableProperties? ConvertStyleTablePropertiesToTableProperties(StyleTableProperties? styleProps)
    {
        if (styleProps == null)
            return null;
        
        var tableProps = new TableProperties();
        
        // Clone all child elements from StyleTableProperties to TableProperties
        foreach (var child in styleProps.ChildElements)
        {
            tableProps.Append(child.CloneNode(true));
        }
        
        return tableProps;
    }

    private static TableProperties? ConvertConditionalTablePropertiesToTableProperties(
        TableStyleConditionalFormattingTableProperties? styleProps)
    {
        if (styleProps == null)
            return null;

        var tableProps = new TableProperties();
        foreach (var child in styleProps.ChildElements)
        {
            tableProps.Append(child.CloneNode(true));
        }

        return tableProps;
    }
    
    /// <summary>
    /// Convert StyleTableCellProperties to TableCellProperties by cloning all child elements.
    /// StyleTableCellProperties and TableCellProperties have the same structure, just different types.
    /// </summary>
    private static TableCellProperties? ConvertStyleTableCellPropertiesToTableCellProperties(StyleTableCellProperties? styleCellProps)
    {
        if (styleCellProps == null)
            return null;
        
        var cellProps = new TableCellProperties();
        
        // Clone all child elements from StyleTableCellProperties to TableCellProperties
        foreach (var child in styleCellProps.ChildElements)
        {
            cellProps.Append(child.CloneNode(true));
        }
        
        return cellProps;
    }

    private static TableCellProperties? ConvertConditionalTableCellPropertiesToTableCellProperties(
        TableStyleConditionalFormattingTableCellProperties? styleCellProps)
    {
        if (styleCellProps == null)
            return null;

        var cellProps = new TableCellProperties();
        foreach (var child in styleCellProps.ChildElements)
        {
            cellProps.Append(child.CloneNode(true));
        }

        return cellProps;
    }

    private static TableRowProperties? ConvertStyleTableRowPropertiesToTableRowProperties(
        TableStyleConditionalFormattingTableRowProperties? styleRowProps)
    {
        if (styleRowProps == null)
            return null;

        var rowProps = new TableRowProperties();
        foreach (var child in styleRowProps.ChildElements)
        {
            rowProps.Append(child.CloneNode(true));
        }

        return rowProps;
    }
    

    /// <summary>
    /// Get the number of columns in a table (from first row's grid span).
    /// </summary>
    private int GetTableColumnCount(Table table)
    {
        var firstRow = table.Elements<TableRow>().FirstOrDefault();
        if (firstRow == null)
            return 0;
        
        int colCount = 0;
        foreach (var cell in firstRow.Elements<TableCell>())
        {
            var tcPr = cell.GetFirstChild<TableCellProperties>();
            var gridSpan = tcPr?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
            colCount += gridSpan;
        }
        
        return colCount;
    }
}

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Represents cell position conditions for conditional table styling.
/// Used to determine which conditional table styles apply to a specific cell.
/// </summary>
public class CellConditions
{
    public bool IsFirstRow { get; set; }
    public bool IsLastRow { get; set; }
    public bool IsFirstCol { get; set; }
    public bool IsLastCol { get; set; }
    public bool IsBand1Horz { get; set; }  // Horizontal banding (odd rows)
    public bool IsBand2Horz { get; set; }  // Horizontal banding (even rows)
    public bool IsBand1Vert { get; set; }  // Vertical banding (odd columns)
    public bool IsBand2Vert { get; set; }  // Vertical banding (even columns)
    public bool IsNwCell { get; set; }     // Northwest corner (top-left)
    public bool IsNeCell { get; set; }     // Northeast corner (top-right)
    public bool IsSwCell { get; set; }     // Southwest corner (bottom-left)
    public bool IsSeCell { get; set; }     // Southeast corner (bottom-right)
}

/// <summary>
/// Cached table style definition from styles.xml.
/// Contains wholeTable properties and conditional formatting rules.
/// </summary>
public class TableStyleDefinition
{
    public string StyleId { get; set; } = string.Empty;
    public string? BasedOnStyleId { get; set; }
    
    // WholeTable properties (apply to all cells unless overridden)
    public TableProperties? WholeTableTblPr { get; set; }
    public TableRowProperties? WholeTableTrPr { get; set; }
    public TableCellProperties? WholeTableTcPr { get; set; }
    
    // Conditional formatting (Dictionary<conditionType, properties>)
    // Keys: "firstRow", "lastRow", "firstCol", "lastCol", "band1Horz", "band2Horz", etc.
    public Dictionary<string, ConditionalTableStyle> ConditionalStyles { get; set; } = new();
}

/// <summary>
/// Represents a conditional table style (w:tblStylePr) with its properties.
/// </summary>
public class ConditionalTableStyle
{
    public TableProperties? TblPr { get; set; }
    public TableRowProperties? TrPr { get; set; }
    public TableCellProperties? TcPr { get; set; }
}

/// <summary>
/// Represents table look flags (w:tblLook) that control which conditional styles are active.
/// </summary>
public class TableLookFlags
{
    public bool FirstRow { get; set; }
    public bool LastRow { get; set; }
    public bool FirstColumn { get; set; }
    public bool LastColumn { get; set; }
    public bool NoHBand { get; set; }  // No horizontal banding
    public bool NoVBand { get; set; }  // No vertical banding
    
    /// <summary>
    /// Parse TableLook from OpenXML TableLook element
    /// </summary>
    public static TableLookFlags FromTableLook(TableLook? tableLook)
    {
        var defaults = new TableLookFlags
        {
            FirstRow = true,
            LastRow = false,
            FirstColumn = true,
            LastColumn = false,
            NoHBand = false,
            NoVBand = true
        };

        if (tableLook == null)
            return defaults;

        var fromVal = ParseTableLookVal(tableLook.Val);

        return new TableLookFlags
        {
            FirstRow = tableLook.FirstRow?.Value ?? fromVal?.FirstRow ?? defaults.FirstRow,
            LastRow = tableLook.LastRow?.Value ?? fromVal?.LastRow ?? defaults.LastRow,
            FirstColumn = tableLook.FirstColumn?.Value ?? fromVal?.FirstColumn ?? defaults.FirstColumn,
            LastColumn = tableLook.LastColumn?.Value ?? fromVal?.LastColumn ?? defaults.LastColumn,
            NoHBand = tableLook.NoHorizontalBand?.Value ?? fromVal?.NoHBand ?? defaults.NoHBand,
            NoVBand = tableLook.NoVerticalBand?.Value ?? fromVal?.NoVBand ?? defaults.NoVBand
        };
    }

    private static TableLookFlags? ParseTableLookVal(HexBinaryValue? val)
    {
        var hex = val?.Value;
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask))
            return null;

        return new TableLookFlags
        {
            FirstRow = (mask & 0x0020) != 0,
            LastRow = (mask & 0x0040) != 0,
            FirstColumn = (mask & 0x0080) != 0,
            LastColumn = (mask & 0x0100) != 0,
            NoHBand = (mask & 0x0200) != 0,
            NoVBand = (mask & 0x0400) != 0
        };
    }
}

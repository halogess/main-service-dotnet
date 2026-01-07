using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents table cell formatting properties extracted from OpenXML w:tcPr
/// </summary>
[Table("dokumen_format_table_cell")]
public class DokumenFormatTableCell
{
    [Key]
    [Column("dftc_id")]
    public uint DftcId { get; set; }

    // Merge/Span Properties
    
    /// <summary>
    /// Grid span (w:gridSpan/@w:val) - number of grid columns this cell spans
    /// </summary>
    [Column("dftc_grid_span")]
    public ushort? DftcGridSpan { get; set; }

    /// <summary>
    /// Vertical merge (w:vMerge/@w:val) - controls vertical cell merging
    /// Values: 'restart' (start of merge region), 'continue' (continuation of merge)
    /// </summary>
    [Column("dftc_v_merge")]
    [MaxLength(10)]
    public string? DftcVMerge { get; set; } // 'restart', 'continue'

    // Alignment Properties

    /// <summary>
    /// Vertical alignment (w:vAlign/@w:val) - vertical alignment of cell content
    /// Values: 'top', 'center', 'bottom'
    /// </summary>
    [Column("dftc_v_align")]
    [MaxLength(10)]
    public string? DftcVAlign { get; set; } // 'top', 'center', 'bottom'

    // Audit/Debug

    /// <summary>
    /// Raw XML of w:tcPr element for debugging purposes
    /// </summary>
    [Column("dftc_raw_tcpr_xml", TypeName = "longtext")]
    public string? DftcRawTcprXml { get; set; }
}

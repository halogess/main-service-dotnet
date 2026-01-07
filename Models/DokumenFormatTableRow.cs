using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents table row formatting properties extracted from OpenXML w:trPr
/// </summary>
[Table("dokumen_format_table_row")]
public class DokumenFormatTableRow
{
    [Key]
    [Column("dftr_id")]
    public uint DftrId { get; set; }

    // Table Header Row (w:tblHeader)
    // Specifies that this row should be repeated at the top of each page when table spans multiple pages
    [Column("dftr_tbl_header")]
    public bool DftrTblHeader { get; set; } = false;

    // Can't Split Row (w:cantSplit)
    // Specifies that the contents of this row cannot be split across multiple pages
    [Column("dftr_cant_split")]
    public bool DftrCantSplit { get; set; } = false;

    // Audit/debug - raw XML
    [Column("dftr_raw_trpr_xml", TypeName = "longtext")]
    public string? DftrRawTrprXml { get; set; }
}

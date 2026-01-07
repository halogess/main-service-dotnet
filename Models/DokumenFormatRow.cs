using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents table row formatting properties extracted from OpenXML w:trPr
/// </summary>
[Table("dokumen_format_row")]
public class DokumenFormatRow
{
    [Key]
    [Column("dfr_id")]
    public uint DfrId { get; set; }

    // Table Header Row (w:tblHeader)
    // Specifies that this row should be repeated at the top of each page when table spans multiple pages
    [Column("dfr_tbl_header")]
    public bool DfrTblHeader { get; set; } = false;

    // Can't Split Row (w:cantSplit)
    // Specifies that the contents of this row cannot be split across multiple pages
    [Column("dfr_cant_split")]
    public bool DfrCantSplit { get; set; } = false;

    // Audit/debug - raw XML
    [Column("dfr_raw_trpr_xml", TypeName = "longtext")]
    public string? DfrRawTrprXml { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents table formatting properties extracted from OpenXML w:tblPr
/// </summary>
[Table("dokumen_format_table")]
public class DokumenFormatTable
{
    [Key]
    [Column("dft_id")]
    public uint DftId { get; set; }

    // Style ID (optional)
    [Column("dft_tbl_style_id")]
    [MaxLength(128)]
    public string? DftTblStyleId { get; set; }

    // Table Width (w:tblW)
    [Column("dft_tbl_w_type")]
    [MaxLength(10)]
    public string? DftTblWType { get; set; } // 'auto', 'dxa', 'pct', 'nil'

    [Column("dft_tbl_w_twips")]
    public uint? DftTblWTwips { get; set; }

    [Column("dft_tbl_w_pct50")]
    public uint? DftTblWPct50 { get; set; }

    // Table Justification/Alignment (w:jc)
    [Column("dft_jc")]
    [MaxLength(10)]
    public string? DftJc { get; set; } // 'left', 'center', 'right', 'start', 'end'

    // Table Indentation (w:tblInd)
    // Note: Only 'dxa' and 'nil' are valid per OpenXML spec
    [Column("dft_tbl_ind_type")]
    [MaxLength(10)]
    public string? DftTblIndType { get; set; } // 'dxa', 'nil'

    [Column("dft_tbl_ind_twips")]
    public int? DftTblIndTwips { get; set; } // Signed integer (can be negative for outdent)

    [Column("dft_tbl_ind_pct50")]
    public uint? DftTblIndPct50 { get; set; }

    // Table Layout (w:tblLayout)
    [Column("dft_tbl_layout_type")]
    [MaxLength(10)]
    public string? DftTblLayoutType { get; set; } // 'fixed', 'autofit'

}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("dokumen_section")]
public class DokumenSection
{
    [Key]
    [Column("dsec_id")]
    public uint DsecId { get; set; }

    [Column("dokumen_id")]
    public uint? DokumenId { get; set; }

    [Column("dsec_index")]
    public uint? DsecIndex { get; set; }

    [Column("dsec_page_num_format")]
    [MaxLength(32)]
    public string? DsecPageNumFormat { get; set; }

    [Column("dsec_page_num_start")]
    public uint? DsecPageNumStart { get; set; }

    [Column("dsec_page_num_restart")]
    public bool DsecPageNumRestart { get; set; } = false;

    [Column("dsec_page_width_twips")]
    public uint? DsecPageWidthTwips { get; set; }

    [Column("dsec_page_height_twips")]
    public uint? DsecPageHeightTwips { get; set; }

    [Column("dsec_orientation")]
    [MaxLength(10)]
    public string? DsecOrientation { get; set; } // 'portrait' or 'landscape'

    [Column("dsec_margin_top_twips")]
    public uint? DsecMarginTopTwips { get; set; }

    [Column("dsec_margin_bottom_twips")]
    public uint? DsecMarginBottomTwips { get; set; }

    [Column("dsec_margin_left_twips")]
    public uint? DsecMarginLeftTwips { get; set; }

    [Column("dsec_margin_right_twips")]
    public uint? DsecMarginRightTwips { get; set; }

    [Column("dsec_header_margin_twips")]
    public uint? DsecHeaderMarginTwips { get; set; }

    [Column("dsec_footer_margin_twips")]
    public uint? DsecFooterMarginTwips { get; set; }

    [Column("dsec_gutter_twips")]
    public uint? DsecGutterTwips { get; set; }

    [Column("dsec_gutter_position")]
    [MaxLength(10)]
    public string? DsecGutterPosition { get; set; } // 'top' or 'left'
}

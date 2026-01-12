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

    /// <summary>
    /// Section break type: 'nextPage', 'continuous', 'evenPage', 'oddPage'
    /// </summary>
    [Column("dsec_type")]
    [MaxLength(50)]
    public string? DsecType { get; set; }

    /// <summary>
    /// Whether first page has different header/footer (Title Page)
    /// </summary>
    [Column("dsec_has_title_page")]
    public bool DsecHasTitlePage { get; set; } = false;

    /// <summary>
    /// Whether odd/even pages have different headers/footers
    /// </summary>
    [Column("dsec_different_odd_even")]
    public bool DsecDifferentOddEven { get; set; } = false;

    /// <summary>
    /// Page number format: 'decimal', 'lowerRoman', 'upperRoman', 'lowerLetter', 'upperLetter'
    /// </summary>
    [Column("dsec_page_num_format")]
    [MaxLength(32)]
    public string? DsecPageNumFormat { get; set; }

    /// <summary>
    /// Starting page number for this section (if set, numbering restarts)
    /// </summary>
    [Column("dsec_page_num_start")]
    public uint? DsecPageNumStart { get; set; }

    /// <summary>
    /// Page width in twips (1 inch = 1440 twips)
    /// </summary>
    [Column("dsec_page_width_twips")]
    public uint? DsecPageWidthTwips { get; set; }

    /// <summary>
    /// Page height in twips
    /// </summary>
    [Column("dsec_page_height_twips")]
    public uint? DsecPageHeightTwips { get; set; }

    /// <summary>
    /// Page orientation: 'portrait' or 'landscape'
    /// </summary>
    [Column("dsec_orientation")]
    [MaxLength(10)]
    public string? DsecOrientation { get; set; }

    [Column("dsec_margin_top_twips")]
    public uint? DsecMarginTopTwips { get; set; }

    [Column("dsec_margin_bottom_twips")]
    public uint? DsecMarginBottomTwips { get; set; }

    [Column("dsec_margin_left_twips")]
    public uint? DsecMarginLeftTwips { get; set; }

    [Column("dsec_margin_right_twips")]
    public uint? DsecMarginRightTwips { get; set; }

    /// <summary>
    /// Distance from top edge to header content
    /// </summary>
    [Column("dsec_header_margin_twips")]
    public uint? DsecHeaderMarginTwips { get; set; }

    /// <summary>
    /// Distance from bottom edge to footer content
    /// </summary>
    [Column("dsec_footer_margin_twips")]
    public uint? DsecFooterMarginTwips { get; set; }

    /// <summary>
    /// Gutter margin for binding
    /// </summary>
    [Column("dsec_gutter_twips")]
    public uint? DsecGutterTwips { get; set; }

    /// <summary>
    /// Gutter position: 'left', 'right', or 'top'
    /// </summary>
    [Column("dsec_gutter_position")]
    [MaxLength(10)]
    public string? DsecGutterPosition { get; set; }

    /// <summary>
    /// Number of text columns (default: 1)
    /// </summary>
    [Column("dsec_column_count")]
    public uint? DsecColumnCount { get; set; }

    // Navigation properties
    public virtual ICollection<DokumenPart> Parts { get; set; } = new List<DokumenPart>();
}

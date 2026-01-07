using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents paragraph formatting properties extracted from OpenXML w:pPr
/// </summary>
[Table("dokumen_format_paragraf")]
public class DokumenFormatParagraf
{
    [Key]
    [Column("dfp_id")]
    public uint DfpId { get; set; }

    // Style ID (optional)
    [Column("dfp_p_style_id")]
    [MaxLength(128)]
    public string? DfpPStyleId { get; set; }

    [Column("dfp_is_list")]
    public bool DfpIsList { get; set; } = false;

    // Pagination / layout toggles
    [Column("dfp_keep_next")]
    public bool DfpKeepNext { get; set; } = false;

    [Column("dfp_keep_lines")]
    public bool DfpKeepLines { get; set; } = false;

    [Column("dfp_page_break_before")]
    public bool DfpPageBreakBefore { get; set; } = false;

    [Column("dfp_widow_control")]
    public bool DfpWidowControl { get; set; } = true;

    [Column("dfp_suppress_line_numbers")]
    public bool DfpSuppressLineNumbers { get; set; } = false;

    [Column("dfp_suppress_auto_hyphens")]
    public bool DfpSuppressAutoHyphens { get; set; } = false;

    [Column("dfp_snap_to_grid")]
    public bool DfpSnapToGrid { get; set; } = false;

    [Column("dfp_adjust_right_ind")]
    public bool DfpAdjustRightInd { get; set; } = false;

    [Column("dfp_mirror_indents")]
    public bool DfpMirrorIndents { get; set; } = false;

    [Column("dfp_suppress_overlap")]
    public bool DfpSuppressOverlap { get; set; } = false;

    [Column("dfp_contextual_spacing")]
    public bool DfpContextualSpacing { get; set; } = false;

    // Spacing (twips; 1 pt = 20 twips)
    [Column("dfp_spacing_before_twips")]
    public uint? DfpSpacingBeforeTwips { get; set; }

    [Column("dfp_spacing_after_twips")]
    public uint? DfpSpacingAfterTwips { get; set; }

    [Column("dfp_spacing_after_autospacing")]
    public bool DfpSpacingAfterAutospacing { get; set; } = false;

    [Column("dfp_spacing_before_autospacing")]
    public bool DfpSpacingBeforeAutospacing { get; set; } = false;

    [Column("dfp_spacing_before_lines")]
    public uint? DfpSpacingBeforeLines { get; set; }

    [Column("dfp_spacing_after_lines")]
    public uint? DfpSpacingAfterLines { get; set; }

    [Column("dfp_spacing_line_twips")]
    public uint? DfpSpacingLineTwips { get; set; }

    [Column("dfp_spacing_line_rule")]
    [MaxLength(10)]
    public string? DfpSpacingLineRule { get; set; } // 'auto', 'atLeast', 'exact'

    // Indentation (twips)
    [Column("dfp_ind_left_twips")]
    public uint? DfpIndLeftTwips { get; set; }

    [Column("dfp_ind_right_twips")]
    public uint? DfpIndRightTwips { get; set; }

    [Column("dfp_ind_first_line_twips")]
    public uint? DfpIndFirstLineTwips { get; set; }

    [Column("dfp_ind_hanging_twips")]
    public uint? DfpIndHangingTwips { get; set; }

    [Column("dfp_ind_start_twips")]
    public uint? DfpIndStartTwips { get; set; }

    [Column("dfp_ind_end_twips")]
    public uint? DfpIndEndTwips { get; set; }

    [Column("dfp_ind_left_chars")]
    public uint? DfpIndLeftChars { get; set; }

    [Column("dfp_ind_right_chars")]
    public uint? DfpIndRightChars { get; set; }

    // Alignment
    [Column("dfp_jc")]
    [MaxLength(15)]
    public string? DfpJc { get; set; } // 'left', 'right', 'center', 'both', 'distribute'

    [Column("dfp_text_alignment")]
    [MaxLength(10)]
    public string? DfpTextAlignment { get; set; } // 'auto', 'baseline', 'top', 'center', 'bottom'

    [Column("dfp_word_wrap")]
    public bool DfpWordWrap { get; set; } = false;

    // Outline
    [Column("dfp_outline_lvl")]
    public byte? DfpOutlineLvl { get; set; }

    // Nested details (JSON)
    [Column("dfp_numpr_json", TypeName = "longtext")]
    public string? DfpNumprJson { get; set; }

    [Column("dfp_pbdr_json", TypeName = "longtext")]
    public string? DfpPbdrJson { get; set; }

    [Column("dfp_shd_json", TypeName = "longtext")]
    public string? DfpShdJson { get; set; }

    [Column("dfp_tabs_json", TypeName = "longtext")]
    public string? DfpTabsJson { get; set; }

    [Column("dfp_cnf_style_json", TypeName = "longtext")]
    public string? DfpCnfStyleJson { get; set; }

    [Column("dfp_para_mark_rpr_json", TypeName = "longtext")]
    public string? DfpParaMarkRprJson { get; set; }

    [Column("dfp_ppr_change_json", TypeName = "longtext")]
    public string? DfpPprChangeJson { get; set; }

    // List helper columns
    [Column("dfp_list_numId")]
    public uint? DfpListNumId { get; set; }

    [Column("dfp_list_ilvl")]
    public uint? DfpListIlvl { get; set; }

    // Audit/debug
    [Column("dfp_raw_ppr_xml", TypeName = "longtext")]
    public string? DfpRawPprXml { get; set; }
}

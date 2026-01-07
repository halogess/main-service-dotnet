using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents drawing/image formatting properties extracted from OpenXML w:drawing
/// </summary>
[Table("dokumen_format_drawing")]
public class DokumenFormatDrawing
{
    [Key]
    [Column("dfdr_id")]
    public ulong DfdrId { get; set; }

    // True if inline drawing, false if anchor (floating)
    [Column("dfdr_is_inline")]
    public bool DfdrIsInline { get; set; } = true;

    // Graphic type: 'picture', 'shape', 'textbox', 'chart', 'smartart', 'ole', 'unknown'
    [Column("dfdr_graphic_type")]
    [MaxLength(20)]
    public string DfdrGraphicType { get; set; } = "unknown";

    // Drawing extent - width in EMUs (English Metric Units)
    [Column("dfdr_cx_emu")]
    public ulong? DfdrCxEmu { get; set; }

    // Drawing extent - height in EMUs (English Metric Units)
    [Column("dfdr_cy_emu")]
    public ulong? DfdrCyEmu { get; set; }

    // Relationship ID for embedded image (r:embed)
    [Column("dfdr_rel_id")]
    [MaxLength(64)]
    public string? DfdrRelId { get; set; }

    // Anchor positioning properties as JSON (for floating drawings)
    [Column("dfdr_anchor_json", TypeName = "longtext")]
    public string? DfdrAnchorJson { get; set; }

    // Text wrapping properties as JSON
    [Column("dfdr_wrap_json", TypeName = "longtext")]
    public string? DfdrWrapJson { get; set; }

    // Preset shape type for shapes (rect, ellipse, rightArrow, star5, etc.)
    [Column("dfdr_preset_shape")]
    [MaxLength(50)]
    public string? DfdrPresetShape { get; set; }

    // Audit/debug - raw XML
    [Column("dfdr_raw_drawing_xml", TypeName = "longtext")]
    public string? DfdrRawDrawingXml { get; set; }
}

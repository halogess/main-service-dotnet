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

    // Drawing extent - width in EMUs (English Metric Units)
    [Column("dfdr_cx_emu")]
    public ulong? DfdrCxEmu { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents text (run) formatting properties extracted from OpenXML w:rPr
/// </summary>
[Table("dokumen_format_text")]
public class DokumenFormatText
{
    [Key]
    [Column("dftx_id")]
    public uint DftxId { get; set; }

    // Font name for ASCII characters (w:rFonts/@w:ascii)
    [Column("dftx_font_ascii")]
    [MaxLength(128)]
    public string? DftxFontAscii { get; set; }

    // Font size in half-points (w:sz/@w:val) - e.g., 24 = 12pt
    [Column("dftx_size_halfpt")]
    public ushort? DftxSizeHalfpt { get; set; }

    // Bold (w:b)
    [Column("dftx_bold")]
    public bool? DftxBold { get; set; }

    // Italic (w:i)
    [Column("dftx_italic")]
    public bool? DftxItalic { get; set; }

    // Underline style (w:u/@w:val)
    [Column("dftx_underline")]
    [MaxLength(10)]
    public string? DftxUnderline { get; set; } // 'none', 'single', 'double', 'dotted', 'dash', 'wavy'

}

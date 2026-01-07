using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents field formatting properties extracted from OpenXML complex fields (w:fldChar, w:instrText)
/// </summary>
[Table("dokumen_format_field")]
public class DokumenFormatField
{
    [Key]
    [Column("dffd_id")]
    public ulong DffdId { get; set; }

    // Field type enum: PAGE, NUMPAGES, SECTION, SECTIONPAGES, SEQ, REF, PAGEREF, HYPERLINK, TOC, DATE, TIME, UNKNOWN
    [Column("dffd_field_type")]
    [MaxLength(20)]
    public string DffdFieldType { get; set; } = "UNKNOWN";

    // Field instruction text (the code inside the field)
    [Column("dffd_instr_text", TypeName = "text")]
    public string? DffdInstrText { get; set; }

    // Field result text (the displayed value)
    [Column("dffd_result_text", TypeName = "text")]
    public string? DffdResultText { get; set; }

    // Field is locked (cannot be updated)
    [Column("dffd_is_locked")]
    public bool DffdIsLocked { get; set; } = false;

    // Field is dirty (needs to be recalculated)
    [Column("dffd_is_dirty")]
    public bool DffdIsDirty { get; set; } = false;
}

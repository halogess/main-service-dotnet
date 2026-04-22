using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents a footnote or endnote scoped to a specific extracted reference target.
/// </summary>
[Table("dokumen_note")]
public class DokumenNote
{
    [Key]
    [Column("dnote_id")]
    public uint DnoteId { get; set; }

    [Column("dnote_ref_tipe")]
    [MaxLength(16)]
    public string DnoteRefTipe { get; set; } = "dokumen";

    [Column("dnote_ref_id")]
    public uint DnoteRefId { get; set; }

    /// <summary>
    /// Link to the element that contains the note reference
    /// </summary>
    [Column("delemen_id")]
    public ulong? DelemenId { get; set; }

    /// <summary>
    /// Note kind: 'footnote' or 'endnote'
    /// </summary>
    [Column("dnote_kind")]
    [MaxLength(10)]
    public string DnoteKind { get; set; } = null!;

    /// <summary>
    /// Note type: 'normal', 'separator', 'continuationSeparator'
    /// </summary>
    [Column("dnote_type")]
    [MaxLength(30)]
    public string? DnoteType { get; set; } = "normal";

    /// <summary>
    /// Visible note number/id from OpenXML for normal notes.
    /// Separator-like notes may leave this null.
    /// </summary>
    [Column("dnote_number")]
    public uint? DnoteNumber { get; set; }

    /// <summary>
    /// Full structured content as JSON
    /// </summary>
    [Column("dnote_json_tree", TypeName = "longtext")]
    public string? DnoteJsonTree { get; set; }

    // Navigation properties
    public virtual DokumenElemen? Elemen { get; set; }
}

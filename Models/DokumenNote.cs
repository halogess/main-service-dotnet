using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents a footnote or endnote in a document (document-wide, not section-specific)
/// </summary>
[Table("dokumen_note")]
public class DokumenNote
{
    [Key]
    [Column("dnote_id")]
    public uint DnoteId { get; set; }

    /// <summary>
    /// Link to the parent document
    /// </summary>
    [Column("dokumen_id")]
    public uint DokumenId { get; set; }

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
    /// Full structured content as JSON
    /// </summary>
    [Column("dnote_json_tree", TypeName = "longtext")]
    public string? DnoteJsonTree { get; set; }

    /// <summary>
    /// Raw XML of the note
    /// </summary>
    [Column("dnote_xml", TypeName = "longtext")]
    public string DnoteXml { get; set; } = string.Empty;

    // Navigation properties
    public virtual DokumenElemen? Elemen { get; set; }
}

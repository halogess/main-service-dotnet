using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents a block element within a document (paragraph, table, etc.)
/// </summary>
[Table("dokumen_elemen")]
public class DokumenElemen
{
    [Key]
    [Column("delemen_id")]
    public ulong DelemenId { get; set; }

    /// <summary>
    /// Link to the parent document
    /// </summary>
    [Column("dokumen_id")]
    public uint? DokumenId { get; set; }

    /// <summary>
    /// Order within the document (1-indexed)
    /// </summary>
    [Column("delemen_sequence")]
    public uint? DelemenSequence { get; set; }

    /// <summary>
    /// Element type: 'paragraph', 'table', 'image', 'math', 'sdt', etc.
    /// </summary>
    [Column("delemen_type")]
    [MaxLength(100)]
    public string? DelemenType { get; set; }

    /// <summary>
    /// Full structured content as JSON
    /// </summary>
    [Column("delemen_json_tree", TypeName = "longtext")]
    public string? DelemenJsonTree { get; set; }

    /// <summary>
    /// Raw XML of the element
    /// </summary>
    [Column("delemen_xml", TypeName = "longtext")]
    public string DelemenXml { get; set; } = string.Empty;

    /// <summary>
    /// Link to the containing part (body, header, footer, etc.)
    /// </summary>
    [Column("dpart_id")]
    public uint? DpartId { get; set; }

    // Navigation properties
    public virtual DokumenPart? Part { get; set; }
    public virtual ICollection<DokumenNote> Notes { get; set; } = new List<DokumenNote>();
}

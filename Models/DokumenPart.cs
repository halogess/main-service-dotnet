using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

/// <summary>
/// Represents a document part that can contain block elements.
/// Types: body, header, footer (section-specific)
/// Note: footnote/endnote are document-wide and use separate models
/// </summary>
[Table("dokumen_part")]
public class DokumenPart
{
    [Key]
    [Column("dpart_id")]
    public uint DpartId { get; set; }

    /// <summary>
    /// Link to section - ALL parts are tied to a section
    /// dokumen_id can be derived via Section.DokumenId
    /// </summary>
    [Column("dsec_id")]
    public uint DsecId { get; set; }

    /// <summary>
    /// Part type: 'body', 'header', 'footer'
    /// </summary>
    [Column("dpart_type")]
    [MaxLength(20)]
    public string DpartType { get; set; } = null!;

    /// <summary>
    /// Position for header/footer: 'default', 'first', 'even'
    /// NULL for body
    /// </summary>
    [Column("dpart_position")]
    [MaxLength(10)]
    public string? DpartPosition { get; set; }

    // Navigation properties
    public virtual DokumenSection? Section { get; set; }
    public virtual ICollection<DokumenElemen> Elements { get; set; } = new List<DokumenElemen>();
}

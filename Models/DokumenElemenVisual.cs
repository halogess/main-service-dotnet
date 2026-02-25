using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("dokumen_elemen_visual")]
public class DokumenElemenVisual
{
    [Key]
    [Column("dev_id")]
    public ulong DevId { get; set; }

    [Column("dev_ref_tipe")]
    [MaxLength(10)]
    public string? DevRefTipe { get; set; }

    [Column("dev_ref_id")]
    public uint? DevRefId { get; set; }

    [Column("dev_page")]
    public uint? DevPage { get; set; }

    [Column("dokumen_elemen_id")]
    public ulong? DokumenElemenId { get; set; }

    [Column("dev_bbox_x0")]
    public float? DevBboxX0 { get; set; }

    [Column("dev_bbox_y0")]
    public float? DevBboxY0 { get; set; }

    [Column("dev_bbox_x1")]
    public float? DevBboxX1 { get; set; }

    [Column("dev_bbox_y1")]
    public float? DevBboxY1 { get; set; }

    [Column("dev_label")]
    [MaxLength(50)]
    public string? DevLabel { get; set; }

    [Column("dev_text", TypeName = "longtext")]
    public string? DevText { get; set; }

    [Column("dev_label_struktural")]
    [MaxLength(50)]
    public string? DevLabelStruktural { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValidasiTugasAkhir.MainService.Models;

[Table("aka_ta_proposal")]
public class Proposal
{
    [Key]
    [Column("proposal_kode")]
    [MaxLength(6)]
    public string ProposalKode { get; set; } = null!;

    [Column("mhs_nrp")]
    [MaxLength(9)]
    public string? MhsNrp { get; set; }

    [Column("proposal_judul_baru")]
    [MaxLength(300)]
    public string? ProposalJudulBaru { get; set; }

    [Column("proposal_tgl_doc")]
    public DateTime? ProposalTglDoc { get; set; }

    [Column("proposal_perpanjangan")]
    public short? ProposalPerpanjangan { get; set; }

    [Column("dosen_pembimbing")]
    [MaxLength(6)]
    public string? DosenPembimbing { get; set; }

    [Column("dosen_co_pembimbing")]
    [MaxLength(6)]
    public string? DosenCoPembimbing { get; set; }
}

using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class DokumenFailureStatusHelperTests
{
    [Fact]
    public async Task TryMarkDokumenTidakLolosAsync_ShouldUpdateDokumenAndReturnNotification()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 7,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab1.docx",
            DokumenStatus = "diproses",
            DokumenSkor = 91,
            DokumenSkorMinimal = 80,
            DokumenJumlahKesalahan = 2,
            DokumenReportPath = "dokumen/05111740000123/7/report/report.pdf"
        });
        await db.SaveChangesAsync();

        var notification = await DokumenFailureStatusHelper.TryMarkDokumenTidakLolosAsync(
            db,
            7,
            CancellationToken.None);

        Assert.NotNull(notification);
        Assert.Equal("05111740000123", notification.Value.Nrp);
        Assert.Equal(7, notification.Value.DokumenId);

        var dokumen = await db.Dokumens.SingleAsync();
        Assert.Equal("tidak_lolos", dokumen.DokumenStatus);
        Assert.Null(dokumen.DokumenSkor);
        Assert.Null(dokumen.DokumenSkorMinimal);
        Assert.Null(dokumen.DokumenJumlahKesalahan);
        Assert.Null(dokumen.DokumenReportPath);
        Assert.NotNull(dokumen.DokumenUpdatedAt);
    }

    [Fact]
    public async Task TryMarkDokumenTidakLolosAsync_ShouldIgnoreCancelledDokumen()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 8,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab2.docx",
            DokumenStatus = "dibatalkan"
        });
        await db.SaveChangesAsync();

        var notification = await DokumenFailureStatusHelper.TryMarkDokumenTidakLolosAsync(
            db,
            8,
            CancellationToken.None);

        Assert.Null(notification);

        var dokumen = await db.Dokumens.SingleAsync();
        Assert.Equal("dibatalkan", dokumen.DokumenStatus);
    }
}

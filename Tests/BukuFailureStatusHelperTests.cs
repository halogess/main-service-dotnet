using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class BukuFailureStatusHelperTests
{
    [Fact]
    public async Task TryMarkBukuTidakLolosAsync_ShouldUpdateBookAndReturnNotification()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 5,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Uji",
            BukuStatus = "diproses",
            BukuSkor = 88,
            BukuJumlahKesalahan = 3,
            BukuReportPath = "buku/05111740000123/5/report/report.pdf"
        });
        await db.SaveChangesAsync();

        var notification = await BukuFailureStatusHelper.TryMarkBukuTidakLolosAsync(
            db,
            5,
            CancellationToken.None);

        Assert.NotNull(notification);
        Assert.Equal("05111740000123", notification.Value.Nrp);
        Assert.Equal(5, notification.Value.BukuId);

        var buku = await db.Bukus.SingleAsync();
        Assert.Equal("tidak_lolos", buku.BukuStatus);
        Assert.Null(buku.BukuSkor);
        Assert.Null(buku.BukuJumlahKesalahan);
        Assert.Null(buku.BukuReportPath);
        Assert.NotNull(buku.BukuUpdatedAt);
    }

    [Fact]
    public async Task TryMarkBukuTidakLolosAsync_ShouldIgnoreCancelledBook()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 6,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Batal",
            BukuStatus = "dibatalkan"
        });
        await db.SaveChangesAsync();

        var notification = await BukuFailureStatusHelper.TryMarkBukuTidakLolosAsync(
            db,
            6,
            CancellationToken.None);

        Assert.Null(notification);

        var buku = await db.Bukus.SingleAsync();
        Assert.Equal("dibatalkan", buku.BukuStatus);
    }
}

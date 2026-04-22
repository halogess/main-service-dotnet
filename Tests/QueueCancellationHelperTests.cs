using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class QueueCancellationHelperTests
{
    [Fact]
    public void ClearActiveStages_ShouldOnlyClearInQueueAndProcessingStages()
    {
        var updatedAt = new DateTime(2026, 4, 15, 10, 30, 0, DateTimeKind.Local);
        var queue = new Antrian
        {
            AntrianExtractionStatus = "in_queue",
            AntrianLabelingStatus = "failed",
            AntrianValidationStatus = "processing",
            AntrianErrorMessage = "Error lama"
        };

        var changed = QueueCancellationHelper.ClearActiveStages(
            queue,
            QueueCancellationHelper.CancelledByUserMessage,
            updatedAt);

        Assert.True(changed);
        Assert.Null(queue.AntrianExtractionStatus);
        Assert.Equal("failed", queue.AntrianLabelingStatus);
        Assert.Null(queue.AntrianValidationStatus);
        Assert.Equal(QueueCancellationHelper.CancelledByUserMessage, queue.AntrianErrorMessage);
        Assert.Equal(updatedAt, queue.AntrianUpdatedAt);
    }

    [Fact]
    public async Task TryHandleCancelledResourceAsync_ShouldClearCancelledDokumenQueue()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 11,
            MhsNrp = "05111740000123",
            DokumenFilename = "Bab1.docx",
            DokumenStatus = "dibatalkan"
        });
        var queue = new Antrian
        {
            AntrianId = 111,
            AntrianTipe = "dokumen",
            DokumenId = 11,
            AntrianExtractionStatus = "in_queue"
        };
        db.Antrians.Add(queue);
        await db.SaveChangesAsync();

        var handled = await QueueCancellationHelper.TryHandleCancelledResourceAsync(
            db,
            queue,
            CancellationToken.None);

        Assert.True(handled);

        var savedQueue = await db.Antrians.SingleAsync();
        Assert.Null(savedQueue.AntrianExtractionStatus);
        Assert.Equal(QueueCancellationHelper.CancelledByUserMessage, savedQueue.AntrianErrorMessage);
        Assert.NotNull(savedQueue.AntrianUpdatedAt);
    }

    [Fact]
    public async Task TryHandleCancelledResourceAsync_ShouldClearCancelledBukuQueueUsingBukuId()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 12,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Uji",
            BukuStatus = "dibatalkan"
        });
        var queue = new Antrian
        {
            AntrianId = 121,
            AntrianTipe = "buku",
            BukuId = 12,
            AntrianLabelingStatus = "processing"
        };
        db.Antrians.Add(queue);
        await db.SaveChangesAsync();

        var handled = await QueueCancellationHelper.TryHandleCancelledResourceAsync(
            db,
            queue,
            CancellationToken.None);

        Assert.True(handled);

        var savedQueue = await db.Antrians.SingleAsync();
        Assert.Null(savedQueue.AntrianLabelingStatus);
        Assert.Equal(QueueCancellationHelper.CancelledByUserMessage, savedQueue.AntrianErrorMessage);
    }

    [Fact]
    public async Task TryHandleCancelledResourceAsync_ShouldClearCancelledBukuQueueUsingLegacyBabId()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Bukus.Add(new Buku
        {
            BukuId = 13,
            MhsNrp = "05111740000123",
            BukuJudul = "Buku Legacy",
            BukuStatus = "dibatalkan"
        });
        db.Babs.Add(new Bab
        {
            BabId = 131,
            BukuId = 13,
            BabOrder = 1,
            BabFilename = "BAB I.docx"
        });
        var queue = new Antrian
        {
            AntrianId = 132,
            AntrianTipe = "buku",
            BabId = 131,
            AntrianValidationStatus = "in_queue"
        };
        db.Antrians.Add(queue);
        await db.SaveChangesAsync();

        var handled = await QueueCancellationHelper.TryHandleCancelledResourceAsync(
            db,
            queue,
            CancellationToken.None);

        Assert.True(handled);

        var savedQueue = await db.Antrians.SingleAsync();
        Assert.Null(savedQueue.AntrianValidationStatus);
        Assert.Equal(QueueCancellationHelper.CancelledByUserMessage, savedQueue.AntrianErrorMessage);
    }
}

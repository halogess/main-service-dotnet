using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanServiceTests
{
    [Fact]
    public async Task GetByIdWithDetailsAsync_ShouldReturnDetailsOrderedByAturanDetailId()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Aturans.Add(new Aturan
        {
            AturanId = 1,
            AturanVersi = "v1",
            AturanStatus = AturanStatusValues.Aktif
        });
        db.AturanDetails.AddRange(
            new AturanDetail
            {
                AturanDetailId = 20,
                AturanId = 1,
                AturanDetailKey = "kedua"
            },
            new AturanDetail
            {
                AturanDetailId = 10,
                AturanId = 1,
                AturanDetailKey = "pertama"
            });
        await db.SaveChangesAsync();

        var service = new AturanService(
            db,
            Mock.Of<IFileService>(),
            Mock.Of<IExtractionArtifactCleanupService>(),
            Mock.Of<ILogger<AturanService>>());

        var result = await service.GetByIdWithDetailsAsync(1);

        Assert.NotNull(result);
        Assert.Equal([(uint)10, (uint)20], result!.Details.Select(d => d.AturanDetailId).ToList());
    }
}

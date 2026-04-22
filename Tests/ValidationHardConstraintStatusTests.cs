using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ValidationHardConstraintStatusTests
{
    [Fact]
    public void ValidationResult_ShouldMarkErrorAsHardConstraint_WhenHardCheckFails()
    {
        var result = new ValidationResult();

        result.IncrementTotalChecks(isHardConstraint: true);
        result.Errors.Add(new ValidationError
        {
            Category = "Isi Buku",
            Field = "paragraf",
            Message = "Hard constraint gagal"
        });

        var error = Assert.Single(result.Errors);
        Assert.True(error.IsHardConstraint);
        Assert.True(result.HasHardConstraintViolation);
    }

    [Fact]
    public void ValidationResult_ShouldNotLeakHardConstraintFlag_AfterPassedCheck()
    {
        var result = new ValidationResult();

        result.IncrementTotalChecks(isHardConstraint: true);
        result.IncrementPassedChecks();
        result.Errors.Add(new ValidationError
        {
            Category = "Isi Buku",
            Field = "judul_bab",
            Message = "Error tambahan di luar check yang sudah lolos"
        });

        var error = Assert.Single(result.Errors);
        Assert.False(error.IsHardConstraint);
        Assert.False(result.HasHardConstraintViolation);
    }

    [Fact]
    public void IsValidationPassed_ShouldReturnFalse_WhenHardConstraintViolationExists()
    {
        var method = typeof(ValidationQueueBackgroundService).GetMethod(
            "IsValidationPassed",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var isPassed = Assert.IsType<bool>(method!.Invoke(null, [90, 80, true]));
        Assert.False(isPassed);
    }

    [Fact]
    public void IsBabValidationPassed_ShouldReturnFalse_WhenBabHasHardConstraintViolation()
    {
        var method = typeof(ValidationQueueBackgroundService).GetMethod(
            "IsBabValidationPassed",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var bab = new Bab
        {
            BabSkor = 90,
            BabSkorMinimal = 80,
            BabHasHardConstraintViolation = true
        };

        var isPassed = Assert.IsType<bool>(method!.Invoke(null, [bab, 80]));
        Assert.False(isPassed);
    }

    [Fact]
    public void IsValidationPassed_ShouldReturnTrue_WhenScoreMeetsMinimumAndNoHardConstraintViolation()
    {
        var method = typeof(ValidationQueueBackgroundService).GetMethod(
            "IsValidationPassed",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var isPassed = Assert.IsType<bool>(method!.Invoke(null, [90, 80, false]));
        Assert.True(isPassed);
    }

    [Fact]
    public async Task EnrichAndStoreErrorsAsync_ShouldPersistHardConstraintFlag()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Dokumens.Add(new Dokumen
        {
            DokumenId = 1,
            MhsNrp = "05111740000123",
            DokumenFilename = "uji.docx",
            DokumenStatus = "diproses"
        });
        await db.SaveChangesAsync();

        using var provider = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:MaxBatchRetries"] = "0",
                ["Gemini:BatchDelaySeconds"] = "0",
                ["Gemini:MaxParallelBatches"] = "1"
            })
            .Build();

        var service = new ValidationQueueBackgroundService(
            provider,
            NullLogger<ValidationQueueBackgroundService>.Instance,
            configuration);

        var errors = new List<ValidationError>
        {
            new()
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = "Hard constraint gagal",
                IsHardConstraint = true
            }
        };

        var method = typeof(ValidationQueueBackgroundService).GetMethod(
            "EnrichAndStoreErrorsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = method!.Invoke(
            service,
            [db, 1u, 1u, "dokumen", 1u, KesalahanRefTipe.dokumen, 1u, errors, CancellationToken.None, null]) as Task<int>;

        Assert.NotNull(task);
        var storedCount = await task!;
        await db.SaveChangesAsync();

        Assert.Equal(1, storedCount);
        var detail = Assert.Single(db.KesalahanDetails);
        Assert.True(detail.KesalahanIsHardConstraint);
    }
}

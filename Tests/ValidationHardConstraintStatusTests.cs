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
    public void ValidationResult_EffectiveScore_ShouldIgnoreErrorsWithoutKnownLocation()
    {
        var result = new ValidationResult();

        result.IncrementTotalChecks(isHardConstraint: true);
        result.Errors.Add(new ValidationError
        {
            Category = "Isi Buku",
            Field = "paragraf",
            Message = "Hard constraint tanpa lokasi"
        });

        result.IncrementTotalChecks();
        result.Errors.Add(new ValidationError
        {
            Category = "Isi Buku",
            Field = "judul",
            Message = "Kesalahan berlokasi",
            Locations =
            [
                new ErrorLocation { HalamanKe = 3 }
            ]
        });

        var locatedErrors = result.Errors
            .Where(error => error.Locations.Any(location => location.HalamanKe > 0))
            .ToList();

        Assert.Equal(50m, result.GetEffectiveScore(locatedErrors));
        Assert.False(result.HasEffectiveHardConstraintViolation(locatedErrors));
    }

    [Fact]
    public void ValidationResult_EffectiveScore_ShouldPassWhenOnlyHardConstraintErrorHasNoLocation()
    {
        var result = new ValidationResult();

        result.IncrementTotalChecks(isHardConstraint: true);
        result.Errors.Add(new ValidationError
        {
            Category = "Isi Buku",
            Field = "paragraf",
            Message = "Hard constraint tanpa lokasi"
        });

        var locatedErrors = result.Errors
            .Where(error => error.Locations.Any(location => location.HalamanKe > 0))
            .ToList();

        Assert.Equal(100m, result.GetEffectiveScore(locatedErrors));
        Assert.False(result.HasEffectiveHardConstraintViolation(locatedErrors));
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
                IsHardConstraint = true,
                Locations =
                [
                    new ErrorLocation { HalamanKe = 2 }
                ]
            }
        };

        var storedCount = await InvokeEnrichAndStoreErrorsAsync(service, db, errors);
        await db.SaveChangesAsync();

        Assert.Equal(1, storedCount);
        var kesalahan = Assert.Single(db.Kesalahans);
        Assert.Contains("\"halaman_ke\":2", kesalahan.KesalahanLokasi);
        var detail = Assert.Single(db.KesalahanDetails);
        Assert.True(detail.KesalahanIsHardConstraint);
    }

    [Fact]
    public async Task EnrichAndStoreErrorsAsync_ShouldSkipErrorWithoutKnownLocation()
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

        var service = CreateQueueService();
        var errors = new List<ValidationError>
        {
            new()
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = "Kesalahan tanpa lokasi",
                IsHardConstraint = true
            }
        };

        var storedCount = await InvokeEnrichAndStoreErrorsAsync(service, db, errors);
        await db.SaveChangesAsync();

        Assert.Equal(0, storedCount);
        Assert.Empty(db.Kesalahans);
        Assert.Empty(db.KesalahanDetails);
    }

    [Fact]
    public async Task EnrichAndStoreErrorsAsync_ShouldSkipBabErrorWithoutKnownLocation()
    {
        await using var db = ControllerTestHelpers.CreateDbContext();
        db.Babs.Add(new Bab
        {
            BabId = 9,
            BukuId = 1,
            BabOrder = 1,
            BabFilename = "BAB I.docx"
        });
        await db.SaveChangesAsync();

        var service = CreateQueueService();
        var errors = new List<ValidationError>
        {
            new()
            {
                Category = "Isi Buku",
                Field = "paragraf",
                Message = "Kesalahan BAB tanpa lokasi"
            }
        };

        var storedCount = await InvokeEnrichAndStoreErrorsAsync(
            service,
            db,
            errors,
            dokumenId: 9u,
            kesalahanRefTipe: KesalahanRefTipe.bab,
            kesalahanRefId: 9u,
            babOrder: 1);
        await db.SaveChangesAsync();

        Assert.Equal(0, storedCount);
        Assert.Empty(db.Kesalahans);
        Assert.Empty(db.KesalahanDetails);
    }

    private static ValidationQueueBackgroundService CreateQueueService()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:MaxBatchRetries"] = "0",
                ["Gemini:BatchDelaySeconds"] = "0",
                ["Gemini:MaxParallelBatches"] = "1"
            })
            .Build();

        return new ValidationQueueBackgroundService(
            provider,
            NullLogger<ValidationQueueBackgroundService>.Instance,
            configuration);
    }

    private static async Task<int> InvokeEnrichAndStoreErrorsAsync(
        ValidationQueueBackgroundService service,
        KorektorBukuDbContext db,
        IReadOnlyList<ValidationError> errors,
        uint dokumenId = 1u,
        KesalahanRefTipe kesalahanRefTipe = KesalahanRefTipe.dokumen,
        uint kesalahanRefId = 1u,
        byte? babOrder = null)
    {
        var method = typeof(ValidationQueueBackgroundService).GetMethod(
            "EnrichAndStoreErrorsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = method!.Invoke(
            service,
            [db, 1u, dokumenId, "dokumen", dokumenId, kesalahanRefTipe, kesalahanRefId, errors, CancellationToken.None, babOrder]) as Task;

        Assert.NotNull(task);
        await task!;

        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        var result = resultProperty!.GetValue(task);
        Assert.NotNull(result);

        var storedDetailCountProperty = result!.GetType().GetProperty("StoredDetailCount");
        Assert.NotNull(storedDetailCountProperty);
        return Assert.IsType<int>(storedDetailCountProperty!.GetValue(result));
    }
}

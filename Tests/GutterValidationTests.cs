using System.Reflection;
using Microsoft.Extensions.Logging;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class GutterValidationTests
{
    [Fact]
    public void ValidateGutter_ShouldSkipPositionCheck_WhenExpectedGutterIsZero()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        using var db = ControllerTestHelpers.CreateDbContext();
        var service = new ValidationService(db, loggerFactory.CreateLogger<ValidationService>());
        var result = new ValidationResult();
        var section = new DokumenSection
        {
            DsecId = 1,
            DsecGutterTwips = 0,
            DsecGutterPosition = "top"
        };
        var rule = new GutterRule
        {
            Size = new DecimalRuleValue { Value = 0m },
            Position = new RuleValue<string> { Value = "left" }
        };

        InvokeValidateGutter(service, result, section, rule);

        Assert.Equal(1, result.TotalChecks);
        Assert.DoesNotContain(result.Errors, error => error.Field == "gutter_position");
    }

    [Fact]
    public void ValidateGutter_ShouldValidatePosition_WhenExpectedGutterIsPositive()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        using var db = ControllerTestHelpers.CreateDbContext();
        var service = new ValidationService(db, loggerFactory.CreateLogger<ValidationService>());
        var result = new ValidationResult();
        var section = new DokumenSection
        {
            DsecId = 1,
            DsecGutterTwips = 567,
            DsecGutterPosition = "top"
        };
        var rule = new GutterRule
        {
            Size = new DecimalRuleValue { Value = 1m },
            Position = new RuleValue<string> { Value = "left" }
        };

        InvokeValidateGutter(service, result, section, rule);

        Assert.Equal(2, result.TotalChecks);
        Assert.Contains(result.Errors, error => error.Field == "gutter_position");
    }

    private static void InvokeValidateGutter(
        ValidationService service,
        ValidationResult result,
        DokumenSection section,
        GutterRule rule)
    {
        var method = typeof(ValidationService).GetMethod(
            "ValidateGutter",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        method!.Invoke(service, new object[]
        {
            result,
            section,
            rule,
            1,
            "isi",
            new Dictionary<uint, int>()
        });
    }
}

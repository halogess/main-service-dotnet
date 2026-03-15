using System.Text.Json;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class AturanDetailJsonNormalizerTests
{
    [Fact]
    public void TryNormalize_ShouldAddIsHardConstraintToExistingWrapper()
    {
        var rawJson = """
                      {"font_name":{"value":"Times New Roman","is_editable":"false"}}
                      """;

        var success = AturanDetailJsonNormalizer.TryNormalize(rawJson, out var normalizedJson, out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(
            """{"font_name":{"value":"Times New Roman","is_editable":false,"is_hard_constraint":false}}""",
            normalizedJson);
    }

    [Fact]
    public void TryNormalize_ShouldConvertFlagStringsToBoolean()
    {
        var rawJson = """
                      {"flag":{"value":1,"is_editable":"false","is_hard_constraint":"true"}}
                      """;

        var success = AturanDetailJsonNormalizer.TryNormalize(rawJson, out var normalizedJson, out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(
            """{"flag":{"value":1,"is_editable":false,"is_hard_constraint":true}}""",
            normalizedJson);
    }

    [Fact]
    public void TryNormalize_ShouldWrapRawLeafValues()
    {
        var rawJson = """
                      {"continue":false,"first_page":{"is_empty":true}}
                      """;

        var success = AturanDetailJsonNormalizer.TryNormalize(rawJson, out var normalizedJson, out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(
            """{"continue":{"value":false,"is_editable":false,"is_hard_constraint":false},"first_page":{"is_empty":{"value":true,"is_editable":false,"is_hard_constraint":false}}}""",
            normalizedJson);
    }

    [Fact]
    public void TryNormalize_ShouldKeepObjectInsideValueUntouched()
    {
        var rawJson = """
                      {"paper":{"a4_portrait":{"value":{"top":4,"left":3},"is_editable":true}}}
                      """;

        var success = AturanDetailJsonNormalizer.TryNormalize(rawJson, out var normalizedJson, out var errorMessage);

        Assert.True(success, errorMessage);

        using var document = JsonDocument.Parse(normalizedJson!);
        var wrapper = document.RootElement
            .GetProperty("paper")
            .GetProperty("a4_portrait");

        Assert.True(wrapper.GetProperty("is_editable").GetBoolean());
        Assert.False(wrapper.GetProperty("is_hard_constraint").GetBoolean());

        var value = wrapper.GetProperty("value");
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.Equal(4, value.GetProperty("top").GetInt32());
        Assert.Equal(3, value.GetProperty("left").GetInt32());
    }
}

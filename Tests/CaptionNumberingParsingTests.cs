using System.Reflection;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class CaptionNumberingParsingTests
{
    [Fact]
    public void TryParseCaptionNumbering_ShouldHandleMovedSequenceValueBeforePrefix()
    {
        var (success, number, title) = InvokeTryParseCaptionNumbering(
            "1Gambar 3. Tampilan Programming Hub",
            "Gambar");

        Assert.True(success);
        Assert.Equal("Gambar 3.1", number);
        Assert.Equal("Tampilan Programming Hub", title);
    }

    [Fact]
    public void TryParseCaptionNumbering_ShouldHandleLegacyMergedPrefixWithoutSpace()
    {
        var (success, number, title) = InvokeTryParseCaptionNumbering(
            "8Gambar 3. Heatmap tugas pertama",
            "Gambar");

        Assert.True(success);
        Assert.Equal("Gambar 3.8", number);
        Assert.Equal("Heatmap tugas pertama", title);
    }

    private static (bool Success, string Number, string? Title) InvokeTryParseCaptionNumbering(
        string text,
        string prefix)
    {
        var method = typeof(ValidationService).GetMethod(
            "TryParseCaptionNumbering",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var args = new object?[] { text, prefix, null, null };
        var success = (bool?)method!.Invoke(null, args) ?? false;
        var number = args[2] as string ?? string.Empty;
        var title = args[3] as string;
        return (success, number, title);
    }
}

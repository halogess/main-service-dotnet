using System.Reflection;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class SubchapterFollowupClassificationTests
{
    [Fact]
    public void ShouldTreatAsParagraphAfterSubchapter_ShouldAcceptAmbiguousPrimaryLabel_WhenVisualHasParagraf()
    {
        var result = InvokeShouldTreatAsParagraphAfterSubchapter(
            elementType: "paragraph",
            primaryLabel: "judul_kode",
            visualLabels: ["paragraf", "judul_kode"],
            hasContent: true);

        Assert.True(result);
    }

    [Fact]
    public void ShouldTreatAsParagraphAfterSubchapter_ShouldRejectJudulKodeWithoutParagraphSignal()
    {
        var result = InvokeShouldTreatAsParagraphAfterSubchapter(
            elementType: "paragraph",
            primaryLabel: "judul_kode",
            visualLabels: ["judul_kode"],
            hasContent: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldTreatAsParagraphAfterSubchapter_ShouldAcceptUnlabeledParagraphFallback()
    {
        var result = InvokeShouldTreatAsParagraphAfterSubchapter(
            elementType: "paragraph",
            primaryLabel: null,
            visualLabels: [],
            hasContent: true);

        Assert.True(result);
    }

    private static bool InvokeShouldTreatAsParagraphAfterSubchapter(
        string? elementType,
        string? primaryLabel,
        IReadOnlyCollection<string> visualLabels,
        bool hasContent)
    {
        var method = typeof(ValidationService).GetMethod(
            "ShouldTreatAsParagraphAfterSubchapter",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var value = method!.Invoke(null, [elementType, primaryLabel, visualLabels, hasContent]);
        return Assert.IsType<bool>(value);
    }
}

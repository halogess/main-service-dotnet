using System.Reflection;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class NomorHalamanNormalizationTests
{
    public static TheoryData<string, string, bool?, bool?, bool?, string?> LegacyRules => new()
    {
        {
            "nomor_halaman_isi",
            """
            {"continue":false,"different_first_page":"True","first_page":{"is_empty":false,"position":{"location":"header","alignment":"right","indentation":"None"},"number_format":{"type":"arabic","prefix":"None"},"text_style":{"font_name":"Times New Roman","font_size":12,"line_spacing":1,"spacing_before":0,"spacing_after":""},"allow_other_content":false},"default_page":{"position":{"location":"footer","alignment":"center","indentation":"None"},"number_format":{"type":"arabic","prefix":"None"},"text_style":{"font_name":"Times New Roman","font_size":12,"line_spacing":1,"spacing_before":0,"spacing_after":""},"allow_other_content":false}}
            """,
            false,
            true,
            false,
            "decimal"
        },
        {
            "nomor_halaman_akhir",
            """
            {"continue":true}
            """,
            true,
            null,
            null,
            null
        },
        {
            "nomor_halaman_lampiran",
            """
            {"continue":false,"different_first_page":"True","first_page":{"is_empty":false,"position":{"location":"header","alignment":"right","indentation":"None"},"number_format":{"type":"arabic","prefix":"None"},"text_style":{"font_name":"Times New Roman","font_size":12,"line_spacing":1,"spacing_before":0,"spacing_after":""},"allow_other_content":false},"default_page":{"position":{"location":"footer","alignment":"center","indentation":"None"},"number_format":{"type":"arabic","prefix":"None"},"text_style":{"font_name":"Times New Roman","font_size":12,"line_spacing":1,"spacing_before":0,"spacing_after":""},"allow_other_content":false}}
            """,
            false,
            true,
            false,
            "decimal"
        }
    };

    [Theory]
    [MemberData(nameof(LegacyRules))]
    public void ParseNomorHalamanSectionRule_ShouldKeepEffectiveValuesAfterNormalization(
        string caseName,
        string legacyJson,
        bool? expectedContinue,
        bool? expectedDifferentFirstPage,
        bool? expectedFirstPageIsEmpty,
        string? expectedNumberFormat)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));

        var success = AturanDetailJsonNormalizer.TryNormalize(legacyJson, out var normalizedJson, out var errorMessage);

        Assert.True(success, errorMessage);

        var parseMethod = typeof(ValidationService).GetMethod(
            "ParseNomorHalamanSectionRule",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(parseMethod);

        var parsedRule = parseMethod!.Invoke(null, new object?[] { normalizedJson });
        Assert.NotNull(parsedRule);

        Assert.Equal(expectedContinue, ReadProperty<bool?>(parsedRule!, "Continue"));
        Assert.Equal(expectedDifferentFirstPage, ReadProperty<bool?>(parsedRule!, "DifferentFirstPage"));
        Assert.Equal(expectedFirstPageIsEmpty, ReadProperty<bool?>(parsedRule!, "FirstPageIsEmpty"));
        Assert.Equal(expectedNumberFormat, ReadProperty<string?>(parsedRule!, "NumberFormat"));
    }

    private static T? ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        return (T?)property!.GetValue(instance);
    }
}

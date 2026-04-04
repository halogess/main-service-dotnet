using System.Globalization;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class ValidationService
{
    private sealed class FirstLineIndentObservation
    {
        public decimal ActualCm { get; init; }
        public string? LeadingManualIndentDescription { get; init; }

        public bool HasLeadingManualIndent =>
            !string.IsNullOrWhiteSpace(LeadingManualIndentDescription);

        public string DisplayActual =>
            HasLeadingManualIndent
                ? ActualCm.ToString("F2", CultureInfo.InvariantCulture) + " cm + " + LeadingManualIndentDescription
                : ActualCm.ToString("F2", CultureInfo.InvariantCulture) + " cm";
    }

    private static FirstLineIndentObservation ObserveFirstLineIndent(
        DokumenFormatParagraf format,
        string? text)
    {
        return new FirstLineIndentObservation
        {
            ActualCm = GetFirstLineIndentCm(format),
            LeadingManualIndentDescription = DescribeLeadingManualIndentation(text)
        };
    }

    private static string? DescribeLeadingManualIndentation(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var leadingTabs = 0;
        var leadingSpaces = 0;

        foreach (var ch in text)
        {
            if (ch == '\r' || ch == '\n')
                continue;

            if (ch == '\t')
            {
                leadingTabs++;
                continue;
            }

            if (ch == ' ')
            {
                leadingSpaces++;
                continue;
            }

            break;
        }

        if (leadingTabs == 0 && leadingSpaces == 0)
            return null;

        var parts = new List<string>();
        if (leadingTabs > 0)
            parts.Add(leadingTabs == 1 ? "1 tab awal" : $"{leadingTabs} tab awal");
        if (leadingSpaces > 0)
            parts.Add(leadingSpaces == 1 ? "1 spasi awal" : $"{leadingSpaces} spasi awal");

        return string.Join(" dan ", parts);
    }
}

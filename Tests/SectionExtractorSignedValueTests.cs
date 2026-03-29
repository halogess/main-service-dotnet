using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;
using Xunit;

namespace Tests;

public class SectionExtractorSignedValueTests
{
    [Fact]
    public void ExtractSectionProperties_ShouldPreserveNegativeTopAndBottomMargins()
    {
        var sectionProperties = new SectionProperties(
            new PageMargin
            {
                Top = -720,
                Bottom = -360,
                Left = 1440U,
                Right = 1440U
            });

        var section = SectionExtractor.ExtractSectionProperties(sectionProperties, dokumenId: 1, sectionIndex: 0);

        Assert.Equal(-720, section.DsecMarginTopTwips);
        Assert.Equal(-360, section.DsecMarginBottomTwips);
    }
}

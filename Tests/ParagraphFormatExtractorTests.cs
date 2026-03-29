using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;
using Xunit;

namespace Tests;

public class ParagraphFormatExtractorTests
{
    [Fact]
    public void ExtractFormat_ShouldPreserveNegativeIndentationValues()
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new Indentation
                {
                    Left = "-720",
                    Right = "-360",
                    Start = "-240",
                    End = "-120"
                }));

        var extractor = new ParagraphFormatExtractor(
            new StyleResolver(null, null),
            numberingPart: null);

        var format = extractor.ExtractFormat(paragraph);

        Assert.Equal(-720, format.DfpIndLeftTwips);
        Assert.Equal(-360, format.DfpIndRightTwips);
        Assert.Equal(-240, format.DfpIndStartTwips);
        Assert.Equal(-120, format.DfpIndEndTwips);
    }

    [Fact]
    public void ExtractFormat_ShouldPreserveNegativeCharacterIndentationValues()
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new Indentation
                {
                    LeftChars = -100,
                    RightChars = -50
                }));

        var extractor = new ParagraphFormatExtractor(
            new StyleResolver(null, null),
            numberingPart: null);

        var format = extractor.ExtractFormat(paragraph);

        Assert.Equal(-100, format.DfpIndLeftChars);
        Assert.Equal(-50, format.DfpIndRightChars);
    }
}

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;
using Xunit;

namespace Tests;

public class ParagraphExtractorCaptionSequenceTests
{
    [Fact]
    public void ExtractParagraphContentSorted_ShouldNotPrependListNumbering_WhenParagraphContainsCaptionSequenceField()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"caption-seq-{Guid.NewGuid():N}.docx");

        try
        {
            CreateDocWithCaptionSequenceField(tempPath);

            using var doc = WordprocessingDocument.Open(tempPath, false);
            var numberingPart = doc.MainDocumentPart?.NumberingDefinitionsPart;
            Assert.NotNull(numberingPart);

            var paragraph = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().First();

            var logger = new Mock<ILogger>().Object;
            var extractor = new ParagraphExtractor(logger, new DrawingExtractor(logger));
            var counters = new Dictionary<int, Dictionary<int, int>>();
            var expectedLabelCounters = new Dictionary<int, Dictionary<int, int>>();
            var expectedLabel = NumberingExtractor.GetNumberingText(numberingPart!, 1, 0, expectedLabelCounters);

            var content = extractor.ExtractParagraphContentSorted(paragraph, numberingPart, counters);

            Assert.Equal("text", content[0]?["type"]?.ToString());
            Assert.Equal("Gambar 3. ", content[0]?["value"]?.ToString());
            Assert.Equal("field", content[1]?["type"]?.ToString());
            Assert.Equal("1", content[1]?["value"]?.ToString());

            Assert.DoesNotContain(content, item =>
                item?["type"]?.ToString() == "text" &&
                string.Equals(item?["value"]?.ToString(), expectedLabel, StringComparison.Ordinal));
            Assert.Contains(content, item =>
                item?["type"]?.ToString() == "field" &&
                item?["value"]?.ToString() == "1");
            Assert.Contains(content, item =>
                item?["type"]?.ToString() == "text" &&
                (item?["value"]?.ToString() ?? string.Empty).Contains("Tampilan Programming Hub", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void CreateDocWithCaptionSequenceField(string path)
    {
        using var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());

        var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1." })
                { LevelIndex = 0 })
            { AbstractNumberId = 1 },
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
        numberingPart.Numbering.Save();

        var captionParagraph = new Paragraph(
            new ParagraphProperties(
                new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = 1 })),
            new Run(new Text("Gambar 3. ")),
            new SimpleField(new Run(new Text("1")))
            {
                Instruction = " SEQ Gambar_3. \\* ARABIC "
            },
            new Run(new Text("Tampilan Programming Hub")));

        main.Document.Body!.Append(captionParagraph);
        main.Document.Save();
    }
}

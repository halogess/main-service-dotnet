using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;
using Xunit;

namespace Tests;

public class ParagraphExtractorNumberingContinuationTests
{
    [Fact]
    public void ExtractParagraphContentSorted_ShouldContinueRestartedNumId_WhenFollowingParagraphUsesStyleNumbering()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"numbering-continuation-{Guid.NewGuid():N}.docx");

        try
        {
            CreateDocWithMixedDirectAndStyleNumbering(tempPath);

            using var doc = WordprocessingDocument.Open(tempPath, false);
            var numberingPart = doc.MainDocumentPart?.NumberingDefinitionsPart;
            var stylesPart = doc.MainDocumentPart?.StyleDefinitionsPart;
            Assert.NotNull(numberingPart);
            Assert.NotNull(stylesPart);

            var logger = new Mock<ILogger>().Object;
            var extractor = new ParagraphExtractor(logger, new DrawingExtractor(logger));
            extractor.SetStyleResolver(new StyleResolver(stylesPart, null, null, numberingPart));
            extractor.ResetNumberingState();

            var counters = new Dictionary<int, Dictionary<int, int>>();
            var paragraphs = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();
            var labels = new List<string>();

            foreach (var paragraph in paragraphs)
            {
                var content = extractor.ExtractParagraphContentSorted(paragraph, numberingPart, counters);
                var label = TryGetLabel(content);
                if (!string.IsNullOrEmpty(label))
                    labels.Add(label);
            }

            Assert.Contains("15:\t", labels);
            Assert.Equal("01:\t", labels[15]);
            Assert.Equal("02:\t", labels[16]);
            Assert.Equal("03:\t", labels[17]);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string? TryGetLabel(JArray content)
    {
        if (content.Count == 0 || content[0]?["type"]?.ToString() != "text")
            return null;

        var value = content[0]?["value"]?.ToString();
        if (string.IsNullOrEmpty(value))
            return null;

        return value.EndsWith(":\t", StringComparison.Ordinal) ? value : null;
    }

    private static void CreateDocWithMixedDirectAndStyleNumbering(string path)
    {
        using var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());

        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style
            {
                Type = StyleValues.Paragraph,
                Default = true,
                StyleId = "Normal",
                StyleName = new StyleName { Val = "Normal" }
            },
            new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = "STTSSegmenProgram",
                StyleName = new StyleName { Val = "[STTS] Segmen Program" }
            },
            new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = "STTSSegmenProgramContent",
                StyleName = new StyleName { Val = "[STTS] Segmen Program Content" },
                BasedOn = new BasedOn { Val = "STTSSegmenProgram" },
                StyleParagraphProperties = new StyleParagraphProperties(
                    new NumberingProperties(
                        new NumberingId { Val = 4 }))
            });
        stylesPart.Styles.Save();

        var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.DecimalZero },
                    new LevelText { Val = "%1:" })
                { LevelIndex = 0 })
            { AbstractNumberId = 78 },
            new NumberingInstance(new AbstractNumId { Val = 78 }) { NumberID = 4 },
            new NumberingInstance(
                new AbstractNumId { Val = 78 },
                new LevelOverride(
                    new StartOverrideNumberingValue { Val = 1 })
                { LevelIndex = 0 })
            { NumberID = 38 });
        numberingPart.Numbering.Save();

        var body = main.Document.Body!;

        for (var i = 1; i <= 15; i++)
            body.Append(CreateStyledParagraph($"pre-{i}", "STTSSegmenProgramContent"));

        body.Append(CreateStyledParagraph("Segmen Program 4.2", "STTSSegmenProgram"));

        body.Append(CreateStyledParagraph("line-1", "STTSSegmenProgramContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 38 })));
        body.Append(CreateStyledParagraph("line-2", "STTSSegmenProgramContent"));
        body.Append(CreateStyledParagraph("line-3", "STTSSegmenProgramContent"));

        main.Document.Save();
    }

    private static Paragraph CreateStyledParagraph(string text, string styleId, NumberingProperties? numPr = null)
    {
        var pPrChildren = new List<OpenXmlElement>
        {
            new ParagraphStyleId { Val = styleId }
        };
        if (numPr != null)
            pPrChildren.Add(numPr);

        return new Paragraph(
            new ParagraphProperties(pPrChildren),
            new Run(new Text(text)));
    }
}

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
    public void ExtractParagraphContentSorted_ShouldKeepSequentialLabels_WhenDirectNumIdSwitchesWithinAlgorithmBlock()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"numbering-direct-switch-{Guid.NewGuid():N}.docx");

        try
        {
            CreateDocWithDirectNumIdSwitchInAlgorithmBlock(tempPath);

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

            Assert.Equal(
                new[] { "01:\t", "02:\t", "03:\t", "04:\t", "01:\t", "02:\t", "03:\t" },
                labels);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

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

    [Fact]
    public void ExtractParagraphContentSorted_ShouldPreserveContinuationAcrossCaptionAndDisabledPlaceholder()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"numbering-caption-continuation-{Guid.NewGuid():N}.docx");

        try
        {
            CreateDocWithCaptionSeparatorInNumberingFlow(tempPath);

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

            Assert.Equal(3, labels.Count);
            Assert.Equal("01:\t", labels[0]);
            Assert.Equal("02:\t", labels[1]);
            Assert.Equal("03:\t", labels[2]);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void ExtractParagraphContentSorted_ShouldContinueAcrossRegularSeparator_WhenXmlNumberingIsCompatible()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"numbering-regular-separator-{Guid.NewGuid():N}.docx");

        try
        {
            CreateDocWithRegularSeparatorInNumberingFlow(tempPath);

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

            Assert.Equal(3, labels.Count);
            Assert.Equal("01:\t", labels[0]);
            Assert.Equal("02:\t", labels[1]);
            Assert.Equal("03:\t", labels[2]);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void ExtractParagraphContentSorted_ShouldResetContinuation_WhenDisabledNumberingHasVisibleContent()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"numbering-disabled-reset-{Guid.NewGuid():N}.docx");

        try
        {
            CreateDocWithDisabledNonEmptySeparatorInNumberingFlow(tempPath);

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

            Assert.Equal(3, labels.Count);
            Assert.Equal("01:\t", labels[0]);
            Assert.Equal("01:\t", labels[1]);
            Assert.Equal("02:\t", labels[2]);
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

    private static void CreateDocWithDirectNumIdSwitchInAlgorithmBlock(string path)
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
                StyleId = "STTSAlgoritmaContent",
                StyleName = new StyleName { Val = "[STTS] Algoritma Content" }
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
            { AbstractNumberId = 1 },
            new NumberingInstance(
                new AbstractNumId { Val = 1 },
                new LevelOverride(
                    new StartOverrideNumberingValue { Val = 1 })
                { LevelIndex = 0 })
            { NumberID = 6 },
            new NumberingInstance(
                new AbstractNumId { Val = 1 },
                new LevelOverride(
                    new StartOverrideNumberingValue { Val = 1 })
                { LevelIndex = 0 })
            { NumberID = 7 });
        numberingPart.Numbering.Save();

        var body = main.Document.Body!;
        body.Append(CreateStyledParagraph("Segmen Program 5.1", "STTSSegmenProgram"));
        body.Append(CreateStyledParagraph("Route::get('/login', function () {", "STTSAlgoritmaContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 6 })));
        body.Append(CreateStyledParagraph("return view('login');", "STTSAlgoritmaContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 6 })));
        body.Append(CreateStyledParagraph("})->name('login');", "STTSAlgoritmaContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 6 })));
        body.Append(CreateStyledParagraph("Route::post('/login', [LoginController::class, 'login']);", "STTSAlgoritmaContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 6 })));

        body.Append(CreateStyledParagraph("Segmen Program 5.2", "STTSSegmenProgram"));
        body.Append(CreateStyledParagraph("public function login(Request $request)", "STTSAlgoritmaContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 7 })));
        body.Append(CreateStyledParagraph("{", "STTSAlgoritmaContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 6 })));
        body.Append(CreateStyledParagraph("$this->autoChecking();", "STTSAlgoritmaContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 6 })));

        main.Document.Save();
    }

    private static void CreateDocWithCaptionSeparatorInNumberingFlow(string path)
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

        body.Append(CreateStyledParagraph("line-1", "STTSSegmenProgramContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 38 })));

        body.Append(CreateStyledParagraph(string.Empty, "STTSSegmenProgramContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 0 })));

        body.Append(CreateStyledParagraph("Segmen Program 5.1 (Lanjutan)", "STTSSegmenProgram"));
        body.Append(CreateStyledParagraph("line-2", "STTSSegmenProgramContent"));
        body.Append(CreateStyledParagraph("line-3", "STTSSegmenProgramContent"));

        main.Document.Save();
    }

    private static void CreateDocWithRegularSeparatorInNumberingFlow(string path)
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
                StyleId = "ListContent",
                StyleName = new StyleName { Val = "List Content" },
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

        body.Append(CreateStyledParagraph("line-1", "ListContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 38 })));
        body.Append(CreateStyledParagraph("regular separator", "Normal"));
        body.Append(CreateStyledParagraph("line-2", "ListContent"));
        body.Append(CreateStyledParagraph("line-3", "ListContent"));

        main.Document.Save();
    }

    private static void CreateDocWithDisabledNonEmptySeparatorInNumberingFlow(string path)
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
                StyleId = "ListContent",
                StyleName = new StyleName { Val = "List Content" },
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

        body.Append(CreateStyledParagraph("line-1", "ListContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 38 })));
        body.Append(CreateStyledParagraph("forced split", "ListContent",
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 0 })));
        body.Append(CreateStyledParagraph("line-2", "ListContent"));
        body.Append(CreateStyledParagraph("line-3", "ListContent"));

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

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IHighlightedTextExtractor
{
    List<string> ExtractHighlightedTexts(Stream docxStream);
}

public class HighlightedTextExtractor : IHighlightedTextExtractor
{
    /// <summary>
    /// Extracts highlighted text segments from a DOCX file.
    /// Each contiguous highlighted segment is returned as "<context> [<highlighted>]".
    /// Context is the non-highlighted text immediately before the segment in the paragraph.
    /// </summary>
    public List<string> ExtractHighlightedTexts(Stream docxStream)
    {
        var highlightedTexts = new List<string>();

        using var doc = WordprocessingDocument.Open(docxStream, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null)
            return highlightedTexts;

        var stylesPart = doc.MainDocumentPart?.StyleDefinitionsPart;
        var stylesWithEffectsPart = doc.MainDocumentPart?.StylesWithEffectsPart;
        var themePart = doc.MainDocumentPart?.ThemePart;
        
        var themeFontResolver = ThemeFontResolver.FromThemePart(themePart);
        var styleResolver = new StyleResolver(stylesPart, stylesWithEffectsPart, themeFontResolver);

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var pPr = paragraph.ParagraphProperties;
            var contextBuffer = new StringBuilder();
            var highlightBuffer = new StringBuilder();
            var isInHighlight = false;

            void FlushHighlight()
            {
                var highlightText = highlightBuffer.ToString().Trim();
                if (string.IsNullOrWhiteSpace(highlightText))
                    return;

                var contextText = contextBuffer.ToString().Trim();
                var formatted = string.IsNullOrWhiteSpace(contextText)
                    ? $"[{highlightText}]"
                    : $"{contextText} [{highlightText}]";
                highlightedTexts.Add(formatted);
            }

            foreach (var run in paragraph.Descendants<Run>())
            {
                var runText = run.InnerText;

                // Check if run has highlight
                var effectiveRun = styleResolver.GetEffectiveRunProperties(run, pPr);
                var runHighlighted = !string.IsNullOrWhiteSpace(effectiveRun.HighlightColor) &&
                    effectiveRun.HighlightColor != "none";

                if (runHighlighted)
                {
                    if (!string.IsNullOrEmpty(runText))
                        highlightBuffer.Append(runText);
                    isInHighlight = true;
                }
                else if (isInHighlight)
                {
                    FlushHighlight();
                    highlightBuffer.Clear();
                    contextBuffer.Clear();
                    isInHighlight = false;
                    if (!string.IsNullOrEmpty(runText))
                        contextBuffer.Append(runText);
                }
                else if (!string.IsNullOrEmpty(runText))
                {
                    contextBuffer.Append(runText);
                }
            }

            if (isInHighlight)
            {
                FlushHighlight();
                highlightBuffer.Clear();
            }
        }

        return highlightedTexts;
    }
}

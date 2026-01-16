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
    /// Extracts highlighted text from a DOCX file, grouped per paragraph.
    /// If a paragraph contains any highlighted runs, the entire paragraph text is captured.
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
            var hasHighlight = false;
            var paragraphText = new StringBuilder();

            foreach (var run in paragraph.Descendants<Run>())
            {
                var runText = run.InnerText;
                paragraphText.Append(runText);

                // Check if run has highlight
                if (!hasHighlight)
                {
                    var effectiveRun = styleResolver.GetEffectiveRunProperties(run, pPr);
                    if (!string.IsNullOrWhiteSpace(effectiveRun.HighlightColor) && 
                        effectiveRun.HighlightColor != "none")
                    {
                        hasHighlight = true;
                    }
                }
            }

            // If paragraph has any highlight, add the entire paragraph text
            if (hasHighlight)
            {
                var text = paragraphText.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(text) && !highlightedTexts.Contains(text))
                {
                    highlightedTexts.Add(text);
                }
            }
        }

        return highlightedTexts;
    }
}

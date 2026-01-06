using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles detection and reordering of floating elements (positioned tables and anchored drawings)
/// </summary>
public class FloatingElementHelper
{
    private readonly ILogger _logger;

    public FloatingElementHelper(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects if an element is floating (positioned) and extracts its Y position.
    /// Floating elements include:
    /// - Tables with w:tblpPr (table positioning properties)
    /// - Paragraphs containing only anchored drawings (wp:anchor)
    /// </summary>
    public (bool isFloating, int yPosition) DetectFloatingElement(OpenXmlElement elem)
    {
        // Check for floating table
        if (elem is Table table)
        {
            var tblPr = table.GetFirstChild<TableProperties>();
            if (tblPr != null)
            {
                var tblpPr = tblPr.GetFirstChild<TablePositionProperties>();
                if (tblpPr != null)
                {
                    var yPos = tblpPr.TablePositionY?.Value ?? 0;
                    _logger.LogInformation("Detected floating table with Y position: {Y} twips", yPos);
                    return (true, yPos);
                }
            }
        }
        
        // Check for paragraph containing anchored drawings
        if (elem is Paragraph para)
        {
            var drawings = para.Descendants<Drawing>().ToList();
            if (drawings.Count > 0)
            {
                foreach (var drawing in drawings)
                {
                    var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
                    if (anchor != null)
                    {
                        var positionV = anchor.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.VerticalPosition>();
                        if (positionV != null)
                        {
                            var posOffset = positionV.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.PositionOffset>();
                            if (posOffset != null && int.TryParse(posOffset.Text, out int yPos))
                            {
                                var yTwips = (int)(yPos / 635.0); // EMU to twips
                                _logger.LogInformation("Detected floating drawing with Y position: {Y} EMUs ({Twips} twips)", yPos, yTwips);
                                return (true, yTwips);
                            }
                        }
                    }
                }
            }
        }
        
        return (false, 0);
    }

    /// <summary>
    /// Reorders floating elements locally within their clusters.
    /// </summary>
    public List<OpenXmlElement> ReorderFloatingElements(List<(OpenXmlElement element, bool isFloating, int floatYPosition, int originalIndex)> elements)
    {
        var result = new List<OpenXmlElement>();
        var cluster = new List<(OpenXmlElement element, int yPos, int origIdx)>();

        foreach (var (element, isFloating, floatY, origIdx) in elements)
        {
            if (isFloating && floatY > 0)
            {
                cluster.Add((element, floatY, origIdx));
            }
            else
            {
                if (cluster.Count > 0)
                {
                    var sortedCluster = cluster
                        .OrderBy(c => c.yPos)
                        .ThenBy(c => c.origIdx)
                        .Select(c => c.element);
                    
                    result.AddRange(sortedCluster);
                    
                    if (cluster.Count > 1)
                        _logger.LogInformation("Sorted local cluster of {Count} floating elements around index {Idx}", cluster.Count, cluster[0].origIdx);
                    
                    cluster.Clear();
                }
                result.Add(element);
            }
        }

        if (cluster.Count > 0)
        {
            var sortedCluster = cluster
                .OrderBy(c => c.yPos)
                .ThenBy(c => c.origIdx)
                .Select(c => c.element);
            result.AddRange(sortedCluster);
            
            if (cluster.Count > 1)
                _logger.LogInformation("Sorted final cluster of {Count} floating elements", cluster.Count);
        }

        return result;
    }
}

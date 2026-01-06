using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Resolves styles and numbering from style chain
/// </summary>
public class StyleResolver
{
    private readonly Dictionary<string, Style> _stylesById = new();
    private readonly StylesPart? _stylesPart;
    
    public StyleResolver(StylesPart? stylesPart)
    {
        _stylesPart = stylesPart;
        if (stylesPart?.Styles != null)
        {
            foreach (var style in stylesPart.Styles.Elements<Style>())
            {
                var styleId = style.StyleId?.Value;
                if (!string.IsNullOrEmpty(styleId))
                    _stylesById[styleId] = style;
            }
        }
    }
    
    /// <summary>
    /// Gets the style chain from basedOn relationships (parent → child order)
    /// </summary>
    public List<Style> GetStyleChain(string? styleId)
    {
        var chain = new List<Style>();
        var visited = new HashSet<string>();
        
        while (!string.IsNullOrEmpty(styleId) && !visited.Contains(styleId))
        {
            visited.Add(styleId);
            if (_stylesById.TryGetValue(styleId, out var style))
            {
                chain.Insert(0, style); // Insert at beginning for parent → child order
                styleId = style.BasedOn?.Val?.Value;
            }
            else
            {
                break;
            }
        }
        
        return chain;
    }
    
    /// <summary>
    /// Gets effective NumberingProperties for a paragraph, checking both direct and style chain
    /// </summary>
    public (int? numId, int ilvl) GetEffectiveNumberingProperties(Paragraph p)
    {
        // 1. Check direct numPr on paragraph
        var directNumPr = p.ParagraphProperties?.NumberingProperties;
        if (directNumPr?.NumberingId?.Val != null)
        {
            return (directNumPr.NumberingId.Val.Value, 
                    directNumPr.NumberingLevelReference?.Val?.Value ?? 0);
        }
        
        // 2. Check style chain for numPr
        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var styleChain = GetStyleChain(styleId);
        
        // Walk from child to parent (reverse order) - child overrides parent
        for (int i = styleChain.Count - 1; i >= 0; i--)
        {
            var style = styleChain[i];
            var stylePPr = style.StyleParagraphProperties;
            var styleNumPr = stylePPr?.NumberingProperties;
            
            if (styleNumPr?.NumberingId?.Val != null)
            {
                return (styleNumPr.NumberingId.Val.Value,
                        styleNumPr.NumberingLevelReference?.Val?.Value ?? 0);
            }
        }
        
        return (null, 0);
    }
}

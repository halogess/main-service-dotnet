using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Extracts section properties from OpenXML SectionProperties elements
/// </summary>
public static class SectionExtractor
{
    /// <summary>
    /// Extracts section properties from OpenXML SectionProperties element
    /// </summary>
    public static DokumenSection ExtractSectionProperties(SectionProperties sectPr, int dokumenId, int sectionIndex)
    {
        var section = new DokumenSection
        {
            DokumenId = (uint)dokumenId,
            DsecIndex = (uint)sectionIndex
        };
        
        // Page Size
        var pageSize = sectPr.GetFirstChild<PageSize>();
        if (pageSize != null)
        {
            if (pageSize.Width != null && pageSize.Width.HasValue)
                section.DsecPageWidthTwips = pageSize.Width.Value;
            if (pageSize.Height != null && pageSize.Height.HasValue)
                section.DsecPageHeightTwips = pageSize.Height.Value;
            section.DsecOrientation = pageSize.Orient?.Value == PageOrientationValues.Landscape ? "landscape" : "portrait";
        }
        
        // Page Margins
        var pageMargin = sectPr.GetFirstChild<PageMargin>();
        if (pageMargin != null)
        {
            if (pageMargin.Top != null)
                section.DsecMarginTopTwips = (uint)Math.Abs((int)pageMargin.Top);
            if (pageMargin.Bottom != null)
                section.DsecMarginBottomTwips = (uint)Math.Abs((int)pageMargin.Bottom);
            if (pageMargin.Left != null)
                section.DsecMarginLeftTwips = pageMargin.Left.Value;
            if (pageMargin.Right != null)
                section.DsecMarginRightTwips = pageMargin.Right.Value;
            if (pageMargin.Header != null)
                section.DsecHeaderMarginTwips = pageMargin.Header.Value;
            if (pageMargin.Footer != null)
                section.DsecFooterMarginTwips = pageMargin.Footer.Value;
            if (pageMargin.Gutter != null)
                section.DsecGutterTwips = pageMargin.Gutter.Value;
        }
        
        // Gutter position
        var bidi = sectPr.GetFirstChild<BiDi>();
        section.DsecGutterPosition = bidi != null ? "top" : "left";
        
        // Page Numbering
        var pageNumberType = sectPr.GetFirstChild<PageNumberType>();
        if (pageNumberType != null)
        {
            if (pageNumberType.Format != null && pageNumberType.Format.HasValue)
                section.DsecPageNumFormat = pageNumberType.Format.Value.ToString().ToLower();
            if (pageNumberType.Start != null)
                section.DsecPageNumStart = (uint)pageNumberType.Start.Value;
            section.DsecPageNumRestart = pageNumberType.ChapterStyle != null;
        }
        
        return section;
    }
}

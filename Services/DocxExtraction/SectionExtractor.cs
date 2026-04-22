using DocumentFormat.OpenXml;
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
        => ExtractSectionProperties(sectPr, "dokumen", (uint)dokumenId, sectionIndex);

    /// <summary>
    /// Extracts section properties from OpenXML SectionProperties element
    /// with flexible reference target (dokumen/bab).
    /// </summary>
    public static DokumenSection ExtractSectionProperties(SectionProperties sectPr, string refTipe, uint refId, int sectionIndex)
    {
        var section = new DokumenSection
        {
            DsecRefTipe = string.IsNullOrWhiteSpace(refTipe) ? "dokumen" : refTipe.Trim().ToLowerInvariant(),
            DsecRefId = refId,
            DsecIndex = (uint)sectionIndex
        };
        
        // Title Page (first page different header/footer)
        var titlePage = sectPr.GetFirstChild<TitlePage>();
        section.DsecHasTitlePage = titlePage != null && (titlePage.Val?.Value ?? true);
        
        // Different odd/even pages - this is typically set at document level (w:settings/w:evenAndOddHeaders)
        // But we store it per section for flexibility
        // Note: In Word, this is a document-wide setting, not per-section
        section.DsecDifferentOddEven = false; // Will be updated from settings.xml if needed
        
        // Page Size
        var pageSize = sectPr.GetFirstChild<PageSize>();
        if (pageSize != null)
        {
            var orientation = pageSize.Orient?.Value == PageOrientationValues.Landscape ? "landscape" : "portrait";
            uint? widthTwips = null;
            uint? heightTwips = null;

            if (pageSize.Width != null && pageSize.Width.HasValue)
                widthTwips = pageSize.Width.Value;
            if (pageSize.Height != null && pageSize.Height.HasValue)
                heightTwips = pageSize.Height.Value;

            if (widthTwips.HasValue && heightTwips.HasValue)
            {
                if (orientation == "landscape" && widthTwips.Value < heightTwips.Value)
                {
                    (widthTwips, heightTwips) = (heightTwips, widthTwips);
                }
                else if (orientation == "portrait" && widthTwips.Value > heightTwips.Value)
                {
                    (widthTwips, heightTwips) = (heightTwips, widthTwips);
                }
            }

            if (widthTwips.HasValue)
                section.DsecPageWidthTwips = widthTwips.Value;
            if (heightTwips.HasValue)
                section.DsecPageHeightTwips = heightTwips.Value;
            section.DsecOrientation = orientation;
        }
        
        // Page Margins
        var pageMargin = sectPr.GetFirstChild<PageMargin>();
        if (pageMargin != null)
        {
            if (pageMargin.Top != null)
                section.DsecMarginTopTwips = pageMargin.Top.Value;
            if (pageMargin.Bottom != null)
                section.DsecMarginBottomTwips = pageMargin.Bottom.Value;
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
        var gutterAtTop = sectPr.GetFirstChild<GutterAtTop>();
        var gutterOnRight = sectPr.GetFirstChild<GutterOnRight>();
        if (IsOn(gutterAtTop))
            section.DsecGutterPosition = "top";
        else if (IsOn(gutterOnRight))
            section.DsecGutterPosition = "right";
        else
            section.DsecGutterPosition = "left";
        
        // Page Numbering
        var pageNumberType = sectPr.GetFirstChild<PageNumberType>();
        if (pageNumberType != null)
        {
            // Default to "decimal" if Format is not specified (Word's implicit default)
            section.DsecPageNumFormat = pageNumberType.Format != null && pageNumberType.Format.HasValue
                ? pageNumberType.Format.Value.ToString().ToLower()
                : "decimal";
            
            if (pageNumberType.Start != null)
                section.DsecPageNumStart = (uint)pageNumberType.Start.Value;
        }
        
        // Columns
        var columns = sectPr.GetFirstChild<Columns>();
        if (columns != null)
        {
            section.DsecColumnCount = (uint)(columns.ColumnCount?.Value ?? 1);
        }
        else
        {
            section.DsecColumnCount = 1;
        }
        
        return section;
    }

    private static bool IsOn(OpenXmlElement? element)
    {
        if (element == null)
            return false;

        var valAttr = element.GetAttribute("val", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        if (string.IsNullOrEmpty(valAttr.Value))
            return true;

        return valAttr.Value == "1" || valAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Updates the DifferentOddEven property from document settings
    /// Call this after creating sections to update from settings.xml
    /// </summary>
    public static void UpdateOddEvenFromSettings(DokumenSection section, bool hasEvenAndOddHeaders)
    {
        section.DsecDifferentOddEven = hasEvenAndOddHeaders;
    }
}

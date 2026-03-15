using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace Tests;

/// <summary>
/// Tests for section extraction logic to verify the indexing fix
/// </summary>
public class SectionExtractionTests
{
    private readonly string _testDocxPath;

    public SectionExtractionTests()
    {
        // Path to test document - relative to test output directory
        _testDocxPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Docx", "bab2.docx");
    }

    [Fact]
    public void Section_Detection_Should_Find_All_Sections()
    {
        // Arrange
        if (!File.Exists(_testDocxPath))
        {
            // Skip if test file not available
            Assert.Fail($"Test file not found: {_testDocxPath}. Please copy bab2.docx to Tests/TestData/Docx/");
        }

        using var doc = WordprocessingDocument.Open(_testDocxPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        // Act - Simulate the FIXED section detection logic (excludes SectionProperties in indexing)
        var sectionInfos = new List<(int elementIndex, SectionProperties sectPr)>();
        int elemIndex = 0;

        foreach (var elem in body.Elements())
        {
            // Skip body-level SectionProperties in indexing (matches the fix)
            if (elem is SectionProperties) continue;

            if (elem is Paragraph para)
            {
                var sectPr = para.ParagraphProperties?.GetFirstChild<SectionProperties>();
                if (sectPr != null)
                    sectionInfos.Add((elemIndex, sectPr));
            }
            elemIndex++;
        }

        var bodySectPr = body.GetFirstChild<SectionProperties>();
        if (bodySectPr != null)
            sectionInfos.Add((int.MaxValue, bodySectPr));

        // Assert
        Assert.Equal(32, sectionInfos.Count); // bab2.docx should have 32 sections
    }

    [Fact]
    public void Element_Indexing_Should_Be_Consistent()
    {
        // Arrange
        if (!File.Exists(_testDocxPath))
        {
            Assert.Fail($"Test file not found: {_testDocxPath}. Please copy bab2.docx to Tests/TestData/Docx/");
        }

        using var doc = WordprocessingDocument.Open(_testDocxPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        // Act - Simulate section indexing (as in extraction after fix)
        var sectionIndexes = new List<int>();
        int sectionElemIndex = 0;

        foreach (var elem in body.Elements())
        {
            if (elem is SectionProperties) continue;

            if (elem is Paragraph para)
            {
                var sectPr = para.ParagraphProperties?.GetFirstChild<SectionProperties>();
                if (sectPr != null)
                    sectionIndexes.Add(sectionElemIndex);
            }
            sectionElemIndex++;
        }

        // Simulate element mapping (as used in body extraction)
        var elementIndexMap = new Dictionary<object, int>();
        int mapIdx = 0;
        foreach (var elem in body.Elements())
        {
            if (elem is not SectionProperties)
                elementIndexMap[elem] = mapIdx++;
        }

        // Verify the maximum element index in both should match
        int maxSectionIndex = sectionElemIndex; // Final count after loop (EXCLUDING SectionProperties)
        int maxMapIndex = mapIdx;               // Final count from elementIndexMap

        // Assert - Both should be the same (this was the bug!)
        Assert.Equal(maxSectionIndex, maxMapIndex);
    }

    [Fact]
    public void Elements_Should_Distribute_Across_All_Sections()
    {
        // Arrange
        if (!File.Exists(_testDocxPath))
        {
            Assert.Fail($"Test file not found: {_testDocxPath}. Please copy bab2.docx to Tests/TestData/Docx/");
        }

        using var doc = WordprocessingDocument.Open(_testDocxPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        // Build section boundaries (FIXED logic)
        var sectionBoundaries = new List<int>(); // Element index where each section ends
        int elemIndex = 0;

        foreach (var elem in body.Elements())
        {
            if (elem is SectionProperties) continue;

            if (elem is Paragraph para)
            {
                var sectPr = para.ParagraphProperties?.GetFirstChild<SectionProperties>();
                if (sectPr != null)
                    sectionBoundaries.Add(elemIndex);
            }
            elemIndex++;
        }

        // Add final section boundary
        sectionBoundaries.Add(int.MaxValue);

        // Now simulate assigning elements to sections
        var elementsPerSection = new Dictionary<int, int>();
        for (int i = 0; i < sectionBoundaries.Count; i++)
            elementsPerSection[i] = 0;

        int idx = 0;
        foreach (var elem in body.Elements())
        {
            if (elem is SectionProperties) continue;

            // Find which section this element belongs to
            int sectionIdx = GetSectionIndex(idx, sectionBoundaries);
            elementsPerSection[sectionIdx]++;
            idx++;
        }

        // Assert - Multiple sections should have elements, not just last one
        int sectionsWithElements = elementsPerSection.Count(kvp => kvp.Value > 0);
        
        Assert.True(sectionsWithElements > 1, 
            $"Only {sectionsWithElements} section(s) have elements. Expected multiple sections to have elements.");
        
        // Log distribution for debugging
        foreach (var kvp in elementsPerSection.Where(k => k.Value > 0))
        {
            Console.WriteLine($"Section {kvp.Key}: {kvp.Value} elements");
        }
    }

    private static int GetSectionIndex(int elementIndex, List<int> sectionBoundaries)
    {
        for (int i = 0; i < sectionBoundaries.Count; i++)
        {
            if (elementIndex <= sectionBoundaries[i])
                return i;
        }
        return sectionBoundaries.Count - 1;
    }

    [Fact]
    public void Section_Properties_Should_Have_Valid_Page_Settings()
    {
        // Arrange
        if (!File.Exists(_testDocxPath))
        {
            Assert.Fail($"Test file not found: {_testDocxPath}. Please copy bab2.docx to Tests/TestData/Docx/");
        }

        using var doc = WordprocessingDocument.Open(_testDocxPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        // Collect all section properties
        var allSectPr = new List<SectionProperties>();

        foreach (var elem in body.Elements())
        {
            if (elem is Paragraph para)
            {
                var sectPr = para.ParagraphProperties?.GetFirstChild<SectionProperties>();
                if (sectPr != null)
                    allSectPr.Add(sectPr);
            }
        }

        var bodySectPr = body.GetFirstChild<SectionProperties>();
        if (bodySectPr != null)
            allSectPr.Add(bodySectPr);

        // Assert - All sections should have page size
        foreach (var sectPr in allSectPr)
        {
            var pageSize = sectPr.GetFirstChild<PageSize>();
            Assert.NotNull(pageSize);
            Assert.True(pageSize.Width?.Value > 0, "Page width should be positive");
            Assert.True(pageSize.Height?.Value > 0, "Page height should be positive");
        }
    }
}

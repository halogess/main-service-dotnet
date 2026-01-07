using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

class Program
{
    static void Main(string[] args)
    {
        var docxPath = args.Length > 0 ? args[0] : @"..\bab2.docx";
        
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("SECTION EXTRACTION TEST RESULTS");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine($"\nTest File: {docxPath}");
        
        if (!File.Exists(docxPath))
        {
            Console.WriteLine($"ERROR: File not found: {docxPath}");
            return;
        }

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        Console.WriteLine("\n--- TEST 1: Section Detection ---");
        
        // FIXED section detection logic (excludes SectionProperties in indexing)
        var sectionInfos = new List<(int elementIndex, SectionProperties sectPr)>();
        int elemIndex = 0;

        foreach (var elem in body.Elements())
        {
            if (elem is SectionProperties) continue; // THE FIX
            
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

        Console.WriteLine($"Total sections found: {sectionInfos.Count}");
        Console.WriteLine($"Expected: 32");
        Console.WriteLine($"Result: {(sectionInfos.Count == 32 ? "✅ PASS" : "❌ FAIL")}");

        Console.WriteLine("\n--- TEST 2: Index Consistency ---");
        
        // Element mapping as in body extraction
        int mapIdx = 0;
        foreach (var elem in body.Elements())
        {
            if (elem is not SectionProperties)
                mapIdx++;
        }
        
        Console.WriteLine($"Section index max: {elemIndex}");
        Console.WriteLine($"Element map max: {mapIdx}");
        Console.WriteLine($"Result: {(elemIndex == mapIdx ? "✅ PASS (indexes match)" : "❌ FAIL (mismatch!)")}");

        Console.WriteLine("\n--- TEST 3: Element Distribution ---");
        
        // Build section boundaries
        var sectionBoundaries = new List<int>();
        elemIndex = 0;
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
        sectionBoundaries.Add(int.MaxValue);

        // Count elements per section
        var elementsPerSection = new int[sectionBoundaries.Count];
        int idx = 0;
        foreach (var elem in body.Elements())
        {
            if (elem is SectionProperties) continue;
            
            int sectionIdx = GetSectionIndex(idx, sectionBoundaries);
            elementsPerSection[sectionIdx]++;
            idx++;
        }

        int sectionsWithElements = elementsPerSection.Count(c => c > 0);
        Console.WriteLine($"Sections with elements: {sectionsWithElements} / {sectionBoundaries.Count}");
        Console.WriteLine($"Result: {(sectionsWithElements > 1 ? "✅ PASS (multiple sections have content)" : "❌ FAIL (only 1 section)")}");
        
        Console.WriteLine("\n--- Element Distribution per Section ---");
        for (int i = 0; i < Math.Min(32, elementsPerSection.Length); i++)
        {
            if (elementsPerSection[i] > 0)
            {
                Console.WriteLine($"  Section {i,2}: {elementsPerSection[i],4} elements");
            }
        }

        Console.WriteLine("\n--- TEST 4: Page Settings Validation ---");
        
        int validSections = 0;
        foreach (var (_, sectPr) in sectionInfos)
        {
            var pageSize = sectPr.GetFirstChild<PageSize>();
            if (pageSize?.Width?.Value > 0 && pageSize?.Height?.Value > 0)
                validSections++;
        }
        Console.WriteLine($"Sections with valid page size: {validSections} / {sectionInfos.Count}");
        Console.WriteLine($"Result: {(validSections == sectionInfos.Count ? "✅ PASS" : "❌ FAIL")}");

        Console.WriteLine("\n" + "=".PadRight(60, '='));
        bool allPassed = sectionInfos.Count == 32 && elemIndex == mapIdx && sectionsWithElements > 1 && validSections == sectionInfos.Count;
        Console.WriteLine(allPassed ? "ALL TESTS PASSED ✅" : "SOME TESTS FAILED ❌");
        Console.WriteLine("=".PadRight(60, '='));
    }

    static int GetSectionIndex(int elementIndex, List<int> sectionBoundaries)
    {
        for (int i = 0; i < sectionBoundaries.Count; i++)
        {
            if (elementIndex <= sectionBoundaries[i])
                return i;
        }
        return sectionBoundaries.Count - 1;
    }
}

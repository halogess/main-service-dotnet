using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.IO;
using System.Linq;

namespace DebugTableExtraction;

class Program
{
    static void Main(string[] args)
    {
        var docxPath = args.Length > 0 ? args[0] : @"e:\main-service-dotnet\bab2.docx";
        
        if (!File.Exists(docxPath))
        {
            Console.WriteLine($"File not found: {docxPath}");
            return;
        }

        Console.WriteLine($"=== Analyzing DOCX: {Path.GetFileName(docxPath)} ===\n");

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        
        int tableCount = 0;
        int totalElementCount = 0;
        
        foreach (var elem in body.Elements())
        {
            if (elem is SectionProperties) continue;
            
            totalElementCount++;
            
            if (elem is Table table)
            {
                tableCount++;
                Console.WriteLine($"\n=== Table #{tableCount} (Element #{totalElementCount}) ===");
                
                try
                {
                    AnalyzeTable(table, tableCount);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ ERROR analyzing table: {ex.Message}");
                    Console.WriteLine($"     Exception Type: {ex.GetType().Name}");
                    Console.WriteLine($"     Stack Trace: {ex.StackTrace}");
                }
            }
        }
        
        Console.WriteLine($"\n=== Summary ===");
        Console.WriteLine($"Total elements: {totalElementCount}");
        Console.WriteLine($"Total tables: {tableCount}");
    }

    static void AnalyzeTable(Table table, int tableNum)
    {
        // Get table properties
        var tblPr = table.GetFirstChild<TableProperties>();
        var styleId = tblPr?.TableStyle?.Val?.Value ?? "(none)";
        
        Console.WriteLine($"  Style ID: {styleId}");
        
        // Count rows and cells
        var rows = table.Descendants<TableRow>().ToList();
        Console.WriteLine($"  Rows: {rows.Count}");
        
        if (rows.Count == 0)
        {
            Console.WriteLine($"  ⚠️  WARNING: Table has no rows!");
            return;
        }
        
        // Analyze first row for column count
        var firstRow = table.Elements<TableRow>().FirstOrDefault();
        if (firstRow == null)
        {
            Console.WriteLine($"  ⚠️  WARNING: Cannot access first row!");
            return;
        }
        
        int colCount = 0;
        var cells = firstRow.Elements<TableCell>().ToList();
        foreach (var cell in cells)
        {
            var tcPr = cell.GetFirstChild<TableCellProperties>();
            var gridSpan = tcPr?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
            colCount += gridSpan;
        }
        
        Console.WriteLine($"  Columns: {colCount} (from {cells.Count} cells in first row)");
        
        // Check for potential issues
        bool hasIssues = false;
        
        // Check each row
        for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var row = rows[rowIdx];
            var rowCells = row.Descendants<TableCell>().ToList();
            
            if (rowCells.Count == 0)
            {
                Console.WriteLine($"  ⚠️  Row {rowIdx + 1}: No cells found!");
                hasIssues = true;
            }
            
            // Check each cell for content
            for (int cellIdx = 0; cellIdx < rowCells.Count; cellIdx++)
            {
                var cell = rowCells[cellIdx];
                var paragraphs = cell.Elements<Paragraph>().ToList();
                var nestedTables = cell.Elements<Table>().ToList();
                
                if (paragraphs.Count == 0 && nestedTables.Count == 0)
                {
                    Console.WriteLine($"  ⚠️  Row {rowIdx + 1}, Cell {cellIdx + 1}: No content (no paragraphs or tables)!");
                    hasIssues = true;
                }
                
                // Check for nested tables
                if (nestedTables.Count > 0)
                {
                    Console.WriteLine($"  ℹ️  Row {rowIdx + 1}, Cell {cellIdx + 1}: Contains {nestedTables.Count} nested table(s)");
                }
            }
        }
        
        if (!hasIssues)
        {
            Console.WriteLine($"  ✅ Table structure looks valid");
        }
        
        // Sample first cell content
        var firstCell = firstRow.Elements<TableCell>().FirstOrDefault();
        if (firstCell != null)
        {
            var firstPara = firstCell.Elements<Paragraph>().FirstOrDefault();
            if (firstPara != null)
            {
                var text = firstPara.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var preview = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
                    Console.WriteLine($"  First cell text: \"{preview}\"");
                }
            }
        }
    }
}

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DebugShapes;

class Program
{
    static void Main(string[] args)
    {
        var docxPath = args.Length > 0 ? args[0] : @"e:\main-service-dotnet\BAB II.docx";
        
        if (!File.Exists(docxPath))
        {
            Console.WriteLine($"File not found: {docxPath}");
            return;
        }

        Console.WriteLine($"=== Analyzing Shapes in: {Path.GetFileName(docxPath)} ===\n");

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        
        int elementIdx = 0;
        int drawingCount = 0;
        int pictCount = 0;
        int groupCount = 0;
        
        foreach (var elem in body.Elements())
        {
            if (elem is SectionProperties) continue;
            elementIdx++;
            
            // Find all drawings in this element
            var drawings = elem.Descendants<Drawing>().ToList();
            var pictures = elem.Descendants<Picture>().ToList(); // VML
            
            foreach (var drawing in drawings)
            {
                drawingCount++;
                AnalyzeDrawing(drawing, elementIdx, drawingCount);
            }
            
            foreach (var pict in pictures)
            {
                pictCount++;
                AnalyzePicture(pict, elementIdx, pictCount);
            }
        }
        
        Console.WriteLine($"\n=== Summary ===");
        Console.WriteLine($"Total elements: {elementIdx}");
        Console.WriteLine($"Total w:drawing: {drawingCount}");
        Console.WriteLine($"Total w:pict (VML): {pictCount}");
        Console.WriteLine($"Total groups skipped: {groupCount}");
    }
    
    static void AnalyzeDrawing(Drawing drawing, int elemIdx, int drawIdx)
    {
        Console.WriteLine($"\n--- Drawing #{drawIdx} in Element #{elemIdx} ---");
        
        // Get DocProperties
        var docPr = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().FirstOrDefault();
        var id = docPr?.Id?.Value;
        var name = docPr?.Name?.Value;
        
        Console.WriteLine($"  ID: {id}, Name: {name}");
        
        // Check if it's anchor or inline
        var anchor = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().FirstOrDefault();
        var inline = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().FirstOrDefault();
        
        Console.WriteLine($"  Type: {(anchor != null ? "Anchor" : inline != null ? "Inline" : "Unknown")}");
        
        // Check for graphic content
        var graphic = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Graphic>().FirstOrDefault();
        if (graphic != null)
        {
            var graphicData = graphic.GetFirstChild<DocumentFormat.OpenXml.Drawing.GraphicData>();
            if (graphicData != null)
            {
                Console.WriteLine($"  Graphic URI: {graphicData.Uri}");
                
                // Check namespace-specific elements
                var xml = graphicData.OuterXml;
                
                // Check for word processing shapes (wsp)
                if (xml.Contains("wsp:"))
                {
                    Console.WriteLine($"  Contains: wsp (Word Shape)");
                    
                    // Check for text
                    if (xml.Contains("wps:txbx") || xml.Contains("<w:txbxContent") || xml.Contains("txbxContent"))
                    {
                        Console.WriteLine($"  Has TextBox: YES");
                    }
                }
                
                // Check for group shapes (wpg)
                if (xml.Contains("wpg:wgp") || xml.Contains("wpg:grpSp"))
                {
                    Console.WriteLine($"  Contains: wpg (Word Group) ⚠️ MAY BE SKIPPED!");
                    
                    // Count shapes inside group
                    var xdoc = XDocument.Parse(xml);
                    XNamespace wpg = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
                    XNamespace wsp = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
                    
                    var wspElements = xdoc.Descendants().Where(e => e.Name.LocalName == "wsp").Count();
                    Console.WriteLine($"  Shapes in group: {wspElements}");
                    
                    // Check for text in group
                    var hasText = xml.Contains("<w:t>") || xml.Contains("<w:t ");
                    Console.WriteLine($"  Has text content: {hasText}");
                }
                
                // Check for pictures (pic:pic)
                if (xml.Contains("pic:pic"))
                {
                    Console.WriteLine($"  Contains: pic (Picture/Image)");
                }
                
                // Check for charts
                if (xml.Contains("c:chart"))
                {
                    Console.WriteLine($"  Contains: chart");
                }
            }
        }
        
        // Check for text directly
        var textBoxContents = drawing.Descendants<TextBoxContent>().ToList();
        if (textBoxContents.Count > 0)
        {
            Console.WriteLine($"  TextBoxContent elements: {textBoxContents.Count}");
            foreach (var txbx in textBoxContents)
            {
                var text = txbx.InnerText.Trim();
                if (text.Length > 0)
                {
                    var preview = text.Length > 80 ? text.Substring(0, 80) + "..." : text;
                    Console.WriteLine($"    Text: \"{preview}\"");
                }
            }
        }
        
        // Check for Drawing.Text (a:t)
        var drawingTexts = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Text>().ToList();
        if (drawingTexts.Count > 0)
        {
            Console.WriteLine($"  Drawing.Text (a:t) elements: {drawingTexts.Count}");
            var allText = string.Join(" ", drawingTexts.Select(t => t.Text).Take(5));
            var preview = allText.Length > 80 ? allText.Substring(0, 80) + "..." : allText;
            Console.WriteLine($"    Sample: \"{preview}\"");
        }
        
        // Final verdict
        var hasContent = textBoxContents.Count > 0 || drawingTexts.Count > 0;
        if (!hasContent && name?.StartsWith("Group ") != true)
        {
            Console.WriteLine($"  ⚠️ WARNING: Drawing has no extractable text content!");
        }
    }
    
    static void AnalyzePicture(Picture pict, int elemIdx, int pictIdx)
    {
        Console.WriteLine($"\n--- VML Picture #{pictIdx} in Element #{elemIdx} ---");
        
        var xml = pict.OuterXml;
        
        // Check for textbox
        if (xml.Contains("v:textbox"))
        {
            Console.WriteLine($"  Contains: v:textbox");
            
            // Extract text
            var textContent = pict.Descendants<TextBoxContent>().FirstOrDefault();
            if (textContent != null)
            {
                var text = textContent.InnerText.Trim();
                var preview = text.Length > 80 ? text.Substring(0, 80) + "..." : text;
                Console.WriteLine($"  Text: \"{preview}\"");
            }
        }
        
        // Check for group
        if (xml.Contains("v:group"))
        {
            Console.WriteLine($"  Contains: v:group");
        }
        
        // Check for shape
        if (xml.Contains("v:shape") || xml.Contains("v:rect") || xml.Contains("v:roundrect"))
        {
            Console.WriteLine($"  Contains: VML shape elements");
        }
        
        // Check for image
        if (xml.Contains("v:imagedata"))
        {
            Console.WriteLine($"  Contains: Image data");
        }
    }
}

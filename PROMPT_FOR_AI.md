# Problem: Mathematical Formulas in Word Documents Not Being Extracted

## Context
I'm building a .NET 9.0 service that extracts content from Word (.docx) files using DocumentFormat.OpenXml library and stores each element (paragraphs, tables, images, formulas) as separate rows in a MySQL database table called `dokumen_elemen`.

## Current Implementation
```csharp
// Service extracts Word document elements
public async Task ExtractDocxToDatabase(string docxPath, int dokumenId)
{
    using var doc = WordprocessingDocument.Open(docxPath, false);
    var body = doc.MainDocumentPart!.Document.Body!;
    int seq = 1;
    
    foreach (var elem in body.Elements())
    {
        var (type, json) = ConvertElementToJson(elem, doc.MainDocumentPart);
        
        var dokumenElemen = new DokumenElemen
        {
            DokumenId = dokumenId,
            DokumenElemenSequence = seq++,
            DokumenElemenType = type,
            DokumenElemenJsonTree = json
        };
        
        _db.DokumenElemens.Add(dokumenElemen);
    }
    
    await _db.SaveChangesAsync();
}
```

## What Works
- Paragraphs are extracted correctly
- Tables are extracted correctly
- Images are extracted correctly
- Text formatting is captured

## The Problem
**Mathematical formulas created using Word's Equation Editor (Insert → Equation) are NOT being saved to the database.**

I've added handlers for:
1. `DocumentFormat.OpenXml.Math.OfficeMath` at Body level (standalone formulas)
2. `OfficeMath` inside Paragraph.ChildElements (inline formulas)
3. `OfficeMath` inside Run.ChildElements (formulas within text runs)

But formulas still don't appear in the database.

## Code Snippets

### ConvertElementToJson method:
```csharp
switch (elem)
{
    case Paragraph p:
        type = DetectParagraphType(p);
        var content = ExtractParagraphContent(p);
        result = content.Count > 0 ? new JObject { ["content"] = content } : new JObject();
        break;
    
    case DocumentFormat.OpenXml.Math.OfficeMath math:
        type = "math";
        result = new JObject { ["xml"] = math.OuterXml };
        break;
    
    // ... other cases
}
```

### ExtractParagraphContent method:
```csharp
foreach (var elem in p.ChildElements)
{
    if (elem is DocumentFormat.OpenXml.Math.OfficeMath math)
    {
        FlushText();
        content.Add(new JObject { ["type"] = "math", ["xml"] = math.OuterXml });
        continue;
    }

    if (elem is not Run run) continue;

    foreach (var child in run.ChildElements)
    {
        if (child is DocumentFormat.OpenXml.Math.OfficeMath mathInRun)
        {
            FlushText();
            content.Add(new JObject { ["type"] = "math", ["xml"] = mathInRun.OuterXml });
            continue;
        }
        
        // ... handle other elements
    }
}
```

## Database Schema
```sql
CREATE TABLE dokumen_elemen (
    dokumen_elemen_id BIGINT PRIMARY KEY AUTO_INCREMENT,
    dokumen_id INT,
    dokumen_elemen_sequence INT,
    dokumen_elemen_type VARCHAR(100),
    dokumen_elemen_json_tree JSON
);
```

## Questions
1. **Where exactly are mathematical formulas located in the OpenXML document structure?** Are they in Body.Elements(), Paragraph.ChildElements, Run.ChildElements, or somewhere else?

2. **What is the correct way to detect and extract OfficeMath elements?** Should I use `OfficeMath`, `Paragraph.ParagraphProperties.MathParagraph`, or something else?

3. **Are there different types of formulas in Word?** (e.g., old Equation Editor 3.0 vs new Equation Editor, MathML, images)

4. **How can I debug this?** What logging should I add to see what element types are actually present in the document?

5. **Is there a complete example** of extracting all Word document elements including formulas using DocumentFormat.OpenXml?

## Expected Output
Each formula should be saved as a separate row in `dokumen_elemen` table with:
- `dokumen_elemen_type` = "math"
- `dokumen_elemen_json_tree` = JSON containing the MathML/XML representation

## Additional Info
- Using DocumentFormat.OpenXml package
- .NET 9.0
- Formulas are created using Insert → Equation in Word 2016+
- Other elements (text, tables, images) are being extracted successfully

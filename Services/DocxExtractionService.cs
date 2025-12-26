using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IDocxExtractionService
{
    Task ExtractDocxToDatabase(string docxPath, int dokumenId);
}

public class DocxExtractionService : IDocxExtractionService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<DocxExtractionService> _logger;
    private readonly string _storagePath;

    public DocxExtractionService(KorektorBukuDbContext db, ILogger<DocxExtractionService> logger)
    {
        _db = db;
        _logger = logger;
        _storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
    }

    public async Task ExtractDocxToDatabase(string docxPath, int dokumenId)
    {
        try
        {
            _logger.LogInformation("Starting extraction for dokumen {DokumenId}, path: {Path}", dokumenId, docxPath);
            
            using var doc = WordprocessingDocument.Open(docxPath, false);
            
            await ExtractAllMedia(doc, dokumenId);
            
            var body = doc.MainDocumentPart!.Document.Body!;
            int seq = 1;
            
            foreach (var elem in body.Elements())
            {
                foreach (var (type, json) in ConvertBodyElementToItems(elem))
                {
                    _db.DokumenElemens.Add(new DokumenElemen
                    {
                        DokumenId = dokumenId,
                        DokumenElemenSequence = seq++,
                        DokumenElemenType = type,
                        DokumenElemenJsonTree = json.ToString(Formatting.None)
                    });
                }
            }
            
            await _db.SaveChangesAsync();
            _logger.LogInformation("Extraction completed for dokumen {DokumenId}, {Count} elements", dokumenId, seq - 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for dokumen {DokumenId}", dokumenId);
            throw;
        }
    }

    private async Task ExtractAllMedia(WordprocessingDocument doc, int dokumenId)
    {
        var main = doc.MainDocumentPart!;
        
        foreach (var imgPart in main.ImageParts)
        {
            string rId = main.GetIdOfPart(imgPart);
            
            using var stream = imgPart.GetStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            byte[] bytes = ms.ToArray();
            
            string folder = Path.Combine(_storagePath, "dokumen_images", dokumenId.ToString());
            Directory.CreateDirectory(folder);
            
            string ext = imgPart.ContentType.Split('/').Last();
            string filename = $"{Guid.NewGuid()}.{ext}";
            string filepath = Path.Combine(folder, filename);
            
            await File.WriteAllBytesAsync(filepath, bytes);
            
            var dokumenMedia = new DokumenMedia
            {
                DokumenId = dokumenId,
                DokumenMediaRid = rId,
                DokumenMediaFilename = filename,
                DokumenMediaFilepath = filepath,
                DokumenMediaContentType = imgPart.ContentType
            };
            
            _db.DokumenMedias.Add(dokumenMedia);
        }
        
        await _db.SaveChangesAsync();
    }

    private IEnumerable<(string type, JObject json)> ConvertBodyElementToItems(OpenXmlElement elem)
    {
        if (elem is Paragraph p)
            return FlattenParagraph(p);

        if (elem is Table t)
            return new[] { ("table", new JObject { ["content"] = new JObject { ["rows"] = ConvertTableRows(t, null!) } }) };

        if (elem is SectionProperties)
            return new[] { ("sectionBreak", new JObject()) };

        if (elem is DocumentFormat.OpenXml.Math.OfficeMath math)
            return new[] { ("math", new JObject { ["content"] = new JArray { new JObject { ["type"] = "math", ["text"] = ExtractMathText(math) } } }) };

        if (elem is BookmarkStart || elem is BookmarkEnd)
            return Array.Empty<(string, JObject)>();

        return new[] { (elem.LocalName, new JObject { ["xml"] = elem.OuterXml }) };
    }

    private IEnumerable<(string type, JObject json)> FlattenParagraph(Paragraph p)
    {
        var paragraphType = DetectParagraphType(p);
        var content = ExtractParagraphContent(p);

        if (content.Count == 0) yield break;

        yield return (paragraphType, new JObject { ["content"] = content });
    }

    private JArray ExtractParagraphContent(Paragraph p)
    {
        var content = new JArray();
        var sb = new System.Text.StringBuilder();

        void FlushText()
        {
            var text = sb.ToString();
            sb.Clear();
            if (!string.IsNullOrWhiteSpace(text))
                content.Add(new JObject { ["type"] = "text", ["value"] = text });
        }

        void ProcessElement(OpenXmlElement elem)
        {
            if (elem is DocumentFormat.OpenXml.Math.OfficeMath om)
            {
                FlushText();
                var result = om.InnerText?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(result))
                    content.Add(new JObject { ["type"] = "math", ["text"] = result });
            }
            else if (elem is DocumentFormat.OpenXml.Math.Paragraph mathPara)
            {
                FlushText();
                foreach (var oMath in mathPara.Elements<DocumentFormat.OpenXml.Math.OfficeMath>())
                {
                    var result = string.Join("", oMath.Descendants<DocumentFormat.OpenXml.Math.Text>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                    if (!string.IsNullOrWhiteSpace(result))
                        content.Add(new JObject { ["type"] = "math", ["text"] = result });
                }
            }
            else if (elem is Text t)
            {
                sb.Append(t.Text);
            }
            else if (elem is TabChar)
            {
                sb.Append('\t');
            }
            else if (elem is Break)
            {
                sb.Append('\n');
            }
            else if (elem is Drawing drawing)
            {
                FlushText();
                var drawingItem = ExtractDrawingContent(drawing);
                if (drawingItem != null)
                    content.Add(drawingItem);
            }
            else if (elem is not DocumentFormat.OpenXml.Wordprocessing.TextBoxContent)
            {
                foreach (var child in elem.ChildElements)
                    ProcessElement(child);
            }
        }

        foreach (var child in p.ChildElements)
            ProcessElement(child);

        FlushText();
        return content;
    }

    private JObject? ExtractDrawingContent(Drawing drawing)
    {
        var blips = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>()
            .Where(b => b.Embed?.Value != null)
            .Select(b => b.Embed!.Value)
            .Distinct()
            .ToList();
        
        if (blips.Count > 0)
        {
            if (blips.Count == 1)
                return new JObject { ["type"] = "image", ["rId"] = blips[0] };
            
            var images = new JArray();
            foreach (var rId in blips)
                images.Add(new JObject { ["type"] = "image", ["rId"] = rId });
            
            return new JObject { ["type"] = "composite", ["content"] = images };
        }

        var shapeContent = new JArray();
        
        var txbxContent = drawing.Descendants<DocumentFormat.OpenXml.Wordprocessing.TextBoxContent>().FirstOrDefault();
        if (txbxContent != null)
        {
            var textboxItems = ExtractTextBoxContent(txbxContent);
            foreach (var item in textboxItems)
                shapeContent.Add(item);
        }
        else
        {
            var drawingTexts = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Text>().Select(t => t.Text);
            foreach (var txt in drawingTexts)
            {
                if (!string.IsNullOrWhiteSpace(txt))
                    shapeContent.Add(new JObject { ["type"] = "text", ["value"] = txt.Trim() });
            }
        }
        
        var result = new JObject { ["type"] = "shape" };
        
        if (shapeContent.Count > 0)
            result["content"] = shapeContent;
        
        return result;
    }

    private JArray ExtractTextBoxContent(OpenXmlElement container)
    {
        var content = new JArray();
        
        foreach (var elem in container.ChildElements)
        {
            if (elem is Paragraph para)
            {
                var paraContent = ExtractParagraphContent(para);
                foreach (var item in paraContent)
                    content.Add(item);
            }
            else if (elem is Table table)
            {
                var tableRows = ConvertTableRows(table, null!);
                content.Add(new JObject { ["type"] = "table", ["rows"] = tableRows });
            }
        }
        
        return content;
    }

    private string DetectParagraphType(Paragraph p)
    {
        var pPr = p.ParagraphProperties;
        
        if (pPr == null)
            return "paragraph";

        // Heading (h1-h9)
        if (pPr.ParagraphStyleId?.Val?.Value != null)
        {
            var styleId = pPr.ParagraphStyleId.Val.Value.ToLower();
            if (styleId.StartsWith("heading"))
            {
                var level = styleId.Replace("heading", "");
                return $"h{level}";
            }
            if (styleId.StartsWith("title"))
                return "title";
            if (styleId.StartsWith("subtitle"))
                return "subtitle";
        }

        // Numbering (ordered/unordered list)
        if (pPr.NumberingProperties?.NumberingId?.Val?.Value != null)
        {
            var numId = pPr.NumberingProperties.NumberingId.Val.Value;
            var ilvl = pPr.NumberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
            return $"list-item-{numId}-{ilvl}";
        }

        return "paragraph";
    }

    private string ExtractMathText(OpenXmlElement mathElement)
    {
        // Try InnerText first
        var innerText = mathElement.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(innerText))
        {
            _logger.LogInformation("Math InnerText: '{Text}'", innerText);
            return innerText;
        }
        
        // Fallback: extract from all Text descendants
        var texts = mathElement.Descendants<DocumentFormat.OpenXml.Math.Text>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        
        var result = string.Join("", texts);
        _logger.LogInformation("Math from descendants: '{Text}'", result);
        
        return string.IsNullOrWhiteSpace(result) ? "[formula]" : result;
    }



    private JArray ConvertTableRows(Table t, MainDocumentPart mainPart)
    {
        var rows = new JArray();

        foreach (var row in t.Elements<TableRow>())
        {
            var cells = new JArray();

            foreach (var cell in row.Elements<TableCell>())
            {
                var cellInlines = new JArray();
                
                foreach (var elem in cell.ChildElements)
                {
                    if (elem is Paragraph para)
                    {
                        var paraContent = ExtractParagraphContent(para);
                        foreach (var item in paraContent)
                            cellInlines.Add(item);
                    }
                    else if (elem is Table nestedTable)
                    {
                        var nestedRows = ConvertTableRows(nestedTable, null!);
                        cellInlines.Add(new JObject { ["type"] = "table", ["rows"] = nestedRows });
                    }
                }
                
                cells.Add(cellInlines);
            }

            rows.Add(new JObject { ["cells"] = cells });
        }

        return rows;
    }
}

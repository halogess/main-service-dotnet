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
        using var doc = WordprocessingDocument.Open(docxPath, false);
        
        await ExtractAllMedia(doc, dokumenId);
        
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

    private (string type, string json) ConvertElementToJson(OpenXmlElement elem, MainDocumentPart mainPart)
    {
        var result = new JObject();
        string type;

        switch (elem)
        {
            case Paragraph p:
                type = DetectParagraphType(p);
                result["content"] = ExtractParagraphContent(p);
                break;

            case Table t:
                type = "table";
                result["rows"] = ConvertTableRows(t, mainPart);
                break;

            case SectionProperties:
                type = "sectionBreak";
                break;

            default:
                type = elem.LocalName;
                result["xml"] = elem.OuterXml;
                break;
        }

        return (type, result.ToString(Formatting.None));
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

    private string ExtractParagraphContent(Paragraph p)
    {
        var content = new System.Text.StringBuilder();

        foreach (var run in p.Elements<Run>())
        {
            foreach (var text in run.Elements<Text>())
            {
                content.Append(text.Text);
            }

            foreach (var br in run.Elements<Break>())
            {
                if (br.Type == null || br.Type == BreakValues.TextWrapping)
                {
                    content.Append("<br/>");
                }
            }

            foreach (var drawing in run.Elements<Drawing>())
            {
                var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
                if (blip?.Embed?.Value != null)
                {
                    content.Append($"<img rId=\"{blip.Embed.Value}\"/>");
                }
            }
        }

        return content.ToString();
    }

    private JArray ConvertTableRows(Table t, MainDocumentPart mainPart)
    {
        var rows = new JArray();

        foreach (var row in t.Elements<TableRow>())
        {
            var rowObj = new JObject();
            var cells = new JArray();

            foreach (var cell in row.Elements<TableCell>())
            {
                var cellContent = new System.Text.StringBuilder();

                foreach (var p in cell.Elements<Paragraph>())
                {
                    if (cellContent.Length > 0)
                        cellContent.Append("<br/>");
                    cellContent.Append(ExtractParagraphContent(p));
                }

                cells.Add(cellContent.ToString());
            }

            rowObj["cells"] = cells;
            rows.Add(rowObj);
        }

        return rows;
    }
}

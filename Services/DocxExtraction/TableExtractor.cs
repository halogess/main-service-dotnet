using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles extraction of table content
/// </summary>
public class TableExtractor
{
    private readonly ILogger _logger;
    private readonly Func<Paragraph, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, JArray> _extractParagraphContent;
    private readonly Func<Paragraph, string> _detectParagraphType;

    public TableExtractor(
        ILogger logger,
        Func<Paragraph, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, JArray> extractParagraphContent,
        Func<Paragraph, string> detectParagraphType)
    {
        _logger = logger;
        _extractParagraphContent = extractParagraphContent;
        _detectParagraphType = detectParagraphType;
    }

    public JArray ConvertTableRows(
        Table table, 
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var rows = new JArray();
        
        foreach (var row in table.Descendants<TableRow>())
        {
            var rowJson = new JObject { ["cells"] = new JArray() };
            foreach (var cell in row.Descendants<TableCell>())
            {
                var cellContent = new JArray();
                
                foreach (var element in cell.Elements())
                {
                    if (element is Paragraph p)
                    {
                        var pType = _detectParagraphType(p);
                        var pContent = _extractParagraphContent(p, numberingPart, numberingCounters);
                        if (pContent.Count > 0)
                            cellContent.Add(new JObject { ["type"] = pType, ["content"] = pContent });
                    }
                    else if (element is Table nestedTable)
                    {
                        var nestedRows = ConvertTableRows(nestedTable, numberingPart, numberingCounters);
                        cellContent.Add(new JObject { ["type"] = "table", ["content"] = new JObject { ["rows"] = nestedRows } });
                    }
                }
                
                ((JArray)rowJson["cells"]!).Add(new JObject { ["content"] = cellContent });
            }
            rows.Add(rowJson);
        }
        return rows;
    }
}

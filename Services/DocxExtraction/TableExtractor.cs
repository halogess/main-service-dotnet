using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles extraction of table content with format property resolution
/// </summary>
public class TableExtractor
{
    private readonly ILogger _logger;
    private readonly KorektorBukuDbContext _db;
    private readonly TableFormatExtractor _tableFormatExtractor;
    private readonly TableRowFormatExtractor _rowFormatExtractor;
    private readonly TableCellFormatExtractor _cellFormatExtractor;
    private readonly ParagraphFormatExtractor? _paragraphFormatExtractor;
    private readonly Func<Paragraph, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, JArray> _extractParagraphContent;
    private readonly Func<Paragraph, string> _detectParagraphType;

    public TableExtractor(
        ILogger logger,
        KorektorBukuDbContext db,
        TableFormatExtractor tableFormatExtractor,
        TableRowFormatExtractor rowFormatExtractor,
        TableCellFormatExtractor cellFormatExtractor,
        ParagraphFormatExtractor? paragraphFormatExtractor,
        Func<Paragraph, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, JArray> extractParagraphContent,
        Func<Paragraph, string> detectParagraphType)
    {
        _logger = logger;
        _db = db;
        _tableFormatExtractor = tableFormatExtractor;
        _rowFormatExtractor = rowFormatExtractor;
        _cellFormatExtractor = cellFormatExtractor;
        _paragraphFormatExtractor = paragraphFormatExtractor;
        _extractParagraphContent = extractParagraphContent;
        _detectParagraphType = detectParagraphType;
    }

    public async Task<JObject> ConvertTableToJsonAsync(
        Table table, 
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var tableJson = new JObject();
        
        try
        {
            // Extract and save table format
            var tableFormat = _tableFormatExtractor.ExtractFormat(table);
            _db.DokumenFormatTables.Add(tableFormat);
            await _db.SaveChangesAsync();
            tableJson["dft_id"] = tableFormat.DftId;
            
            _logger.LogDebug("Table format saved with ID {DftId}", tableFormat.DftId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract/save table format. Table will be skipped.");
            throw;
        }
        
        try
        {
            // Extract rows with format
            var rows = await ConvertTableRowsAsync(table, numberingPart, numberingCounters);
            tableJson["content"] = new JObject { ["rows"] = rows };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract table rows. Table has dft_id={DftId}", tableJson["dft_id"]);
            throw;
        }
        
        return tableJson;
    }


    private async Task<JArray> ConvertTableRowsAsync(
        Table table, 
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        var rows = new JArray();
        var tableRows = table.Elements<TableRow>().ToList();
        int totalRows = tableRows.Count;
        int totalCols = GetTableColumnCount(table);
        
        for (int rowIndex = 0; rowIndex < tableRows.Count; rowIndex++)
        {
            var row = tableRows[rowIndex];
            
            // Extract and save row format
            var rowFormat = _rowFormatExtractor.ExtractFormat(row, rowIndex, totalRows, table);
            _db.DokumenFormatTableRows.Add(rowFormat);
            await _db.SaveChangesAsync();
            
            var rowJson = new JObject 
            { 
                ["dftr_id"] = rowFormat.DftrId,
                ["cells"] = new JArray() 
            };
            
            var cells = row.Elements<TableCell>().ToList();
            for (int colIndex = 0; colIndex < cells.Count; colIndex++)
            {
                var cell = cells[colIndex];
                
                // Extract and save cell format
                var cellFormat = _cellFormatExtractor.ExtractFormat(
                    cell, rowIndex, colIndex, totalRows, totalCols, table);
                _db.DokumenFormatTableCells.Add(cellFormat);
                await _db.SaveChangesAsync();
                
                var cellContent = new JArray();
                
                foreach (var element in cell.Elements())
                {
                    if (element is Paragraph p)
                    {
                        var pType = _detectParagraphType(p);
                        var pContent = _extractParagraphContent(p, numberingPart, numberingCounters);
                        uint? dfpId = null;
                        if (_paragraphFormatExtractor != null)
                        {
                            var format = _paragraphFormatExtractor.ExtractFormat(p);
                            _db.DokumenFormatParagrafs.Add(format);
                            await _db.SaveChangesAsync();
                            dfpId = format.DfpId;
                        }

                        if (pContent.Count > 0)
                        {
                            var paragraphJson = new JObject { ["type"] = pType };
                            if (dfpId.HasValue)
                                paragraphJson["dfp_id"] = dfpId.Value;
                            paragraphJson["content"] = pContent;
                            cellContent.Add(paragraphJson);
                        }
                    }
                    else if (element is Table nestedTable)
                    {
                        var nestedTableJson = await ConvertTableToJsonAsync(nestedTable, numberingPart, numberingCounters);
                        cellContent.Add(new JObject { ["type"] = "table", ["content"] = nestedTableJson });
                    }
                }
                
                ((JArray)rowJson["cells"]!).Add(new JObject 
                { 
                    ["dftc_id"] = cellFormat.DftcId,
                    ["content"] = cellContent 
                });
            }
            rows.Add(rowJson);
        }
        return rows;
    }
    
    /// <summary>
    /// Get the number of columns in a table (from first row's grid span)
    /// </summary>
    private int GetTableColumnCount(Table table)
    {
        var firstRow = table.Elements<TableRow>().FirstOrDefault();
        if (firstRow == null)
            return 0;
        
        int colCount = 0;
        foreach (var cell in firstRow.Elements<TableCell>())
        {
            var tcPr = cell.GetFirstChild<TableCellProperties>();
            var gridSpan = tcPr?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
            colCount += gridSpan;
        }
        
        return colCount;
    }
}

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;

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
    
    // Helper instances
    private readonly FloatingElementHelper _floatingHelper;
    private readonly MediaExtractor _mediaExtractor;
    private readonly DrawingExtractor _drawingExtractor;
    private readonly ParagraphExtractor _paragraphExtractor;
    private readonly TableExtractor _tableExtractor;

    public DocxExtractionService(KorektorBukuDbContext db, ILogger<DocxExtractionService> logger)
    {
        _db = db;
        _logger = logger;
        _storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        
        // Initialize helpers
        _floatingHelper = new FloatingElementHelper(_logger);
        _mediaExtractor = new MediaExtractor(_logger, _storagePath);
        _drawingExtractor = new DrawingExtractor(_logger);
        _paragraphExtractor = new ParagraphExtractor(_logger, _drawingExtractor);
        _tableExtractor = new TableExtractor(
            _logger, 
            _paragraphExtractor.ExtractParagraphContentSorted,
            _paragraphExtractor.DetectParagraphType);
    }

    public async Task ExtractDocxToDatabase(string docxPath, int dokumenId)
    {
        try
        {
            _logger.LogInformation("Starting extraction for dokumen {DokumenId}, path: {Path}", dokumenId, docxPath);
            
            using var doc = WordprocessingDocument.Open(docxPath, false);
            
            // Initialize StyleResolver for style chain resolution
            var stylesPart = doc.MainDocumentPart?.StyleDefinitionsPart;
            var styleResolver = new StyleResolver(stylesPart);
            _paragraphExtractor.SetStyleResolver(styleResolver);
            
            await _mediaExtractor.ExtractAllMedia(doc, dokumenId);
            
            var body = doc.MainDocumentPart!.Document.Body!;
            var numberingPart = doc.MainDocumentPart.NumberingDefinitionsPart;
            var numberingCounters = new Dictionary<int, Dictionary<int, int>>();
            
            // === SECTION EXTRACTION ===
            var sectionInfos = new List<(int elementIndex, SectionProperties sectPr)>();
            int elemIndex = 0;
            
            foreach (var elem in body.Elements())
            {
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
            
            var sectionIdMap = new List<(int upToElementIndex, uint dsecId)>();
            int sectionIndex = 0;
            
            foreach (var (upToIndex, sectPr) in sectionInfos)
            {
                var section = SectionExtractor.ExtractSectionProperties(sectPr, dokumenId, sectionIndex++);
                _db.DokumenSections.Add(section);
                await _db.SaveChangesAsync();
                sectionIdMap.Add((upToIndex, section.DsecId));
                _logger.LogInformation("Created section {Index} with ID {DsecId} for dokumen {DokumenId}", 
                    sectionIndex, section.DsecId, dokumenId);
            }
            
            // === ELEMENT EXTRACTION ===
            var elementsWithPosition = new List<(OpenXmlElement element, bool isFloating, int floatYPosition, int originalIndex)>();
            int idx = 0;
            
            foreach (var elem in body.Elements())
            {
                if (elem is SectionProperties) continue;
                
                var (isFloating, yPos) = _floatingHelper.DetectFloatingElement(elem);
                elementsWithPosition.Add((elem, isFloating, yPos, idx++));
            }
            
            var reorderedElements = _floatingHelper.ReorderFloatingElements(elementsWithPosition);
            
            uint GetSectionId(int originalElementIndex)
            {
                foreach (var (upToIndex, dsecId) in sectionIdMap)
                {
                    if (originalElementIndex <= upToIndex)
                        return dsecId;
                }
                return sectionIdMap.Count > 0 ? sectionIdMap[^1].dsecId : 0;
            }
            
            int seq = 1;
            var elementIndexMap = new Dictionary<OpenXmlElement, int>();
            idx = 0;
            foreach (var elem in body.Elements())
            {
                if (elem is not SectionProperties)
                    elementIndexMap[elem] = idx++;
            }
            
            foreach (var elem in reorderedElements)
            {
                int origIndex = elementIndexMap.TryGetValue(elem, out var i) ? i : 0;
                uint dsecId = GetSectionId(origIndex);
                
                foreach (var (type, json) in ConvertBodyElementToItems(elem, numberingPart, numberingCounters))
                {
                    _db.DokumenElemens.Add(new DokumenElemen
                    {
                        DokumenId = dokumenId,
                        DsecId = dsecId,
                        DokumenElemenSequence = seq++,
                        DokumenElemenType = type,
                        DokumenElemenJsonTree = json.ToString(Formatting.None),
                        DokumenElemenXml = elem.OuterXml
                    });
                }
            }
            
            await _db.SaveChangesAsync();
            _logger.LogInformation("Extraction completed for dokumen {DokumenId}, {Count} elements, {SectionCount} sections", 
                dokumenId, seq - 1, sectionInfos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for dokumen {DokumenId}", dokumenId);
            throw;
        }
    }

    private IEnumerable<(string type, JObject json)> ConvertBodyElementToItems(
        OpenXmlElement elem, 
        NumberingDefinitionsPart? numberingPart = null,
        Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
    {
        if (elem is Paragraph p)
            return _paragraphExtractor.FlattenParagraph(p, numberingPart, numberingCounters);

        if (elem is Table t)
            return new[] { ("table", new JObject { ["content"] = new JObject { ["rows"] = _tableExtractor.ConvertTableRows(t, numberingPart, numberingCounters) } }) };

        if (elem is SectionProperties)
            return new[] { ("sectionBreak", new JObject()) };

        if (elem is DocumentFormat.OpenXml.Math.OfficeMath math)
            return new[] { ("math", new JObject { ["content"] = new JArray { new JObject { ["type"] = "math", ["text"] = _paragraphExtractor.ExtractMathText(math) } } }) };

        if (elem is BookmarkStart || elem is BookmarkEnd)
            return Array.Empty<(string, JObject)>();

        return new[] { (elem.LocalName, new JObject { ["xml"] = elem.OuterXml }) };
    }
}

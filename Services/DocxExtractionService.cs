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
    }

    public async Task ExtractDocxToDatabase(string docxPath, int dokumenId)
    {
        try
        {
            _logger.LogInformation("Starting extraction for dokumen {DokumenId}, path: {Path}", dokumenId, docxPath);
            
            using var doc = WordprocessingDocument.Open(docxPath, false);
            var hasEvenAndOddHeaders = IsEvenAndOddHeadersEnabled(doc.MainDocumentPart?.DocumentSettingsPart);
            
            // Initialize StyleResolver for paragraph style chain resolution
            var stylesPart = doc.MainDocumentPart?.StyleDefinitionsPart;
            var styleResolver = new StyleResolver(stylesPart);
            _paragraphExtractor.SetStyleResolver(styleResolver);
            _paragraphExtractor.SetDbContext(_db); // Enable inline format saving
            
            // Initialize TableStyleResolver for table format resolution
            var tableStyleResolver = new TableStyleResolver(stylesPart);
            var tableFormatExtractor = new TableFormatExtractor(tableStyleResolver);
            var rowFormatExtractor = new TableRowFormatExtractor(tableStyleResolver);
            var cellFormatExtractor = new TableCellFormatExtractor(tableStyleResolver);
            
            // Initialize TableExtractor with format extractors
            var tableExtractor = new TableExtractor(
                _logger,
                _db,
                tableFormatExtractor,
                rowFormatExtractor,
                cellFormatExtractor,
                _paragraphExtractor.ExtractParagraphContentSorted,
                _paragraphExtractor.DetectParagraphType);
            
            // Inject table extraction callback into ParagraphExtractor for handling nested tables in textboxes
            _paragraphExtractor.SetTableExtractor(tableExtractor.ConvertTableToJsonAsync);
            
            await _mediaExtractor.ExtractAllMedia(doc, dokumenId);
            
            var body = doc.MainDocumentPart!.Document.Body!;
            var numberingPart = doc.MainDocumentPart.NumberingDefinitionsPart;
            var numberingCounters = new Dictionary<int, Dictionary<int, int>>();
            
            // Create ParagraphFormatExtractor with StyleResolver for effective property resolution
            var paragraphFormatExtractor = new ParagraphFormatExtractor(styleResolver, numberingPart);
            
            // === SECTION EXTRACTION ===
            // IMPORTANT: elemIndex must EXCLUDE SectionProperties to match elementIndexMap logic later
            var sectionInfos = new List<(int elementIndex, SectionProperties sectPr)>();
            int elemIndex = 0;
            
            foreach (var elem in body.Elements())
            {
                // Skip body-level SectionProperties in indexing (it's handled separately)
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
            
            // Map: sectionIndex -> (upToElementIndex, dsecId)
            var sectionIdMap = new List<(int upToElementIndex, uint dsecId, DokumenSection section)>();
            int sectionIndex = 0;
            
            foreach (var (upToIndex, sectPr) in sectionInfos)
            {
                var section = SectionExtractor.ExtractSectionProperties(sectPr, dokumenId, sectionIndex);
                SectionExtractor.UpdateOddEvenFromSettings(section, hasEvenAndOddHeaders);
                _db.DokumenSections.Add(section);
                await _db.SaveChangesAsync();
                sectionIdMap.Add((upToIndex, section.DsecId, section));
                _logger.LogInformation("Created section {Index} with ID {DsecId} for dokumen {DokumenId}", 
                    sectionIndex, section.DsecId, dokumenId);
                sectionIndex++;
            }
            
            // === PART EXTRACTION ===
            // For each section, create parts (body, headers, footers)
            var partMap = new Dictionary<uint, Dictionary<string, uint>>(); // dsecId -> (partKey -> dpartId)
            
            foreach (var (upToIndex, dsecId, section) in sectionIdMap)
            {
                partMap[dsecId] = new Dictionary<string, uint>();
                
                // Create body part for each section
                var bodyPart = new DokumenPart
                {
                    DsecId = dsecId,
                    DpartType = "body",
                    DpartPosition = null
                };
                _db.DokumenParts.Add(bodyPart);
                await _db.SaveChangesAsync();
                partMap[dsecId]["body"] = bodyPart.DpartId;
                _logger.LogDebug("Created body part {DpartId} for section {DsecId}", bodyPart.DpartId, dsecId);
            }
            
            // Extract header/footer references from each section and create parts
            sectionIndex = 0;
            foreach (var (upToIndex, sectPr) in sectionInfos)
            {
                var dsecId = sectionIdMap[sectionIndex].dsecId;
                
                // Get header references
                foreach (var headerRef in sectPr.Elements<HeaderReference>())
                {
                    var headerType = headerRef.Type?.Value ?? HeaderFooterValues.Default;
                    var position = GetPartPosition(headerType);
                    var partKey = $"header-{position}";
                    
                    if (!partMap[dsecId].ContainsKey(partKey))
                    {
                        var headerPart = new DokumenPart
                        {
                            DsecId = dsecId,
                            DpartType = "header",
                            DpartPosition = position
                        };
                        _db.DokumenParts.Add(headerPart);
                        await _db.SaveChangesAsync();
                        partMap[dsecId][partKey] = headerPart.DpartId;
                        _logger.LogDebug("Created header part {DpartId} ({Position}) for section {DsecId}", 
                            headerPart.DpartId, position, dsecId);
                        
                        // Extract header content
                        var headerPartDoc = doc.MainDocumentPart!.GetPartById(headerRef.Id!) as HeaderPart;
                        if (headerPartDoc?.Header != null)
                        {
                            await ExtractPartContent(headerPartDoc.Header, headerPart.DpartId, (uint)dokumenId, 
                                numberingPart, numberingCounters, paragraphFormatExtractor, tableExtractor);
                        }
                    }
                }
                
                // Get footer references
                foreach (var footerRef in sectPr.Elements<FooterReference>())
                {
                    var footerType = footerRef.Type?.Value ?? HeaderFooterValues.Default;
                    var position = GetPartPosition(footerType);
                    var partKey = $"footer-{position}";
                    
                    if (!partMap[dsecId].ContainsKey(partKey))
                    {
                        var footerPart = new DokumenPart
                        {
                            DsecId = dsecId,
                            DpartType = "footer",
                            DpartPosition = position
                        };
                        _db.DokumenParts.Add(footerPart);
                        await _db.SaveChangesAsync();
                        partMap[dsecId][partKey] = footerPart.DpartId;
                        _logger.LogDebug("Created footer part {DpartId} ({Position}) for section {DsecId}", 
                            footerPart.DpartId, position, dsecId);
                        
                        // Extract footer content
                        var footerPartDoc = doc.MainDocumentPart!.GetPartById(footerRef.Id!) as FooterPart;
                        if (footerPartDoc?.Footer != null)
                        {
                            await ExtractPartContent(footerPartDoc.Footer, footerPart.DpartId, (uint)dokumenId, 
                                numberingPart, numberingCounters, paragraphFormatExtractor, tableExtractor);
                        }
                    }
                }
                
                sectionIndex++;
            }
            
            // === BODY ELEMENT EXTRACTION ===
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
                foreach (var (upToIndex, dsecId, _) in sectionIdMap)
                {
                    if (originalElementIndex <= upToIndex)
                        return dsecId;
                }
                return sectionIdMap.Count > 0 ? sectionIdMap[^1].dsecId : 0;
            }
            
            int seq = 1;
            
            // Track which sequence contains footnote/endnote references
            // Key: (kind, refId), Value: sequence number
            var noteRefToSequence = new Dictionary<(string kind, long refId), uint>();
            
            foreach (var (elem, origIndex) in reorderedElements)
            {
                uint dsecId = GetSectionId(origIndex);
                uint dpartId = partMap.TryGetValue(dsecId, out var parts) && parts.TryGetValue("body", out var bodyPartId) 
                    ? bodyPartId : 0;
                
                uint? dfpId = null;
                // Extract paragraph format if this is a paragraph
                if (elem is Paragraph para)
                {
                    // Create format record for paragraph (with EFFECTIVE resolved properties)
                    var format = paragraphFormatExtractor.ExtractFormat(para);
                    _db.DokumenFormatParagrafs.Add(format);
                    await _db.SaveChangesAsync();
                    dfpId = format.DfpId;
                    
                    // Detect footnote/endnote references
                    foreach (var fnRef in para.Descendants<DocumentFormat.OpenXml.Wordprocessing.FootnoteReference>())
                    {
                        var refId = fnRef.Id?.Value ?? 0;
                        if (refId >= 1)
                            noteRefToSequence[("footnote", refId)] = (uint)seq;
                    }
                    foreach (var enRef in para.Descendants<DocumentFormat.OpenXml.Wordprocessing.EndnoteReference>())
                    {
                        var refId = enRef.Id?.Value ?? 0;
                        if (refId >= 1)
                            noteRefToSequence[("endnote", refId)] = (uint)seq;
                    }
                }
                
                try 
                {
                    foreach (var (type, json) in await ConvertBodyElementToItemsAsync(elem, numberingPart, numberingCounters, tableExtractor))
                    {
                        // Add dfp_id to JSON for paragraph/list types - ensure it comes before content
                        if (dfpId.HasValue && (type.StartsWith("paragraph") || type.StartsWith("list-item") || type.StartsWith("h") || type == "title" || type == "subtitle"))
                        {
                            // Reconstruct to put dfp_id before content
                            var reordered = new JObject { ["dfp_id"] = dfpId.Value };
                            foreach (var prop in json.Properties())
                            {
                                reordered[prop.Name] = prop.Value;
                            }
                            // Replace json with reordered version
                            json.RemoveAll();
                            foreach (var prop in reordered.Properties())
                            {
                                json[prop.Name] = prop.Value;
                            }
                        }
                        
                        _db.DokumenElemens.Add(new DokumenElemen
                        {
                            DpartId = dpartId > 0 ? dpartId : null,
                            DelemenSequence = (uint)seq++,
                            DelemenType = type,
                            DelemenJsonTree = json.ToString(Formatting.None),
                            DelemenXml = elem.OuterXml
                        });
                    }
                }
                catch (Exception ex)
                {
                    var xmlPreview = elem.OuterXml.Length > 200 ? elem.OuterXml.Substring(0, 200) + "..." : elem.OuterXml;
                    _logger.LogError(ex, 
                        "Failed to extract element sequence {Seq} of type {Type}. " +
                        "InnerException: {Inner}. XML Preview: {XmlPreview}. Skipping.", 
                        seq, elem.GetType().Name, 
                        ex.InnerException?.Message ?? "none",
                        xmlPreview);
                    // Continue to next element
                }
            }
            
            // Save remaining elements
            await _db.SaveChangesAsync();
            
            // Build map: sequence -> DelemenId
            // Get all parts for this dokumen to filter elements
            var allPartIds = partMap.Values.SelectMany(p => p.Values).ToHashSet();
            var sequenceToDelemenId = new Dictionary<uint, ulong>();
            var savedElements = _db.DokumenElemens
                .Where(e => e.DpartId.HasValue && allPartIds.Contains(e.DpartId.Value))
                .Select(e => new { e.DelemenId, e.DelemenSequence })
                .ToList();
            foreach (var elem in savedElements)
            {
                if (elem.DelemenSequence.HasValue)
                    sequenceToDelemenId[elem.DelemenSequence.Value] = elem.DelemenId;
            }
            
            // Helper to get DelemenId for a note
            ulong? GetDelemenIdForNote(string kind, long refId)
            {
                if (noteRefToSequence.TryGetValue((kind, refId), out var sequence))
                {
                    if (sequenceToDelemenId.TryGetValue(sequence, out var delemenId))
                        return delemenId;
                }
                return null;
            }
            
            // === FOOTNOTE EXTRACTION ===
            var footnotesPart = doc.MainDocumentPart!.FootnotesPart;
            if (footnotesPart?.Footnotes != null)
            {
                foreach (var footnote in footnotesPart.Footnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Footnote>())
                {
                    var fnId = footnote.Id?.Value ?? 0;
                    var fnType = footnote.Type?.Value.ToString().ToLower() ?? "normal";
                    
                    // Skip separator and continuation separator (they are system footnotes)
                    if (fnId < 1) continue;
                    
                    var jsonContent = ExtractNoteContent(footnote, numberingPart, numberingCounters);
                    
                    _db.DokumenNotes.Add(new DokumenNote
                    {
                        DokumenId = (uint)dokumenId,
                        DelemenId = GetDelemenIdForNote("footnote", fnId),
                        DnoteKind = "footnote",
                        DnoteType = fnType,
                        DnoteJsonTree = jsonContent.ToString(Formatting.None),
                        DnoteXml = footnote.OuterXml
                    });
                }
                _logger.LogDebug("Extracted footnotes for dokumen {DokumenId}", dokumenId);
            }
            
            // === ENDNOTE EXTRACTION ===
            var endnotesPart = doc.MainDocumentPart!.EndnotesPart;
            if (endnotesPart?.Endnotes != null)
            {
                foreach (var endnote in endnotesPart.Endnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Endnote>())
                {
                    var enId = endnote.Id?.Value ?? 0;
                    var enType = endnote.Type?.Value.ToString().ToLower() ?? "normal";
                    
                    // Skip separator and continuation separator (they are system endnotes)
                    if (enId < 1) continue;
                    
                    var jsonContent = ExtractNoteContent(endnote, numberingPart, numberingCounters);
                    
                    _db.DokumenNotes.Add(new DokumenNote
                    {
                        DokumenId = (uint)dokumenId,
                        DelemenId = GetDelemenIdForNote("endnote", enId),
                        DnoteKind = "endnote",
                        DnoteType = enType,
                        DnoteJsonTree = jsonContent.ToString(Formatting.None),
                        DnoteXml = endnote.OuterXml
                    });
                }
                _logger.LogDebug("Extracted endnotes for dokumen {DokumenId}", dokumenId);
            }
            
            await _db.SaveChangesAsync();
            
            int totalParts = partMap.Values.Sum(p => p.Count);
            _logger.LogInformation("Extraction completed for dokumen {DokumenId}: {ElementCount} elements, {SectionCount} sections, {PartCount} parts", 
                dokumenId, seq - 1, sectionInfos.Count, totalParts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for dokumen {DokumenId}", dokumenId);
            throw;
        }
    }

    /// <summary>
    /// Extract content from a header or footer part
    /// </summary>
    private async Task ExtractPartContent(
        OpenXmlCompositeElement partContent, 
        uint dpartId, 
        uint dokumenId,
        NumberingDefinitionsPart? numberingPart,
        Dictionary<int, Dictionary<int, int>> numberingCounters,
        ParagraphFormatExtractor paragraphFormatExtractor,
        TableExtractor tableExtractor)
    {
        int seq = 1;
        foreach (var elem in partContent.Elements())
        {
            if (elem is Paragraph para)
            {
                // Create format record for paragraph (with EFFECTIVE resolved properties)
                var format = paragraphFormatExtractor.ExtractFormat(para);
                _db.DokumenFormatParagrafs.Add(format);
                await _db.SaveChangesAsync();
                uint? dfpId = format.DfpId;
                
                foreach (var (type, json) in await ConvertBodyElementToItemsAsync(elem, numberingPart, numberingCounters, tableExtractor))
                {
                    // Reorder dfp_id to come before content
                    if (dfpId.HasValue)
                    {
                        var reordered = new JObject { ["dfp_id"] = dfpId.Value };
                        foreach (var prop in json.Properties())
                            reordered[prop.Name] = prop.Value;
                        json.RemoveAll();
                        foreach (var prop in reordered.Properties())
                            json[prop.Name] = prop.Value;
                    }
                    
                    _db.DokumenElemens.Add(new DokumenElemen
                    {
                        DpartId = dpartId,
                        DelemenSequence = (uint)seq++,
                        DelemenType = type,
                        DelemenJsonTree = json.ToString(Formatting.None),
                        DelemenXml = elem.OuterXml
                    });
                }
            }
            else if (elem is Table)
            {
                foreach (var (type, json) in await ConvertBodyElementToItemsAsync(elem, numberingPart, numberingCounters, tableExtractor))
                {
                    _db.DokumenElemens.Add(new DokumenElemen
                    {
                        DpartId = dpartId,
                        DelemenSequence = (uint)seq++,
                        DelemenType = type,
                        DelemenJsonTree = json.ToString(Formatting.None),
                        DelemenXml = elem.OuterXml
                    });
                }
            }
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Convert HeaderFooterValues to position string
    /// </summary>
    private static string GetPartPosition(HeaderFooterValues headerFooterType)
    {
        if (headerFooterType == HeaderFooterValues.First)
            return "first";
        if (headerFooterType == HeaderFooterValues.Even)
            return "even";
        return "default";
    }

    private async Task<IEnumerable<(string type, JObject json)>> ConvertBodyElementToItemsAsync(
        OpenXmlElement elem, 
        NumberingDefinitionsPart? numberingPart,
        Dictionary<int, Dictionary<int, int>>? numberingCounters,
        TableExtractor tableExtractor)
    {
        if (elem is Paragraph p)
            return _paragraphExtractor.FlattenParagraph(p, numberingPart, numberingCounters);

        if (elem is Table t)
        {
            var tableJson = await tableExtractor.ConvertTableToJsonAsync(t, numberingPart, numberingCounters);
            return new[] { ("table", tableJson) };
        }

        if (elem is SectionProperties)
            return new[] { ("sectionBreak", new JObject()) };

        if (elem is DocumentFormat.OpenXml.Math.OfficeMath math)
            return new[] { ("math", new JObject { ["content"] = new JArray { new JObject { ["type"] = "math", ["text"] = MathExtractor.ExtractMathText(math) } } }) };

        // Extract bookmark markers to maintain element count consistency
        if (elem is BookmarkStart bookmarkStart)
            return new[] { ("bookmarkStart", new JObject { ["name"] = bookmarkStart.Name?.Value ?? "", ["id"] = bookmarkStart.Id?.Value ?? "" }) };
        
        if (elem is BookmarkEnd bookmarkEnd)
            return new[] { ("bookmarkEnd", new JObject { ["id"] = bookmarkEnd.Id?.Value ?? "" }) };

        return new[] { (elem.LocalName, new JObject { ["xml"] = elem.OuterXml }) };
    }

    /// <summary>
    /// Extract content from a footnote or endnote as JSON
    /// </summary>
    private JObject ExtractNoteContent(
        OpenXmlCompositeElement noteElement,
        NumberingDefinitionsPart? numberingPart,
        Dictionary<int, Dictionary<int, int>> numberingCounters)
    {
        var content = new JArray();
        
        foreach (var elem in noteElement.Elements())
        {
            if (elem is Paragraph p)
            {
                foreach (var (type, json) in _paragraphExtractor.FlattenParagraph(p, numberingPart, numberingCounters))
                {
                    content.Add(new JObject { ["type"] = type, ["content"] = json["content"] });
                }
            }
            else if (elem is Table t)
            {
                // Note: Tables in footnotes/endnotes don't get format extraction
                // to keep the implementation simpler. They use basic structure only.
                content.Add(new JObject 
                { 
                    ["type"] = "table", 
                    ["rows"] = new JArray() // Empty for now, or extract basic structure without format
                });
            }
        }
        
        return new JObject { ["content"] = content };
    }

    private static bool IsEvenAndOddHeadersEnabled(DocumentSettingsPart? settingsPart)
    {
        var evenAndOdd = settingsPart?.Settings?.EvenAndOddHeaders;
        if (evenAndOdd == null)
            return false;

        var valAttr = evenAndOdd.GetAttribute("val", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        if (string.IsNullOrEmpty(valAttr.Value))
            return true;

        return valAttr.Value == "1" || valAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}

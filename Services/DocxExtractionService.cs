using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IDocxExtractionService
{
    Task ExtractDocxToDatabase(string docxPath, int dokumenId);
    Task ExtractDocxToDatabase(string docxPath, string refTipe, uint refId);
}

public class DocxExtractionService : IDocxExtractionService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<DocxExtractionService> _logger;
    // Helper instances
    private readonly FloatingElementHelper _floatingHelper;
    private readonly DrawingExtractor _drawingExtractor;
    private readonly ParagraphExtractor _paragraphExtractor;

    public DocxExtractionService(KorektorBukuDbContext db, ILogger<DocxExtractionService> logger)
    {
        _db = db;
        _logger = logger;
        // Initialize helpers
        _floatingHelper = new FloatingElementHelper(_logger);
        _drawingExtractor = new DrawingExtractor(_logger);
        _paragraphExtractor = new ParagraphExtractor(_logger, _drawingExtractor);
    }

    public async Task ExtractDocxToDatabase(string docxPath, int dokumenId)
        => await ExtractDocxToDatabase(docxPath, "dokumen", (uint)dokumenId);

    public async Task ExtractDocxToDatabase(string docxPath, string refTipe, uint refId)
    {
        var normalizedRefTipe = string.IsNullOrWhiteSpace(refTipe) ? "dokumen" : refTipe.Trim().ToLowerInvariant();
        var supportsNotes =
            normalizedRefTipe == "dokumen" ||
            normalizedRefTipe == "bab" ||
            normalizedRefTipe == "aturan";

        try
        {
            _logger.LogInformation("Starting extraction for {RefTipe} {RefId}, path: {Path}", normalizedRefTipe, refId, docxPath);
            
            using var doc = WordprocessingDocument.Open(docxPath, false);
            var hasEvenAndOddHeaders = IsEvenAndOddHeadersEnabled(doc.MainDocumentPart?.DocumentSettingsPart);
            
            // Initialize StyleResolver for paragraph style chain resolution
            var stylesPart = doc.MainDocumentPart?.StyleDefinitionsPart;
            var stylesWithEffectsPart = doc.MainDocumentPart?.StylesWithEffectsPart;
            var numberingPart = doc.MainDocumentPart?.NumberingDefinitionsPart;
            var themeResolver = ThemeFontResolver.FromThemePart(doc.MainDocumentPart?.ThemePart);
            var themeLangResolver = ThemeFontLangResolver.FromSettingsPart(doc.MainDocumentPart?.DocumentSettingsPart);
            var styleResolver = new StyleResolver(stylesPart, stylesWithEffectsPart, themeResolver, numberingPart);
            _paragraphExtractor.SetStyleResolver(styleResolver);
            _paragraphExtractor.SetThemeFontLangResolver(themeLangResolver);
            _paragraphExtractor.SetThemeFontResolver(themeResolver);
            _paragraphExtractor.SetDbContext(_db); // Enable inline format saving
            
            // Initialize TableStyleResolver for table format resolution
            var tableStyleResolver = new TableStyleResolver(stylesPart, stylesWithEffectsPart);
            var tableFormatExtractor = new TableFormatExtractor(tableStyleResolver);

            // Create ParagraphFormatExtractor with StyleResolver for effective property resolution
            var paragraphFormatExtractor = new ParagraphFormatExtractor(styleResolver, numberingPart);
            
            // Initialize TableExtractor with format extractors
            var tableExtractor = new TableExtractor(
                _logger,
                _db,
                tableFormatExtractor,
                paragraphFormatExtractor,
                _paragraphExtractor.ExtractParagraphContentSorted,
                _paragraphExtractor.DetectParagraphType);
            
            // Inject table extraction callback into ParagraphExtractor for handling nested tables in textboxes
            _paragraphExtractor.SetTableExtractor(tableExtractor.ConvertTableToJsonAsync);
            _paragraphExtractor.ResetNumberingState();
            
            var body = doc.MainDocumentPart!.Document.Body!;
            var numberingCounters = new Dictionary<int, Dictionary<int, int>>();
            var defaultTabStopTwips = GetDefaultTabStopTwips(doc.MainDocumentPart?.DocumentSettingsPart);
            var useHangingIndentTabStop = ShouldUseHangingIndentTabStop(doc.MainDocumentPart?.DocumentSettingsPart);
            
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
                var section = SectionExtractor.ExtractSectionProperties(sectPr, normalizedRefTipe, refId, sectionIndex);
                SectionExtractor.UpdateOddEvenFromSettings(section, hasEvenAndOddHeaders);
                _db.DokumenSections.Add(section);
                await _db.SaveChangesAsync();
                sectionIdMap.Add((upToIndex, section.DsecId, section));
                _logger.LogInformation("Created section {Index} with ID {DsecId} for {RefTipe} {RefId}", 
                    sectionIndex, section.DsecId, normalizedRefTipe, refId);
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
                            await ExtractPartContent(headerPartDoc.Header, headerPart.DpartId, refId, 
                                numberingPart, numberingCounters, paragraphFormatExtractor, tableExtractor, defaultTabStopTwips, useHangingIndentTabStop);
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
                            await ExtractPartContent(footerPartDoc.Footer, footerPart.DpartId, refId, 
                                numberingPart, numberingCounters, paragraphFormatExtractor, tableExtractor, defaultTabStopTwips, useHangingIndentTabStop);
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
            _paragraphExtractor.ResetNumberingState();
            
            // Track which sequence contains footnote/endnote references
            // Key: (kind, refId), Value: sequence number
            var noteRefToSequence = new Dictionary<(string kind, long refId), uint>();
            
            foreach (var (elem, origIndex) in reorderedElements)
            {
                uint dsecId = GetSectionId(origIndex);
                uint dpartId = partMap.TryGetValue(dsecId, out var parts) && parts.TryGetValue("body", out var bodyPartId) 
                    ? bodyPartId : 0;
                
                uint? dfpId = null;
                List<(string type, JObject json)>? cachedItems = null;
                // Extract paragraph format if this is a paragraph
                if (elem is Paragraph para)
                {
                    cachedItems = (await ConvertBodyElementToItemsAsync(elem, numberingPart, numberingCounters, tableExtractor))
                        .ToList();

                    // Create format record for paragraph (with EFFECTIVE resolved properties)
                    var format = paragraphFormatExtractor.ExtractFormat(para);

                    if (format.DfpIsList && numberingPart != null && format.DfpListNumId.HasValue && format.DfpListIlvl.HasValue)
                    {
                        var label = TryGetNumberingLabelFromItems(cachedItems);
                        var level = NumberingResolver.GetNumberingLevel(numberingPart, (int)format.DfpListNumId.Value, (int)format.DfpListIlvl.Value);
                        var effectiveHanging = NumberingLayoutHelper.TryComputeEffectiveHangingTwips(
                            level,
                            label,
                            defaultTabStopTwips,
                            useHangingIndentTabStop);
                        if (effectiveHanging.HasValue)
                        {
                            var existingHanging = format.DfpIndHangingTwips ?? 0;
                            var resolvedHanging = (long)effectiveHanging.Value;
                            if (resolvedHanging > existingHanging)
                                format.DfpIndHangingTwips = resolvedHanging;
                        }
                    }

                    _db.DokumenFormatParagrafs.Add(format);
                    await _db.SaveChangesAsync();
                    dfpId = format.DfpId;
                    
                    // Detect footnote/endnote references
                    foreach (var fnRef in para.Descendants<DocumentFormat.OpenXml.Wordprocessing.FootnoteReference>())
                    {
                        var noteRefId = fnRef.Id?.Value ?? 0;
                        if (noteRefId >= 1)
                            noteRefToSequence[("footnote", noteRefId)] = (uint)seq;
                    }
                    foreach (var enRef in para.Descendants<DocumentFormat.OpenXml.Wordprocessing.EndnoteReference>())
                    {
                        var noteRefId = enRef.Id?.Value ?? 0;
                        if (noteRefId >= 1)
                            noteRefToSequence[("endnote", noteRefId)] = (uint)seq;
                    }
                }
                
                try 
                {
                    var items = cachedItems ?? (await ConvertBodyElementToItemsAsync(elem, numberingPart, numberingCounters, tableExtractor)).ToList();
                    foreach (var (type, json) in items)
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
            if (supportsNotes && footnotesPart?.Footnotes != null)
            {
                foreach (var footnote in footnotesPart.Footnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Footnote>())
                {
                    var fnId = footnote.Id?.Value ?? 0;
                    var fnType = footnote.Type?.Value.ToString().ToLower() ?? "normal";

                    var jsonContent = await ExtractNoteContent(
                        footnote,
                        paragraphFormatExtractor,
                        numberingPart,
                        numberingCounters);

                    _db.DokumenNotes.Add(new DokumenNote
                    {
                        DnoteRefTipe = normalizedRefTipe,
                        DnoteRefId = refId,
                        DelemenId = fnId >= 1 ? GetDelemenIdForNote("footnote", fnId) : null,
                        DnoteKind = "footnote",
                        DnoteType = fnType,
                        DnoteNumber = fnId >= 1 ? (uint?)fnId : null,
                        DnoteJsonTree = jsonContent.ToString(Formatting.None)
                    });
                }
                _logger.LogDebug("Extracted footnotes for {RefTipe} {RefId}", normalizedRefTipe, refId);
            }

            // === ENDNOTE EXTRACTION ===
            var endnotesPart = doc.MainDocumentPart!.EndnotesPart;
            if (supportsNotes && endnotesPart?.Endnotes != null)
            {
                foreach (var endnote in endnotesPart.Endnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Endnote>())
                {
                    var enId = endnote.Id?.Value ?? 0;
                    var enType = endnote.Type?.Value.ToString().ToLower() ?? "normal";

                    var jsonContent = await ExtractNoteContent(
                        endnote,
                        paragraphFormatExtractor,
                        numberingPart,
                        numberingCounters);

                    _db.DokumenNotes.Add(new DokumenNote
                    {
                        DnoteRefTipe = normalizedRefTipe,
                        DnoteRefId = refId,
                        DelemenId = enId >= 1 ? GetDelemenIdForNote("endnote", enId) : null,
                        DnoteKind = "endnote",
                        DnoteType = enType,
                        DnoteNumber = enId >= 1 ? (uint?)enId : null,
                        DnoteJsonTree = jsonContent.ToString(Formatting.None)
                    });
                }
                _logger.LogDebug("Extracted endnotes for {RefTipe} {RefId}", normalizedRefTipe, refId);
            }
            
            await _db.SaveChangesAsync();
            
            int totalParts = partMap.Values.Sum(p => p.Count);
            _logger.LogInformation("Extraction completed for {RefTipe} {RefId}: {ElementCount} elements, {SectionCount} sections, {PartCount} parts", 
                normalizedRefTipe, refId, seq - 1, sectionInfos.Count, totalParts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for {RefTipe} {RefId}", refTipe, refId);
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
        TableExtractor tableExtractor,
        int defaultTabStopTwips,
        bool useHangingIndentTabStop)
    {
        int seq = 1;
        _paragraphExtractor.ResetNumberingState();
        
        async Task ProcessPartElement(OpenXmlElement elem)
        {
            if (elem is Paragraph para)
            {
                var items = (await ConvertBodyElementToItemsAsync(elem, numberingPart, numberingCounters, tableExtractor))
                    .ToList();

                // Create format record for paragraph (with EFFECTIVE resolved properties)
                var format = paragraphFormatExtractor.ExtractFormat(para);

                if (format.DfpIsList && numberingPart != null && format.DfpListNumId.HasValue && format.DfpListIlvl.HasValue)
                {
                    var label = TryGetNumberingLabelFromItems(items);
                    var level = NumberingResolver.GetNumberingLevel(numberingPart, (int)format.DfpListNumId.Value, (int)format.DfpListIlvl.Value);
                    var effectiveHanging = NumberingLayoutHelper.TryComputeEffectiveHangingTwips(
                        level,
                        label,
                        defaultTabStopTwips,
                        useHangingIndentTabStop);
                    if (effectiveHanging.HasValue)
                    {
                        var existingHanging = format.DfpIndHangingTwips ?? 0;
                        var resolvedHanging = (long)effectiveHanging.Value;
                        if (resolvedHanging > existingHanging)
                            format.DfpIndHangingTwips = resolvedHanging;
                    }
                }

                _db.DokumenFormatParagrafs.Add(format);
                await _db.SaveChangesAsync();
                uint? dfpId = format.DfpId;

                foreach (var (type, json) in items)
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
                return;
            }
            
            if (elem is Table)
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
                return;
            }
            
            if (elem is SdtBlock sdtBlock)
            {
                var content = sdtBlock.SdtContentBlock;
                if (content != null)
                {
                    foreach (var child in content.Elements())
                        await ProcessPartElement(child);
                }
            }
        }
        
        foreach (var elem in partContent.Elements())
            await ProcessPartElement(elem);

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

    private static int GetDefaultTabStopTwips(DocumentSettingsPart? settingsPart)
    {
        var defaultTab = settingsPart?.Settings?.Elements<DefaultTabStop>().FirstOrDefault();
        return defaultTab?.Val?.Value ?? 720;
    }

    private static bool ShouldUseHangingIndentTabStop(DocumentSettingsPart? settingsPart)
    {
        var settingsXml = settingsPart?.Settings?.OuterXml;
        if (string.IsNullOrWhiteSpace(settingsXml))
            return true;

        try
        {
            var doc = XDocument.Parse(settingsXml);
            var root = doc.Root;
            if (root == null)
                return true;

            var wordNs = root.GetDefaultNamespace();
            if (wordNs == XNamespace.None)
                wordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

            var compat = root.Element(wordNs + "compat");
            if (compat == null)
                return true;

            var disableIndentAsNumberingTabStop = compat
                .Elements(wordNs + "doNotUseIndentAsNumberingTabStop")
                .Any(e => IsOnOffEnabled(e, wordNs));
            var disableHangingVirtualTabStop = compat
                .Elements(wordNs + "noTabHangInd")
                .Any(e => IsOnOffEnabled(e, wordNs));

            return !(disableIndentAsNumberingTabStop || disableHangingVirtualTabStop);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsOnOffEnabled(XElement element, XNamespace wordNs)
    {
        var val = element.Attribute(wordNs + "val")?.Value ?? element.Attribute("val")?.Value;
        if (string.IsNullOrWhiteSpace(val))
            return true;

        return !string.Equals(val, "0", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(val, "off", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetNumberingLabelFromItems(IEnumerable<(string type, JObject json)> items)
    {
        foreach (var (_, json) in items)
        {
            if (json["content"] is not JArray content || content.Count == 0)
                continue;

            foreach (var token in content)
            {
                if (token?["type"]?.ToString() != "text")
                    continue;

                var value = token?["value"]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Extract content from a footnote or endnote as JSON
    /// </summary>
    private async Task<JObject> ExtractNoteContent(
        OpenXmlCompositeElement noteElement,
        ParagraphFormatExtractor paragraphFormatExtractor,
        NumberingDefinitionsPart? numberingPart,
        Dictionary<int, Dictionary<int, int>> numberingCounters)
    {
        _paragraphExtractor.ResetNumberingState();
        var content = new JArray();
        
        foreach (var elem in noteElement.Elements())
        {
            if (elem is Paragraph p)
            {
                var format = paragraphFormatExtractor.ExtractFormat(p);
                _db.DokumenFormatParagrafs.Add(format);
                await _db.SaveChangesAsync();

                foreach (var (type, json) in _paragraphExtractor.FlattenParagraph(p, numberingPart, numberingCounters))
                {
                    var paragraphJson = new JObject
                    {
                        ["type"] = type,
                        ["dfp_id"] = format.DfpId,
                        ["content"] = json["content"] ?? new JArray()
                    };
                    content.Add(paragraphJson);
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
        var evenAndOdd = settingsPart?.Settings?.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.EvenAndOddHeaders>();
        if (evenAndOdd == null)
            return false;

        var valAttr = evenAndOdd.GetAttribute("val", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        if (string.IsNullOrEmpty(valAttr.Value))
            return true;

        return valAttr.Value == "1" || valAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}

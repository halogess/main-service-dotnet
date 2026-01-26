using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Resolves styles with full inheritance:
/// docDefaults → basedOn chain → paragraph style → character style → direct formatting
/// </summary>
public class StyleResolver
{
    private readonly Dictionary<string, Style> _stylesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThemeFontResolver? _themeFontResolver;
    private readonly NumberingDefinitionsPart? _numberingPart;
    private readonly Styles? _stylesRoot;
    private readonly string? _defaultParagraphStyleId;
    private readonly string? _defaultCharacterStyleId;
    
    // Cached docDefaults
    private RunPropertiesBaseStyle? _docDefaultsRPr;
    private ParagraphPropertiesBaseStyle? _docDefaultsPPr;
    
    public StyleResolver(
        StylesPart? stylesPart,
        StylesWithEffectsPart? stylesWithEffectsPart,
        ThemeFontResolver? themeFontResolver = null,
        NumberingDefinitionsPart? numberingPart = null)
    {
        _themeFontResolver = themeFontResolver;
        _numberingPart = numberingPart;

        var primaryStyles = stylesWithEffectsPart?.Styles;
        var fallbackStyles = stylesPart?.Styles;
        _stylesRoot = primaryStyles ?? fallbackStyles;

        if (_stylesRoot != null)
        {
            // Cache all styles by ID
            if (fallbackStyles != null)
                AddStylesToCache(fallbackStyles, overwrite: false);
            if (primaryStyles != null)
                AddStylesToCache(primaryStyles, overwrite: true);
            
            // Cache docDefaults
            var docDefaults = primaryStyles?.DocDefaults ?? fallbackStyles?.DocDefaults;
            if (docDefaults != null)
            {
                _docDefaultsRPr = docDefaults.RunPropertiesDefault?.RunPropertiesBaseStyle;
                _docDefaultsPPr = docDefaults.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
            }

            _defaultParagraphStyleId = GetDefaultStyleId(primaryStyles, StyleValues.Paragraph)
                ?? GetDefaultStyleId(fallbackStyles, StyleValues.Paragraph);

            _defaultCharacterStyleId = GetDefaultStyleId(primaryStyles, StyleValues.Character)
                ?? GetDefaultStyleId(fallbackStyles, StyleValues.Character);

            if (string.IsNullOrWhiteSpace(_defaultParagraphStyleId) && _stylesById.ContainsKey("Normal"))
                _defaultParagraphStyleId = "Normal";

            if (string.IsNullOrWhiteSpace(_defaultCharacterStyleId) && _stylesById.ContainsKey("DefaultParagraphFont"))
                _defaultCharacterStyleId = "DefaultParagraphFont";
        }
    }
    
    /// <summary>
    /// Gets the style chain from basedOn relationships (parent → child order)
    /// Result: [oldest ancestor, ..., immediate parent, the style itself]
    /// </summary>
    public List<Style> GetStyleChain(string? styleId)
    {
        var chain = new List<Style>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        while (!string.IsNullOrEmpty(styleId) && !visited.Contains(styleId))
        {
            visited.Add(styleId);
            if (_stylesById.TryGetValue(styleId, out var style))
            {
                chain.Insert(0, style); // Insert at beginning for parent → child order
                styleId = style.BasedOn?.Val?.Value;
            }
            else
            {
                break;
            }
        }
        
        return chain;
    }

    private static string? GetDefaultStyleId(Styles? styles, StyleValues type)
    {
        return styles?.Elements<Style>()
            .FirstOrDefault(s => s.Type?.Value == type && (s.Default?.Value ?? false))
            ?.StyleId?.Value;
    }

    private void AddStylesToCache(Styles styles, bool overwrite)
    {
        foreach (var style in styles.Elements<Style>())
        {
            var styleId = style.StyleId?.Value;
            if (string.IsNullOrEmpty(styleId))
                continue;

            if (overwrite || !_stylesById.ContainsKey(styleId))
                _stylesById[styleId] = style;
        }
    }

    private static bool MatchesStyleType(Style style, StyleValues expectedType)
    {
        return style.Type?.Value == null || style.Type.Value == expectedType;
    }

    private static bool TryReadNumberingProperties(
        NumberingProperties? numPr,
        out int numId,
        out int ilvl,
        out bool disabled)
    {
        numId = 0;
        ilvl = 0;
        disabled = false;

        if (numPr?.NumberingId?.Val == null)
            return false;

        numId = numPr.NumberingId.Val.Value;
        ilvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;
        if (numId == 0)
            disabled = true;

        return true;
    }

    private bool TryResolveNumberingFromStyleChain(
        string? styleId,
        out int numId,
        out int ilvl,
        out bool disabled)
    {
        numId = 0;
        ilvl = 0;
        disabled = false;

        if (string.IsNullOrWhiteSpace(styleId))
            return false;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return TryResolveNumberingFromStyleChainInternal(styleId, visited, out numId, out ilvl, out disabled);
    }

    private bool TryResolveNumberingFromStyleChainInternal(
        string styleId,
        HashSet<string> visited,
        out int numId,
        out int ilvl,
        out bool disabled)
    {
        numId = 0;
        ilvl = 0;
        disabled = false;

        if (!visited.Add(styleId))
            return false;

        if (!_stylesById.TryGetValue(styleId, out var style))
            return false;

        if (TryReadNumberingProperties(style.StyleParagraphProperties?.NumberingProperties, out numId, out ilvl, out disabled))
            return true;

        var basedOnId = style.BasedOn?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(basedOnId))
        {
            if (TryResolveNumberingFromStyleChainInternal(basedOnId, visited, out numId, out ilvl, out disabled))
                return true;
            if (disabled)
                return true;
        }

        return false;
    }

    private IEnumerable<string> EnumerateStyleIdCandidates(string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
            yield break;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(styleId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
                continue;

            yield return current;

            if (!_stylesById.TryGetValue(current, out var style))
                continue;

            var basedOnId = style.BasedOn?.Val?.Value;
            if (!string.IsNullOrWhiteSpace(basedOnId))
                queue.Enqueue(basedOnId);
        }
    }

    private bool TryResolveNumberingFromNumberingPart(
        string? styleId,
        out int numId,
        out int ilvl)
    {
        numId = 0;
        ilvl = 0;

        if (string.IsNullOrWhiteSpace(styleId) || _numberingPart?.Numbering == null)
            return false;

        var numbering = _numberingPart.Numbering;
        foreach (var abstractNum in numbering.Elements<AbstractNum>())
        {
            var abstractId = abstractNum.AbstractNumberId?.Value;
            if (!abstractId.HasValue)
                continue;

            var styleLink = abstractNum.StyleLink?.Val?.Value;
            var numStyleLink = abstractNum.NumberingStyleLink?.Val?.Value;

            bool matchesStyle = string.Equals(styleLink, styleId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(numStyleLink, styleId, StringComparison.OrdinalIgnoreCase);

            int? matchedLevel = null;
            foreach (var level in abstractNum.Elements<Level>())
            {
                var levelStyleId = level.ParagraphStyleIdInLevel?.Val?.Value;
                if (string.IsNullOrWhiteSpace(levelStyleId))
                    continue;

                if (string.Equals(levelStyleId, styleId, StringComparison.OrdinalIgnoreCase))
                {
                    matchedLevel = level.LevelIndex?.Value ?? 0;
                    matchesStyle = true;
                    break;
                }
            }

            if (!matchesStyle)
                continue;

            var numInstance = numbering.Elements<NumberingInstance>()
                .Where(n => n.AbstractNumId?.Val?.Value == abstractId.Value)
                .OrderBy(n => n.NumberID?.Value ?? int.MaxValue)
                .FirstOrDefault();

            if (numInstance?.NumberID?.Value == null)
                continue;

            numId = numInstance.NumberID.Value;
            ilvl = matchedLevel ?? 0;
            if (numId > 0)
                return true;
        }

        return false;
    }
    
    /// <summary>
    /// Resolves effective RunProperties for a Run, walking the full inheritance chain:
    /// 1. docDefaults (rPrDefault)
    /// 2. Default character style (if any)
    /// 3. Paragraph style's rPr from basedOn chain
    /// 4. Character style (rStyle) basedOn chain
    /// 5. Direct rPr on the run
    /// </summary>
    public EffectiveRunProperties GetEffectiveRunProperties(Run run, ParagraphProperties? paragraphProps = null)
    {
        var effective = new EffectiveRunProperties();
        
        // 1. Start with docDefaults
        if (_docDefaultsRPr != null)
        {
            MergeRunProperties(effective, _docDefaultsRPr, "docDefaults");
        }

        // 2. Apply default character style (if any)
        if (!string.IsNullOrEmpty(_defaultCharacterStyleId))
        {
            var defaultCharChain = GetStyleChain(_defaultCharacterStyleId);
            foreach (var style in defaultCharChain)
            {
                if (!MatchesStyleType(style, StyleValues.Character))
                    continue;

                var styleRPr = style.StyleRunProperties;
                if (styleRPr != null)
                {
                    MergeRunProperties(effective, styleRPr, $"defaultCharStyle:{style.StyleId?.Value}");
                }
            }
        }
        
        // 3. Apply paragraph style's rPr (from basedOn chain)
        var paragraphStyleId = paragraphProps?.ParagraphStyleId?.Val?.Value
            ?? _defaultParagraphStyleId
            ?? "Normal";
        if (!string.IsNullOrEmpty(paragraphStyleId))
        {
            var paragraphStyleChain = GetStyleChain(paragraphStyleId);
            // Apply from oldest ancestor to the style itself
            foreach (var style in paragraphStyleChain)
            {
                if (!MatchesStyleType(style, StyleValues.Paragraph))
                    continue;

                // Paragraph styles can have StyleRunProperties that apply to text
                var styleRPr = style.StyleRunProperties;
                if (styleRPr != null)
                {
                    MergeRunProperties(effective, styleRPr, $"paragraphStyle:{style.StyleId?.Value}");
                }
            }
        }

        // 4. Apply character style (rStyle) from basedOn chain
        var runProps = run.RunProperties;
        var charStyleId = runProps?.RunStyle?.Val?.Value;
        if (!string.IsNullOrEmpty(charStyleId))
        {
            var charStyleChain = GetStyleChain(charStyleId);
            foreach (var style in charStyleChain)
            {
                if (!MatchesStyleType(style, StyleValues.Character))
                    continue;

                var styleRPr = style.StyleRunProperties;
                if (styleRPr != null)
                {
                    MergeRunProperties(effective, styleRPr, $"charStyle:{style.StyleId?.Value}");
                }
            }
        }

        // 5. Apply direct formatting (highest priority)
        if (runProps != null)
        {
            MergeRunProperties(effective, runProps, "direct");
        }
        
        return effective;
    }
    
    /// <summary>
    /// Resolves effective ParagraphProperties for a Paragraph, walking the full inheritance chain:
    /// 1. docDefaults (pPrDefault)
    /// 2. Normal style (implicit base if no explicit style)
    /// 3. Paragraph style basedOn chain
    /// 4. Direct pPr on the paragraph
    /// </summary>
    public EffectiveParagraphProperties GetEffectiveParagraphProperties(Paragraph paragraph)
    {
        var effective = new EffectiveParagraphProperties();
        
        // 1. Start with docDefaults
        if (_docDefaultsPPr != null)
        {
            MergeParagraphProperties(effective, _docDefaultsPPr);
        }
        
        // 2. Get paragraph style and apply basedOn chain
        var directPPr = paragraph.ParagraphProperties;
        var hasDirectStyle = directPPr?.ParagraphStyleId?.Val?.Value != null;
        var styleId = directPPr?.ParagraphStyleId?.Val?.Value
            ?? _defaultParagraphStyleId
            ?? "Normal";
        effective.StyleId = styleId;
        
        var styleChain = GetStyleChain(styleId);
        foreach (var style in styleChain)
        {
            if (!MatchesStyleType(style, StyleValues.Paragraph))
                continue;

            effective.StyleName = style.StyleName?.Val?.Value ?? style.StyleId?.Value;
            var stylePPr = style.StyleParagraphProperties;
            if (stylePPr != null)
            {
                MergeParagraphProperties(effective, stylePPr);
            }
        }
        
        // 3. Apply direct formatting (highest priority)
        if (directPPr != null)
        {
            MergeParagraphProperties(effective, directPPr);
        }
        
        return effective;
    }
    
    /// <summary>
    /// Gets effective NumberingProperties for a paragraph, checking both direct and style chain
    /// </summary>
    public (int? numId, int ilvl) GetEffectiveNumberingProperties(Paragraph p)
    {
        // 1. Check direct numPr on paragraph
        var directNumPr = p.ParagraphProperties?.NumberingProperties;
        if (TryReadNumberingProperties(directNumPr, out var directNumId, out var directIlvl, out var directDisabled))
            return (directNumId, directIlvl);

        // 2. Check style chain and linked styles for numPr
        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value
            ?? _defaultParagraphStyleId;
        if (TryResolveNumberingFromStyleChain(styleId, out var styleNumId, out var styleIlvl, out var disabled))
            return (styleNumId, styleIlvl);

        if (!disabled)
        {
            // 3. Check numbering definitions that link to the paragraph style
            foreach (var candidateStyleId in EnumerateStyleIdCandidates(styleId))
            {
                if (TryResolveNumberingFromNumberingPart(candidateStyleId, out var linkedNumId, out var linkedIlvl))
                    return (linkedNumId, linkedIlvl);
            }
        }

        return (null, 0);
    }
    
    /// <summary>
    /// Gets a style by ID
    /// </summary>
    public Style? GetStyle(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return null;
        return _stylesById.TryGetValue(styleId, out var style) ? style : null;
    }
    
    // ========================================================================
    // NUMBERING-AWARE PROPERTY RESOLUTION
    // ========================================================================
    
    /// <summary>
    /// Resolves effective ParagraphProperties for a list paragraph, including w:lvl/w:pPr.
    /// Order: docDefaults → style chain → w:lvl/w:pPr (numbering.xml) → direct pPr
    /// </summary>
    public EffectiveParagraphProperties GetEffectiveParagraphPropertiesWithNumbering(
        Paragraph paragraph,
        NumberingDefinitionsPart? numberingPart,
        int? numId = null,
        int ilvl = 0)
    {
        var effective = new EffectiveParagraphProperties();
        
        // 1. Start with docDefaults
        if (_docDefaultsPPr != null)
        {
            MergeParagraphProperties(effective, _docDefaultsPPr);
        }
        
        // 2. Get paragraph style and apply basedOn chain
        var directPPr = paragraph.ParagraphProperties;
        var hasDirectStyle = directPPr?.ParagraphStyleId?.Val?.Value != null;
        var styleId = directPPr?.ParagraphStyleId?.Val?.Value
            ?? _defaultParagraphStyleId
            ?? "Normal";
        effective.StyleId = styleId;
        
        var styleChain = GetStyleChain(styleId);
        foreach (var style in styleChain)
        {
            if (!MatchesStyleType(style, StyleValues.Paragraph))
                continue;

            effective.StyleName = style.StyleName?.Val?.Value ?? style.StyleId?.Value;
            var stylePPr = style.StyleParagraphProperties;
            if (stylePPr != null)
            {
                MergeParagraphProperties(effective, stylePPr);
            }
        }
        
        // 3. Apply w:lvl/w:pStyle (if any) and w:lvl/w:pPr from numbering.xml (overrides style)
        if (numberingPart != null && numId.HasValue && numId.Value > 0)
        {
            var level = NumberingResolver.GetNumberingLevel(numberingPart, numId.Value, ilvl);
            if (level != null)
            {
                if (!hasDirectStyle)
                {
                    var levelStyleId = level.ParagraphStyleIdInLevel?.Val?.Value;
                    if (!string.IsNullOrWhiteSpace(levelStyleId))
                    {
                        var levelStyleChain = GetStyleChain(levelStyleId);
                        foreach (var levelStyle in levelStyleChain)
                        {
                            if (!MatchesStyleType(levelStyle, StyleValues.Paragraph))
                                continue;

                            if (effective.StyleName == null)
                                effective.StyleName = levelStyle.StyleName?.Val?.Value ?? levelStyle.StyleId?.Value;

                            var levelStylePPr = levelStyle.StyleParagraphProperties;
                            if (levelStylePPr != null)
                            {
                                MergeParagraphProperties(effective, levelStylePPr);
                            }
                        }

                        effective.StyleId = levelStyleId;
                    }
                }

                var levelPPr = level.PreviousParagraphProperties;
                if (levelPPr != null)
                {
                    NumberingResolver.MergeNumberingLevelParagraphProperties(effective, levelPPr);
                }
            }
        }
        
        // 4. Apply direct formatting (highest priority)
        if (directPPr != null)
        {
            MergeParagraphProperties(effective, directPPr);
        }
        
        return effective;
    }
    
    /// <summary>
    /// Resolves effective RunProperties for the numbering label (the "1." or "•").
    /// This is different from regular run text - it uses w:lvl/w:rPr.
    /// Order: docDefaults → w:lvl/w:rPr (numbering.xml)
    /// </summary>
    public EffectiveRunProperties GetEffectiveNumberingLabelRunProperties(
        NumberingDefinitionsPart? numberingPart,
        int numId,
        int ilvl = 0)
    {
        var effective = new EffectiveRunProperties();
        
        // 1. Start with docDefaults
        if (_docDefaultsRPr != null)
        {
            MergeRunProperties(effective, _docDefaultsRPr, "docDefaults");
        }
        
        // 2. Apply w:lvl/w:rPr from numbering.xml
        if (numberingPart != null && numId > 0)
        {
            var level = NumberingResolver.GetNumberingLevel(numberingPart, numId, ilvl);
            if (level != null)
            {
                var levelRPr = level.NumberingSymbolRunProperties;
                if (levelRPr != null)
                {
                    NumberingResolver.MergeNumberingLevelRunProperties(effective, levelRPr, $"lvl:{ilvl}");
                }
            }
        }
        
        return effective;
    }
    
    // ========================================================================
    // MERGE METHODS - RunProperties
    // ========================================================================
    
    /// <summary>
    /// Merge RunProperties into EffectiveRunProperties (later values override earlier)
    /// </summary>
    private void MergeRunProperties(EffectiveRunProperties effective, RunProperties rPr, string source)
    {
        effective.ResolvedFromStyle = source;
        
        // Font
        var fonts = rPr.GetFirstChild<RunFonts>();
        if (fonts != null)
            ApplyRunFonts(effective, fonts);
        
        ApplyLanguages(effective, rPr.GetFirstChild<Languages>());
        
        // Font Size
        var fontSize = rPr.GetFirstChild<FontSize>();
        if (fontSize?.Val?.Value != null && int.TryParse(fontSize.Val.Value, out int sz))
            effective.FontSize = sz;
        
        var fontSizeCs = rPr.GetFirstChild<FontSizeComplexScript>();
        if (fontSizeCs?.Val?.Value != null && int.TryParse(fontSizeCs.Val.Value, out int szCs))
            effective.FontSizeCs = szCs;
        
        // Bold - note: presence of element without Val means true, Val=false means false
        var bold = rPr.GetFirstChild<Bold>();
        if (bold != null)
            effective.Bold = bold.Val?.Value ?? true;
        
        // Italic
        var italic = rPr.GetFirstChild<Italic>();
        if (italic != null)
            effective.Italic = italic.Val?.Value ?? true;
        
        // Underline
        var underline = rPr.GetFirstChild<Underline>();
        if (underline != null)
            ApplyUnderline(effective, underline);
        
        // Strike
        var strike = rPr.GetFirstChild<Strike>();
        if (strike != null)
            effective.Strike = strike.Val?.Value ?? true;
        
        var dblStrike = rPr.GetFirstChild<DoubleStrike>();
        if (dblStrike != null)
            effective.DoubleStrike = dblStrike.Val?.Value ?? true;
        
        // Vertical alignment (superscript/subscript)
        var vertAlign = rPr.GetFirstChild<VerticalTextAlignment>();
        if (vertAlign?.Val?.Value != null)
            effective.VerticalAlignment = vertAlign.Val.Value.ToString().ToLower();
        
        // Color
        var color = rPr.GetFirstChild<Color>();
        if (color?.Val?.Value != null)
            effective.Color = color.Val.Value;
        
        // Highlight
        var highlight = rPr.GetFirstChild<Highlight>();
        if (highlight?.Val?.Value != null)
            effective.HighlightColor = highlight.Val.Value.ToString().ToLower();
        
        // Caps
        var caps = rPr.GetFirstChild<Caps>();
        if (caps != null)
            effective.Caps = caps.Val?.Value ?? true;
        
        var smallCaps = rPr.GetFirstChild<SmallCaps>();
        if (smallCaps != null)
            effective.SmallCaps = smallCaps.Val?.Value ?? true;
        
        // Hidden
        var vanish = rPr.GetFirstChild<Vanish>();
        if (vanish != null)
            effective.Hidden = vanish.Val?.Value ?? true;
        
        // Spacing
        var spacing = rPr.GetFirstChild<Spacing>();
        if (spacing?.Val?.Value != null)
            effective.Spacing = spacing.Val.Value;
    }
    
    // Overload for StyleRunProperties which has similar structure
    private void MergeRunProperties(EffectiveRunProperties effective, StyleRunProperties rPr, string source)
    {
        effective.ResolvedFromStyle = source;
        
        var fonts = rPr.GetFirstChild<RunFonts>();
        if (fonts != null)
            ApplyRunFonts(effective, fonts);

        ApplyLanguages(effective, rPr.GetFirstChild<Languages>());
        
        var fontSize = rPr.GetFirstChild<FontSize>();
        if (fontSize?.Val?.Value != null && int.TryParse(fontSize.Val.Value, out int sz))
            effective.FontSize = sz;
        
        var fontSizeCs = rPr.GetFirstChild<FontSizeComplexScript>();
        if (fontSizeCs?.Val?.Value != null && int.TryParse(fontSizeCs.Val.Value, out int szCs))
            effective.FontSizeCs = szCs;
        
        var bold = rPr.GetFirstChild<Bold>();
        if (bold != null)
            effective.Bold = bold.Val?.Value ?? true;
        
        var italic = rPr.GetFirstChild<Italic>();
        if (italic != null)
            effective.Italic = italic.Val?.Value ?? true;
        
        var underline = rPr.GetFirstChild<Underline>();
        if (underline != null)
            ApplyUnderline(effective, underline);
        
        var strike = rPr.GetFirstChild<Strike>();
        if (strike != null)
            effective.Strike = strike.Val?.Value ?? true;
        
        var dblStrike = rPr.GetFirstChild<DoubleStrike>();
        if (dblStrike != null)
            effective.DoubleStrike = dblStrike.Val?.Value ?? true;
        
        var vertAlign = rPr.GetFirstChild<VerticalTextAlignment>();
        if (vertAlign?.Val?.Value != null)
            effective.VerticalAlignment = vertAlign.Val.Value.ToString().ToLower();
        
        var color = rPr.GetFirstChild<Color>();
        if (color?.Val?.Value != null)
            effective.Color = color.Val.Value;
        
        var highlight = rPr.GetFirstChild<Highlight>();
        if (highlight?.Val?.Value != null)
            effective.HighlightColor = highlight.Val.Value.ToString().ToLower();
        
        var caps = rPr.GetFirstChild<Caps>();
        if (caps != null)
            effective.Caps = caps.Val?.Value ?? true;
        
        var smallCaps = rPr.GetFirstChild<SmallCaps>();
        if (smallCaps != null)
            effective.SmallCaps = smallCaps.Val?.Value ?? true;
        
        var vanish = rPr.GetFirstChild<Vanish>();
        if (vanish != null)
            effective.Hidden = vanish.Val?.Value ?? true;
        
        var spacing = rPr.GetFirstChild<Spacing>();
        if (spacing?.Val?.Value != null)
            effective.Spacing = spacing.Val.Value;
    }
    
    // Overload for RunPropertiesBaseStyle (from docDefaults)
    private void MergeRunProperties(EffectiveRunProperties effective, RunPropertiesBaseStyle rPr, string source)
    {
        effective.ResolvedFromStyle = source;
        
        var fonts = rPr.GetFirstChild<RunFonts>();
        if (fonts != null)
            ApplyRunFonts(effective, fonts);

        ApplyLanguages(effective, rPr.GetFirstChild<Languages>());
        
        var fontSize = rPr.GetFirstChild<FontSize>();
        if (fontSize?.Val?.Value != null && int.TryParse(fontSize.Val.Value, out int sz))
            effective.FontSize = sz;
        
        var fontSizeCs = rPr.GetFirstChild<FontSizeComplexScript>();
        if (fontSizeCs?.Val?.Value != null && int.TryParse(fontSizeCs.Val.Value, out int szCs))
            effective.FontSizeCs = szCs;

        var bold = rPr.GetFirstChild<Bold>();
        if (bold != null)
            effective.Bold = bold.Val?.Value ?? true;

        var italic = rPr.GetFirstChild<Italic>();
        if (italic != null)
            effective.Italic = italic.Val?.Value ?? true;

        var underline = rPr.GetFirstChild<Underline>();
        if (underline != null)
            ApplyUnderline(effective, underline);

        var strike = rPr.GetFirstChild<Strike>();
        if (strike != null)
            effective.Strike = strike.Val?.Value ?? true;

        var dblStrike = rPr.GetFirstChild<DoubleStrike>();
        if (dblStrike != null)
            effective.DoubleStrike = dblStrike.Val?.Value ?? true;

        var vertAlign = rPr.GetFirstChild<VerticalTextAlignment>();
        if (vertAlign?.Val?.Value != null)
            effective.VerticalAlignment = vertAlign.Val.Value.ToString().ToLowerInvariant();

        var color = rPr.GetFirstChild<Color>();
        if (color?.Val?.Value != null)
            effective.Color = color.Val.Value;

        var highlight = rPr.GetFirstChild<Highlight>();
        if (highlight?.Val?.Value != null)
            effective.HighlightColor = highlight.Val.Value.ToString().ToLowerInvariant();

        var caps = rPr.GetFirstChild<Caps>();
        if (caps != null)
            effective.Caps = caps.Val?.Value ?? true;

        var smallCaps = rPr.GetFirstChild<SmallCaps>();
        if (smallCaps != null)
            effective.SmallCaps = smallCaps.Val?.Value ?? true;

        var vanish = rPr.GetFirstChild<Vanish>();
        if (vanish != null)
            effective.Hidden = vanish.Val?.Value ?? true;

        var spacing = rPr.GetFirstChild<Spacing>();
        if (spacing?.Val?.Value != null)
            effective.Spacing = spacing.Val.Value;
    }
    
    // ========================================================================
    // MERGE METHODS - ParagraphProperties
    // ========================================================================
    
    /// <summary>
    /// Merge ParagraphProperties into EffectiveParagraphProperties
    /// </summary>
    private void MergeParagraphProperties(EffectiveParagraphProperties effective, ParagraphProperties pPr)
    {
        // Justification
        var jc = pPr.GetFirstChild<Justification>();
        if (jc?.Val?.Value != null)
            effective.Justification = StyleResolverHelpers.ConvertJustification(jc.Val.Value);
            
        // Text Alignment
        var textAlignment = pPr.GetFirstChild<TextAlignment>();
        if (textAlignment?.Val?.Value != null)
            effective.TextAlignment = StyleResolverHelpers.ConvertTextAlignment(textAlignment.Val.Value);
        
        // Indentation
        var ind = pPr.GetFirstChild<Indentation>();
        if (ind != null)
        {
            if (ind.Left?.Value != null) effective.IndentLeft = int.Parse(ind.Left.Value);
            if (ind.Right?.Value != null) effective.IndentRight = int.Parse(ind.Right.Value);
            if (ind.FirstLine?.Value != null) effective.IndentFirstLine = int.Parse(ind.FirstLine.Value);
            if (ind.Hanging?.Value != null) effective.IndentHanging = int.Parse(ind.Hanging.Value);
            if (ind.Start?.Value != null) effective.IndentStart = int.Parse(ind.Start.Value);
            if (ind.End?.Value != null) effective.IndentEnd = int.Parse(ind.End.Value);
            if (ind.LeftChars?.Value != null) effective.IndentLeftChars = ind.LeftChars.Value;
            if (ind.RightChars?.Value != null) effective.IndentRightChars = ind.RightChars.Value;
        }
        
        // Spacing
        var spacing = pPr.GetFirstChild<SpacingBetweenLines>();
        if (spacing != null)
        {
            if (spacing.Before?.Value != null) effective.SpaceBefore = int.Parse(spacing.Before.Value);
            if (spacing.After?.Value != null) effective.SpaceAfter = int.Parse(spacing.After.Value);
            if (spacing.Line?.Value != null) effective.LineSpacing = int.Parse(spacing.Line.Value);
            if (spacing.LineRule?.Value != null) effective.LineRule = StyleResolverHelpers.ConvertLineRule(spacing.LineRule.Value);
            
            if (spacing.BeforeAutoSpacing?.Value != null) effective.SpaceBeforeAuto = spacing.BeforeAutoSpacing.Value;
            if (spacing.AfterAutoSpacing?.Value != null) effective.SpaceAfterAuto = spacing.AfterAutoSpacing.Value;
            if (spacing.BeforeLines?.Value != null) effective.SpaceBeforeLines = spacing.BeforeLines.Value;
            if (spacing.AfterLines?.Value != null) effective.SpaceAfterLines = spacing.AfterLines.Value;
        }
        
        // Keep options & Pagination
        if (pPr.KeepNext != null) effective.KeepNext = StyleResolverHelpers.IsToggleOn(pPr.KeepNext);
        if (pPr.KeepLines != null) effective.KeepLines = StyleResolverHelpers.IsToggleOn(pPr.KeepLines);
        if (pPr.PageBreakBefore != null) effective.PageBreakBefore = StyleResolverHelpers.IsToggleOn(pPr.PageBreakBefore);
        if (pPr.WidowControl != null) effective.WidowControl = !StyleResolverHelpers.IsToggleOff(pPr.WidowControl);
        if (pPr.SuppressLineNumbers != null) effective.SuppressLineNumbers = StyleResolverHelpers.IsToggleOn(pPr.SuppressLineNumbers);
        if (pPr.SuppressAutoHyphens != null) effective.SuppressAutoHyphens = StyleResolverHelpers.IsToggleOn(pPr.SuppressAutoHyphens);
        
        // Layout / Toggle properties
        if (pPr.SnapToGrid != null) effective.SnapToGrid = !StyleResolverHelpers.IsToggleOff(pPr.SnapToGrid);
        if (pPr.AdjustRightIndent != null) effective.AdjustRightIndent = !StyleResolverHelpers.IsToggleOff(pPr.AdjustRightIndent);
        if (pPr.MirrorIndents != null) effective.MirrorIndents = StyleResolverHelpers.IsToggleOn(pPr.MirrorIndents);
        if (pPr.SuppressOverlap != null) effective.SuppressOverlap = StyleResolverHelpers.IsToggleOn(pPr.SuppressOverlap);
        if (pPr.ContextualSpacing != null) effective.ContextualSpacing = StyleResolverHelpers.IsToggleOn(pPr.ContextualSpacing);
        if (pPr.WordWrap != null) effective.WordWrap = StyleResolverHelpers.IsToggleOn(pPr.WordWrap);
        
        // Outline level
        var outlineLvl = pPr.GetFirstChild<OutlineLevel>();
        if (outlineLvl?.Val?.Value != null)
            effective.OutlineLevel = outlineLvl.Val.Value;
    }
    
    // Overload for StyleParagraphProperties
    private void MergeParagraphProperties(EffectiveParagraphProperties effective, StyleParagraphProperties pPr)
    {
        var jc = pPr.GetFirstChild<Justification>();
        if (jc?.Val?.Value != null)
            effective.Justification = StyleResolverHelpers.ConvertJustification(jc.Val.Value);
            
        var textAlignment = pPr.GetFirstChild<TextAlignment>();
        if (textAlignment?.Val?.Value != null)
            effective.TextAlignment = StyleResolverHelpers.ConvertTextAlignment(textAlignment.Val.Value);
        
        var ind = pPr.GetFirstChild<Indentation>();
        if (ind != null)
        {
            if (ind.Left?.Value != null) effective.IndentLeft = int.Parse(ind.Left.Value);
            if (ind.Right?.Value != null) effective.IndentRight = int.Parse(ind.Right.Value);
            if (ind.FirstLine?.Value != null) effective.IndentFirstLine = int.Parse(ind.FirstLine.Value);
            if (ind.Hanging?.Value != null) effective.IndentHanging = int.Parse(ind.Hanging.Value);
            if (ind.Start?.Value != null) effective.IndentStart = int.Parse(ind.Start.Value);
            if (ind.End?.Value != null) effective.IndentEnd = int.Parse(ind.End.Value);
            if (ind.LeftChars?.Value != null) effective.IndentLeftChars = ind.LeftChars.Value;
            if (ind.RightChars?.Value != null) effective.IndentRightChars = ind.RightChars.Value;
        }
        
        var spacing = pPr.GetFirstChild<SpacingBetweenLines>();
        if (spacing != null)
        {
            if (spacing.Before?.Value != null) effective.SpaceBefore = int.Parse(spacing.Before.Value);
            if (spacing.After?.Value != null) effective.SpaceAfter = int.Parse(spacing.After.Value);
            if (spacing.Line?.Value != null) effective.LineSpacing = int.Parse(spacing.Line.Value);
            if (spacing.LineRule?.Value != null) effective.LineRule = StyleResolverHelpers.ConvertLineRule(spacing.LineRule.Value);
            
            if (spacing.BeforeAutoSpacing?.Value != null) effective.SpaceBeforeAuto = spacing.BeforeAutoSpacing.Value;
            if (spacing.AfterAutoSpacing?.Value != null) effective.SpaceAfterAuto = spacing.AfterAutoSpacing.Value;
            if (spacing.BeforeLines?.Value != null) effective.SpaceBeforeLines = spacing.BeforeLines.Value;
            if (spacing.AfterLines?.Value != null) effective.SpaceAfterLines = spacing.AfterLines.Value;
        }
        
        if (pPr.KeepNext != null) effective.KeepNext = StyleResolverHelpers.IsToggleOn(pPr.KeepNext);
        if (pPr.KeepLines != null) effective.KeepLines = StyleResolverHelpers.IsToggleOn(pPr.KeepLines);
        if (pPr.PageBreakBefore != null) effective.PageBreakBefore = StyleResolverHelpers.IsToggleOn(pPr.PageBreakBefore);
        if (pPr.WidowControl != null) effective.WidowControl = !StyleResolverHelpers.IsToggleOff(pPr.WidowControl);
        if (pPr.SuppressLineNumbers != null) effective.SuppressLineNumbers = StyleResolverHelpers.IsToggleOn(pPr.SuppressLineNumbers);
        if (pPr.SuppressAutoHyphens != null) effective.SuppressAutoHyphens = StyleResolverHelpers.IsToggleOn(pPr.SuppressAutoHyphens);
        
        if (pPr.SnapToGrid != null) effective.SnapToGrid = !StyleResolverHelpers.IsToggleOff(pPr.SnapToGrid);
        if (pPr.AdjustRightIndent != null) effective.AdjustRightIndent = !StyleResolverHelpers.IsToggleOff(pPr.AdjustRightIndent);
        if (pPr.MirrorIndents != null) effective.MirrorIndents = StyleResolverHelpers.IsToggleOn(pPr.MirrorIndents);
        if (pPr.SuppressOverlap != null) effective.SuppressOverlap = StyleResolverHelpers.IsToggleOn(pPr.SuppressOverlap);
        if (pPr.ContextualSpacing != null) effective.ContextualSpacing = StyleResolverHelpers.IsToggleOn(pPr.ContextualSpacing);
        if (pPr.WordWrap != null) effective.WordWrap = StyleResolverHelpers.IsToggleOn(pPr.WordWrap);
        
        var outlineLvl = pPr.GetFirstChild<OutlineLevel>();
        if (outlineLvl?.Val?.Value != null)
            effective.OutlineLevel = outlineLvl.Val.Value;
    }
    
    // Overload for ParagraphPropertiesBaseStyle (from docDefaults)
    private void MergeParagraphProperties(EffectiveParagraphProperties effective, ParagraphPropertiesBaseStyle pPr)
    {
        var jc = pPr.GetFirstChild<Justification>();
        if (jc?.Val?.Value != null)
            effective.Justification = StyleResolverHelpers.ConvertJustification(jc.Val.Value);
            
        var textAlignment = pPr.GetFirstChild<TextAlignment>();
        if (textAlignment?.Val?.Value != null)
            effective.TextAlignment = StyleResolverHelpers.ConvertTextAlignment(textAlignment.Val.Value);
        
        var ind = pPr.GetFirstChild<Indentation>();
        if (ind != null)
        {
            if (ind.Left?.Value != null) effective.IndentLeft = int.Parse(ind.Left.Value);
            if (ind.Right?.Value != null) effective.IndentRight = int.Parse(ind.Right.Value);
            if (ind.FirstLine?.Value != null) effective.IndentFirstLine = int.Parse(ind.FirstLine.Value);
            if (ind.Hanging?.Value != null) effective.IndentHanging = int.Parse(ind.Hanging.Value);
            if (ind.Start?.Value != null) effective.IndentStart = int.Parse(ind.Start.Value);
            if (ind.End?.Value != null) effective.IndentEnd = int.Parse(ind.End.Value);
            if (ind.LeftChars?.Value != null) effective.IndentLeftChars = ind.LeftChars.Value;
            if (ind.RightChars?.Value != null) effective.IndentRightChars = ind.RightChars.Value;
        }
        
        var spacing = pPr.GetFirstChild<SpacingBetweenLines>();
        if (spacing != null)
        {
            if (spacing.Before?.Value != null) effective.SpaceBefore = int.Parse(spacing.Before.Value);
            if (spacing.After?.Value != null) effective.SpaceAfter = int.Parse(spacing.After.Value);
            if (spacing.Line?.Value != null) effective.LineSpacing = int.Parse(spacing.Line.Value);
            if (spacing.LineRule?.Value != null) effective.LineRule = StyleResolverHelpers.ConvertLineRule(spacing.LineRule.Value);
            
            if (spacing.BeforeAutoSpacing?.Value != null) effective.SpaceBeforeAuto = spacing.BeforeAutoSpacing.Value;
            if (spacing.AfterAutoSpacing?.Value != null) effective.SpaceAfterAuto = spacing.AfterAutoSpacing.Value;
            if (spacing.BeforeLines?.Value != null) effective.SpaceBeforeLines = spacing.BeforeLines.Value;
            if (spacing.AfterLines?.Value != null) effective.SpaceAfterLines = spacing.AfterLines.Value;
        }
    }

    private void ApplyRunFonts(EffectiveRunProperties effective, RunFonts fonts)
    {
        var asciiTheme = GetRunFontsAttributeValue(fonts, "asciiTheme");
        var highAnsiTheme = GetRunFontsAttributeValue(fonts, "hAnsiTheme");
        var eastAsiaTheme = GetRunFontsAttributeValue(fonts, "eastAsiaTheme");
        var complexTheme = GetRunFontsAttributeValue(fonts, "csTheme");
        var hint = GetRunFontsAttributeValue(fonts, "hint");

        var ascii = fonts.Ascii?.Value;
        if (!string.IsNullOrWhiteSpace(ascii))
        {
            effective.FontAscii = ascii.Trim();
            effective.FontAsciiTheme = null;
        }
        else if (!string.IsNullOrWhiteSpace(asciiTheme))
        {
            effective.FontAscii = null;
            effective.FontAsciiTheme = asciiTheme.Trim();
        }

        var highAnsi = fonts.HighAnsi?.Value;
        if (!string.IsNullOrWhiteSpace(highAnsi))
        {
            effective.FontHighAnsi = highAnsi.Trim();
            effective.FontHighAnsiTheme = null;
        }
        else if (!string.IsNullOrWhiteSpace(highAnsiTheme))
        {
            effective.FontHighAnsi = null;
            effective.FontHighAnsiTheme = highAnsiTheme.Trim();
        }

        var eastAsia = fonts.EastAsia?.Value;
        if (!string.IsNullOrWhiteSpace(eastAsia))
        {
            effective.FontEastAsia = eastAsia.Trim();
            effective.FontEastAsiaTheme = null;
        }
        else if (!string.IsNullOrWhiteSpace(eastAsiaTheme))
        {
            effective.FontEastAsia = null;
            effective.FontEastAsiaTheme = eastAsiaTheme.Trim();
        }

        var complex = fonts.ComplexScript?.Value;
        if (!string.IsNullOrWhiteSpace(complex))
        {
            effective.FontComplexScript = complex.Trim();
            effective.FontComplexScriptTheme = null;
        }
        else if (!string.IsNullOrWhiteSpace(complexTheme))
        {
            effective.FontComplexScript = null;
            effective.FontComplexScriptTheme = complexTheme.Trim();
        }
        if (!string.IsNullOrWhiteSpace(hint))
            effective.FontHint = hint;
    }

    private void ApplyLanguages(EffectiveRunProperties effective, Languages? languages)
    {
        if (languages == null)
            return;

        var latin = GetLanguageAttributeValue(languages, "val");
        var eastAsia = GetLanguageAttributeValue(languages, "eastAsia");
        var bidi = GetLanguageAttributeValue(languages, "bidi");

        if (!string.IsNullOrWhiteSpace(latin))
            effective.LangLatin = latin;
        if (!string.IsNullOrWhiteSpace(eastAsia))
            effective.LangEastAsia = eastAsia;
        if (!string.IsNullOrWhiteSpace(bidi))
            effective.LangBidi = bidi;
    }

    private static string? GetLanguageAttributeValue(OpenXmlElement element, string localName)
    {
        var attr = element.GetAttributes().FirstOrDefault(a => a.LocalName == localName);
        return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
    }

    private static string? GetRunFontsAttributeValue(RunFonts fonts, string localName)
    {
        var attr = fonts.GetAttributes().FirstOrDefault(a => a.LocalName == localName);
        return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
    }

    private static void ApplyUnderline(EffectiveRunProperties effective, Underline underline)
    {
        var val = underline.Val?.Value;
        if (!val.HasValue)
        {
            effective.Underline = true;
            effective.UnderlineStyle = "single";
            return;
        }

        if (val.Value == UnderlineValues.None)
        {
            effective.Underline = false;
            effective.UnderlineStyle = "none";
            return;
        }

        effective.Underline = true;
        effective.UnderlineStyle = val.Value.ToString().ToLowerInvariant();
    }
}

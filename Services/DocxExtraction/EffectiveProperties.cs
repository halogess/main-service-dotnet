using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Holds effective (flattened) run properties after resolving inheritance chain
/// </summary>
public class EffectiveRunProperties
{
    // Font
    public string? FontAscii { get; set; }
    public string? FontHighAnsi { get; set; }
    public string? FontEastAsia { get; set; }
    public string? FontComplexScript { get; set; }
    public string? FontHint { get; set; } // default, eastAsia, cs

    // Theme font keys (asciiTheme, hAnsiTheme, eastAsiaTheme, csTheme)
    public string? FontAsciiTheme { get; set; }
    public string? FontHighAnsiTheme { get; set; }
    public string? FontEastAsiaTheme { get; set; }
    public string? FontComplexScriptTheme { get; set; }
    
    // Language hints (from w:lang)
    public string? LangLatin { get; set; }
    public string? LangEastAsia { get; set; }
    public string? LangBidi { get; set; }
    
    // Size (in half-points, divide by 2 for pt)
    public int? FontSize { get; set; }
    public int? FontSizeCs { get; set; }
    
    // Style
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? UnderlineStyle { get; set; } // single, double, wave, etc.
    public bool Strike { get; set; }
    public bool DoubleStrike { get; set; }
    
    // Position
    public string? VerticalAlignment { get; set; } // superscript, subscript, baseline
    
    // Color
    public string? Color { get; set; }
    public string? HighlightColor { get; set; }
    
    // Other
    public bool Caps { get; set; }
    public bool SmallCaps { get; set; }
    public bool Hidden { get; set; }
    public int? Spacing { get; set; } // Character spacing in twips
    
    // Source tracking for debugging
    public string? ResolvedFromStyle { get; set; }
}

/// <summary>
/// Holds effective (flattened) paragraph properties after resolving inheritance chain
/// </summary>
public class EffectiveParagraphProperties
{
    // Alignment
    public string? Justification { get; set; } // left, center, right, both
    public string? TextAlignment { get; set; } // auto, baseline, top, center, bottom
    
    // Indentation (in twips unless specified)
    public int? IndentLeft { get; set; }
    public int? IndentRight { get; set; }
    public int? IndentFirstLine { get; set; }
    public int? IndentHanging { get; set; }
    public int? IndentStart { get; set; }
    public int? IndentEnd { get; set; }
    public int? IndentLeftChars { get; set; } 
    public int? IndentRightChars { get; set; }
    
    // Spacing
    public int? SpaceBefore { get; set; }
    public int? SpaceAfter { get; set; }
    public int? LineSpacing { get; set; }
    public string? LineRule { get; set; } // auto, exact, atLeast
    public bool SpaceBeforeAuto { get; set; }
    public bool SpaceAfterAuto { get; set; }
    public int? SpaceBeforeLines { get; set; } // in 100ths of a line
    public int? SpaceAfterLines { get; set; }
    
    // Keep options & Pagination
    public bool KeepNext { get; set; }
    public bool KeepLines { get; set; }
    public bool PageBreakBefore { get; set; }
    public bool WidowControl { get; set; } = true; // Default is true in Word
    public bool SuppressLineNumbers { get; set; }
    public bool SuppressAutoHyphens { get; set; }
    
    // Layout / Frame
    public bool SnapToGrid { get; set; } = true; // Default true
    public bool AdjustRightIndent { get; set; } = true; // Default true
    public bool MirrorIndents { get; set; }
    public bool SuppressOverlap { get; set; }
    public bool ContextualSpacing { get; set; }
    public bool WordWrap { get; set; } = true; // Word wraps by default
    
    // Outline level (for headings)
    public int? OutlineLevel { get; set; }
    
    // Style name
    public string? StyleId { get; set; }
    public string? StyleName { get; set; }
}

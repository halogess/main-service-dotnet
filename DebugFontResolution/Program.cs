using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ValidasiTugasAkhir.MainService.Services.DocxExtraction;

var docPath = args.Length > 0 ? args[0] : GetRepoDocxPath("Bab 1 - Pendahuluan.docx");
if (!File.Exists(docPath))
{
    Console.WriteLine($"File not found: {docPath}");
    return;
}

using var doc = WordprocessingDocument.Open(docPath, false);
var themeResolver = ThemeFontResolver.FromThemePart(doc.MainDocumentPart?.ThemePart);
var themeLangResolver = ThemeFontLangResolver.FromSettingsPart(doc.MainDocumentPart?.DocumentSettingsPart);
var styleResolver = new StyleResolver(
    doc.MainDocumentPart?.StyleDefinitionsPart,
    doc.MainDocumentPart?.StylesWithEffectsPart,
    themeResolver);

if (themeResolver == null)
{
    Console.WriteLine("Theme resolver: (null)");
}
else
{
    var major = themeResolver.ResolveThemeFont("majorHAnsi") ?? "(null)";
    var minor = themeResolver.ResolveThemeFont("minorHAnsi") ?? "(null)";
    Console.WriteLine($"Theme majorHAnsi={major}");
    Console.WriteLine($"Theme minorHAnsi={minor}");
}
if (themeLangResolver == null)
{
    Console.WriteLine("Theme font lang: (null)");
}
else
{
    Console.WriteLine($"Theme font lang: latin={themeLangResolver.LatinLang ?? "(null)"} eastAsia={themeLangResolver.EastAsiaLang ?? "(null)"} bidi={themeLangResolver.BidiLang ?? "(null)"}");
}

var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Title",
    "Heading1",
    "Heading2",
    "Heading3"
};

var body = doc.MainDocumentPart?.Document.Body;
if (body == null)
{
    Console.WriteLine("Missing document body.");
    return;
}

int paraCount = 0;
foreach (var para in body.Elements<Paragraph>())
{
    var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    if (string.IsNullOrWhiteSpace(styleId) || !targets.Contains(styleId))
        continue;

    paraCount++;
    Console.WriteLine($"Paragraph style={styleId} text='{para.InnerText}'");

    foreach (var run in para.Elements<Run>())
    {
        var effective = styleResolver.GetEffectiveRunProperties(run, para.ParagraphProperties);
        var font = TextFormatExtractor.ResolvePreferredFont(effective, run, themeLangResolver);
        var runFonts = run.RunProperties?.RunFonts;
        var ascii = runFonts?.Ascii?.Value;
        var highAnsi = runFonts?.HighAnsi?.Value;
        var asciiTheme = runFonts != null ? GetThemeAttributeValue(runFonts, "asciiTheme") : null;
        var highAnsiTheme = runFonts != null ? GetThemeAttributeValue(runFonts, "hAnsiTheme") : null;

        Console.WriteLine($"  run text='{run.InnerText}' font={font ?? "(null)"} ascii={ascii ?? "(null)"} asciiTheme={asciiTheme ?? "(null)"} hAnsi={highAnsi ?? "(null)"} hAnsiTheme={highAnsiTheme ?? "(null)"}");
    }
}

if (paraCount == 0)
    Console.WriteLine("No Title/Heading paragraphs found.");

static string? GetThemeAttributeValue(RunFonts fonts, string localName)
{
    var attr = fonts.GetAttributes().FirstOrDefault(a => a.LocalName == localName);
    return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
}

static string GetRepoDocxPath(string fileName)
    => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "Tests",
        "TestData",
        "Docx",
        fileName));

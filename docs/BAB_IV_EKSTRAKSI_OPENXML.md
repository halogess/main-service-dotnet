# BAB IV: PROSES EKSTRAKSI DOKUMEN DENGAN OPENXML

## Ringkasan Bab
Bab ini membahas secara teknis proses ekstraksi konten dari dokumen Microsoft Word (.docx) menggunakan teknologi OpenXML SDK di platform .NET. Ekstraksi merupakan fase pertama dalam sistem yang mengubah dokumen biner menjadi representasi data terstruktur dalam database.

---

## 4.1 Arsitektur OpenXML dan Struktur DOCX

### 4.1.1 Pengenalan Open Office XML (OOXML)
#### 4.1.1.1 Standar ISO/IEC 29500 untuk Format Dokumen
- Latar belakang standardisasi Microsoft Office formats
- Versi OOXML: Transitional vs Strict
- Adoption di berbagai aplikasi office suite

#### 4.1.1.2 Sejarah dan Evolusi Format DOCX
- Dari DOC (Binary) ke DOCX (XML-based)
- Pengenalan di Microsoft Office 2007
- Backward compatibility dengan format lama

#### 4.1.1.3 Perbandingan dengan Format DOC (Binary)
- Struktur binary compound document vs ZIP archive
- Kesulitan parsing binary vs kemudahan XML parsing
- Ukuran file dan kompresi

#### 4.1.1.4 Keuntungan Format XML untuk Pemrosesan Otomatis
- Human-readable structure
- Partial document access tanpa load keseluruhan
- Extensibility dengan custom XML parts

### 4.1.2 Struktur Internal File DOCX
#### 4.1.2.1 DOCX sebagai Arsip ZIP
- Unzip untuk melihat struktur internal
- Hierarchical folder structure
- File naming conventions

#### 4.1.2.2 File [Content_Types].xml
- Registry tipe konten untuk setiap part
- Default extensions dan override entries
- MIME type declarations

#### 4.1.2.3 Folder word/ dan File Utama
- document.xml: Main document content
- styles.xml: Style definitions
- settings.xml: Document settings
- fontTable.xml: Font declarations

#### 4.1.2.4 Folder _rels/ dan Sistem Relationship
- .rels files untuk relationship definitions
- Relationship types dan target URIs
- Internal vs External relationships

### 4.1.3 Komponen XML dalam Package DOCX
#### 4.1.3.1 document.xml: Konten Utama Dokumen
- Root element w:document
- w:body sebagai container utama
- Namespace declarations (w:, r:, wp:, a:, m:)

#### 4.1.3.2 styles.xml: Definisi Style
- w:docDefaults untuk default formatting
- w:style elements dengan styleId
- Style types: paragraph, character, table, numbering

#### 4.1.3.3 numbering.xml: Definisi Penomoran
- w:abstractNum untuk template numbering
- w:num untuk numbering instances
- Level definitions (w:lvl) dengan format patterns

#### 4.1.3.4 theme1.xml: Tema Warna dan Font
- Color schemes (a:clrScheme)
- Font schemes (a:fontScheme): major dan minor fonts
- Effect schemes dan format schemes

### 4.1.4 DocumentFormat.OpenXml SDK
#### 4.1.4.1 Library Resmi Microsoft untuk OpenXML
- NuGet package: DocumentFormat.OpenXml
- Version compatibility dengan .NET versions
- Open source di GitHub

#### 4.1.4.2 WordprocessingDocument sebagai Entry Point
- Static Open() methods
- DocumentType enumeration
- AutoSave behavior

#### 4.1.4.3 MainDocumentPart dan Part Lainnya
- Part hierarchy dan access patterns
- Lazy loading behavior
- Part creation dan deletion

#### 4.1.4.4 Strongly-Typed Access ke XML Elements
- OpenXmlElement base class
- Type-safe navigation dengan GetFirstChild<T>()
- LINQ queries pada Elements<T>()

---

## 4.2 Membuka dan Membaca Dokumen DOCX

### 4.2.1 Inisialisasi WordprocessingDocument
#### 4.2.1.1 Method Open() dengan Mode Read-Only
```csharp
using var doc = WordprocessingDocument.Open(path, isEditable: false);
```
- Parameter isEditable untuk mode read-only
- AutoSave implications
- Lock behavior pada file

#### 4.2.1.2 File Path vs Stream Input
- Open dari file path langsung
- Open dari MemoryStream atau FileStream
- Considerations untuk cloud storage

#### 4.2.1.3 Exception Handling untuk File Corrupt
- OpenXmlPackageException untuk invalid format
- FileFormatException untuk corruption
- Graceful degradation strategies

#### 4.2.1.4 Dispose Pattern untuk Resource Management
- IDisposable implementation
- using statement untuk automatic cleanup
- Memory considerations untuk large documents

### 4.2.2 Akses ke MainDocumentPart
#### 4.2.2.1 MainDocumentPart.Document Property
- Null check untuk empty documents
- Root element access
- Lazy loading behavior

#### 4.2.2.2 Body Element sebagai Container Konten
- doc.MainDocumentPart.Document.Body
- Direct children: Paragraph, Table, SectionProperties
- Other possible elements: CustomXmlBlock, SdtBlock

#### 4.2.2.3 Iterasi Elements() untuk Traversal
```csharp
foreach (var element in body.Elements())
{
    // Process each top-level element
}
```
- Type checking dengan `is` pattern matching
- Casting ke specific types
- Skip unknown elements

#### 4.2.2.4 Descendants() untuk Deep Search
- Recursive traversal semua descendants
- Type-filtered dengan Descendants<T>()
- Performance considerations untuk large documents

### 4.2.3 Akses ke Part Pendukung
#### 4.2.3.1 StylesPart untuk Definisi Style
```csharp
var stylesPart = doc.MainDocumentPart.StyleDefinitionsPart;
var styles = stylesPart?.Styles;
```
- Null handling jika tidak ada styles
- Iteration melalui Style elements
- DocDefaults access

#### 4.2.3.2 NumberingDefinitionsPart untuk Numbering
```csharp
var numberingPart = doc.MainDocumentPart.NumberingDefinitionsPart;
var numbering = numberingPart?.Numbering;
```
- AbstractNum dan NumberingInstance access
- Level definitions lookup
- Counter initialization

#### 4.2.3.3 ThemePart untuk Tema Dokumen
```csharp
var themePart = doc.MainDocumentPart.ThemePart;
```
- Font scheme extraction
- Color scheme access
- Theme effects

#### 4.2.3.4 ImageParts untuk Media Tertanam
```csharp
foreach (var imagePart in doc.MainDocumentPart.ImageParts)
{
    var rId = doc.MainDocumentPart.GetIdOfPart(imagePart);
    // Extract image data
}
```
- ContentType untuk format detection
- Stream access untuk binary data
- Relationship ID mapping

### 4.2.4 Relationship dan Reference Resolution
#### 4.2.4.1 GetIdOfPart() untuk Mendapatkan rId
- Mapping Part object ke relationship ID
- Usage dalam content references
- Header/Footer references

#### 4.2.4.2 GetPartById() untuk Lookup Part
```csharp
var part = doc.MainDocumentPart.GetPartById(rId);
```
- Reverse lookup dari rId ke Part
- Type casting hasil
- Null handling untuk missing parts

#### 4.2.4.3 Hyperlink dan External References
- HyperlinkRelationship extraction
- External vs Internal hyperlinks
- Target URI resolution

#### 4.2.4.4 Embedded Objects dan OLE
- EmbeddedPackagePart untuk embedded files
- OLE objects handling
- Legacy embedded content

---

## 4.3 Ekstraksi Section Properties (Pengaturan Halaman)

### 4.3.1 Lokasi SectionProperties dalam Dokumen
#### 4.3.1.1 w:sectPr di Akhir Body (Default Section)
```csharp
var lastSectPr = body.Elements<SectionProperties>().LastOrDefault();
```
- Section terakhir sebagai default
- Applies ke semua content sebelumnya tanpa sectPr

#### 4.3.1.2 w:sectPr dalam w:pPr (Section Break)
```csharp
var sectPr = paragraph.ParagraphProperties?.GetFirstChild<SectionProperties>();
```
- Section break sebelum paragraf ini
- Applies ke content sebelum paragraf

#### 4.3.1.3 Urutan Section dari Awal ke Akhir Dokumen
- Collect semua sectPr dengan urutan
- Index tracking untuk identification
- Mapping content ke section

#### 4.3.1.4 Multiple Sections dan Index Tracking
```csharp
int sectionIndex = 0;
foreach (var element in body.Elements())
{
    if (element is Paragraph p && p.ParagraphProperties?.SectionProperties != null)
    {
        // Process section break
        sectionIndex++;
    }
}
```

### 4.3.2 Ekstraksi Ukuran Halaman
#### 4.3.2.1 PageSize Element (w:pgSz)
```csharp
var pageSize = sectPr.GetFirstChild<PageSize>();
var width = pageSize?.Width?.Value;   // dalam Twips
var height = pageSize?.Height?.Value; // dalam Twips
```

#### 4.3.2.2 Width dan Height dalam Twips
- 1 Twip = 1/20 point = 1/1440 inch
- A4: 11906 x 16838 twips (210mm x 297mm)
- Conversion formula: cm = twips / 566.929

#### 4.3.2.3 Orientation: Portrait vs Landscape
```csharp
var orientation = pageSize?.Orient?.Value;
// PageOrientationValues.Portrait atau Landscape
```
- Default adalah Portrait jika tidak specified
- Swap width/height untuk Landscape

#### 4.3.2.4 Konversi Twips ke Satuan Lain
```csharp
const decimal TwipsPerCm = 566.929m;
const decimal TwipsPerInch = 1440m;
const decimal TwipsPerPoint = 20m;

decimal cm = twips / TwipsPerCm;
decimal inches = twips / TwipsPerInch;
decimal points = twips / TwipsPerPoint;
```

### 4.3.3 Ekstraksi Margin Halaman
#### 4.3.3.1 PageMargin Element (w:pgMar)
```csharp
var pageMargin = sectPr.GetFirstChild<PageMargin>();
```

#### 4.3.3.2 Top, Bottom, Left, Right Margins
```csharp
var top = pageMargin?.Top?.Value;      // signed int (twips)
var bottom = pageMargin?.Bottom?.Value;
var left = pageMargin?.Left?.Value;    // UInt32 (twips)  
var right = pageMargin?.Right?.Value;
```
- Top/Bottom bisa negative (untuk overlap)
- Left/Right selalu positive

#### 4.3.3.3 Header dan Footer Margins
```csharp
var header = pageMargin?.Header?.Value; // dari edge atas
var footer = pageMargin?.Footer?.Value; // dari edge bawah
```
- Jarak dari tepi kertas ke area header/footer
- Berbeda dengan margin content

#### 4.3.3.4 Gutter dan GutterPosition
```csharp
var gutter = pageMargin?.Gutter?.Value;
var gutterAtTop = sectPr.GetFirstChild<GutterAtTop>();
```
- Gutter untuk binding margin
- Default position: left (portrait) atau top (landscape)
- GutterAtTop element untuk override

### 4.3.4 Ekstraksi Pengaturan Section Lainnya
#### 4.3.4.1 SectionType: nextPage, continuous, evenPage, oddPage
```csharp
var sectionType = sectPr.GetFirstChild<SectionType>();
var type = sectionType?.Val?.Value;
// SectionMarkValues: NextPage, Continuous, EvenPage, OddPage, NextColumn
```

#### 4.3.4.2 TitlePage untuk First Page Different
```csharp
var titlePage = sectPr.GetFirstChild<TitlePage>();
bool hasTitlePage = titlePage != null && (titlePage.Val?.Value ?? true);
```
- Jika ada tanpa Val, default true
- First page menggunakan header/footer khusus

#### 4.3.4.3 PageNumberType: Format dan Start Number
```csharp
var pageNumberType = sectPr.GetFirstChild<PageNumberType>();
var format = pageNumberType?.Format?.Value; // NumberFormatValues
var start = pageNumberType?.Start?.Value;   // int? 
```
- Format: decimal, upperRoman, lowerRoman, upperLetter, lowerLetter
- Start null = continue from previous section

#### 4.3.4.4 Columns untuk Multi-Column Layout
```csharp
var columns = sectPr.GetFirstChild<Columns>();
var columnCount = columns?.ColumnCount?.Value ?? 1;
var space = columns?.Space?.Value; // space between columns
```

---

## 4.4 Ekstraksi Paragraf dan Konten Teks

### 4.4.1 Struktur Paragraph Element
#### 4.4.1.1 w:p Element sebagai Container Paragraf
```csharp
foreach (var para in body.Elements<Paragraph>())
{
    // Process paragraph
}
```
- Setiap w:p adalah satu logical paragraph
- Bisa empty (no content)

#### 4.4.1.2 w:pPr (ParagraphProperties) untuk Formatting
```csharp
var pPr = para.ParagraphProperties;
var styleId = pPr?.ParagraphStyleId?.Val?.Value;
```
- Optional element
- Contains formatting dan style reference

#### 4.4.1.3 w:r (Run) Elements untuk Konten
```csharp
foreach (var run in para.Elements<Run>())
{
    // Process each run
}
```
- Multiple runs dalam satu paragraph
- Each run has uniform formatting

#### 4.4.1.4 Urutan Children Elements
- ParagraphProperties selalu pertama (jika ada)
- Kemudian Run elements, Drawing, dll
- Order matters untuk content reconstruction

### 4.4.2 Ekstraksi Paragraph Properties
#### 4.4.2.1 Justification (w:jc)
```csharp
var jc = pPr?.Justification?.Val?.Value;
// JustificationValues: Left, Center, Right, Both, Distribute
```
- Default Left jika tidak specified
- Both = justify

#### 4.4.2.2 Indentation (w:ind)
```csharp
var ind = pPr?.Indentation;
var left = ind?.Left?.Value;      // twips string
var right = ind?.Right?.Value;
var firstLine = ind?.FirstLine?.Value;
var hanging = ind?.Hanging?.Value; // mutually exclusive dengan firstLine
```
- FirstLine dan Hanging tidak bisa bersamaan
- Values dalam twips sebagai string

#### 4.4.2.3 SpacingBetweenLines (w:spacing)
```csharp
var spacing = pPr?.SpacingBetweenLines;
var before = spacing?.Before?.Value;    // twips string
var after = spacing?.After?.Value;
var line = spacing?.Line?.Value;
var lineRule = spacing?.LineRule?.Value; // LineSpacingRuleValues
```
- LineRule: Auto (multiple), AtLeast, Exact
- Line value interpretation depends on LineRule

#### 4.4.2.4 KeepNext, KeepLines, PageBreakBefore
```csharp
var keepNext = pPr?.KeepNext;      // keep with next paragraph
var keepLines = pPr?.KeepLines;    // don't break paragraph
var pageBreakBefore = pPr?.PageBreakBefore;
```
- Boolean toggle elements
- Null atau Val=false means off

### 4.4.3 Ekstraksi Konten Paragraf
#### 4.4.3.1 Text (w:t) Element
```csharp
foreach (var text in run.Elements<Text>())
{
    var content = text.Text;
    var preserveSpace = text.Space?.Value == SpaceProcessingModeValues.Preserve;
}
```
- xml:space="preserve" untuk keep whitespace
- Multiple Text elements di satu Run possible

#### 4.4.3.2 TabChar (w:tab) untuk Tab Characters
```csharp
if (element is TabChar)
{
    // Append "\t" to text buffer
}
```
- Represents tab stop
- Convert ke "\t" character

#### 4.4.3.3 Break (w:br) untuk Line/Page Breaks
```csharp
if (element is Break br)
{
    var type = br.Type?.Value;
    // BreakValues: Page, Column, TextWrapping
}
```
- No type atau TextWrapping = line break ("\n")
- Page = page break
- Column = column break

#### 4.4.3.4 SymbolChar dan Special Characters
```csharp
if (element is SymbolChar sym)
{
    var font = sym.Font?.Value;
    var charCode = sym.Char?.Value; // hex string
}
```
- Symbol dari special fonts (Wingdings, Symbol)
- Requires font mapping untuk rendering

### 4.4.4 Deteksi Tipe Paragraf
#### 4.4.4.1 Heading Detection via ParagraphStyleId
```csharp
var styleId = pPr?.ParagraphStyleId?.Val?.Value;
if (styleId?.StartsWith("Heading") == true)
{
    // Extract level: Heading1 -> h1
    var level = styleId.Replace("Heading", "");
}
```
- Standard heading styles: Heading1-Heading9
- Map ke h1-h9 types

#### 4.4.4.2 List Item Detection via NumberingProperties
```csharp
var numPr = pPr?.NumberingProperties;
if (numPr != null)
{
    var numId = numPr.NumberingId?.Val?.Value;
    var ilvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;
    // Type: list-item-{numId}-{ilvl}
}
```

#### 4.4.4.3 Title dan Subtitle Styles
```csharp
switch (styleId?.ToLower())
{
    case "title": return "title";
    case "subtitle": return "subtitle";
}
```

#### 4.4.4.4 Default Paragraph Type
- Jika tidak match heading/list/title
- Return "paragraph" sebagai default type

---

## 4.5 Ekstraksi Run dan Format Teks

### 4.5.1 Struktur Run Element
#### 4.5.1.1 w:r Element sebagai Unit Formatting
```csharp
foreach (var run in paragraph.Elements<Run>())
{
    var rPr = run.RunProperties;
    var texts = run.Elements<Text>();
}
```
- Semua content dalam satu run punya formatting sama
- Change formatting = new run

#### 4.5.1.2 w:rPr (RunProperties) untuk Formatting
```csharp
var rPr = run.RunProperties;
var bold = rPr?.Bold;
var italic = rPr?.Italic;
```
- Optional, null means inherit from style
- Contains all run-level formatting

#### 4.5.1.3 Multiple Runs dalam Satu Paragraf
- Formatting change creates new run
- Combine text dari semua runs untuk full paragraph text
- Track formatting per-run untuk detailed extraction

#### 4.5.1.4 Empty Runs dan Significance Check
```csharp
bool HasSignificantFormatting(RunProperties rPr)
{
    return rPr?.Bold != null ||
           rPr?.Italic != null ||
           rPr?.Underline != null ||
           rPr?.FontSize != null;
}
```
- Skip saving format if no significant formatting
- Reduces database storage

### 4.5.2 Ekstraksi Font Properties
#### 4.5.2.1 RunFonts (w:rFonts)
```csharp
var fonts = rPr?.RunFonts;
var ascii = fonts?.Ascii?.Value;        // Latin text
var hAnsi = fonts?.HighAnsi?.Value;     // Extended Latin
var eastAsia = fonts?.EastAsia?.Value;  // CJK
var cs = fonts?.ComplexScript?.Value;   // RTL scripts
```
- Different fonts untuk different scripts
- Usually ascii dan hAnsi sama

#### 4.5.2.2 Theme Font References
```csharp
var asciiTheme = fonts?.AsciiTheme?.Value;
// ThemeFontValues: MajorAscii, MinorHAnsi, etc
```
- Reference ke theme fonts bukan explicit font name
- Requires theme resolution

#### 4.5.2.3 FontSize (w:sz) dalam Half-Points
```csharp
var size = rPr?.FontSize?.Val?.Value;
// "24" means 12pt (24 half-points)
decimal pointSize = decimal.Parse(size) / 2;
```
- Stored as string
- 1 point = 2 half-points

#### 4.5.2.4 FontSizeComplexScript (w:szCs)
```csharp
var sizeCs = rPr?.FontSizeComplexScript?.Val?.Value;
```
- Separate size untuk complex scripts (Arabic, Hebrew)
- May differ from regular font size

### 4.5.3 Ekstraksi Text Styling
#### 4.5.3.1 Bold (w:b) dan BoldComplexScript (w:bCs)
```csharp
var bold = rPr?.Bold;
bool isBold = bold != null && (bold.Val?.Value ?? true);
```
- Presence tanpa Val means true
- Val="0" atau Val="false" means false

#### 4.5.3.2 Italic (w:i) dan ItalicComplexScript (w:iCs)
```csharp
var italic = rPr?.Italic;
bool isItalic = italic != null && (italic.Val?.Value ?? true);
```
- Same toggle pattern as bold

#### 4.5.3.3 Underline (w:u) dengan Style Variants
```csharp
var underline = rPr?.Underline;
var style = underline?.Val?.Value;
// UnderlineValues: Single, Double, Wave, Dash, etc
bool isUnderlined = style != null && style != UnderlineValues.None;
```
- Multiple underline styles
- None means explicitly no underline

#### 4.5.3.4 Strike (w:strike) dan DoubleStrike
```csharp
var strike = rPr?.Strike;
var dstrike = rPr?.DoubleStrike;
```
- Strikethrough text
- Single vs double line

### 4.5.4 Ekstraksi Properti Run Lainnya
#### 4.5.4.1 Color (w:color)
```csharp
var color = rPr?.Color;
var hexValue = color?.Val?.Value; // "FF0000" for red
var themeColor = color?.ThemeColor?.Value;
```
- Hex RGB value atau theme color reference
- "auto" untuk automatic color

#### 4.5.4.2 Highlight (w:highlight)
```csharp
var highlight = rPr?.Highlight;
var color = highlight?.Val?.Value;
// HighlightColorValues: Yellow, Green, Cyan, etc
```
- Predefined highlight colors
- For background highlighting

#### 4.5.4.3 VerticalTextAlignment
```csharp
var vAlign = rPr?.VerticalTextAlignment?.Val?.Value;
// VerticalPositionValues: Superscript, Subscript, Baseline
```
- For superscript/subscript text
- Alternative: Position property

#### 4.5.4.4 Languages (w:lang)
```csharp
var lang = rPr?.Languages;
var latin = lang?.Val?.Value;      // "en-US"
var eastAsia = lang?.EastAsia?.Value; // "ja-JP"
var bidi = lang?.Bidi?.Value;      // "ar-SA"
```
- Language settings per script
- Used for spell-check dan hyphenation

---

## 4.6 Ekstraksi Numbering dan List

### 4.6.1 Struktur NumberingDefinitionsPart
#### 4.6.1.1 AbstractNum: Template Numbering
```csharp
var abstractNums = numberingPart.Numbering.Elements<AbstractNum>();
foreach (var absNum in abstractNums)
{
    var id = absNum.AbstractNumberId?.Value;
    var levels = absNum.Elements<Level>();
}
```
- Template definition yang bisa di-reuse
- Contains all level definitions

#### 4.6.1.2 NumberingInstance: Penggunaan Template
```csharp
var numInstances = numberingPart.Numbering.Elements<NumberingInstance>();
foreach (var numInst in numInstances)
{
    var numId = numInst.NumberID?.Value;
    var absNumId = numInst.AbstractNumId?.Val?.Value;
}
```
- numId is what paragraphs reference
- Points to abstractNumId

#### 4.6.1.3 Level (w:lvl) Definitions
```csharp
foreach (var level in absNum.Elements<Level>())
{
    var ilvl = level.LevelIndex?.Value; // 0-8
    var numFmt = level.NumberingFormat?.Val?.Value;
    var lvlText = level.LevelText?.Val?.Value;
}
```
- 9 levels (0-8) per abstractNum
- Each level has independent format

#### 4.6.1.4 LevelOverride untuk Customisasi
```csharp
var overrides = numInst.Elements<LevelOverride>();
foreach (var over in overrides)
{
    var ilvl = over.LevelIndex?.Value;
    var startOverride = over.StartOverrideNumberingValue?.Val?.Value;
    var levelDef = over.Level; // full level override
}
```
- Override specific levels per instance
- Start value atau full level redefinition

### 4.6.2 Ekstraksi Level Properties
#### 4.6.2.1 Start Value dan NumberFormat
```csharp
var start = level.StartNumberingValue?.Val?.Value ?? 1;
var numFmt = level.NumberingFormat?.Val?.Value;
// NumberFormatValues: Decimal, LowerLetter, UpperRoman, Bullet, etc
```
- Start value untuk counter initialization
- Format menentukan tampilan

#### 4.6.2.2 LevelText Pattern
```csharp
var lvlText = level.LevelText?.Val?.Value;
// "%1." for first level
// "%1.%2" for second level showing both
// "•" for bullet
```
- %1, %2, etc replaced dengan counter values
- Static text untuk bullets

#### 4.6.2.3 LevelSuffix
```csharp
var suffix = level.LevelSuffix?.Val?.Value;
// LevelSuffixValues: Tab, Space, Nothing
```
- What comes after the number
- Default is Tab

#### 4.6.2.4 IsLegalNumberingStyle
```csharp
var isLegal = level.IsLegalNumberingStyle;
```
- Legal numbering: 1, 1.1, 1.1.1 format
- Affects how sublevel numbers display

### 4.6.3 Ekstraksi Numbering dari Paragraf
#### 4.6.3.1 NumberingProperties dalam ParagraphProperties
```csharp
var numPr = pPr?.NumberingProperties;
var numId = numPr?.NumberingId?.Val?.Value;
var ilvl = numPr?.NumberingLevelReference?.Val?.Value ?? 0;
```
- numId references NumberingInstance
- ilvl adalah level index (0-based)

#### 4.6.3.2 Numbering via Style
```csharp
// Check if style has numbering
var style = GetStyle(styleId);
var styleNumPr = style?.StyleParagraphProperties?.NumberingProperties;
```
- List styles have embedded numbering
- Paragraph may not have direct numPr

#### 4.6.3.3 Resolusi Level via NumberingResolver
```csharp
var level = NumberingResolver.GetNumberingLevel(numberingPart, numId, ilvl);
```
- Lookup through numInst → abstractNum → level
- Handle LevelOverride

#### 4.6.3.4 Abstract Numbering ID Lookup
```csharp
var numInst = numbering.Elements<NumberingInstance>()
    .FirstOrDefault(n => n.NumberID?.Value == numId);
var abstractNumId = numInst?.AbstractNumId?.Val?.Value;
```
- Required untuk counter management
- Multiple numIds can share same abstractNumId

### 4.6.4 Generate Numbering Text
#### 4.6.4.1 Counter Management per AbstractNumId
```csharp
Dictionary<int, Dictionary<int, int>> counters = new();
// counters[abstractNumId][ilvl] = current count

void IncrementCounter(int absNumId, int ilvl)
{
    if (!counters.ContainsKey(absNumId))
        counters[absNumId] = new Dictionary<int, int>();
    
    // Reset higher levels
    for (int i = ilvl + 1; i <= 8; i++)
        counters[absNumId][i] = 0;
    
    // Increment current level
    counters[absNumId][ilvl] = counters[absNumId].GetValueOrDefault(ilvl, 0) + 1;
}
```

#### 4.6.4.2 FormatNumber berdasarkan NumberFormat
```csharp
string FormatNumber(int number, NumberFormatValues format)
{
    return format switch
    {
        NumberFormatValues.Decimal => number.ToString(),
        NumberFormatValues.LowerLetter => GetLetter(number, lower: true),
        NumberFormatValues.UpperLetter => GetLetter(number, lower: false),
        NumberFormatValues.LowerRoman => ToRoman(number).ToLower(),
        NumberFormatValues.UpperRoman => ToRoman(number),
        NumberFormatValues.Bullet => "•",
        _ => number.ToString()
    };
}
```

#### 4.6.4.3 Bullet Character Normalization
```csharp
string NormalizeBullet(string bulletChar, string fontName)
{
    // Symbol font characters need mapping
    if (fontName == "Symbol")
        return bulletChar switch
        {
            "\uf0b7" => "•", // bullet
            "\uf0a7" => "◦", // empty bullet
            _ => bulletChar
        };
    
    if (fontName == "Wingdings")
        return bulletChar switch
        {
            "\uf0fc" => "✓", // checkmark
            "\uf0a8" => "➔", // arrow
            _ => bulletChar
        };
    
    return bulletChar;
}
```

#### 4.6.4.4 Multi-Level Numbering Assembly
```csharp
string BuildNumberingText(string pattern, int absNumId, int currentLevel)
{
    var result = pattern;
    for (int i = 0; i <= currentLevel; i++)
    {
        var count = counters[absNumId].GetValueOrDefault(i, 1);
        var format = GetLevelFormat(absNumId, i);
        result = result.Replace($"%{i + 1}", FormatNumber(count, format));
    }
    return result;
}
// "%1.%2" with level 1 counts → "1.3"
```

---

## 4.7 Ekstraksi Tabel

### 4.7.1 Struktur Table Element
#### 4.7.1.1 w:tbl Element sebagai Container
```csharp
foreach (var table in body.Elements<Table>())
{
    var tblPr = table.GetFirstChild<TableProperties>();
    var tblGrid = table.GetFirstChild<TableGrid>();
    var rows = table.Elements<TableRow>();
}
```

#### 4.7.1.2 TableProperties (w:tblPr)
```csharp
var tblPr = table.GetFirstChild<TableProperties>();
var styleId = tblPr?.TableStyle?.Val?.Value;
var width = tblPr?.TableWidth;
var jc = tblPr?.TableJustification;
```
- Style reference
- Table-level formatting

#### 4.7.1.3 TableGrid untuk Column Definitions
```csharp
var gridCols = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>();
foreach (var col in gridCols)
{
    var width = col.Width?.Value; // twips
}
```
- Defines column widths
- Each GridColumn = one logical column

#### 4.7.1.4 w:tr (TableRow) dan w:tc (TableCell)
```csharp
foreach (var row in table.Elements<TableRow>())
{
    foreach (var cell in row.Elements<TableCell>())
    {
        var content = cell.Elements<Paragraph>();
    }
}
```
- Nested iteration for full table

### 4.7.2 Ekstraksi Table Properties
#### 4.7.2.1 TableWidth dan TableWidthType
```csharp
var width = tblPr?.TableWidth;
var widthValue = width?.Width?.Value;
var widthType = width?.Type?.Value;
// TableWidthUnitValues: Dxa (twips), Pct (percent), Auto, Nil
```

#### 4.7.2.2 TableJustification
```csharp
var jc = tblPr?.TableJustification?.Val?.Value;
// TableRowAlignmentValues: Left, Center, Right
```
- Table horizontal alignment on page

#### 4.7.2.3 TableIndentation
```csharp
var ind = tblPr?.TableIndentation;
var indent = ind?.Width?.Value;
var indentType = ind?.Type?.Value;
```
- Left indentation of table

#### 4.7.2.4 TableBorders
```csharp
var borders = tblPr?.TableBorders;
var top = borders?.TopBorder;
var bottom = borders?.BottomBorder;
var left = borders?.LeftBorder;
var right = borders?.RightBorder;
var insideH = borders?.InsideHorizontalBorder;
var insideV = borders?.InsideVerticalBorder;
```
- Six border types untuk table
- Each border has style, width, color

### 4.7.3 Ekstraksi Row dan Cell
#### 4.7.3.1 TableRowHeight
```csharp
var trPr = row.TableRowProperties;
var height = trPr?.TableRowHeight;
var heightVal = height?.Val?.Value;
var heightRule = height?.HeightType?.Value;
// HeightRuleValues: Auto, AtLeast, Exact
```

#### 4.7.3.2 TableCellWidth
```csharp
var tcPr = cell.TableCellProperties;
var width = tcPr?.TableCellWidth;
```

#### 4.7.3.3 GridSpan untuk Column Merge
```csharp
var gridSpan = tcPr?.GridSpan?.Val?.Value ?? 1;
// Jika gridSpan > 1, cell spans multiple columns
```
- Horizontal merge

#### 4.7.3.4 VMerge untuk Row Merge
```csharp
var vMerge = tcPr?.VerticalMerge;
if (vMerge != null)
{
    var mergeType = vMerge.Val?.Value;
    // MergedCellValues.Restart = start of merge
    // null/Continue = continuation
}
```
- Vertical merge
- Restart indicates first cell of merged group

### 4.7.4 Ekstraksi Konten Cell
#### 4.7.4.1 Paragraf dalam Cell
```csharp
foreach (var para in cell.Elements<Paragraph>())
{
    // Same extraction as body paragraphs
    var type = DetectParagraphType(para);
    var content = ExtractParagraphContent(para);
}
```
- Each cell must have at least one paragraph
- Same structure as body paragraphs

#### 4.7.4.2 Nested Tables
```csharp
foreach (var element in cell.Elements())
{
    if (element is Table nestedTable)
    {
        // Recursive extraction
        var nestedContent = ExtractTable(nestedTable);
    }
}
```
- Tables can contain other tables
- Recursive processing required

#### 4.7.4.3 Cell Shading
```csharp
var shading = tcPr?.Shading;
var fill = shading?.Fill?.Value;     // background color
var pattern = shading?.Val?.Value;   // pattern type
var color = shading?.Color?.Value;   // pattern color
```

#### 4.7.4.4 Cell Borders Individual
```csharp
var tcBorders = tcPr?.TableCellBorders;
// Can override table-level borders
```
- Cell-specific border overrides

---

## 4.8 Ekstraksi Gambar dan Drawing

### 4.8.1 Struktur Drawing Element
#### 4.8.1.1 w:drawing sebagai Container
```csharp
foreach (var drawing in run.Elements<Drawing>())
{
    // Process drawing content
}
```
- Can contain inline or anchor

#### 4.8.1.2 wp:inline untuk Inline Images
```csharp
var inline = drawing.Descendants<Inline>().FirstOrDefault();
if (inline != null)
{
    var extent = inline.Extent;
    var docPr = inline.DocProperties;
}
```
- Image flows with text
- Has extent (size) and properties

#### 4.8.1.3 wp:anchor untuk Floating Images
```csharp
var anchor = drawing.Descendants<Anchor>().FirstOrDefault();
if (anchor != null)
{
    var posH = anchor.HorizontalPosition;
    var posV = anchor.VerticalPosition;
    var wrapType = anchor.Descendants<WrapSquare>().Any() ? "square" : "none";
}
```
- Positioned independently of text
- Various wrap types

#### 4.8.1.4 a:blip untuk Image Reference
```csharp
var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
var rId = blip?.Embed?.Value;
// Use rId to get actual image from ImageParts
```

### 4.8.2 Ekstraksi Image Properties
#### 4.8.2.1 Extent: cx dan cy dalam EMUs
```csharp
var extent = inline?.Extent ?? anchor?.Extent;
var widthEmu = extent?.Cx?.Value;   // long
var heightEmu = extent?.Cy?.Value;

// 1 inch = 914400 EMUs
// 1 cm = 360000 EMUs
const long EmusPerCm = 360000;
decimal widthCm = widthEmu / (decimal)EmusPerCm;
```

#### 4.8.2.2 Embed Relationship ID
```csharp
var rId = blip?.Embed?.Value;  // "rId5"
var imagePart = doc.MainDocumentPart.GetPartById(rId) as ImagePart;
```

#### 4.8.2.3 DocProperties: id dan name
```csharp
var docPr = inline?.DocProperties ?? anchor?.DocProperties;
var id = docPr?.Id?.Value;
var name = docPr?.Name?.Value;
var description = docPr?.Description?.Value;
```
- Unique ID within document
- Name untuk identification

#### 4.8.2.4 Effect dan Transform
```csharp
var effectExtent = inline?.EffectExtent;
// Additional margins for effects (shadows, etc)
```

### 4.8.3 Ekstraksi Anchor Properties
#### 4.8.3.1 SimplePosition vs Positioned
```csharp
var simplePos = anchor?.SimplePos?.Value ?? false;
if (simplePos)
{
    var x = anchor?.SimplePosition?.X?.Value;
    var y = anchor?.SimplePosition?.Y?.Value;
}
```
- SimplePos = absolute coordinates
- Otherwise = relative positioning

#### 4.8.3.2 HorizontalPosition dan VerticalPosition
```csharp
var posH = anchor?.HorizontalPosition;
var relativeFrom = posH?.RelativeFrom?.Value;
// HorizontalRelativePositionValues: Character, Column, Page, Margin
var posOffset = posH?.PositionOffset?.Text; // EMUs
var align = posH?.HorizontalAlignment?.Text; // left, center, right
```

#### 4.8.3.3 WrapType Detection
```csharp
var wrapType = "none";
if (anchor?.Descendants<WrapNone>().Any() == true) wrapType = "none";
else if (anchor?.Descendants<WrapSquare>().Any() == true) wrapType = "square";
else if (anchor?.Descendants<WrapTight>().Any() == true) wrapType = "tight";
else if (anchor?.Descendants<WrapThrough>().Any() == true) wrapType = "through";
else if (anchor?.Descendants<WrapTopBottom>().Any() == true) wrapType = "topAndBottom";
```

#### 4.8.3.4 AllowOverlap dan BehindDoc
```csharp
var allowOverlap = anchor?.AllowOverlap?.Value;
var behindDoc = anchor?.BehindDocumentText?.Value;
```
- Overlap dengan other floating objects
- Behind text layer

### 4.8.4 Ekstraksi Media dari Package
#### 4.8.4.1 ImageParts Enumeration
```csharp
foreach (var imagePart in doc.MainDocumentPart.ImageParts)
{
    var contentType = imagePart.ContentType;
    var rId = doc.MainDocumentPart.GetIdOfPart(imagePart);
}
```

#### 4.8.4.2 GetStream() untuk Binary Data
```csharp
using var stream = imagePart.GetStream();
using var ms = new MemoryStream();
await stream.CopyToAsync(ms);
byte[] imageBytes = ms.ToArray();
```

#### 4.8.4.3 ContentType Detection
```csharp
var ext = imagePart.ContentType switch
{
    "image/png" => "png",
    "image/jpeg" => "jpg",
    "image/gif" => "gif",
    "image/bmp" => "bmp",
    "image/tiff" => "tiff",
    _ => "bin"
};
```

#### 4.8.4.4 Saving ke File System
```csharp
var filename = $"{dokumenId}_{rId}.{ext}";
var fullPath = Path.Combine(storagePath, "images", filename);
await File.WriteAllBytesAsync(fullPath, imageBytes);
```
- Save dengan unique filename
- Track path untuk later reference

---

## 4.9 Resolusi Style dan Inheritance

### 4.9.1 Struktur StylesPart
#### 4.9.1.1 DocDefaults: rPrDefault dan pPrDefault
```csharp
var docDefaults = stylesPart.Styles.DocDefaults;
var rPrDefault = docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
var pPrDefault = docDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
```
- Base formatting untuk semua content
- Applied before any style

#### 4.9.1.2 Style Elements dengan StyleId
```csharp
foreach (var style in styles.Elements<Style>())
{
    var styleId = style.StyleId?.Value;
    var type = style.Type?.Value; // StyleValues: Paragraph, Character, Table
    var name = style.StyleName?.Val?.Value;
}
```

#### 4.9.1.3 BasedOn untuk Inheritance
```csharp
var basedOn = style.BasedOn?.Val?.Value;
// styleId of parent style
```
- Chain of inheritance
- Normal style is often the root

#### 4.9.1.4 LinkedStyle untuk Paragraph-Character Link
```csharp
var linkedStyle = style.LinkedStyle?.Val?.Value;
```
- Paragraph style linked to character style
- E.g., "Heading 1" paragraph linked to "Heading 1 Char"

### 4.9.2 Style Resolution Algorithm
#### 4.9.2.1 GetStyleChain() untuk Inheritance
```csharp
List<Style> GetStyleChain(string styleId)
{
    var chain = new List<Style>();
    var current = GetStyle(styleId);
    
    while (current != null)
    {
        chain.Insert(0, current); // parent first
        var basedOnId = current.BasedOn?.Val?.Value;
        current = basedOnId != null ? GetStyle(basedOnId) : null;
    }
    
    return chain;
}
// Result: [Normal, Heading, Heading1] (oldest first)
```

#### 4.9.2.2 Resolution Order
1. docDefaults (rPrDefault, pPrDefault)
2. Normal style (pPr, rPr)
3. BasedOn chain styles (oldest to newest)
4. Direct formatting (on element)

#### 4.9.2.3 Property Merging Strategy
```csharp
void MergeRunProperties(EffectiveRunProperties effective, RunProperties direct)
{
    // Later values override earlier
    if (direct.Bold != null)
        effective.Bold = direct.Bold.Val?.Value ?? true;
    
    if (direct.FontSize?.Val?.Value != null)
        effective.FontSize = int.Parse(direct.FontSize.Val.Value);
    
    // ... other properties
}
```

#### 4.9.2.4 Toggle Properties dan Null Values
```csharp
// Toggle property: presence means true unless Val=false
bool GetToggleValue(OnOffType? element)
{
    if (element == null) return false; // not set in this level
    return element.Val?.Value ?? true; // present = true unless explicit false
}
```

### 4.9.3 Effective Properties Calculation
#### 4.9.3.1 EffectiveRunProperties Data Class
```csharp
public class EffectiveRunProperties
{
    public string? FontAscii { get; set; }
    public string? FontHighAnsi { get; set; }
    public string? FontEastAsia { get; set; }
    public string? FontComplexScript { get; set; }
    public int? FontSize { get; set; }      // half-points
    public int? FontSizeCs { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public string? UnderlineStyle { get; set; }
    public string? Color { get; set; }
    public bool? Strike { get; set; }
    public string? LangLatin { get; set; }
    public string? LangEastAsia { get; set; }
    public string? LangBidi { get; set; }
}
```

#### 4.9.3.2 EffectiveParagraphProperties Data Class
```csharp
public class EffectiveParagraphProperties
{
    public string? Justification { get; set; }
    public int? IndentLeft { get; set; }       // twips
    public int? IndentRight { get; set; }
    public int? IndentFirstLine { get; set; }
    public int? IndentHanging { get; set; }
    public int? SpaceBefore { get; set; }      // twips
    public int? SpaceAfter { get; set; }
    public int? LineSpacing { get; set; }      // twips or percentage
    public string? LineRule { get; set; }      // auto, atLeast, exact
    public bool? KeepNext { get; set; }
    public bool? KeepLines { get; set; }
    public bool? PageBreakBefore { get; set; }
}
```

#### 4.9.3.3 Full Resolution Example
```csharp
EffectiveRunProperties GetEffectiveRunProperties(Run run, Paragraph para)
{
    var effective = new EffectiveRunProperties();
    
    // 1. docDefaults
    MergeRunProperties(effective, docDefaults.RunPropertiesDefault);
    
    // 2. Paragraph style's linked character style
    var pStyleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    var pStyle = GetStyle(pStyleId);
    var linkedCharStyle = pStyle?.LinkedStyle?.Val?.Value;
    if (linkedCharStyle != null)
    {
        foreach (var s in GetStyleChain(linkedCharStyle))
            MergeRunProperties(effective, s.StyleRunProperties);
    }
    
    // 3. Paragraph style's rPr
    foreach (var s in GetStyleChain(pStyleId))
        MergeRunProperties(effective, s.StyleRunProperties);
    
    // 4. Character style (rStyle)
    var rStyleId = run.RunProperties?.RunStyle?.Val?.Value;
    foreach (var s in GetStyleChain(rStyleId))
        MergeRunProperties(effective, s.StyleRunProperties);
    
    // 5. Direct formatting
    MergeRunProperties(effective, run.RunProperties);
    
    return effective;
}
```

#### 4.9.3.4 Font Resolution dengan Theme
```csharp
string? ResolveFont(string? explicit, string? themeRef, ThemeFontResolver? resolver)
{
    if (!string.IsNullOrEmpty(explicit))
        return explicit;
    
    if (!string.IsNullOrEmpty(themeRef) && resolver != null)
        return resolver.ResolveThemeFont(themeRef);
    
    return null;
}
```

### 4.9.4 Theme Font Resolution
#### 4.9.4.1 ThemePart Parsing
```csharp
var themePart = doc.MainDocumentPart.ThemePart;
using var stream = themePart.GetStream();
var xdoc = XDocument.Load(stream);
```

#### 4.9.4.2 FontScheme Extraction
```csharp
XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
var fontScheme = xdoc.Descendants(a + "fontScheme").FirstOrDefault();
var majorFont = fontScheme?.Element(a + "majorFont");
var minorFont = fontScheme?.Element(a + "minorFont");

var majorLatin = majorFont?.Element(a + "latin")?.Attribute("typeface")?.Value;
var minorLatin = minorFont?.Element(a + "latin")?.Attribute("typeface")?.Value;
```

#### 4.9.4.3 Theme Key Mapping
```csharp
Dictionary<string, string> themeFonts = new()
{
    ["majorHAnsi"] = majorLatin,
    ["majorAscii"] = majorLatin,
    ["minorHAnsi"] = minorLatin,
    ["minorAscii"] = minorLatin,
    ["majorEastAsia"] = majorEa,
    ["minorEastAsia"] = minorEa,
    // ... etc
};
```

#### 4.9.4.4 Script-Specific Font Resolution
```csharp
// Latin text uses ascii/hAnsi
// CJK text uses eastAsia
// Arabic/Hebrew uses cs (complex script)

string GetPreferredFont(EffectiveRunProperties props, string textContent)
{
    if (ContainsEastAsian(textContent))
        return props.FontEastAsia ?? props.FontAscii;
    
    if (ContainsComplexScript(textContent))
        return props.FontComplexScript ?? props.FontAscii;
    
    return props.FontAscii;
}
```

---

## 4.10 Penyimpanan Hasil Ekstraksi

### 4.10.1 Skema Database untuk Hasil Ekstraksi
#### 4.10.1.1 Tabel dokumen_section
```sql
CREATE TABLE dokumen_section (
    dsec_id INT AUTO_INCREMENT PRIMARY KEY,
    dokumen_id INT NOT NULL,
    dsec_index INT NOT NULL,
    dsec_type VARCHAR(20),           -- nextPage, continuous, etc
    dsec_page_width_twips INT,
    dsec_page_height_twips INT,
    dsec_orientation VARCHAR(10),     -- portrait, landscape
    dsec_margin_top_twips INT,
    dsec_margin_bottom_twips INT,
    dsec_margin_left_twips INT,
    dsec_margin_right_twips INT,
    dsec_header_margin_twips INT,
    dsec_footer_margin_twips INT,
    dsec_gutter_twips INT,
    dsec_gutter_position VARCHAR(10), -- left, top
    dsec_page_num_format VARCHAR(20),
    dsec_page_num_start INT,
    dsec_has_title_page BOOLEAN,
    dsec_different_odd_even BOOLEAN,
    dsec_column_count INT DEFAULT 1
);
```

#### 4.10.1.2 Tabel dokumen_elemen
```sql
CREATE TABLE dokumen_elemen (
    delemen_id BIGINT AUTO_INCREMENT PRIMARY KEY,
    dpart_id INT NOT NULL,            -- FK to dokumen_part
    delemen_sequence INT NOT NULL,
    delemen_type VARCHAR(50),          -- paragraph, h1, table, image, etc
    delemen_json_tree JSON
);
```

#### 4.10.1.3 Tabel dokumen_format_paragraf
```sql
CREATE TABLE dokumen_format_paragraf (
    dfp_id INT AUTO_INCREMENT PRIMARY KEY,
    dfp_jc VARCHAR(20),               -- left, center, right, both
    dfp_indent_left INT,              -- twips
    dfp_indent_right INT,
    dfp_indent_first_line INT,
    dfp_indent_hanging INT,
    dfp_space_before INT,             -- twips
    dfp_space_after INT,
    dfp_line INT,                     -- line spacing value
    dfp_line_rule VARCHAR(10),        -- auto, atLeast, exact
    dfp_keep_next BOOLEAN,
    dfp_keep_lines BOOLEAN,
    dfp_page_break_before BOOLEAN,
    dfp_shading JSON,
    dfp_tabs JSON,
    dfp_borders JSON
);
```

#### 4.10.1.4 Tabel dokumen_format_text
```sql
CREATE TABLE dokumen_format_text (
    dft_id INT AUTO_INCREMENT PRIMARY KEY,
    dft_font VARCHAR(100),
    dft_size_half_points INT,
    dft_bold BOOLEAN,
    dft_italic BOOLEAN,
    dft_underline BOOLEAN,
    dft_underline_style VARCHAR(20),
    dft_strike BOOLEAN,
    dft_color VARCHAR(20),
    dft_highlight VARCHAR(20),
    dft_valign VARCHAR(20)           -- superscript, subscript
);
```

### 4.10.2 Format JSON untuk Konten Elemen
#### 4.10.2.1 Paragraf Sederhana
```json
{"text":"Ini adalah paragraf biasa.","dfp_id":123}
```

#### 4.10.2.2 Paragraf dengan Multiple Runs
```json
{
  "content": [
    {"type":"text","value":"Normal text ","dft_id":1},
    {"type":"text","value":"bold text","dft_id":2},
    {"type":"text","value":" normal again.","dft_id":1}
  ],
  "dfp_id": 123
}
```

#### 4.10.2.3 List Item
```json
{
  "text": "First list item",
  "numbering_text": "1.",
  "dfp_id": 124
}
```

#### 4.10.2.4 Image
```json
{
  "rId": "rId5",
  "dfd_id": 45
}
```

#### 4.10.2.5 Table
```json
{
  "dft_id": 10,
  "content": {
    "rows": [
      {
        "cells": [
          {
            "content": [
              {"type":"paragraph","text":"Cell content","dfp_id":125}
            ]
          }
        ]
      }
    ]
  }
}
```

### 4.10.3 Sequence dan Ordering
#### 4.10.3.1 Elemen Sequence
```csharp
int sequence = 0;
foreach (var element in body.Elements())
{
    var items = ConvertBodyElementToItems(element);
    foreach (var item in items)
    {
        item.Sequence = sequence++;
        // Save to database
    }
}
```
- Global sequence untuk ordering
- Increment untuk setiap item

#### 4.10.3.2 Section Index
- Each section gets incrementing index
- Elements reference section via Part

#### 4.10.3.3 Part Type
```csharp
enum PartType { Body, Header, Footer }
```
- Track apakah content dari body, header, atau footer
- Different extraction untuk each

#### 4.10.3.4 Nested Content
- Tables contain cells with sequence per-cell
- Shapes contain textbox items dengan internal sequence

### 4.10.4 Optimisasi Penyimpanan
#### 4.10.4.1 Batch Insert
```csharp
var elements = new List<DokumenElemen>();
foreach (var bodyElement in body.Elements())
{
    var converted = ConvertElement(bodyElement);
    elements.AddRange(converted);
}
_db.DokumenElemens.AddRange(elements);
await _db.SaveChangesAsync(); // Single batch
```

#### 4.10.4.2 Format Deduplication
```csharp
// Check if identical format already exists
var existing = await _db.DokumenFormatTexts
    .FirstOrDefaultAsync(f => 
        f.Font == format.Font &&
        f.SizeHalfPoints == format.SizeHalfPoints &&
        f.Bold == format.Bold);

if (existing != null)
    return existing.DftId; // Reuse existing
```

#### 4.10.4.3 Significant Formatting Check
```csharp
bool HasSignificantFormatting(EffectiveRunProperties props)
{
    // Only save if different from defaults
    return props.Bold == true ||
           props.Italic == true ||
           props.Underline == true ||
           props.FontSize != defaultFontSize ||
           props.FontAscii != defaultFont;
}
```

#### 4.10.4.4 JSON Compact Format
```csharp
var json = JsonConvert.SerializeObject(content, Formatting.None);
// No whitespace, minimal size
```

---

## Ringkasan

Proses ekstraksi OpenXML mengubah dokumen Microsoft Word menjadi representasi data terstruktur melalui tahapan sistematis:

1. **Pembukaan Dokumen** → WordprocessingDocument, MainDocumentPart, dan Part pendukung
2. **Ekstraksi Section** → Pengaturan halaman (ukuran, margin, orientation, gutter, page numbering)
3. **Ekstraksi Paragraf** → Konten teks, paragraph properties (alignment, indentation, spacing)
4. **Ekstraksi Run** → Font properties (family, size), text styling (bold, italic, underline)
5. **Ekstraksi Numbering** → List dengan counter management dan multi-level support
6. **Ekstraksi Tabel** → Struktur row/cell, merge cells, borders, dan nested content
7. **Ekstraksi Gambar** → Drawing properties, inline/anchor positioning, dan media file extraction
8. **Resolusi Style** → Full inheritance chain dari docDefaults hingga direct formatting
9. **Theme Resolution** → Font scheme mapping dari theme references
10. **Penyimpanan** → Database schema terstruktur dengan JSON format dan optimisasi

Data hasil ekstraksi disimpan dalam format yang terstruktur dan siap digunakan untuk proses selanjutnya dalam pipeline sistem pemrosesan dokumen.

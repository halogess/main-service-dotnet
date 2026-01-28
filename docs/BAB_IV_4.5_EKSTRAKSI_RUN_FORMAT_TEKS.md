# BAB IV - Subbab 4.5: Ekstraksi Run dan Format Teks

## Ringkasan Subbab
Subbab ini membahas secara teknis proses ekstraksi Run (w:r) dan format teks dari dokumen Word menggunakan OpenXML SDK. Run merupakan unit atomic formatting dalam paragraf dimana semua konten dalam satu run memiliki formatting yang seragam. Dalam implementasi `DocxExtractionService`, ekstraksi run melibatkan `TextFormatExtractor` yang menangani ekstraksi direct formatting maupun effective formatting melalui `StyleResolver`. Proses ini mencakup ekstraksi font properties (nama font, ukuran, theme font references), text styling (bold, italic, underline, strike), serta properti run lainnya seperti color, highlight, dan vertical alignment. Implementasi juga menangani kompleksitas font resolution untuk berbagai script (Latin, EastAsia, ComplexScript) dan tri-state handling untuk toggle properties. Hasil ekstraksi disimpan dalam model `DokumenFormatText` yang memungkinkan validasi detail format teks sesuai requirement tugas akhir.

---

## 4.5.1 Struktur Run Element

### 4.5.1.1 w:r Element sebagai Unit Formatting

Elemen w:r (Run) dalam WordprocessingML adalah unit terkecil yang memiliki formatting seragam. Dalam satu paragraf, perubahan formatting apapun (bold, font size, color, dll.) akan menciptakan run baru. Hal ini karena desain XML yang mengharuskan semua konten dalam satu run memiliki properties yang identik. Dalam implementasi, iterasi runs dilakukan melalui `paragraph.Elements<Run>()` yang mengembalikan sequence of Run objects. Setiap Run memiliki dua komponen utama: `RunProperties` (w:rPr) yang opsional berisi formatting, dan content elements seperti `Text`, `TabChar`, `Break`, dan `Drawing`. Pattern ini memungkinkan ekstraksi yang efficient dimana formatting hanya perlu di-resolve sekali per run, bukan per karakter.

```csharp
// ParagraphExtractor.cs - iterasi run dalam paragraf
foreach (var child in p.ChildElements)
{
    if (child is Run run)
    {
        var rPr = run.RunProperties;
        var texts = run.Elements<Text>();
        // Process run content with uniform formatting
    }
}
```

### 4.5.1.2 w:rPr (RunProperties) untuk Formatting

Elemen w:rPr (RunProperties) adalah container untuk semua formatting properties pada level run. Dalam OpenXML SDK, akses dilakukan melalui `run.RunProperties` yang mengembalikan null jika tidak ada explicit formatting. Properties yang umum diekstrak meliputi: `RunFonts` untuk font specification, `FontSize` untuk ukuran dalam half-points, `Bold`/`Italic` untuk emphasis, `Underline` untuk garis bawah, dan `Color` untuk warna teks. Kehadiran null untuk `RunProperties` tidak berarti run tidak memiliki formatting - sebaliknya, run tersebut sepenuhnya inherit formatting dari style hierarchy (docDefaults → paragraph style → character style). Dalam `TextFormatExtractor`, ini ditangani dengan dua method berbeda: `ExtractFormat()` untuk direct formatting only dan `ExtractEffectiveFormat()` untuk full inheritance resolution.

```csharp
// TextFormatExtractor.cs lines 16-26
public DokumenFormatText ExtractFormat(Run run)
{
    var format = new DokumenFormatText();
    var rPr = run.RunProperties;
    
    // Store raw XML for debugging
    if (rPr != null)
        format.DftxRawRprXml = rPr.OuterXml;
    
    if (rPr == null)
        return format; // No direct formatting
    
    // Extract direct properties...
}
```

### 4.5.1.3 Multiple Runs dalam Satu Paragraf

Satu paragraf dapat mengandung puluhan hingga ratusan run tergantung kompleksitas formatting. Setiap kali pengguna mengubah formatting (bold, font, color) di Microsoft Word, run baru dibuat. Dalam `ParagraphExtractor`, teknik run accumulation digunakan untuk efisiensi: runs dengan formatting identik digabungkan menjadi satu text element. Signature formatting dibuat dari properties kunci untuk mendeteksi perubahan. Method `FlushAccumulatedRun()` dipanggil ketika signature berubah, menyimpan accumulated text ke JSON dan database. Pattern ini secara signifikan mengurangi jumlah format records yang disimpan tanpa kehilangan informasi formatting. Untuk dokumen dengan banyak formatting changes, ini bisa mengurangi storage hingga 50-70%.

```csharp
// ParagraphExtractor.cs - Run accumulation pattern
string currentFormatSignature = "";
var accumulatedRunText = new StringBuilder();

void FlushAccumulatedRun()
{
    if (accumulatedRunText.Length == 0) return;
    
    var textContent = accumulatedRunText.ToString();
    accumulatedRunText.Clear();
    
    // Create text JSON item with format reference
    var textItem = new JObject { ["type"] = "text", ["value"] = textContent };
    if (currentDftxId.HasValue)
        textItem["dftx_id"] = currentDftxId.Value;
    
    regularItems.Add((textItem, itemIndex++));
}
```

### 4.5.1.4 Empty Runs dan Significance Check

Tidak semua runs perlu format recordnya disimpan ke database. Empty runs (tanpa content visible) atau runs dengan formatting yang tidak signifikan bisa di-skip untuk menghemat storage. Method `HasSignificantFormatting()` pada `TextFormatExtractor` lines 349-361 memeriksa apakah run memiliki formatting yang meaningful. Kriteria significance meliputi: keberadaan Bold, Italic, Underline, FontSize, RunFonts, atau RunStyle (character style reference). Runs yang hanya inherit default formatting tidak memerlukan record terpisah. Pattern ini terutama berguna untuk dokumen akademik dimana mayoritas teks menggunakan formatting standar; hanya heading, emphasis, dan special text yang memerlukan format records.

```csharp
// TextFormatExtractor.cs lines 349-361
public bool HasSignificantFormatting(Run run)
{
    var rPr = run.RunProperties;
    if (rPr == null) return false;
    
    // Check if any formatting properties exist
    return rPr.Bold != null ||
           rPr.Italic != null ||
           rPr.Underline != null ||
           rPr.FontSize != null ||
           rPr.RunFonts != null ||
           rPr.RunStyle != null; // Character style reference
}
```

---

## 4.5.2 Ekstraksi Font Properties

### 4.5.2.1 RunFonts (w:rFonts)

Elemen w:rFonts dalam WordprocessingML memiliki multiple atribut untuk mendukung multilingual documents. Atribut `ascii` untuk Latin characters (A-Z, 0-9, basic symbols), `hAnsi` untuk extended Latin (characters 128-255), `eastAsia` untuk CJK characters (Chinese, Japanese, Korean), dan `cs` (ComplexScript) untuk RTL scripts (Arabic, Hebrew). Dalam praktik, `ascii` dan `hAnsi` biasanya sama. Dalam `TextFormatExtractor` lines 28-33, font ASCII diekstrak dengan fallback: jika `fonts.Ascii` null, gunakan `fonts.HighAnsi`. Pattern ini memastikan nama font selalu ter-resolve untuk Latin text yang merupakan mayoritas konten tugas akhir berbahasa Indonesia.

```csharp
// TextFormatExtractor.cs lines 28-33
var fonts = rPr.RunFonts;
if (fonts?.Ascii?.Value != null)
    format.DftxFontAscii = fonts.Ascii.Value;
else if (fonts?.HighAnsi?.Value != null)
    format.DftxFontAscii = fonts.HighAnsi.Value;
```

### 4.5.2.2 Theme Font References

Alih-alih menyimpan nama font explicit, dokumen Word modern sering menggunakan theme font references yang mereferensikan theme definition. Atribut `asciiTheme`, `hAnsiTheme`, `eastAsiaTheme`, dan `csTheme` menyimpan nilai seperti "majorAscii", "minorHAnsi", dll. Nilai-nilai ini kemudian di-resolve ke nama font actual melalui theme1.xml. Dalam implementasi, `ThemeFontResolver` bertanggung jawab untuk resolution ini. Method `ResolveThemeFont(themeKey, languageTag)` mencari font mapping dalam theme, mempertimbangkan language-specific overrides untuk fonts tertentu. Untuk tugas akhir Indonesia, biasanya font theme default (Calibri untuk minor, Cambria untuk major) atau explicit font (Times New Roman) digunakan.

```csharp
// TextFormatExtractor.cs lines 262-275
private static string? ResolveFontValue(
    string? explicitFont,
    string? themeKey,
    ThemeFontResolver? themeFontResolver,
    string? languageTag)
{
    if (!string.IsNullOrWhiteSpace(explicitFont))
        return explicitFont;

    if (!string.IsNullOrWhiteSpace(themeKey))
        return themeFontResolver?.ResolveThemeFont(themeKey, languageTag);

    return null;
}
```

### 4.5.2.3 FontSize (w:sz) dalam Half-Points

Ukuran font dalam WordprocessingML disimpan sebagai half-points dalam string format. Nilai "24" berarti 12pt (24 / 2 = 12). Konversi diperlukan untuk mendapatkan ukuran dalam points yang familiar. Dalam `TextFormatExtractor` lines 36-41, parsing dilakukan dengan `ushort.TryParse()` untuk handle invalid values gracefully. Model database `DokumenFormatText` menyimpan nilai dalam half-points (`dftx_size_halfpt`) untuk menghindari precision loss. Konversi ke points dilakukan saat validasi. Untuk tugas akhir Indonesia, ukuran font 12pt (24 half-points) adalah standar untuk body text, dengan variasi untuk headings (14pt-16pt).

```csharp
// TextFormatExtractor.cs lines 35-41
// Font Size in half-points (w:sz/@w:val)
var fontSize = rPr.FontSize;
if (fontSize?.Val?.Value != null)
{
    if (ushort.TryParse(fontSize.Val.Value, out ushort size))
        format.DftxSizeHalfpt = size;
}
```

### 4.5.2.4 FontSizeComplexScript (w:szCs)

Elemen w:szCs menyimpan ukuran font untuk complex scripts (Arabic, Hebrew, Indic scripts) yang mungkin berbeda dari ukuran regular font. Ini karena beberapa aksara memerlukan ukuran lebih besar untuk keterbacaan yang setara. Dalam `StyleResolver.MergeRunProperties()` lines 594-596, kedua ukuran diekstrak ke `EffectiveRunProperties`. Method `ResolvePreferredFontSize()` pada `TextFormatExtractor` lines 205-218 memilih ukuran yang tepat berdasarkan script yang terdeteksi. Untuk dokumen tugas akhir bahasa Indonesia yang predominantly Latin script, `FontSize` (w:sz) yang digunakan, tetapi implementasi tetap menyimpan keduanya untuk completeness.

```csharp
// TextFormatExtractor.cs lines 205-218
public static int? ResolvePreferredFontSize(
    EffectiveRunProperties effective,
    Run run,
    ThemeFontLangResolver? themeFontLangResolver)
{
    var hint = effective.FontHint;
    var text = run.InnerText;
    var isComplexScript = IsComplexScriptForSize(hint, text);

    if (isComplexScript && effective.FontSizeCs.HasValue)
        return effective.FontSizeCs;

    return effective.FontSize ?? effective.FontSizeCs;
}
```

---

## 4.5.3 Ekstraksi Text Styling

### 4.5.3.1 Bold (w:b) dan BoldComplexScript (w:bCs)

Elemen w:b adalah toggle element dengan tri-state behavior yang memerlukan handling khusus. Kehadiran elemen tanpa attribute `val` berarti TRUE (bold aktif). Attribute `val="false"` atau `val="0"` secara eksplisit berarti FALSE (bold nonaktif). Null (tidak ada elemen w:b) berarti INHERIT dari style hierarchy. Dalam `TextFormatExtractor` lines 44-46, logic ini diimplementasikan dengan `bold.Val == null || bold.Val.Value`. Dalam `StyleResolver` lines 599-601, pattern yang sama digunakan dengan `bold.Val?.Value ?? true`. Untuk complex scripts, elemen w:bCs terpisah mengontrol bold independently. Model `DokumenFormatText` menyimpan nilai sebagai `bool?` dengan null menandakan inheritance.

```csharp
// TextFormatExtractor.cs lines 43-46
// Bold (w:b) - tri-state handling
var bold = rPr.Bold;
if (bold != null)
    format.DftxBold = bold.Val == null || bold.Val.Value; // no val = ON, val=false = OFF
```

### 4.5.3.2 Italic (w:i) dan ItalicComplexScript (w:iCs)

Elemen w:i mengikuti pattern toggle yang sama dengan bold. Italic penting untuk emphasis, foreign words, dan keterangan gambar dalam tugas akhir. Dalam implementasi, Italic dan Bold diproses identically karena keduanya toggle elements. Untuk complex scripts, w:iCs mengontrol italic secara terpisah. Dalam `StyleResolver.MergeRunProperties()` lines 604-606, italic diekstrak ke `EffectiveRunProperties.Italic`. Untuk validasi tugas akhir, penggunaan italic yang excessive bisa dideteksi dengan menghitung persentase teks italic dalam dokumen.

```csharp
// TextFormatExtractor.cs lines 48-51
// Italic (w:i) - tri-state handling
var italic = rPr.Italic;
if (italic != null)
    format.DftxItalic = italic.Val == null || italic.Val.Value;
```

### 4.5.3.3 Underline (w:u) dengan Style Variants

Elemen w:u berbeda dari toggle elements karena memiliki banyak style variants. Attribute `val` menentukan jenis underline: Single (default), Double, Dotted, Dash, Wave, DottedHeavy, DashedHeavy, DashLong, dan lainnya. Dalam `TextFormatExtractor` lines 54-74, nilai-nilai ini di-normalize ke kategori yang lebih sederhana untuk storage: "none", "single", "double", "dotted", "dash", "wavy". Pattern matching dengan `Contains()` digunakan untuk mengelompokkan variants (e.g., "DashedHeavy" → "dash"). Kehadiran w:u tanpa val berarti "single" underline. Nilai `UnderlineValues.None` secara eksplisit menandakan no underline.

```csharp
// TextFormatExtractor.cs lines 53-74
var underline = rPr.Underline;
if (underline != null)
{
    var ulVal = underline.Val?.Value;
    if (ulVal == null)
        format.DftxUnderline = "single"; // w:u default when val is omitted
    else if (ulVal == UnderlineValues.None)
        format.DftxUnderline = "none";
    else if (ulVal == UnderlineValues.Single)
        format.DftxUnderline = "single";
    else if (ulVal == UnderlineValues.Double)
        format.DftxUnderline = "double";
    else if (ulVal == UnderlineValues.Dotted || ulVal == UnderlineValues.DottedHeavy)
        format.DftxUnderline = "dotted";
    else if (ulVal == UnderlineValues.Dash || /* other dash variants */)
        format.DftxUnderline = "dash";
    else if (ulVal == UnderlineValues.Wave || /* other wave variants */)
        format.DftxUnderline = "wavy";
    else
        format.DftxUnderline = "single"; // default fallback
}
```

### 4.5.3.4 Strike (w:strike) dan DoubleStrike (w:dstrike)

Strikethrough text ditandai oleh elemen w:strike (single line) atau w:dstrike (double line). Keduanya adalah toggle elements dengan tri-state handling. Strikethrough umumnya digunakan dalam mode Track Changes untuk menandai deleted text, atau secara manual untuk menunjukkan koreksi. Dalam `StyleResolver.MergeRunProperties()` lines 614-620, kedua property diekstrak ke `EffectiveRunProperties.Strike` dan `EffectiveRunProperties.DoubleStrike`. Untuk validasi tugas akhir, keberadaan strikethrough text mungkin mengindikasikan dokumen yang belum selesai diedit atau masih dalam proses revisi.

```csharp
// StyleResolver.cs lines 614-620
var strike = rPr.GetFirstChild<Strike>();
if (strike != null)
    effective.Strike = strike.Val?.Value ?? true;

var dblStrike = rPr.GetFirstChild<DoubleStrike>();
if (dblStrike != null)
    effective.DoubleStrike = dblStrike.Val?.Value ?? true;
```

---

## 4.5.4 Ekstraksi Properti Run Lainnya

### 4.5.4.1 Color (w:color)

Elemen w:color menentukan warna teks dengan beberapa cara specification. Attribute `val` menyimpan hex RGB value (e.g., "FF0000" untuk merah) atau nilai special "auto" untuk automatic color. Attribute `themeColor` mereferensikan warna dari theme color scheme (e.g., "accent1", "text1"). Dalam `StyleResolver.MergeRunProperties()` lines 627-630, hanya hex value yang diekstrak ke `EffectiveRunProperties.Color`. Theme color resolution memerlukan ThemeColorResolver terpisah yang tidak diimplementasikan dalam scope saat ini. Untuk validasi tugas akhir, warna teks biasanya harus hitam ("000000" atau "auto") kecuali untuk hyperlinks atau special cases.

```csharp
// StyleResolver.cs lines 627-630
var color = rPr.GetFirstChild<Color>();
if (color?.Val?.Value != null)
    effective.Color = color.Val.Value;
```

### 4.5.4.2 Highlight (w:highlight)

Elemen w:highlight menentukan background highlighting dengan predefined color values dari `HighlightColorValues` enum. Nilai yang tersedia: Yellow, Green, Cyan, Magenta, Blue, Red, DarkBlue, DarkCyan, DarkGreen, DarkMagenta, DarkRed, DarkYellow, DarkGray, LightGray, Black, dan None. Dalam `StyleResolver.MergeRunProperties()` lines 632-635, nilai dikonversi ke lowercase string untuk storage. Highlighting berbeda dengan shading (background paragraf) - highlight adalah per-run property untuk menandai teks spesifik. Untuk validasi tugas akhir, highlighted text mungkin mengindikasikan catatan reviewer atau teks yang perlu perhatian.

```csharp
// StyleResolver.cs lines 632-635
var highlight = rPr.GetFirstChild<Highlight>();
if (highlight?.Val?.Value != null)
    effective.HighlightColor = highlight.Val.Value.ToString().ToLower();
```

### 4.5.4.3 VerticalTextAlignment (w:vertAlign)

Elemen w:vertAlign mengontrol posisi vertikal teks relatif terhadap baseline, dengan nilai dari `VerticalPositionValues` enum: Superscript, Subscript, dan Baseline. Superscript dan subscript penting untuk notasi matematika, referensi footnote, dan rumus kimia. Dalam `StyleResolver.MergeRunProperties()` lines 622-625, nilai dikonversi ke lowercase string. Alternative mechanism menggunakan property `Position` (w:position) yang menentukan offset dalam half-points. Untuk tugas akhir teknik, superscript/subscript sering digunakan untuk satuan (m², kg/m³) dan referensi.

```csharp
// StyleResolver.cs lines 622-625
var vertAlign = rPr.GetFirstChild<VerticalTextAlignment>();
if (vertAlign?.Val?.Value != null)
    effective.VerticalAlignment = vertAlign.Val.Value.ToString().ToLower();
```

### 4.5.4.4 Languages (w:lang)

Elemen w:lang menentukan language settings per script untuk spell-checking dan hyphenation. Attribute `val` untuk Latin script (e.g., "en-US", "id-ID"), `eastAsia` untuk CJK (e.g., "ja-JP"), dan `bidi` untuk RTL scripts (e.g., "ar-SA"). Dalam `StyleResolver`, languages diekstrak melalui method `ApplyLanguages()` ke `EffectiveRunProperties.LangLatin`, `LangEastAsia`, dan `LangBidi`. Language settings digunakan dalam font resolution untuk memilih font yang tepat berdasarkan script. Untuk dokumen tugas akhir Indonesia, language biasanya "id-ID" untuk Indonesian atau "en-US" untuk sections dalam bahasa Inggris.

```csharp
// StyleResolver.cs - ApplyLanguages method
private void ApplyLanguages(EffectiveRunProperties effective, Languages? lang)
{
    if (lang == null) return;
    
    if (lang.Val?.Value != null)
        effective.LangLatin = lang.Val.Value;
    if (lang.EastAsia?.Value != null)
        effective.LangEastAsia = lang.EastAsia.Value;
    if (lang.Bidi?.Value != null)
        effective.LangBidi = lang.Bidi.Value;
}
```

---

## 4.5.5 Style Inheritance untuk Run Properties

### 4.5.5.1 Inheritance Chain untuk RunProperties

Resolusi effective run properties mengikuti chain inheritance yang spesifik dengan urutan: docDefaults (rPrDefault) → default character style → paragraph style's rPr → character style (rStyle) → direct rPr. Setiap level dalam chain dapat override properties dari level sebelumnya. Dalam `StyleResolver.GetEffectiveRunProperties()` lines 281-356, chain ini ditraversal secara berurutan dengan merge di setiap langkah. Pattern merge adalah "last wins" - property yang ter-set di level lebih rendah (lebih spesifik) override property dari level lebih tinggi. Null values tidak override; hanya explicit values yang override.

```csharp
// StyleResolver.cs lines 281-356 (summary)
public EffectiveRunProperties GetEffectiveRunProperties(Run run, ParagraphProperties? paragraphProps = null)
{
    var effective = new EffectiveRunProperties();
    
    // 1. Start with docDefaults
    if (_docDefaultsRPr != null)
        MergeRunProperties(effective, _docDefaultsRPr, "docDefaults");

    // 2. Apply default character style (if any)
    // 3. Apply paragraph style's rPr (from basedOn chain)
    // 4. Apply character style (rStyle) from basedOn chain
    // 5. Apply direct formatting (highest priority)
    
    return effective;
}
```

### 4.5.5.2 DocDefaults sebagai Base

DocDefaults dari styles.xml menyediakan document-wide default formatting untuk runs dan paragraphs. Element `rPrDefault/rPr` berisi default run properties yang applied ke semua runs dalam dokumen. Dalam `StyleResolver` constructor lines 46-51, docDefaults di-cache untuk efficient access. Typical docDefaults mencakup default font (biasanya theme font "minorHAnsi"), default size (22 half-points = 11pt untuk Calibri default), dan language settings. Untuk tugas akhir yang menggunakan template dengan custom formatting, docDefaults mungkin sudah di-set ke Times New Roman 12pt.

```csharp
// StyleResolver.cs lines 46-51
var docDefaults = primaryStyles?.DocDefaults ?? fallbackStyles?.DocDefaults;
if (docDefaults != null)
{
    _docDefaultsRPr = docDefaults.RunPropertiesDefault?.RunPropertiesBaseStyle;
    _docDefaultsPPr = docDefaults.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
}
```

### 4.5.5.3 Character Style (rStyle) Resolution

Character styles (style dengan type="character") menyediakan reusable run formatting. Runs dapat mereferensikan character style via w:rStyle dalam rPr-nya. Dalam `StyleResolver.GetEffectiveRunProperties()` lines 330-347, character style chain di-resolve menggunakan `GetStyleChain()` yang menangani basedOn relationships. Setiap style dalam chain di-traverse dari oldest ancestor ke style itu sendiri, dengan merge di setiap langkah. Character styles sering digunakan untuk special formatting seperti "Emphasis" (italic), "Strong" (bold), atau custom styles seperti "CodeText" untuk code snippets.

```csharp
// StyleResolver.cs lines 330-347
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
```

### 4.5.5.4 Direct Formatting Override

Direct formatting pada run (w:rPr langsung dalam w:r) memiliki priority tertinggi dan override semua inherited properties. Dalam `StyleResolver.GetEffectiveRunProperties()` lines 349-353, direct formatting applied terakhir. Method `MergeRunProperties()` hanya override properties yang explicitly set; null properties tidak override. Dalam `TextFormatExtractor.ExtractEffectiveFormat()` lines 83-131, effective properties yang sudah ter-resolve dimapping ke model `DokumenFormatText`. Direct formatting penting untuk one-off formatting changes yang tidak memerlukan style definition.

```csharp
// StyleResolver.cs lines 349-353
// 5. Apply direct formatting (highest priority)
if (runProps != null)
{
    MergeRunProperties(effective, runProps, "direct");
}
```

---

## 4.5.6 Script Detection dan Font Resolution

### 4.5.6.1 Latin, EastAsia, dan ComplexScript Categories

WordprocessingML membagi text menjadi tiga script categories yang mempengaruhi font selection. Latin untuk Western alphabets dan basic symbols, EastAsia untuk Chinese/Japanese/Korean characters, dan ComplexScript untuk Right-to-Left scripts (Arabic, Hebrew) dan Indic scripts. Dalam `TextFormatExtractor`, enum `ScriptCategory` (lines 277-282) mendefinisikan kategori ini. Method `DetermineScript()` lines 229-260 menganalisis hint dari rPr, language settings, dan actual text content untuk menentukan script category. Determination ini crucial karena menentukan font mana yang digunakan dari RunFonts.

```csharp
// TextFormatExtractor.cs lines 277-282
private enum ScriptCategory
{
    Latin,
    EastAsia,
    ComplexScript
}
```

### 4.5.6.2 Font Hint dan Language-Based Detection

Detection script dimulai dengan checking font hint dari w:rFonts/@w:hint attribute. Nilai "eastAsia" explicity menandakan EastAsia, "cs" atau "bidi" menandakan ComplexScript. Jika hint tidak ada, language settings di-check: LangBidi untuk ComplexScript, LangEastAsia untuk EastAsia. Jika language juga tidak ada, actual text content dianalisis menggunakan Unicode ranges. Method `ContainsEastAsian()` lines 284-313 checks untuk CJK character ranges. Method `ContainsComplexScript()` lines 316-343 checks untuk Arabic, Hebrew, Indic ranges. Fallback terakhir adalah checking ThemeFontLangResolver settings.

```csharp
// TextFormatExtractor.cs lines 229-260
private static ScriptCategory DetermineScript(
    EffectiveRunProperties effective,
    string? hint,
    string? text,
    ThemeFontLangResolver? themeFontLangResolver)
{
    var normalizedHint = hint?.Trim().ToLowerInvariant();
    if (normalizedHint == "eastasia")
        return ScriptCategory.EastAsia;
    if (normalizedHint == "cs" || normalizedHint == "bidi")
        return ScriptCategory.ComplexScript;

    if (!string.IsNullOrWhiteSpace(effective.LangBidi))
        return ScriptCategory.ComplexScript;
    if (!string.IsNullOrWhiteSpace(effective.LangEastAsia))
        return ScriptCategory.EastAsia;

    if (!string.IsNullOrEmpty(text))
    {
        if (ContainsComplexScript(text))
            return ScriptCategory.ComplexScript;
        if (ContainsEastAsian(text))
            return ScriptCategory.EastAsia;
    }
    
    return ScriptCategory.Latin;
}
```

### 4.5.6.3 Font Selection per Script

Setelah script ditentukan, font yang tepat dipilih dari RunFonts berdasarkan script category. Method `PickLatin()` (lines 160-173), `PickEastAsia()` (lines 175-188), dan `PickComplexScript()` (lines 190-203) mengimplementasikan logic ini. Untuk Latin: Ascii → HighAnsi → EastAsia → ComplexScript (fallback chain). Untuk EastAsia: EastAsia → Ascii → HighAnsi → ComplexScript. Untuk ComplexScript: ComplexScript → Ascii → HighAnsi → EastAsia. Setiap pick method juga attempts theme font resolution jika explicit font tidak disponible.

```csharp
// TextFormatExtractor.cs lines 160-173
private static string? PickLatin(
    EffectiveRunProperties effective,
    ThemeFontLangResolver? themeFontLangResolver,
    ThemeFontResolver? themeFontResolver)
{
    var latinLang = effective.LangLatin ?? themeFontLangResolver?.LatinLang;
    var eastAsiaLang = effective.LangEastAsia ?? themeFontLangResolver?.EastAsiaLang;
    var bidiLang = effective.LangBidi ?? themeFontLangResolver?.BidiLang;

    return ResolveFontValue(effective.FontAscii, effective.FontAsciiTheme, themeFontResolver, latinLang)
        ?? ResolveFontValue(effective.FontHighAnsi, effective.FontHighAnsiTheme, themeFontResolver, latinLang)
        ?? ResolveFontValue(effective.FontEastAsia, effective.FontEastAsiaTheme, themeFontResolver, eastAsiaLang)
        ?? ResolveFontValue(effective.FontComplexScript, effective.FontComplexScriptTheme, themeFontResolver, bidiLang);
}
```

---

## 4.5.7 Penyimpanan Format Teks

### 4.5.7.1 Model DokumenFormatText

Model `DokumenFormatText` (mapped ke tabel `dokumen_format_text`) menyimpan text formatting properties yang diekstrak. Kolom `dftx_font_ascii` menyimpan resolved font name (max 128 chars). Kolom `dftx_size_halfpt` menyimpan ukuran dalam half-points sebagai ushort. Kolom boolean nullable `dftx_bold` dan `dftx_italic` menyimpan tri-state values. Kolom `dftx_underline` menyimpan normalized underline style (max 10 chars). Kolom `dftx_raw_rpr_xml` (longtext) menyimpan raw XML untuk debugging. Design menggunakan nullable types untuk membedakan "tidak diset" (null) dari "explicitly false" (false).

```csharp
// DokumenFormatText.cs lines 1-42
[Table("dokumen_format_text")]
public class DokumenFormatText
{
    [Key]
    [Column("dftx_id")]
    public uint DftxId { get; set; }

    [Column("dftx_font_ascii")]
    [MaxLength(128)]
    public string? DftxFontAscii { get; set; }

    [Column("dftx_size_halfpt")]
    public ushort? DftxSizeHalfpt { get; set; }

    [Column("dftx_bold")]
    public bool? DftxBold { get; set; }

    [Column("dftx_italic")]
    public bool? DftxItalic { get; set; }

    [Column("dftx_underline")]
    [MaxLength(10)]
    public string? DftxUnderline { get; set; }

    [Column("dftx_raw_rpr_xml", TypeName = "longtext")]
    public string? DftxRawRprXml { get; set; }
}
```

### 4.5.7.2 Proses Save dan ID Reference

Dalam `ParagraphExtractor`, format text diekstrak dan disimpan saat processing runs. Setelah mendeteksi run dengan content, `TextFormatExtractor.ExtractEffectiveFormat()` dipanggil. Record ditambahkan ke context dan SaveChanges dipanggil untuk mendapatkan generated `dftx_id`. ID ini kemudian di-include dalam JSON text element dengan key `dftx_id`, memungkinkan join antara `dokumen_elemen` dan `dokumen_format_text`. Pattern ini memastikan traceability antara content dan formatting untuk keperluan validasi detail.

### 4.5.7.3 Run Accumulation dan Format Signature

Untuk efisiensi storage, runs dengan formatting identik digabungkan. Format signature dibuat dari key properties: font, size, bold, italic, underline. Jika signature sama dengan run sebelumnya, text diappend ke accumulated buffer. Jika berbeda, accumulated text di-flush dengan format ID saat ini, kemudian format baru diekstrak dan disimpan. Pattern ini signifikan mengurangi jumlah format records: paragraf dengan 50 runs tapi hanya 3 format berbeda akan menghasilkan 3 records, bukan 50.

---

## Kesimpulan Subbab 4.5

Ekstraksi Run dan Format Teks merupakan komponen penting dalam sistem yang menentukan bagaimana formatting detail direpresentasikan untuk validasi. Implementasi dalam proyek ini mencakup:

1. **Struktur Run**: Pemahaman w:r sebagai unit formatting, w:rPr untuk properties, dan technique run accumulation untuk efisiensi.

2. **Font Properties**: Ekstraksi RunFonts dengan multiple script support (Latin, EastAsia, ComplexScript), theme font resolution, dan font size dalam half-points.

3. **Text Styling**: Tri-state handling untuk toggle elements (bold, italic, strike), multiple underline styles dengan normalization.

4. **Additional Properties**: Color, highlight, vertical alignment, dan language settings untuk completeness.

5. **Style Inheritance**: Full resolution chain dari docDefaults melalui style hierarchy ke direct formatting dengan `StyleResolver`.

6. **Script Detection**: Intelligent font selection berdasarkan font hint, language settings, dan actual text content analysis.

7. **Database Storage**: Model `DokumenFormatText` yang compact dengan nullable types untuk tri-state semantics.

Pemahaman detail tentang ekstraksi run dan format teks ini menjadi dasar untuk validasi formatting teks dalam dokumen tugas akhir, memungkinkan pemeriksaan yang akurat terhadap requirements seperti font Times New Roman 12pt, penggunaan bold/italic yang tepat, dan konsistensi formatting secara keseluruhan.

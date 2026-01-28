# BAB IV - Subbab 4.4: Ekstraksi Paragraf dan Konten Teks

## Ringkasan Subbab
Subbab ini membahas secara teknis proses ekstraksi paragraf dan konten teks dari dokumen Word menggunakan OpenXML SDK. Paragraf (w:p) merupakan unit struktural fundamental dalam dokumen Word yang menjadi container untuk teks, gambar, rumus matematika, dan elemen inline lainnya. Dalam implementasi `DocxExtractionService`, ekstraksi paragraf melibatkan beberapa komponen utama: `ParagraphExtractor` untuk mengekstrak konten paragraf menjadi array JSON, `ParagraphFormatExtractor` untuk mengekstrak format paragraf dengan resolusi style inheritance, dan berbagai extractor pendukung untuk elemen-elemen khusus. Proses ini mencakup deteksi tipe paragraf (heading, list item, title, atau paragraph biasa), ekstraksi konten dengan buffering teks yang efisien, serta penanganan elemen-elemen kompleks seperti fields, math equations, dan drawings. Hasil ekstraksi disimpan dalam format JSON terstruktur yang memungkinkan representasi konten dokumen secara akurat untuk kebutuhan validasi.

---

## 4.4.1 Struktur Paragraph Element

### 4.4.1.1 w:p Element sebagai Container Paragraf

Elemen w:p (paragraph) dalam WordprocessingML adalah container utama untuk konten teks dan elemen inline lainnya dalam dokumen Word. Setiap paragraf direpresentasikan sebagai satu entity independen yang memiliki properties formatting dan sekumpulan run elements sebagai konten. Dalam `DocxExtractionService`, iterasi paragraf dilakukan melalui body elements dengan pattern matching: `if (elem is Paragraph para)` yang memungkinkan akses langsung ke object strongly-typed `Paragraph`. Class `Paragraph` dalam OpenXML SDK menyediakan akses ke child elements termasuk `ParagraphProperties`, `Run`, `Drawing`, `Hyperlink`, dan berbagai elemen inline lainnya. Paragraf bisa kosong (empty paragraph) yang sering digunakan untuk spacing vertikal dalam dokumen; implementasi tetap mengekstrak paragraf kosong untuk menjaga konsistensi element count dengan dokumen asli.

```csharp
// DocxExtractionService.cs - iterasi body elements
foreach (var elem in body.Elements())
{
    if (elem is Paragraph para)
    {
        var paragraphType = _paragraphExtractor.DetectParagraphType(para);
        var content = _paragraphExtractor.ExtractParagraphContentSorted(para, numberingPart, numberingCounters);
        // ... save to database
    }
}
```

### 4.4.1.2 w:pPr (ParagraphProperties) untuk Formatting

Elemen w:pPr (ParagraphProperties) adalah child opsional dari w:p yang menyimpan seluruh informasi formatting paragraf termasuk alignment, indentation, spacing, dan style reference. Dalam implementasi, akses ke properties dilakukan dengan `para.ParagraphProperties` yang mengembalikan null jika paragraf tidak memiliki explicit properties. Properties yang umumnya diekstrak meliputi: `ParagraphStyleId` untuk referensi ke style definition, `NumberingProperties` untuk list/numbering, `Justification` untuk alignment horizontal, `Indentation` untuk margin paragraf, dan `SpacingBetweenLines` untuk jarak antar baris. Class `ParagraphFormatExtractor` bertanggung jawab untuk mengekstrak properties ini dengan resolusi style inheritance yang lengkap, memastikan bahwa effective properties yang dihasilkan sudah memperhitungkan nilai dari docDefaults, style chain, dan numbering level.

```csharp
// ParagraphExtractor.cs line 112
var pPr = p.ParagraphProperties;

// Check for style ID
var styleId = pPr?.ParagraphStyleId?.Val?.Value;

// Check for numbering
var numPr = pPr?.NumberingProperties;
```

### 4.4.1.3 w:r (Run) Elements untuk Konten

Elemen w:r (Run) dalam paragraf merupakan unit terkecil yang memiliki formatting seragam. Satu paragraf dapat mengandung multiple runs dimana setiap run memiliki formatting berbeda seperti bold, italic, font size, atau warna yang berbeda. Dalam `ParagraphExtractor.ExtractParagraphContentSorted()`, runs diproses secara berurutan dengan teknik accumulation: runs dengan formatting identik digabungkan menjadi satu text element untuk efisiensi. Format signature dibuat dari properties kunci (font, size, bold, italic, underline) untuk mendeteksi perubahan formatting. Ketika format signature berubah, accumulated text di-flush dan run baru dimulai. Pattern ini menghasilkan output yang lebih ringkas sambil mempertahankan informasi formatting yang akurat.

```csharp
// ParagraphExtractor.cs lines 232-280 - Run accumulation
string currentFormatSignature = "";
EffectiveRunProperties? currentEffectiveProps = null;
Run? currentRun = null;
var accumulatedRunText = new System.Text.StringBuilder();

void FlushAccumulatedRun()
{
    if (accumulatedRunText.Length == 0) return;
    
    var textContent = accumulatedRunText.ToString();
    accumulatedRunText.Clear();
    
    // Save format record to database and create JSON item
    // ...
}
```

### 4.4.1.4 Urutan Children Elements

Urutan children elements dalam paragraf sangat penting dan harus dipertahankan untuk merekonstruksi konten secara akurat. Menurut skema WordprocessingML, `ParagraphProperties` jika ada selalu menjadi child pertama, diikuti oleh elemen-elemen konten seperti `Run`, `Hyperlink`, `Drawing`, `OfficeMath`, `SimpleField`, dan lainnya dalam urutan kemunculannya dalam dokumen. Dalam `ParagraphExtractor`, iterasi menggunakan `p.ChildElements` yang mengembalikan children dalam document order. Setiap item hasil ekstraksi diberi `itemIndex` yang terus di-increment untuk menjaga urutan. Pada akhir proses, items diurutkan berdasarkan `originalIndex` untuk memastikan output JSON mempertahankan urutan yang sama dengan dokumen sumber. Anchored elements (floating images) diperlakukan khusus dengan sorting berdasarkan posisi Y setelah regular items.

```csharp
// ParagraphExtractor.cs lines 742-764
foreach (var child in p.ChildElements)
    ProcessElement(child);

// ... flush remaining content

var result = new JArray();

// Regular items maintain original order
foreach (var (item, _) in regularItems.OrderBy(x => x.originalIndex))
{
    // Remove internal _sortY property
    if (item is JObject jObj && jObj["_sortY"] != null)
        jObj.Remove("_sortY");
    result.Add(item);
}

// Anchored items sorted by Y position
foreach (var (item, sortY, _) in anchoredItems.OrderBy(x => x.sortY))
{
    item.Remove("_sortY");
    result.Add(item);
}
```

---

## 4.4.2 Ekstraksi Paragraph Properties

### 4.4.2.1 Justification (w:jc)

Justification (w:jc) menentukan alignment horizontal teks dalam paragraf dengan nilai yang mungkin: Left (rata kiri), Center (rata tengah), Right (rata kanan), Both (justify/rata kiri-kanan), dan Distribute (stretched across line). Dalam `ParagraphFormatExtractor`, ekstraksi dilakukan melalui `pPr?.Justification?.Val?.Value` yang mengembalikan `JustificationValues` enum. Method helper `ConvertJustification()` pada line 392-405 mengkonversi enum ke string lowercase untuk konsistensi database. Default value adalah "left" jika tidak dispesifikasikan. Untuk tugas akhir, justification "both" (justify) umumnya merupakan requirement untuk body text, sedangkan headings mungkin menggunakan "left" atau "center". Nilai yang dihasilkan disimpan dalam kolom `dfp_jc` pada tabel `dokumen_format_paragraf`.

```csharp
// ParagraphFormatExtractor.cs lines 388-405
private static string ConvertJustification(JustificationValues value)
{
    if (value == JustificationValues.Left) return "left";
    if (value == JustificationValues.Start) return "start";
    if (value == JustificationValues.Right) return "right";
    if (value == JustificationValues.End) return "end";
    if (value == JustificationValues.Center) return "center";
    if (value == JustificationValues.Both) return "both";
    if (value == JustificationValues.Distribute) return "distribute";
    
    return "left"; // Default fallback
}
```

### 4.4.2.2 Indentation (w:ind)

Indentation dalam WordprocessingML mencakup beberapa atribut berbeda yang mengontrol margin paragraf. Atribut `left` dan `right` menentukan indentation dari margin halaman dalam twips. Atribut `firstLine` dan `hanging` mengontrol indentasi baris pertama: `firstLine` untuk positive first-line indent (menjorok ke dalam) dan `hanging` untuk negative indent (baris pertama lebih ke kiri dari baris lainnya). Kedua atribut ini mutually exclusive dalam praktik. Dalam `ParagraphFormatExtractor`, semua atribut indentation diekstrak ke kolom terpisah: `dfp_ind_left_twips`, `dfp_ind_right_twips`, `dfp_ind_first_line_twips`, dan `dfp_ind_hanging_twips`. Fallback default 0 diterapkan jika nilai tidak ada. Untuk dokumen tugas akhir, indentasi baris pertama (first line indent) adalah requirement umum untuk paragraf body text.

```csharp
// ParagraphFormatExtractor.cs lines 216-236
var ind = pPr.Indentation;
if (ind != null)
{
    if (ind.Left?.HasValue == true)
        format.DfpIndLeftTwips = ParseUint(ind.Left.Value?.ToString() ?? "");
    if (ind.Right?.HasValue == true)
        format.DfpIndRightTwips = ParseUint(ind.Right.Value?.ToString() ?? "");
    if (ind.FirstLine?.HasValue == true)
        format.DfpIndFirstLineTwips = ParseUint(ind.FirstLine.Value?.ToString() ?? "");
    if (ind.Hanging?.HasValue == true)
        format.DfpIndHangingTwips = ParseUint(ind.Hanging.Value?.ToString() ?? "");
    // Start, End, LeftChars, RightChars also extracted...
}
```

### 4.4.2.3 SpacingBetweenLines (w:spacing)

Spacing paragraf dikontrol oleh elemen w:spacing yang memiliki beberapa atribut penting. Atribut `before` dan `after` menentukan spacing sebelum dan sesudah paragraf dalam twips. Atribut `line` menentukan line spacing dengan interpretasi yang bergantung pada `lineRule`. Tiga nilai `lineRule` yang umum: `Auto` (nilai line adalah persentase, 240 = single, 480 = double), `AtLeast` (minimum spacing dalam twips), dan `Exact` (exact spacing dalam twips). Dalam `ParagraphFormatExtractor`, semua atribut spacing diekstrak dengan konversi lineRule menjadi string lowercase ("auto", "atLeast", "exact"). Default line spacing single (240 twips dengan auto rule) diterapkan jika tidak ada nilai eksplisit. Untuk tugas akhir, line spacing 1.5 atau 2 biasanya merupakan requirement, yang setara dengan nilai line=360 atau 480 dengan lineRule=auto.

```csharp
// ParagraphFormatExtractor.cs lines 196-214
var spacing = pPr.SpacingBetweenLines;
if (spacing != null)
{
    if (spacing.Before?.HasValue == true)
        format.DfpSpacingBeforeTwips = ParseUint(spacing.Before.Value?.ToString() ?? "");
    if (spacing.After?.HasValue == true)
        format.DfpSpacingAfterTwips = ParseUint(spacing.After.Value?.ToString() ?? "");
    if (spacing.Line?.HasValue == true)
        format.DfpSpacingLineTwips = ParseUint(spacing.Line.Value?.ToString() ?? "");
    if (spacing.LineRule?.HasValue == true)
        format.DfpSpacingLineRule = ConvertLineRuleValue(spacing.LineRule.Value);
    // AutoSpacing properties also extracted...
}
```

### 4.4.2.4 KeepNext, KeepLines, PageBreakBefore

Properties pagination mengontrol bagaimana paragraf diperlakukan saat page break terjadi. `KeepNext` (w:keepNext) memastikan paragraf tetap di halaman yang sama dengan paragraf berikutnya - berguna untuk heading yang tidak boleh terpisah dari kontennya. `KeepLines` (w:keepLines) mencegah paragraf terpecah di antara dua halaman. `PageBreakBefore` (w:pageBreakBefore) memaksa page break sebelum paragraf dimulai. Ketiga properties ini adalah toggle elements dengan logika yang spesifik: kehadiran elemen tanpa attribute val berarti true, sementara `val="false"` atau `val="0"` berarti false. Method helper `IsToggleOn()` pada line 256-261 mengimplementasikan logika ini. Ketiga nilai disimpan sebagai boolean di kolom `dfp_keep_next`, `dfp_keep_lines`, dan `dfp_page_break_before`.

```csharp
// ParagraphFormatExtractor.cs lines 182-188
format.DfpKeepNext = IsToggleOn(pPr.KeepNext);
format.DfpKeepLines = IsToggleOn(pPr.KeepLines);
format.DfpPageBreakBefore = IsToggleOn(pPr.PageBreakBefore);

// Helper method (lines 256-261)
private static bool IsToggleOn(OnOffType? toggle)
{
    if (toggle == null) return false;
    if (toggle.Val == null) return true; // Presence without val means true
    return toggle.Val.Value;
}
```

---

## 4.4.3 Ekstraksi Konten Paragraf

### 4.4.3.1 Text (w:t) Element

Elemen w:t (Text) adalah container untuk plain text content dalam run. Akses ke teks dilakukan melalui property `Text` pada object `Text`. Attribute `xml:space="preserve"` yang direpresentasikan oleh `Space` property menentukan apakah whitespace harus dipertahankan - penting untuk teks yang memiliki spasi leading/trailing yang signifikan. Dalam `ParagraphExtractor.ProcessElement()`, ketika menemukan elemen `Text`, konten ditambahkan ke run text buffer yang akan di-flush saat format berubah atau elemen non-text ditemukan. Special handling diperlukan untuk context field: jika teks berada dalam field (fieldStack.Count > 0), teks diappend ke field result bukan ke regular text buffer.

```csharp
// ParagraphExtractor.cs lines 462-469
if (child is Text t)
{
    if (fieldStack.Count > 0)
        AppendFieldResult(t.Text, run);
    else
        runText.Append(t.Text);
    continue;
}
```

### 4.4.3.2 TabChar (w:tab) untuk Tab Characters

Elemen w:tab merepresentasikan tab character dalam dokumen yang dalam Word mengarahkan kursor ke tab stop berikutnya. Dalam ekstraksi, tab dikonversi menjadi karakter tab literal (`\t`) untuk mempertahankan struktur dokumen. Processing dilakukan sama seperti text: jika dalam context field, tab diappend ke field result, otherwise ke run text buffer. Tab stops yang aktual (posisi dan leader characters) didefinisikan di paragraph properties atau style, yang diekstrak terpisah dalam `DfpTabsJson`. Untuk kebutuhan validasi, kehadiran tab characters dalam dokumen bisa mengindikasikan formatting yang perlu diperiksa, misalnya penggunaan tab untuk alignment yang mungkin tidak konsisten.

```csharp
// ParagraphExtractor.cs lines 471-478
if (child is TabChar)
{
    if (fieldStack.Count > 0)
        AppendFieldResult("\t", run);
    else
        runText.Append('\t');
    continue;
}
```

### 4.4.3.3 Break (w:br) untuk Line/Page Breaks

Elemen w:br merepresentasikan berbagai jenis break dalam dokumen. Attribute `Type` menentukan jenis break: nilai default (null) atau `TextWrapping` berarti line break (soft return), `Page` berarti page break, dan `Column` berarti column break untuk layout multi-kolom. Dalam ekstraksi, line break dikonversi ke newline character (`\n`). Page break dan column break juga dikonversi ke newline dalam implementasi saat ini karena fokus pada konten teks. Break dalam context field diperlakukan sama dengan text, diappend ke field result. Untuk validasi tugas akhir, kehadiran page break eksplisit bisa mengindikasikan formatting yang perlu diperiksa versus section break yang lebih semantik.

```csharp
// ParagraphExtractor.cs lines 480-487
if (child is Break)
{
    if (fieldStack.Count > 0)
        AppendFieldResult("\n", run);
    else
        runText.Append('\n');
    continue;
}
```

### 4.4.3.4 SymbolChar dan Special Characters

Elemen w:sym (SymbolChar) merepresentasikan karakter spesial dari font symbol seperti Wingdings, Symbol, atau Webdings. Atribut `font` menentukan nama font dan `char` berisi hex code dari karakter. Dalam implementasi saat ini, symbol characters di-handle melalui recursive descent ke child elements. Karakter dari font symbol memerlukan mapping khusus untuk rendering yang akurat karena codepoint dalam font-font ini tidak sesuai dengan Unicode standar. Untuk ekstraksi konten tugas akhir, penggunaan symbol characters relatif jarang, biasanya terbatas pada bullet points khusus atau simbol matematika tertentu yang tidak tersedia dalam font standar.

---

## 4.4.4 Deteksi Tipe Paragraf

### 4.4.4.1 Heading Detection via ParagraphStyleId

Deteksi heading dilakukan dengan memeriksa style ID paragraf terhadap pattern heading standar Word. Dalam `ParagraphExtractor.DetectParagraphType()`, style ID diekstrak dari `pPr?.ParagraphStyleId?.Val?.Value` dan dikonversi ke lowercase untuk comparison case-insensitive. Pattern `styleId.StartsWith("heading")` mendeteksi semua heading styles (Heading1-Heading9). Level heading diekstrak dengan menghapus prefix "heading" dari style ID, menghasilkan type string seperti "h1", "h2", dst. hingga "h9". Mapping ini mempertahankan hierarki heading yang penting untuk validasi struktur dokumen tugas akhir yang umumnya mengharuskan struktur heading yang konsisten (misalnya BAB sebagai h1, Subbab sebagai h2, dst.).

```csharp
// ParagraphExtractor.cs lines 117-129
if (pPr.ParagraphStyleId?.Val?.Value != null)
{
    var styleId = pPr.ParagraphStyleId.Val.Value.ToLower();
    if (styleId.StartsWith("heading"))
    {
        var level = styleId.Replace("heading", "");
        return $"h{level}";
    }
    if (styleId.StartsWith("title"))
        return "title";
    if (styleId.StartsWith("subtitle"))
        return "subtitle";
}
```

### 4.4.4.2 List Item Detection via NumberingProperties

Paragraf yang merupakan bagian dari list/numbering dideteksi melalui keberadaan `NumberingProperties` dalam paragraph properties. NumberingProperties memiliki dua komponen utama: `NumberingId` (w:numId) yang mereferensikan numbering instance dan `NumberingLevelReference` (w:ilvl) yang menentukan level dalam hierarki list (0-based). Dalam `ParagraphExtractor`, type string diformat sebagai `list-item-{numId}-{ilvl}` yang unik untuk setiap kombinasi. Format ini memungkinkan grouping list items yang termasuk dalam list yang sama. Nilai numId = 0 adalah special case yang menandakan numbering disabled (paragraf yang secara eksplisit tidak ter-number). Resolusi numbering juga mempertimbangkan style chain jika `_styleResolver` tersedia.

```csharp
// ParagraphExtractor.cs lines 131-136
if (pPr.NumberingProperties?.NumberingId?.Val?.Value != null)
{
    var numId = pPr.NumberingProperties.NumberingId.Val.Value;
    var ilvl = pPr.NumberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
    return $"list-item-{numId}-{ilvl}";
}
```

### 4.4.4.3 Title dan Subtitle Styles

Style "Title" dan "Subtitle" adalah built-in styles Word yang sering digunakan untuk judul utama dokumen dan sub-judul. Dalam deteksi tipe, style ID dicek dengan `StartsWith()` untuk menangani variasi seperti "Title", "TitleCustom", dll. Return value adalah literal string "title" atau "subtitle" yang memberikan semantic meaning yang jelas. Untuk dokumen tugas akhir, style Title mungkin digunakan untuk halaman judul dokumen. Deteksi ini memungkinkan validasi khusus untuk elemen-elemen title seperti pemeriksaan formatting yang sesuai dengan pedoman institusi.

```csharp
// ParagraphExtractor.cs lines 125-128
if (styleId.StartsWith("title"))
    return "title";
if (styleId.StartsWith("subtitle"))
    return "subtitle";
```

### 4.4.4.4 Default Paragraph Type

Jika paragraf tidak match dengan heading, list, title, atau subtitle, tipe default "paragraph" digunakan. Ini adalah fallback yang mencakup sebagian besar konten body text dokumen. Dalam implementasi, check dilakukan secara berurutan: heading first (karena memiliki struktur semantic yang penting), kemudian title/subtitle (semantic khusus), kemudian list item (structural), dan akhirnya default paragraph. Return "paragraph" juga terjadi jika `pPr` null (paragraf tanpa properties) yang mengindikasikan paragraf dengan formatting default sepenuhnya. Type string ini disimpan di kolom `dokumen_elemen_type` untuk setiap elemen.

```csharp
// ParagraphExtractor.cs lines 110-139 (complete method)
public string DetectParagraphType(Paragraph p)
{
    var pPr = p.ParagraphProperties;
    
    if (pPr == null)
        return "paragraph";

    // Style-based detection
    if (pPr.ParagraphStyleId?.Val?.Value != null)
    {
        var styleId = pPr.ParagraphStyleId.Val.Value.ToLower();
        if (styleId.StartsWith("heading"))
        {
            var level = styleId.Replace("heading", "");
            return $"h{level}";
        }
        if (styleId.StartsWith("title"))
            return "title";
        if (styleId.StartsWith("subtitle"))
            return "subtitle";
    }

    // Numbering-based detection
    if (pPr.NumberingProperties?.NumberingId?.Val?.Value != null)
    {
        var numId = pPr.NumberingProperties.NumberingId.Val.Value;
        var ilvl = pPr.NumberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
        return $"list-item-{numId}-{ilvl}";
    }

    return "paragraph";
}
```

---

## 4.4.5 Ekstraksi Numbering Text

### 4.4.5.1 Resolusi NumberingProperties dari Direct dan Style

Numbering properties dapat berasal dari direct formatting pada paragraf atau dari style yang applied. Dalam `ParagraphExtractor.ExtractParagraphContentSorted()` lines 165-216, resolusi dilakukan dengan dua tahap. Pertama, check direct numbering: `p.ParagraphProperties?.NumberingProperties`. Jika numId = 0, numbering explicitly disabled. Kedua, jika tidak ada direct numbering dan `_styleResolver` tersedia, numbering di-resolve dari style chain menggunakan `_styleResolver.GetEffectiveNumberingProperties(p)`. Pattern ini memastikan bahwa paragraf yang menggunakan list style (seperti "List Bullet" atau "List Number") tetap terdeteksi sebagai list item meskipun tidak memiliki direct NumberingProperties.

```csharp
// ParagraphExtractor.cs lines 172-201
int? numId = null;
int ilvl = 0;
string source = "none";

var directNumPr = p.ParagraphProperties?.NumberingProperties;
if (directNumPr?.NumberingId?.Val != null)
{
    int directNumId = directNumPr.NumberingId.Val.Value;
    if (directNumId == 0)
    {
        numId = null;
        source = "disabled";
    }
    else
    {
        numId = directNumId;
        ilvl = directNumPr.NumberingLevelReference?.Val?.Value ?? 0;
        source = "direct";
    }
}
else if (_styleResolver != null)
{
    var (styleNumId, styleIlvl) = _styleResolver.GetEffectiveNumberingProperties(p);
    if (styleNumId != null && styleNumId.Value != 0)
    {
        numId = styleNumId;
        ilvl = styleIlvl;
        source = "style";
    }
}
```

### 4.4.5.2 Delegasi ke NumberingExtractor

Setelah numId dan ilvl ter-resolve, generate numbering text didelegasikan ke `NumberingExtractor.GetNumberingText()`. Method ini menerima numberingPart, numId, ilvl, dan dictionary numberingCounters. NumberingExtractor melakukan beberapa langkah: lookup AbstractNum dari numId, dapatkan level definition, increment counter untuk level tersebut (dengan reset level di bawahnya), dan generate text sesuai format pattern (misalnya "1.", "a)", "i."). Hasil numbering text ditambahkan sebagai text element pertama dalam konten paragraf. Logging dilakukan untuk troubleshooting dengan mencatat source (direct/style), numId, ilvl, dan label yang dihasilkan.

```csharp
// ParagraphExtractor.cs lines 203-219
if (numId != null && numId.Value > 0)
{
    string label = NumberingExtractor.GetNumberingText(
        numberingPart, numId.Value, ilvl, numberingCounters);
    if (!string.IsNullOrEmpty(label))
        numberingText = label;
    _logger.LogInformation(
        "Numbering: source={Source}, numId={NumId}, ilvl={Ilvl}, label={Label}", 
        source, numId, ilvl, label);
}

if (!string.IsNullOrEmpty(numberingText))
    regularItems.Add((new JObject { ["type"] = "text", ["value"] = numberingText }, itemIndex++));
```

---

## 4.4.6 Style Inheritance Resolution

### 4.4.6.1 Konsep Inheritance Chain

Inheritance dalam WordprocessingML mengikuti chain resolusi yang spesifik untuk paragraph properties: docDefaults → Normal style → basedOn chain → direct pPr. DocDefaults (dari styles.xml) adalah base formatting untuk semua paragraf. Normal style adalah implicit base untuk paragraf tanpa explicit style. BasedOn chain adalah inheritance dari parent style(s). Direct pPr adalah formatting eksplisit pada paragraf yang override semua. Dalam `ParagraphFormatExtractor`, chain ini di-resolve melalui `_styleResolver.GetEffectiveParagraphPropertiesWithNumbering()` yang juga memperhitungkan w:lvl/w:pPr dari numbering definition untuk list paragraphs.

### 4.4.6.2 Penggunaan StyleResolver

`StyleResolver` diinisialisasi di `DocxExtractionService` dengan stylesPart dan themeResolver sebagai dependencies. Instance ini kemudian di-inject ke `ParagraphExtractor` dan `ParagraphFormatExtractor`. Method `GetEffectiveParagraphProperties()` melakukan traversal basedOn chain dan merge properties dari setiap level. Method `GetEffectiveParagraphPropertiesWithNumbering()` menambahkan integrasi dengan numbering level pPr yang penting karena list formatting sering didefinisikan di numbering.xml bukan styles.xml. Hasil adalah `EffectiveParagraphProperties` object yang berisi semua resolved values.

```csharp
// ParagraphFormatExtractor.cs lines 65-72
if (_styleResolver != null)
{
    var effective = _styleResolver.GetEffectiveParagraphPropertiesWithNumbering(
        paragraph, _numberingPart, numId, ilvl);
    
    // Map effective properties to model
    MapEffectivePropertiesToFormat(format, effective);
}
```

### 4.4.6.3 Mapping EffectiveProperties ke Model

Method `MapEffectivePropertiesToFormat()` pada lines 126-173 memetakan object `EffectiveParagraphProperties` ke model `DokumenFormatParagraf`. Mapping dilakukan untuk semua category properties: alignment (jc, textAlignment), indentation (6 atribut), spacing (8 atribut), pagination toggles (6 atribut), layout toggles (6 atribut), dan outline level. Setiap property di-map dengan null-coalescing yang appropriate untuk konversi type. Model database menggunakan nullable types untuk membedakan antara "tidak ada nilai" dan "nilai default". Pattern ini memastikan representasi data yang akurat sekaligus memungkinkan query yang efisien untuk validasi.

---

## 4.4.7 Penyimpanan Paragraph Format

### 4.4.7.1 Model DokumenFormatParagraf

Model `DokumenFormatParagraf` adalah entity database yang menyimpan semua extracted paragraph formatting. Kolom-kolom utama meliputi: style reference (`dfp_pstyle_id`), list info (`dfp_is_list`, `dfp_list_num_id`, `dfp_list_ilvl`), alignment (`dfp_jc`, `dfp_text_alignment`), indentation (8 kolom untuk various indent types), spacing (8 kolom), dan pagination/layout toggles (12 kolom boolean). JSON columns digunakan untuk complex nested structures: `dfp_numpr_json`, `dfp_pbdr_json` (borders), `dfp_shd_json` (shading), `dfp_tabs_json` (tab stops), dan lainnya. Raw XML juga disimpan di `dfp_raw_ppr_xml` untuk debugging dan future reference.

### 4.4.7.2 Proses Save dan ID Assignment

Dalam `DocxExtractionService`, paragraph format diekstrak dan disimpan sebelum element content. Pada line 252-255, setelah mendeteksi bahwa element adalah paragraph, `ParagraphFormatExtractor.ExtractFormat()` dipanggil. Record ditambahkan ke context dan SaveChanges dipanggil untuk mendapatkan generated ID (`dfpId`). ID ini kemudian di-include dalam JSON element dengan key `dfp_id`, memungkinkan join antara `dokumen_elemen` dan `dokumen_format_paragraf` untuk mendapatkan formatting detail. Pattern synchronous save (bukan batch) diperlukan karena ID harus tersedia segera untuk JSON construction.

### 4.4.7.3 Fallback Defaults

Method `ApplyFallbackDefaults()` pada lines 347-386 menerapkan nilai default WordprocessingML ketika explicit values tidak ada. Defaults yang diterapkan meliputi: justification default "left", text alignment default "auto", spacings default 0, line spacing default 240 twips (single) dengan rule "auto", dan indentations default 0. Penerapan defaults ini memastikan bahwa kolom-kolom non-nullable terisi dan memungkinkan comparison yang konsisten dalam validasi. Defaults dipilih berdasarkan spesifikasi ISO/IEC 29500 yang mendefinisikan behavior Word ketika properties tidak dispesifikasikan.

```csharp
// ParagraphFormatExtractor.cs lines 347-386
private static void ApplyFallbackDefaults(DokumenFormatParagraf format)
{
    if (string.IsNullOrWhiteSpace(format.DfpJc))
        format.DfpJc = "left";
    if (string.IsNullOrWhiteSpace(format.DfpTextAlignment))
        format.DfpTextAlignment = "auto";

    // Spacing defaults
    if (!format.DfpSpacingBeforeTwips.HasValue && !format.DfpSpacingBeforeAutospacing)
        format.DfpSpacingBeforeTwips = 0;
    // ... more defaults

    // Line spacing default: single (240 twips)
    if (string.IsNullOrWhiteSpace(format.DfpSpacingLineRule))
        format.DfpSpacingLineRule = "auto";
    if (!format.DfpSpacingLineTwips.HasValue)
        format.DfpSpacingLineTwips = 240; // Single line
}
```

---

## 4.4.8 Output JSON

### 4.4.8.1 Struktur Content Array

Output ekstraksi paragraf adalah JArray yang berisi sequence of content items dalam urutan kemunculannya. Setiap item adalah JObject dengan minimal property `type` yang menentukan jenis konten. Supporting properties bergantung pada type: text memiliki `value`, image memiliki `rId`, math memiliki `text`, field memiliki `field_type` dan `value`. Format ID jika ada ditambahkan setelah type: `dftx_id` untuk text, `dfdr_id` untuk drawing, `dffd_id` untuk field. Array ini diserialisasi dan disimpan di kolom `dokumen_elemen_json_tree`.

```json
{
  "content": [
    {"type": "text", "dftx_id": 123, "value": "This is normal text "},
    {"type": "text", "dftx_id": 124, "value": "and this is bold text."},
    {"type": "image", "dfdr_id": 45, "rId": "rId5"},
    {"type": "field", "field_type": "PAGE", "dffd_id": 78, "result_dftx_id": 79, "value": "12"}
  ]
}
```

### 4.4.8.2 Paragraph Level JSON

Di level paragraph (bukan content level), JSON object mengandung format reference dan content array. Property `dfp_id` mereferensikan `dokumen_format_paragraf` record yang menyimpan paragraph formatting. Property `content` berisi array seperti dijelaskan di atas. Tipe paragraf ("paragraph", "h1", "list-item-1-0", dll.) disimpan di kolom `dokumen_elemen_type`, bukan di JSON. Pemisahan ini memungkinkan querying efisien berdasarkan type tanpa parse JSON.

```json
{
  "dfp_id": 456,
  "content": [
    {"type": "text", "value": "This is the paragraph content"}
  ]
}
```

### 4.4.8.3 Empty Paragraph Handling

Paragraf kosong (tanpa run atau content visible) tetap diekstrak dan disimpan untuk menjaga konsistensi element count dengan dokumen asli. Dalam `ParagraphExtractor.FlattenParagraph()` line 149-150, selalu ada yield return bahkan jika content array kosong. Paragraf kosong direpresentasikan dengan `content: []`. Preservasi paragraf kosong penting karena mereka memiliki semantic meaning dalam dokumen Word - sering digunakan untuk vertical spacing atau sebagai placeholder. Untuk validasi, keberadaan banyak empty paragraphs berurutan bisa mengindikasikan formatting yang perlu diperbaiki (seharusnya menggunakan paragraph spacing).

```csharp
// ParagraphExtractor.cs lines 141-151
public IEnumerable<(string type, JObject json)> FlattenParagraph(
    Paragraph p,
    NumberingDefinitionsPart? numberingPart = null,
    Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
{
    var paragraphType = DetectParagraphType(p);
    var content = ExtractParagraphContentSorted(p, numberingPart, numberingCounters);

    // Always yield a result, even for empty paragraphs
    yield return (paragraphType, new JObject { ["content"] = content });
}
```

---

## Kesimpulan Subbab 4.4

Ekstraksi paragraf dan konten teks merupakan inti dari proses parsing dokumen Word yang menentukan bagaimana konten textual direpresentasikan dalam database. Dalam implementasi sistem validasi tugas akhir, proses ini melibatkan beberapa komponen dan konsep kunci:

1. **Struktur Paragraph**: Pemahaman tentang hierarki w:p → w:pPr → w:r → w:t memungkinkan ekstraksi yang akurat. Urutan elements dipertahankan untuk rekonstruksi konten yang faithful.

2. **Paragraph Properties**: Justification, indentation, spacing, dan pagination controls diekstrak dengan detail menggunakan `ParagraphFormatExtractor`. Style inheritance di-resolve melalui `StyleResolver` untuk mendapatkan effective properties.

3. **Content Extraction**: Text, tabs, dan breaks diekstrak dengan buffering yang efisien. Runs dengan formatting identik digabung untuk output yang ringkas.

4. **Type Detection**: Heading detection (h1-h9), list item detection (list-item-{numId}-{ilvl}), dan default paragraph type memberikan semantic classification yang berguna untuk validasi.

5. **Numbering Integration**: Numbering text di-generate dengan mempertimbangkan direct dan style-derived numbering properties, dengan counter management yang proper untuk ordered lists.

6. **Database Persistence**: Model `DokumenFormatParagraf` menyimpan comprehensive formatting information dengan fallback defaults sesuai spesifikasi WordprocessingML.

Pemahaman detail tentang ekstraksi paragraf ini menjadi dasar untuk memahami subbab-subbab selanjutnya yang membahas ekstraksi Run dan Format Teks (4.5), Numbering dan List (4.6), serta elemen-elemen inline kompleks lainnya.

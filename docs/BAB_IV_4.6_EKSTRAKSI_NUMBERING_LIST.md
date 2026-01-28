# BAB IV - Subbab 4.6: Ekstraksi Numbering dan List

## Ringkasan Subbab
Subbab ini membahas secara teknis proses ekstraksi numbering (penomoran) dan list dari dokumen Word menggunakan OpenXML SDK. Numbering dalam WordprocessingML memiliki arsitektur kompleks yang terpisah dari konten paragraf: definisi template disimpan dalam AbstractNum, instance penggunaan dalam NumberingInstance, dan referensi dari paragraf melalui NumberingProperties. Dalam implementasi `DocxExtractionService`, ekstraksi numbering melibatkan dua komponen utama: `NumberingExtractor` yang bertanggung jawab untuk generate numbering text dengan counter management, dan `NumberingResolver` yang menangani resolusi level dan property merging. Proses ini mencakup lookup level definitions, penanganan LevelOverride, format conversion (decimal, roman, letter, bullet), multi-level numbering assembly, dan normalisasi bullet characters dari font symbol. Hasil ekstraksi numbering text ditambahkan ke konten paragraf untuk merepresentasikan dokumen secara akurat.

---

## 4.6.1 Struktur NumberingDefinitionsPart

### 4.6.1.1 AbstractNum: Template Numbering

AbstractNum adalah template reusable yang mendefinisikan karakteristik numbering untuk semua level (0-8). Setiap AbstractNum memiliki `AbstractNumberId` yang unik untuk identification dan diakses melalui `numberingPart.Numbering.Elements<AbstractNum>()`. Template ini berisi sembilan Level elements yang masing-masing mendefinisikan format, start value, dan text pattern untuk level tertentu. Desain abstraksi ini memungkinkan satu template digunakan oleh multiple list instances dalam dokumen dengan customization per-instance jika diperlukan. Dalam `NumberingExtractor.GetNumberingText()` lines 29-31, AbstractNum di-lookup berdasarkan abstractNumId yang direferensikan oleh NumberingInstance.

```csharp
// NumberingExtractor.cs lines 24-31
var numInstance = numbering.Elements<NumberingInstance>()
    .FirstOrDefault(n => n.NumberID?.Value == numId);
if (numInstance == null) return "?";

int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
var abstractNum = numbering.Elements<AbstractNum>()
    .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
if (abstractNum == null) return "?";
```

### 4.6.1.2 NumberingInstance: Penggunaan Template

NumberingInstance merepresentasikan instance aktual dari numbering yang digunakan dalam dokumen. Setiap instance memiliki `NumberID` yang menjadi referensi dari paragraf melalui `w:numId` dalam NumberingProperties. Property `AbstractNumId` menunjukkan template AbstractNum mana yang digunakan. Multiple NumberingInstances dapat mereferensikan AbstractNum yang sama, memungkinkan list-list berbeda share format yang sama dengan counter independen. Dalam implementasi, counter di-manage per numId (bukan per abstractNumId) untuk menjaga independensi setiap list instance sebagaimana disebutkan di comment line 13: "Counters are keyed by numId to keep each numbering instance independent."

```csharp
// NumberingExtractor.cs lines 36-40
if (!counters.TryGetValue(numId, out var numCounters))
{
    numCounters = new Dictionary<int, int>();
    counters[numId] = numCounters;
}
```

### 4.6.1.3 Level (w:lvl) Definitions

Setiap AbstractNum memiliki sembilan Level elements (ilvl 0-8) yang mendefinisikan formatting untuk setiap level hierarki. Akses level dilakukan dengan `abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == ilvl)`. Properties utama dalam Level meliputi: `StartNumberingValue` untuk nilai awal counter, `NumberingFormat` untuk jenis penomoran (decimal, roman, letter, bullet), `LevelText` untuk pattern text ("%1.", "%1.%2", dll.), `LevelSuffix` untuk separator setelah nomor (tab, space, nothing), dan `LevelRestart` untuk mengontrol reset counter. Dalam `NumberingResolver.GetNumberingLevel()` lines 15-36, level di-resolve dengan mempertimbangkan LevelOverride terlebih dahulu sebelum fallback ke AbstractNum.

```csharp
// NumberingResolver.cs lines 15-36
public static Level? GetNumberingLevel(NumberingDefinitionsPart numberingPart, int numId, int ilvl)
{
    var numInstance = numberingPart.Numbering?.Elements<NumberingInstance>()
        .FirstOrDefault(n => n.NumberID?.Value == numId);
    if (numInstance == null) return null;
    
    int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
    if (abstractNumId < 0) return null;
    
    // Check for level override first
    var levelOverride = numInstance.Elements<LevelOverride>()
        .FirstOrDefault(lo => lo.LevelIndex?.Value == ilvl);
    if (levelOverride?.Level != null)
        return levelOverride.Level;
    
    // Fall back to abstract numbering definition
    var abstractNum = numberingPart.Numbering?.Elements<AbstractNum>()
        .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
    
    return abstractNum?.Elements<Level>()
        .FirstOrDefault(l => l.LevelIndex?.Value == ilvl);
}
```

### 4.6.1.4 LevelOverride untuk Customisasi

LevelOverride memungkinkan NumberingInstance meng-override definisi level dari template AbstractNum tanpa mengubah template itu sendiri. Override bisa berupa `StartOverrideNumberingValue` untuk mengganti start value saja, atau full `Level` element untuk mengganti seluruh definisi level. Dalam `NumberingExtractor.GetEffectiveLevel()` lines 117-136, level override dicek terlebih dahulu; jika ada full Level dalam override, itu yang digunakan. Jika hanya ada StartOverrideNumberingValue, level definition dari AbstractNum tetap digunakan tetapi start value di-override. Pattern ini memungkinkan restart numbering di tengah dokumen atau customization level tertentu tanpa membuat template baru.

```csharp
// NumberingExtractor.cs lines 117-136
private static Level? GetEffectiveLevel(
    NumberingInstance numInstance,
    AbstractNum abstractNum,
    int ilvl,
    out int? startOverride)
{
    startOverride = null;

    var levelOverride = numInstance.Elements<LevelOverride>()
        .FirstOrDefault(lo => lo.LevelIndex?.Value == ilvl);
    if (levelOverride != null)
    {
        startOverride = levelOverride.StartOverrideNumberingValue?.Val?.Value;
        if (levelOverride.Level != null)
            return levelOverride.Level;
    }

    return abstractNum.Elements<Level>()
        .FirstOrDefault(l => l.LevelIndex?.Value == ilvl);
}
```

---

## 4.6.2 Ekstraksi Level Properties

### 4.6.2.1 Start Value dan NumberFormat

Start value menentukan nilai awal counter untuk level tertentu dan diekstrak dari `level.StartNumberingValue?.Val?.Value` dengan default 1. NumberFormat menentukan bagaimana nilai counter ditampilkan dan diekstrak dari `level.NumberingFormat?.Val?.Value`. Dalam `NumberingExtractor` line 55, default format adalah Decimal jika tidak dispesifikasikan. Format umum yang didukung meliputi: Decimal (1, 2, 3), LowerLetter (a, b, c), UpperLetter (A, B, C), LowerRoman (i, ii, iii), UpperRoman (I, II, III), dan Bullet untuk list tidak bernomor. Method helper `GetLevelStart()` pada lines 138-141 menyediakan akses safe dengan null handling.

```csharp
// NumberingExtractor.cs lines 55-56, 138-141
var numFmt = level.NumberingFormat?.Val?.Value ?? NumberFormatValues.Decimal;
var lvlText = level.LevelText?.Val?.Value ?? $"%{ilvl + 1}";

private static int GetLevelStart(Level? level)
{
    return level?.StartNumberingValue?.Val?.Value ?? 1;
}
```

### 4.6.2.2 LevelText Pattern

LevelText mendefinisikan template string untuk numbering text dengan placeholder pattern. Placeholder `%1` digantikan dengan nilai counter level 0, `%2` dengan level 1, dst. Pattern seperti "%1." menghasilkan "1.", "2.", dst. Pattern multi-level seperti "%1.%2" menghasilkan "1.1", "1.2", "2.1", dst. yang umum untuk heading numbering. Untuk bullet, LevelText biasanya berisi karakter bullet seperti "•", "○", atau karakter dari font Symbol/Wingdings. Dalam `NumberingExtractor.ReplaceLevelText()` lines 172-218, pattern di-parse dan placeholder digantikan dengan nilai terformat melalui callback function.

```csharp
// NumberingExtractor.cs lines 172-218 (summary)
private static string ReplaceLevelText(string levelText, Func<int, string?> resolvePlaceholder)
{
    var sb = new System.Text.StringBuilder();
    for (int i = 0; i < levelText.Length; i++)
    {
        var ch = levelText[i];
        if (ch != '%' || i + 1 >= levelText.Length)
        {
            sb.Append(ch);
            continue;
        }
        
        // Parse %N pattern and replace with resolved value
        var next = levelText[i + 1];
        if (char.IsDigit(next))
        {
            // Extract number and resolve placeholder
            var replacement = resolvePlaceholder(number);
            if (replacement != null)
                sb.Append(replacement);
        }
    }
    return sb.ToString();
}
```

### 4.6.2.3 LevelSuffix

LevelSuffix menentukan karakter yang mengikuti numbering text: Tab (default), Space, atau Nothing. Dalam `NumberingExtractor.GetLevelSuffix()` lines 221-229, nilai dikonversi ke string literal: Tab menjadi `"\t"`, Space menjadi `" "`, dan Nothing menjadi string kosong. Suffix ditambahkan ke hasil akhir numbering text pada line 91: `return formatted + GetLevelSuffix(level)`. Untuk tugas akhir, tab suffix adalah yang paling umum karena menghasilkan alignment yang konsisten antara nomor dan teks. Suffix information ini mempengaruhi bagaimana numbering text ditampilkan dalam output JSON.

```csharp
// NumberingExtractor.cs lines 221-229
private static string GetLevelSuffix(Level level)
{
    var suffix = level.LevelSuffix?.Val?.Value ?? LevelSuffixValues.Tab;
    if (suffix == LevelSuffixValues.Nothing)
        return "";
    if (suffix == LevelSuffixValues.Space)
        return " ";
    return "\t";
}
```

### 4.6.2.4 IsLegalNumberingStyle

IsLegalNumberingStyle adalah flag yang mengontrol format multi-level numbering. Ketika aktif, semua sublevel placeholder ditampilkan sebagai decimal terlepas dari format level tersebut. Ini menghasilkan "legal style" numbering: 1, 1.1, 1.1.1, dst. alih-alih format mixed seperti 1, 1.a, 1.a.i. Dalam `NumberingExtractor.GetNumberingText()` lines 68-69, flag ini di-extract dengan default true jika element ada tanpa Val. Pada lines 84-86, ketika isLegal true, format Decimal digunakan untuk semua sublevel. Pattern ini umum untuk heading numbering dalam dokumen formal seperti tugas akhir yang menggunakan hierarki BAB, Subbab, dan Sub-subbab.

```csharp
// NumberingExtractor.cs lines 68-69, 84-86
var isLegal = level.IsLegalNumberingStyle != null &&
              (level.IsLegalNumberingStyle.Val?.Value ?? true);

var subFmt = isLegal
    ? NumberFormatValues.Decimal
    : (subLevel?.NumberingFormat?.Val?.Value ?? NumberFormatValues.Decimal);
```

---

## 4.6.3 Ekstraksi Numbering dari Paragraf

### 4.6.3.1 NumberingProperties dalam ParagraphProperties

Paragraf mereferensikan numbering melalui NumberingProperties (w:numPr) dalam ParagraphProperties. Elemen ini memiliki dua komponen utama: `NumberingId` (w:numId) yang mereferensikan NumberingInstance, dan `NumberingLevelReference` (w:ilvl) yang menentukan level hierarki (0-based). Nilai numId = 0 adalah special case yang berarti numbering explicitly disabled untuk paragraf tersebut. Dalam `ParagraphExtractor.ExtractParagraphContentSorted()`, numbering properties diextract dari direct pPr terlebih dahulu sebelum checking style chain. Kehadiran numPr menandakan paragraf adalah list item yang memerlukan generate numbering text.

```csharp
// ParagraphExtractor.cs (simplified)
var directNumPr = p.ParagraphProperties?.NumberingProperties;
if (directNumPr?.NumberingId?.Val != null)
{
    int directNumId = directNumPr.NumberingId.Val.Value;
    if (directNumId == 0)
    {
        // Numbering explicitly disabled
        numId = null;
    }
    else
    {
        numId = directNumId;
        ilvl = directNumPr.NumberingLevelReference?.Val?.Value ?? 0;
    }
}
```

### 4.6.3.2 Numbering via Style

List styles seperti "List Bullet" atau "List Number" memiliki numbering properties embedded dalam style definition. Paragraf yang menggunakan style tersebut menjadi list item tanpa memerlukan direct numPr. Dalam `StyleResolver.TryResolveNumberingFromStyleChain()`, style chain ditraversal untuk mencari numbering properties. Method `GetEffectiveNumberingProperties()` pada lines 409-433 mengimplementasikan full resolution: first check direct numPr, then style chain, then numbering definitions yang link ke paragraph style. Pattern ini memastikan paragraf dengan list style terdeteksi sebagai list item meskipun tidak memiliki explicit NumberingProperties.

```csharp
// StyleResolver.cs lines 409-433 (summary)
public (int? numId, int ilvl) GetEffectiveNumberingProperties(Paragraph p)
{
    // 1. Check direct numPr on paragraph
    var directNumPr = p.ParagraphProperties?.NumberingProperties;
    if (TryReadNumberingProperties(directNumPr, out var directNumId, out var directIlvl, out _))
        return (directNumId, directIlvl);

    // 2. Check style chain for numPr
    var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    if (TryResolveNumberingFromStyleChain(styleId, out var styleNumId, out var styleIlvl, out _))
        return (styleNumId, styleIlvl);

    // 3. Check numbering definitions that link to the paragraph style
    foreach (var candidateStyleId in EnumerateStyleIdCandidates(styleId))
    {
        if (TryResolveNumberingFromNumberingPart(candidateStyleId, out var linkedNumId, out var linkedIlvl))
            return (linkedNumId, linkedIlvl);
    }

    return (null, 0);
}
```

### 4.6.3.3 Resolusi Level via NumberingResolver

`NumberingResolver.GetNumberingLevel()` menyediakan single entry point untuk mendapatkan Level element dengan mempertimbangkan LevelOverride. Method ini menerima numberingPart, numId, dan ilvl, kemudian melakukan lookup: NumberingInstance → check LevelOverride → fallback ke AbstractNum. Return value adalah Level element atau null jika tidak ditemukan. Dalam `ParagraphExtractor`, level di-resolve untuk mendapatkan paragraph properties dari numbering yang kemudian di-merge ke effective properties. Dalam `NumberingExtractor`, level di-resolve untuk mendapatkan format dan text pattern untuk generate numbering text.

```csharp
// Usage in NumberingExtractor.cs line 33
var level = GetEffectiveLevel(numInstance, abstractNum, ilvl, out var startOverride);
if (level == null) return "?";
```

### 4.6.3.4 Abstract Numbering ID Lookup

Lookup abstractNumId diperlukan untuk mengakses template Level definitions. Dari numId, first lookup NumberingInstance untuk mendapatkan `AbstractNumId.Val.Value`. Kemudian AbstractNum dengan matching AbstractNumberId di-lookup. Pattern two-step lookup ini memisahkan instance dari template, memungkinkan reuse template dengan counter independen per instance. Dalam `NumberingExtractor` lines 24-31, kedua lookup dilakukan secara berurutan dengan early return jika tidak ditemukan. AbstractNumId juga digunakan untuk keying dalam beberapa scenarios, meskipun counter management menggunakan numId untuk independensi instance.

---

## 4.6.4 Generate Numbering Text

### 4.6.4.1 Counter Management per NumId

Counter untuk numbering di-manage menggunakan nested dictionary: `Dictionary<int, Dictionary<int, int>>` dimana outer key adalah numId dan inner key adalah ilvl. Desain ini memastikan setiap NumberingInstance memiliki counter independen, sehingga dua list berbeda tidak saling mempengaruhi. Dalam `NumberingExtractor.GetNumberingText()` lines 36-51, counter di-initialize dengan start value dari level definition (atau override) saat pertama kali level tersebut ditemui. Pada encounter berikutnya, counter di-increment. Method `ResetLowerLevels()` pada lines 143-161 menangani reset counter sublevel ketika parent level di-encounter.

```csharp
// NumberingExtractor.cs lines 36-51
if (!counters.TryGetValue(numId, out var numCounters))
{
    numCounters = new Dictionary<int, int>();
    counters[numId] = numCounters;
}

if (!numCounters.TryGetValue(ilvl, out var currentVal))
{
    currentVal = startOverride ?? GetLevelStart(level);
    numCounters[ilvl] = currentVal;
}
else
{
    currentVal++;
    numCounters[ilvl] = currentVal;
}

ResetLowerLevels(numCounters, numInstance, abstractNum, ilvl);
```

### 4.6.4.2 FormatNumber berdasarkan NumberFormat

Method `FormatNumber()` pada lines 97-115 mengkonversi nilai integer counter ke string representation sesuai format. Decimal menggunakan `ToString()` langsung. DecimalZero menggunakan format "D2" untuk padding (01, 02, dst.). LowerLetter dan UpperLetter menggunakan helper `GetLetter()` yang menghasilkan a-z, aa-az, dst. Roman (upper dan lower) menggunakan recursive `ToRoman()` method. Bullet format mengembalikan normalized bullet character. None format mengembalikan empty string. Default fallback ke Decimal untuk format yang tidak dikenali.

```csharp
// NumberingExtractor.cs lines 97-115
public static string FormatNumber(int value, NumberFormatValues format, string? levelText = null)
{
    if (format == NumberFormatValues.DecimalZero)
        return value.ToString("D2");
    if (format == NumberFormatValues.LowerLetter)
        return GetLetter(value, true);
    if (format == NumberFormatValues.UpperLetter)
        return GetLetter(value, false);
    if (format == NumberFormatValues.LowerRoman)
        return ToRoman(value).ToLowerInvariant();
    if (format == NumberFormatValues.UpperRoman)
        return ToRoman(value);
    if (format == NumberFormatValues.Bullet)
        return NormalizeBulletChar(levelText ?? string.Empty);
    if (format == NumberFormatValues.None)
        return string.Empty;

    return value.ToString();
}
```

### 4.6.4.3 Bullet Character Normalization

Bullet characters dari font Symbol atau Wingdings menggunakan Private Use Area (PUA) Unicode codepoints yang tidak displayable dengan font standar. Method `NormalizeBulletChar()` pada lines 234-291 mengkonversi karakter PUA ke Unicode standar yang equivalent. Mapping untuk Symbol font meliputi: `\uF0B7` → "•" (bullet), `\uF0FC` → "✓" (checkmark), `\uF0D8` → "◆" (diamond). Mapping untuk Wingdings meliputi: `\u006C` → "●" (circle), `\u006E` → "■" (square), `\u00FC` → "✓" (checkmark). Karakter yang sudah Unicode standar (•, ●, ■, dll.) di-pass through. Karakter PUA yang tidak di-map di-fallback ke bullet standar "•".

```csharp
// NumberingExtractor.cs lines 234-291 (summary)
public static string NormalizeBulletChar(string bulletChar)
{
    if (string.IsNullOrEmpty(bulletChar)) return "•";
    
    // Symbol font character mappings
    var symbolMappings = new Dictionary<char, char>
    {
        { '\uF0B7', '•' },  // Bullet
        { '\uF0A7', '•' },  // Bullet variant
        { '\uF0D8', '◆' },  // Diamond
        { '\uF0FC', '✓' },  // Check mark
        // ... more mappings
    };
    
    // Wingdings font character mappings
    var wingdingsMappings = new Dictionary<char, char>
    {
        { '\u006C', '●' },  // Circle
        { '\u006E', '■' },  // Square
        // ... more mappings
    };
    
    if (bulletChar.Length == 1)
    {
        char c = bulletChar[0];
        if (symbolMappings.TryGetValue(c, out char symbol))
            return symbol.ToString();
        if (wingdingsMappings.TryGetValue(c, out char wingding))
            return wingding.ToString();
            
        // Fallback for unmapped PUA characters
        if (c >= '\uE000' && c <= '\uF8FF')
            return "•";
    }
    
    return bulletChar;
}
```

### 4.6.4.4 Multi-Level Numbering Assembly

Untuk LevelText dengan multiple placeholders (e.g., "%1.%2.%3"), method `ReplaceLevelText()` memproses setiap placeholder secara berurutan. Callback function pada lines 73-88 menerima placeholder index (1-based), mengkonversi ke level index (0-based), mendapatkan counter value untuk level tersebut, dan format sesuai level's NumberFormat (atau Decimal jika isLegal). Hasil dari setiap placeholder digabungkan dengan static text dari pattern. Contoh: pattern "%1.%2" dengan counter level 0 = 2, level 1 = 3 menghasilkan "2.3". Counter values diambil dari numCounters dictionary, dengan fallback ke start value jika level belum pernah di-encounter.

```csharp
// NumberingExtractor.cs lines 71-89
var formatted = ReplaceLevelText(
    lvlText,
    placeholderIndex =>
    {
        int levelIndex = placeholderIndex - 1;
        if (levelIndex < 0)
            return null;

        var subLevel = GetEffectiveLevel(numInstance, abstractNum, levelIndex, out var subStartOverride);
        var levelValue = numCounters.TryGetValue(levelIndex, out var existing)
            ? existing
            : (subStartOverride ?? GetLevelStart(subLevel));

        var subFmt = isLegal
            ? NumberFormatValues.Decimal
            : (subLevel?.NumberingFormat?.Val?.Value ?? NumberFormatValues.Decimal);

        return FormatNumber(levelValue, subFmt, subLevel?.LevelText?.Val?.Value);
    });
```

---

## 4.6.5 Level Restart dan Counter Reset

### 4.6.5.1 LevelRestart Property

LevelRestart (w:lvlRestart) pada Level element mengontrol kapan counter untuk level tersebut di-reset. Nilai menunjukkan level parent yang trigger reset. Contoh: jika level 2 memiliki LevelRestart = 1, maka counter level 2 di-reset setiap kali level 1 (atau higher) ditemui. Default behavior (tanpa LevelRestart) adalah reset ketika immediate parent level ditemui. Dalam `NumberingExtractor.GetLevelRestart()` lines 163-170, property ini diekstrak dari effective level. Value null berarti default behavior, value 0 berarti selalu reset (restart after any higher level).

```csharp
// NumberingExtractor.cs lines 163-170
private static int? GetLevelRestart(
    NumberingInstance numInstance,
    AbstractNum abstractNum,
    int levelIndex)
{
    var level = GetEffectiveLevel(numInstance, abstractNum, levelIndex, out _);
    return level?.LevelRestart?.Val?.Value;
}
```

### 4.6.5.2 Reset Lower Levels Logic

Method `ResetLowerLevels()` pada lines 143-161 dipanggil setelah setiap increment counter untuk menangani reset sublevel. Untuk setiap level yang lebih tinggi dari current level dalam counters, check LevelRestart property. Jika LevelRestart exists dan nilainya > currentLevel, level tersebut tidak di-reset (karena restart point belum tercapai). Otherwise, counter untuk level tersebut di-remove dari dictionary, causing re-initialization ke start value pada encounter berikutnya. Pattern remove-from-dictionary alih-alih reset-to-start memastikan startOverride juga ter-apply saat level kembali aktif.

```csharp
// NumberingExtractor.cs lines 143-161
private static void ResetLowerLevels(
    Dictionary<int, int> numCounters,
    NumberingInstance numInstance,
    AbstractNum abstractNum,
    int currentLevel)
{
    var keys = numCounters.Keys.ToList();
    foreach (var levelIndex in keys)
    {
        if (levelIndex <= currentLevel)
            continue;

        var restart = GetLevelRestart(numInstance, abstractNum, levelIndex);
        if (restart.HasValue && restart.Value > currentLevel)
            continue;

        numCounters.Remove(levelIndex);
    }
}
```

---

## 4.6.6 Numbering Level Properties untuk Formatting

### 4.6.6.1 PreviousParagraphProperties (w:lvl/w:pPr)

Level element dapat memiliki `PreviousParagraphProperties` (w:pPr) yang mendefinisikan paragraph formatting untuk list items pada level tersebut. Properties yang umum di-define meliputi indentation (untuk hanging indent list), spacing, dan justification. Dalam `NumberingResolver.MergeNumberingLevelParagraphProperties()` lines 41-83, properties ini di-merge ke EffectiveParagraphProperties. Indentation dari level pPr meng-override style indentation secara keseluruhan (bukan merge per-property) karena list indentation harus konsisten dengan numbering position. Spacing dan justification di-merge dengan pattern normal.

```csharp
// NumberingResolver.cs lines 41-67
public static void MergeNumberingLevelParagraphProperties(
    EffectiveParagraphProperties effective, 
    PreviousParagraphProperties pPr)
{
    // Indentation - override style indentation entirely
    var ind = pPr.GetFirstChild<Indentation>();
    if (ind != null)
    {
        // Clear all indentation first
        effective.IndentLeft = null;
        effective.IndentRight = null;
        effective.IndentFirstLine = null;
        effective.IndentHanging = null;
        // ... more clearing

        // Then apply level indentation
        if (ind.Left?.Value != null) effective.IndentLeft = int.Parse(ind.Left.Value);
        if (ind.Hanging?.Value != null) effective.IndentHanging = int.Parse(ind.Hanging.Value);
        // ... more application
    }
}
```

### 4.6.6.2 NumberingSymbolRunProperties (w:lvl/w:rPr)

Level element juga dapat memiliki `NumberingSymbolRunProperties` (w:rPr) yang mendefinisikan formatting untuk numbering symbol itu sendiri (angka atau bullet), bukan teks paragraf. Properties umum meliputi font (untuk bullet dari font khusus), size, color, dan styling. Dalam `NumberingResolver.MergeNumberingLevelRunProperties()` lines 89-189, properties ini di-merge ke EffectiveRunProperties. Font handling mempertimbangkan both explicit fonts dan theme references. Ini memungkinkan bullet memiliki formatting berbeda dari body text, misalnya bullet merah atau nomor dengan font berbeda.

```csharp
// NumberingResolver.cs lines 89-189 (summary)
public static void MergeNumberingLevelRunProperties(
    EffectiveRunProperties effective, 
    NumberingSymbolRunProperties rPr, 
    string source)
{
    effective.ResolvedFromStyle = source;
    
    // Font handling with both explicit and theme references
    var fonts = rPr.GetFirstChild<RunFonts>();
    if (fonts != null)
    {
        var ascii = fonts.Ascii?.Value;
        if (!string.IsNullOrWhiteSpace(ascii))
            effective.FontAscii = ascii.Trim();
        // ... more font handling
    }
    
    // Other run properties
    var bold = rPr.GetFirstChild<Bold>();
    if (bold != null)
        effective.Bold = bold.Val?.Value ?? true;
    // ... more properties
}
```

---

## 4.6.7 Helper Methods untuk Format Conversion

### 4.6.7.1 GetLetter() untuk Alphabetic Numbering

Method `GetLetter()` pada lines 296-307 mengkonversi angka ke huruf alphabet. Untuk nilai 1-26, output adalah a-z (lowercase) atau A-Z (uppercase). Untuk nilai > 26, output menjadi multi-character: 27 = aa, 28 = ab, ..., 52 = az, 53 = ba, dst. Algorithm bekerja dengan iterative division by 26 dan prepending characters. Values ≤ 0 menghasilkan "?" sebagai error indicator. Pattern ini mendukung list dengan lebih dari 26 items, meskipun dalam praktik jarang diperlukan untuk tugas akhir.

```csharp
// NumberingExtractor.cs lines 296-307
public static string GetLetter(int val, bool lower)
{
    if (val <= 0) return "?";
    val--; 
    string s = "";
    do {
        s = (char)('A' + (val % 26)) + s;
        val /= 26;
        val--;
    } while (val >= 0);
    return lower ? s.ToLowerInvariant() : s;
}
```

### 4.6.7.2 ToRoman() untuk Roman Numerals

Method `ToRoman()` pada lines 312-328 mengkonversi angka ke Roman numerals menggunakan recursive algorithm. Setiap call menangani satu symbol (M, CM, D, CD, C, XC, L, XL, X, IX, V, IV, I) dari largest ke smallest dengan subtraction. Recursion continues dengan remainder hingga nilai menjadi 0. Contoh: 1994 → M (1000) + CM (900) + XC (90) + IV (4) = "MCMXCIV". Untuk lowercase roman (i, ii, iii), caller menggunakan `ToRoman(value).ToLowerInvariant()`. Values < 1 menghasilkan empty string.

```csharp
// NumberingExtractor.cs lines 312-328
public static string ToRoman(int number)
{
    if (number < 1) return string.Empty;
    if (number >= 1000) return "M" + ToRoman(number - 1000);
    if (number >= 900) return "CM" + ToRoman(number - 900);
    if (number >= 500) return "D" + ToRoman(number - 500);
    if (number >= 400) return "CD" + ToRoman(number - 400);
    if (number >= 100) return "C" + ToRoman(number - 100);
    if (number >= 90) return "XC" + ToRoman(number - 90);
    if (number >= 50) return "L" + ToRoman(number - 50);
    if (number >= 40) return "XL" + ToRoman(number - 40);
    if (number >= 10) return "X" + ToRoman(number - 10);
    if (number >= 9) return "IX" + ToRoman(number - 9);
    if (number >= 5) return "V" + ToRoman(number - 5);
    if (number >= 4) return "IV" + ToRoman(number - 4);
    return "I" + ToRoman(number - 1);
}
```

---

## 4.6.8 Integrasi dengan ParagraphExtractor

### 4.6.8.1 Deteksi List Item

Dalam `ParagraphExtractor.DetectParagraphType()`, paragraf dengan NumberingProperties diidentifikasi sebagai list item dengan type string format "list-item-{numId}-{ilvl}". Format ini uniquely identifies list membership dan level, memungkinkan grouping list items yang termasuk dalam list yang sama. Dalam `ExtractParagraphContentSorted()`, numbering properties di-resolve dari direct atau style, kemudian `NumberingExtractor.GetNumberingText()` dipanggil untuk generate numbering text. Text tersebut ditambahkan sebagai item pertama dalam content array.

### 4.6.8.2 Numbering Text sebagai Content Item

Hasil dari `NumberingExtractor.GetNumberingText()` berupa string seperti "1.", "a)", "•", atau "1.2.3" ditambahkan ke content array sebagai text element. Dalam JSON output, numbering text muncul sebelum actual paragraph text. Format: `{"type": "text", "value": "1.\t"}` dimana `\t` adalah suffix. Pattern ini memperlihatkan numbering sebagai bagian dari content yang memudahkan rendering dan validasi. Untuk validasi format, numbering text dapat di-check untuk memastikan konsistensi format dalam dokumen.

### 4.6.8.3 Counter Passthrough

Dictionary counters di-maintain di level `DocxExtractionService` dan di-pass ke setiap call `ExtractParagraphContentSorted()`. Ini memastikan counter state persists across paragraphs dalam dokumen. Counter di-initialize sebagai empty dictionary di awal ekstraksi, kemudian populated dan updated seiring dengan processing list items. Pattern stateless extraction dengan external state (counters) memungkinkan parallelization di masa depan jika diperlukan untuk dokumen sangat besar.

---

## Kesimpulan Subbab 4.6

Ekstraksi Numbering dan List merupakan komponen penting yang menangani struktur list dan penomoran dalam dokumen Word. Implementasi dalam proyek ini mencakup:

1. **Struktur NumberingDefinitionsPart**: Pemahaman AbstractNum sebagai template, NumberingInstance sebagai instance, dan Level sebagai definisi per-hierarki.

2. **Level Properties**: Ekstraksi StartValue, NumberFormat, LevelText pattern, LevelSuffix, dan IsLegalNumberingStyle.

3. **Numbering dari Paragraf**: Resolusi NumberingProperties dari direct pPr dan style chain, dengan full inheritance support.

4. **Generate Numbering Text**: Counter management per numId, format conversion untuk berbagai NumberFormat, multi-level assembly dengan placeholder replacement.

5. **Bullet Normalization**: Mapping karakter dari font Symbol/Wingdings ke Unicode standar untuk portability.

6. **Level Restart**: Handling LevelRestart property untuk reset counter behavior yang correct.

7. **Level Properties untuk Formatting**: Merge PreviousParagraphProperties dan NumberingSymbolRunProperties ke effective properties.

8. **Helper Methods**: Conversion ke letter (a, b, aa, ab) dan Roman numerals (I, II, III, IV) dengan comprehensive support.

Pemahaman detail tentang ekstraksi numbering ini menjadi dasar untuk merepresentasikan list dan heading numbering secara akurat dalam output JSON, memungkinkan validasi struktur dokumen tugas akhir yang menggunakan penomoran konsisten untuk BAB, Subbab, dan list items.

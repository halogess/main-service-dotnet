# BAB IV - Subbab 4.7: Ekstraksi Tabel

## Ringkasan Subbab
Subbab ini membahas secara teknis proses ekstraksi tabel dari dokumen Word menggunakan OpenXML SDK. Tabel dalam WordprocessingML memiliki struktur hierarkis yang kompleks: Table (w:tbl) → TableRow (w:tr) → TableCell (w:tc) → Paragraphs/Nested Content. Dalam implementasi `DocxExtractionService`, ekstraksi tabel melibatkan beberapa komponen utama: `TableExtractor` sebagai orchestrator utama, `TableFormatExtractor` untuk table-level properties, `TableRowFormatExtractor` untuk row properties, `TableCellFormatExtractor` untuk cell properties, `TableStyleResolver` untuk inheritance dan conditional formatting, serta `TablePropertyMerger` untuk property merging. Proses ini mencakup ekstraksi properties pada setiap level (table, row, cell), penanganan cell merging (GridSpan dan VerticalMerge), resolusi style dengan conditional formatting (firstRow, lastCol, banding), dan recursive extraction untuk nested content termasuk nested tables. Hasil ekstraksi disimpan dalam model database terpisah untuk setiap level, memungkinkan validasi detail terhadap format tabel dalam tugas akhir.

---

## 4.7.1 Struktur Table Element

### 4.7.1.1 w:tbl Element sebagai Container

Elemen w:tbl (Table) dalam WordprocessingML adalah container root untuk struktur tabel. Table berisi tiga jenis child elements utama: `TableProperties` (w:tblPr) untuk table-level formatting, `TableGrid` (w:tblGrid) untuk column definitions, dan sequence of `TableRow` (w:tr) untuk konten. Dalam `TableExtractor.ConvertTableToJsonAsync()` lines 41-77, table diproses secara hierarkis: pertama extract table format, kemudian iterate rows. Akses dilakukan melalui `table.GetFirstChild<TableProperties>()` untuk properties dan `table.Elements<TableRow>()` untuk rows. Struktur JSON output menggunakan `dft_id` untuk referensi table format dan `content.rows` untuk array row objects.

```csharp
// TableExtractor.cs lines 41-77
public async Task<JObject> ConvertTableToJsonAsync(
    Table table, 
    NumberingDefinitionsPart? numberingPart = null,
    Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
{
    var tableJson = new JObject();
    
    // Extract and save table format
    var tableFormat = _tableFormatExtractor.ExtractFormat(table);
    _db.DokumenFormatTables.Add(tableFormat);
    await _db.SaveChangesAsync();
    tableJson["dft_id"] = tableFormat.DftId;
    
    // Extract rows with format
    var rows = await ConvertTableRowsAsync(table, numberingPart, numberingCounters);
    tableJson["content"] = new JObject { ["rows"] = rows };
    
    return tableJson;
}
```

### 4.7.1.2 TableProperties (w:tblPr)

Elemen w:tblPr berisi table-level formatting properties yang berlaku untuk keseluruhan tabel. Properties utama meliputi: `TableStyle` (referensi ke table style), `TableWidth` untuk lebar tabel, `TableJustification` untuk horizontal alignment, `TableIndentation` untuk left indentation, `TableLayout` (fixed atau autofit), `TableBorders` untuk border styles, dan `TablePositionProperties` untuk floating tables. Dalam `TableFormatExtractor.ExtractFormat()` lines 25-108, properties ini diekstrak dengan mempertimbangkan style inheritance melalui `_styleResolver.ResolveEffectiveTableProperties()`. Raw XML disimpan untuk debugging sebelum effective properties diekstrak.

```csharp
// TableFormatExtractor.cs lines 25-42
public DokumenFormatTable ExtractFormat(Table table)
{
    var format = new DokumenFormatTable();
    
    // Get effective (resolved) table properties if resolver is available
    var effectiveTblPr = _styleResolver?.ResolveEffectiveTableProperties(table) 
                         ?? table.GetFirstChild<TableProperties>();
    
    // Store raw XML for debugging (original direct formatting)
    var directTblPr = table.GetFirstChild<TableProperties>();
    if (directTblPr != null)
        format.DftRawTblprXml = directTblPr.OuterXml;
    
    // Style ID (from original, not effective)
    format.DftTblStyleId = directTblPr?.TableStyle?.Val?.Value;
    
    // ... extract properties
}
```

### 4.7.1.3 TableGrid untuk Column Definitions

Elemen w:tblGrid mendefinisikan struktur kolom tabel melalui sequence of `GridColumn` elements. Setiap GridColumn memiliki atribut `Width` dalam twips yang menentukan lebar kolom. TableGrid digunakan untuk menghitung total kolom dan sebagai referensi untuk GridSpan pada cells. Dalam `TableExtractor.GetTableColumnCount()` lines 148-163, jumlah kolom dihitung dari first row dengan mempertimbangkan GridSpan setiap cell. Ini diperlukan untuk menentukan posisi kolom cells untuk conditional styling (firstCol, lastCol) dan untuk validasi struktur tabel.

```csharp
// TableExtractor.cs lines 148-163
private int GetTableColumnCount(Table table)
{
    var firstRow = table.Elements<TableRow>().FirstOrDefault();
    if (firstRow == null)
        return 0;
    
    int colCount = 0;
    foreach (var cell in firstRow.Elements<TableCell>())
    {
        var tcPr = cell.GetFirstChild<TableCellProperties>();
        var gridSpan = tcPr?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
        colCount += gridSpan;
    }
    
    return colCount;
}
```

### 4.7.1.4 w:tr (TableRow) dan w:tc (TableCell)

Struktur tabel menggunakan nested elements: TableRow (w:tr) berisi TableCell (w:tc), dan TableCell berisi content elements (Paragraph, nested Table). Dalam `TableExtractor.ConvertTableRowsAsync()` lines 80-143, iterasi dilakukan secara nested: for each row → for each cell → for each content element. Setiap level memiliki format extractor terpisah: `_rowFormatExtractor` untuk row dan `_cellFormatExtractor` untuk cell. Position context (rowIndex, colIndex, totalRows, totalCols) dipass ke extractors untuk conditional style resolution. JSON output menyimpan hierarchy dengan `dftr_id` untuk row format dan `dftc_id` untuk cell format.

```csharp
// TableExtractor.cs lines 80-143 (simplified)
private async Task<JArray> ConvertTableRowsAsync(Table table, ...)
{
    var rows = new JArray();
    var tableRows = table.Elements<TableRow>().ToList();
    int totalRows = tableRows.Count;
    int totalCols = GetTableColumnCount(table);
    
    for (int rowIndex = 0; rowIndex < tableRows.Count; rowIndex++)
    {
        var row = tableRows[rowIndex];
        var rowFormat = _rowFormatExtractor.ExtractFormat(row, rowIndex, totalRows, table);
        // ... save row format
        
        var cells = row.Elements<TableCell>().ToList();
        for (int colIndex = 0; colIndex < cells.Count; colIndex++)
        {
            var cell = cells[colIndex];
            var cellFormat = _cellFormatExtractor.ExtractFormat(
                cell, rowIndex, colIndex, totalRows, totalCols, table);
            // ... save cell format and extract content
        }
    }
    return rows;
}
```

---

## 4.7.2 Ekstraksi Table Properties

### 4.7.2.1 TableWidth dan TableWidthType

TableWidth (w:tblW) menentukan lebar tabel dengan dua komponen: Type dan Width. Type values dari `TableWidthUnitValues` enum: `Auto` (automatic sizing), `Dxa` (twips), `Pct` (percentage in 50ths, e.g., 5000 = 100%), dan `Nil` (no width). Width value adalah string yang perlu di-parse ke integer. Dalam `TableFormatExtractor` lines 45-63, type dikonversi ke lowercase string untuk database, dan width disimpan ke kolom yang sesuai berdasarkan type: `DftTblWTwips` untuk dxa, `DftTblWPct50` untuk pct. Untuk dokumen tugas akhir, biasanya table width = 100% atau auto.

```csharp
// TableFormatExtractor.cs lines 45-63
var tblW = effectiveTblPr.TableWidth;
if (tblW != null)
{
    if (tblW.Type?.HasValue == true)
        format.DftTblWType = ConvertTableWidthType(tblW.Type.Value);
    
    if (tblW.Width?.HasValue == true)
    {
        if (int.TryParse(tblW.Width.Value?.ToString() ?? "", out int widthValue))
        {
            // Store based on type
            if (format.DftTblWType == "pct")
                format.DftTblWPct50 = widthValue >= 0 ? (uint)widthValue : null;
            else if (format.DftTblWType == "dxa")
                format.DftTblWTwips = widthValue >= 0 ? (uint)widthValue : null;
        }
    }
}
```

### 4.7.2.2 TableJustification

TableJustification (w:jc) menentukan horizontal alignment tabel dalam page. Values dari `TableRowAlignmentValues`: Left, Center, Right. Dalam `TableFormatExtractor` lines 65-67, nilai dikonversi ke lowercase string. Catatan: meskipun enum bernama "TableRowAlignment", ini sebenarnya berlaku untuk table-level justification. Default adalah "left" jika tidak dispesifikasikan. Untuk tugas akhir, tabel biasanya center-aligned atau left-aligned dengan indentation.

```csharp
// TableFormatExtractor.cs lines 65-67, 144-152
if (effectiveTblPr.TableJustification?.Val?.HasValue == true)
    format.DftJc = ConvertTableJustification(effectiveTblPr.TableJustification.Val.Value);

private static string ConvertTableJustification(TableRowAlignmentValues value)
{
    if (value == TableRowAlignmentValues.Left) return "left";
    if (value == TableRowAlignmentValues.Center) return "center";
    if (value == TableRowAlignmentValues.Right) return "right";
    return "left"; // Default fallback
}
```

### 4.7.2.3 TableIndentation

TableIndentation (w:tblInd) menentukan left indentation tabel dari margin. Per OpenXML spec, hanya type "dxa" dan "nil" yang valid untuk indentation. Width value dapat negatif untuk outdent. Dalam `TableFormatExtractor` lines 69-89, type dan width diekstrak dengan proper parsing. `DftTblIndTwips` adalah signed integer untuk mendukung negative values. Indentation biasa digunakan untuk nested tables atau untuk memposisikan tabel yang tidak full-width.

```csharp
// TableFormatExtractor.cs lines 69-89
var tblInd = effectiveTblPr.TableIndentation;
if (tblInd != null)
{
    if (tblInd.Type?.HasValue == true)
        format.DftTblIndType = ConvertTableIndentationType(tblInd.Type.Value);
    
    if (tblInd.Width?.HasValue == true)
    {
        if (int.TryParse(tblInd.Width.Value.ToString(), out int indentValue))
        {
            if (format.DftTblIndType == "dxa")
                format.DftTblIndTwips = indentValue; // Signed integer
        }
    }
}
```

### 4.7.2.4 TableLayout

TableLayout (w:tblLayout) menentukan algoritma layout untuk table columns. Values: `Fixed` (column widths are fixed as specified), `Autofit` (columns adjust to content). Dalam `TableFormatExtractor` lines 91-93, type dikonversi ke lowercase string. Default adalah "autofit" jika tidak dispesifikasikan (lines 252-253). Fixed layout memberikan kontrol lebih presisi terhadap lebar kolom, sementara autofit lebih flexible untuk content yang bervariasi.

```csharp
// TableFormatExtractor.cs lines 91-93, 158-165
if (effectiveTblPr.TableLayout?.Type?.HasValue == true)
    format.DftTblLayoutType = ConvertTableLayout(effectiveTblPr.TableLayout.Type.Value);

private static string ConvertTableLayout(TableLayoutValues value)
{
    if (value == TableLayoutValues.Fixed) return "fixed";
    if (value == TableLayoutValues.Autofit) return "autofit";
    return "autofit"; // Default fallback
}
```

### 4.7.2.5 TableBorders

TableBorders (w:tblBorders) adalah complex property yang berisi border definitions untuk enam sisi: Top, Bottom, Left, Right, InsideHorizontal, dan InsideVertical. Setiap border memiliki properties: Val (border style), Color (hex), Size (eighths of a point), Space (spacing), Shadow, dan Frame. Dalam `TableFormatExtractor.SerializeTableBordersToJson()` lines 170-188, borders di-serialize ke JSON object dengan struktur `{top: {...}, bottom: {...}, ...}`. Setiap individual border di-serialize oleh `SerializeBorderToJson()` lines 193-211. JSON format memungkinkan storage yang flexible untuk complex border configurations.

```csharp
// TableFormatExtractor.cs lines 170-211
private static string SerializeTableBordersToJson(TableBorders borders)
{
    var obj = new JObject();
    
    if (borders.TopBorder != null)
        obj["top"] = SerializeBorderToJson(borders.TopBorder);
    if (borders.BottomBorder != null)
        obj["bottom"] = SerializeBorderToJson(borders.BottomBorder);
    // ... other borders
    
    return obj.ToString(Formatting.None);
}

private static JObject SerializeBorderToJson(BorderType border)
{
    var obj = new JObject();
    if (border.Val?.HasValue == true)
        obj["val"] = border.Val.Value.ToString();
    if (border.Color?.HasValue == true)
        obj["color"] = border.Color.Value;
    if (border.Size?.HasValue == true)
        obj["size"] = border.Size.Value;
    // ... other properties
    return obj;
}
```

### 4.7.2.6 TablePositionProperties untuk Floating Tables

TablePositionProperties (w:tblpPr) menentukan positioning untuk floating tables (tables yang diposisikan relative to text, bukan inline). Properties meliputi: LeftFromText, RightFromText, TopFromText, BottomFromText untuk margins dari text; VerticalAnchor dan HorizontalAnchor untuk anchor reference; TablePositionX/Y untuk position offset; dan alignment options. Dalam `TableFormatExtractor.SerializeTablePositionToJson()` lines 216-245, properties ini di-serialize ke JSON. Floating tables kurang umum dalam tugas akhir, tetapi implementasi tetap comprehensive untuk handling semua document types.

```csharp
// TableFormatExtractor.cs lines 216-245
private static string SerializeTablePositionToJson(TablePositionProperties tblpPr)
{
    var obj = new JObject();
    
    if (tblpPr.LeftFromText?.HasValue == true)
        obj["leftFromText"] = tblpPr.LeftFromText.Value;
    // ... other margin properties
    
    if (tblpPr.VerticalAnchor?.HasValue == true)
        obj["verticalAnchor"] = tblpPr.VerticalAnchor.Value.ToString();
    if (tblpPr.HorizontalAnchor?.HasValue == true)
        obj["horizontalAnchor"] = tblpPr.HorizontalAnchor.Value.ToString();
    
    // Position offsets and alignments
    if (tblpPr.TablePositionX?.HasValue == true)
        obj["positionX"] = tblpPr.TablePositionX.Value;
    // ...
    
    return obj.ToString(Formatting.None);
}
```

---

## 4.7.3 Ekstraksi Row dan Cell

### 4.7.3.1 TableRowProperties dan Header Row

TableRowProperties (w:trPr) berisi row-level formatting. Properties utama yang diekstrak: `TableHeader` (w:tblHeader) yang menandakan row sebagai header yang repeat di setiap halaman, dan `CantSplit` (w:cantSplit) yang mencegah row split across pages. Dalam `TableRowFormatExtractor.ExtractFormat()` lines 23-62, properties diekstrak dengan style resolution. Toggle elements seperti TableHeader dan CantSplit menggunakan `OnOffOnlyValues` - kehadiran element tanpa Val berarti true. Untuk tugas akhir, header row penting untuk tabel yang panjang agar header tetap visible di setiap halaman.

```csharp
// TableRowFormatExtractor.cs lines 23-62
public DokumenFormatTableRow ExtractFormat(
    TableRow row, int rowIndex = 0, int totalRows = 1, Table? table = null)
{
    var format = new DokumenFormatTableRow();
    
    var effectiveTrPr = (_styleResolver != null && table != null)
        ? _styleResolver.ResolveEffectiveRowProperties(row, rowIndex, totalRows, table)
        : row.GetFirstChild<TableRowProperties>();
    
    // Table Header Row
    var tableHeader = effectiveTrPr.GetFirstChild<TableHeader>();
    if (tableHeader != null)
        format.DftrTblHeader = tableHeader.Val?.HasValue == true 
            ? (tableHeader.Val.Value == OnOffOnlyValues.On) 
            : true;
    
    // Can't Split Row
    var cantSplit = effectiveTrPr.GetFirstChild<CantSplit>();
    if (cantSplit != null)
        format.DftrCantSplit = cantSplit.Val?.HasValue == true 
            ? (cantSplit.Val.Value == OnOffOnlyValues.On) 
            : true;
    
    return format;
}
```

### 4.7.3.2 GridSpan untuk Column Merge

GridSpan (w:gridSpan) menentukan berapa grid columns yang di-span oleh cell (horizontal merge). Default adalah 1. Value > 1 berarti cell spans multiple columns. Dalam `TableCellFormatExtractor.ExtractFormat()` lines 46-52, GridSpan diekstrak sebagai ushort. Fallback default 1 di-apply pada line 103-104. GridSpan berbeda dari visual column count karena grid columns bisa lebih banyak dari actual cells. Untuk validasi tugas akhir, GridSpan penting untuk mendeteksi merged cells dalam header atau data tables.

```csharp
// TableCellFormatExtractor.cs lines 46-52, 101-104
// Grid Span (w:gridSpan)
if (effectiveTcPr.GridSpan?.Val?.HasValue == true)
{
    var gridSpanValue = effectiveTcPr.GridSpan.Val.Value;
    if (gridSpanValue > 0)
        format.DftcGridSpan = (ushort)gridSpanValue;
}

// Fallback defaults
if (!format.DftcGridSpan.HasValue)
    format.DftcGridSpan = 1;
```

### 4.7.3.3 VMerge untuk Row Merge

VerticalMerge (w:vMerge) mengontrol vertical cell merging. Two values: `Restart` menandakan cell sebagai start of merge region, dan `Continue` (atau absence of Val when element exists) menandakan cell sebagai continuation. Dalam `TableCellFormatExtractor.ExtractFormat()` lines 54-63, kehadiran w:vMerge tanpa Val defaulted ke "continue". Cells dengan vMerge="continue" biasanya kosong secara visual karena content ditampilkan di restart cell. Untuk validasi, detecting vertically merged cells penting untuk memahami struktur data table.

```csharp
// TableCellFormatExtractor.cs lines 54-63
// Vertical Merge (w:vMerge)
var vMerge = effectiveTcPr.VerticalMerge;
if (vMerge != null)
{
    // If w:vMerge exists without @w:val, it means "continue"
    if (vMerge.Val?.HasValue == true)
        format.DftcVMerge = ConvertVerticalMerge(vMerge.Val.Value);
    else
        format.DftcVMerge = "continue"; // Default when w:vMerge is present without value
}
```

### 4.7.3.4 Vertical Alignment dalam Cell

TableCellVerticalAlignment (w:vAlign) menentukan vertical alignment konten dalam cell: Top, Center, atau Bottom. Dalam `TableCellFormatExtractor` lines 65-67, value dikonversi ke lowercase string. Default adalah "top" jika tidak dispesifikasikan (lines 106-107). Vertical alignment penting untuk cells dengan varying content heights, terutama dalam tables dengan multiple rows di beberapa cells dan single row di cells lain.

```csharp
// TableCellFormatExtractor.cs lines 65-67, 91-99
// Vertical Alignment (w:vAlign)
if (effectiveTcPr.TableCellVerticalAlignment?.Val?.HasValue == true)
    format.DftcVAlign = ConvertVerticalAlignment(effectiveTcPr.TableCellVerticalAlignment.Val.Value);

private static string ConvertVerticalAlignment(TableVerticalAlignmentValues value)
{
    if (value == TableVerticalAlignmentValues.Top) return "top";
    if (value == TableVerticalAlignmentValues.Center) return "center";
    if (value == TableVerticalAlignmentValues.Bottom) return "bottom";
    return "top"; // Default fallback
}
```

---

## 4.7.4 Ekstraksi Konten Cell

### 4.7.4.1 Paragraf dalam Cell

Setiap TableCell harus berisi minimal satu Paragraph (requirement OpenXML). Dalam `TableExtractor.ConvertTableRowsAsync()` lines 118-126, cell content diiterate dan paragraphs diproses menggunakan callback functions yang di-inject: `_detectParagraphType` untuk menentukan type dan `_extractParagraphContent` untuk extract content. Content items ditambahkan ke `cellContent` JArray dengan struktur `{type: "paragraph", content: [...]}`. Pattern callback injection memungkinkan reuse paragraph extraction logic dari `ParagraphExtractor` tanpa coupling.

```csharp
// TableExtractor.cs lines 118-126
foreach (var element in cell.Elements())
{
    if (element is Paragraph p)
    {
        var pType = _detectParagraphType(p);
        var pContent = _extractParagraphContent(p, numberingPart, numberingCounters);
        if (pContent.Count > 0)
            cellContent.Add(new JObject { ["type"] = pType, ["content"] = pContent });
    }
}
```

### 4.7.4.2 Nested Tables

Tables dapat contain other tables dalam cells. Dalam `TableExtractor.ConvertTableRowsAsync()` lines 127-131, nested tables dideteksi dan diproses recursively dengan `ConvertTableToJsonAsync()`. Hasil nested table ditambahkan sebagai content item dengan `{type: "table", content: <nested table json>}`. Recursive calling memastikan nested tables pada kedalaman apapun dapat diproses. Untuk dokumen tugas akhir, nested tables kurang umum tetapi mungkin digunakan untuk complex layouts.

```csharp
// TableExtractor.cs lines 127-131
else if (element is Table nestedTable)
{
    var nestedTableJson = await ConvertTableToJsonAsync(nestedTable, numberingPart, numberingCounters);
    cellContent.Add(new JObject { ["type"] = "table", ["content"] = nestedTableJson });
}
```

---

## 4.7.5 Table Style Resolution

### 4.7.5.1 TableStyleResolver Overview

`TableStyleResolver` menangani kompleksitas table style inheritance yang berbeda dari paragraph/character styles. Table styles memiliki: wholeTable properties (applied to all), conditional formatting (applied based on position), dan direct formatting. Inheritance chain: Defaults → Table Style (basedOn chain) → Conditional Styles → Direct Formatting. Dalam constructor lines 17-34, styles di-cache dan default table style diidentifikasi. Tiga public methods tersedia: `ResolveEffectiveTableProperties()`, `ResolveEffectiveRowProperties()`, dan `ResolveEffectiveCellProperties()`.

```csharp
// TableStyleResolver.cs lines 10-34
public class TableStyleResolver
{
    private readonly Dictionary<string, Style> _stylesById = new();
    private readonly Dictionary<string, TableStyleDefinition> _tableStylesCache = new();
    private readonly Styles? _stylesRoot;
    private readonly string? _defaultTableStyleId;
    
    public TableStyleResolver(StylesPart? stylesPart, StylesWithEffectsPart? stylesWithEffectsPart = null)
    {
        _stylesRoot = stylesWithEffectsPart?.Styles ?? stylesPart?.Styles;
        if (_stylesRoot != null)
        {
            // Cache all styles by ID
            foreach (var style in _stylesRoot.Elements<Style>())
            {
                var styleId = style.StyleId?.Value;
                if (!string.IsNullOrEmpty(styleId))
                    _stylesById[styleId] = style;
            }

            _defaultTableStyleId = _stylesRoot.Elements<Style>()
                .FirstOrDefault(s => s.Type?.Value == StyleValues.Table && (s.Default?.Value ?? false))
                ?.StyleId?.Value;
        }
    }
}
```

### 4.7.5.2 Table Style Chain Resolution

Table styles mendukung basedOn inheritance seperti paragraph styles. Method `GetTableStyleChain()` lines 185-207 membangun chain dari oldest ancestor ke style itu sendiri. Setiap style dalam chain di-load dan di-cache dalam `TableStyleDefinition` yang berisi wholeTable properties dan conditional styles. Chain traversal menggunakan visited set untuk prevent infinite loops. Properties di-merge secara berurutan dengan later styles overriding earlier ones.

```csharp
// TableStyleResolver.cs lines 185-207
private List<TableStyleDefinition> GetTableStyleChain(string? styleId)
{
    var chain = new List<TableStyleDefinition>();
    var visited = new HashSet<string>();
    
    while (!string.IsNullOrEmpty(styleId) && !visited.Contains(styleId))
    {
        visited.Add(styleId);
        
        var styleDef = GetOrLoadTableStyle(styleId);
        if (styleDef != null)
        {
            chain.Insert(0, styleDef); // Insert at beginning for parent → child order
            styleId = styleDef.BasedOnStyleId;
        }
        else
        {
            break;
        }
    }
    
    return chain;
}
```

### 4.7.5.3 Conditional Table Styles

Table styles memiliki conditional formatting melalui `TableStyleProperties` (w:tblStylePr) dengan 13 possible types: firstRow, lastRow, firstCol, lastCol, band1Horz, band2Horz, band1Vert, band2Vert, nwCell, neCell, swCell, seCell, dan wholeTable. Dalam `GetOrLoadTableStyle()` lines 240-258, conditional styles di-extract dan di-cache dalam dictionary. Corner cells (nw, ne, sw, se) memiliki priority tertinggi karena mereka adalah intersection dari row dan column conditions.

```csharp
// TableStyleResolver.cs lines 240-258
// Extract conditional styles (w:tblStylePr)
foreach (var tblStylePr in style.Elements<TableStyleProperties>())
{
    var type = tblStylePr.Type?.Value.ToString();
    if (string.IsNullOrEmpty(type))
        continue;
    
    var conditionalStyle = new ConditionalTableStyle
    {
        TblPr = ConvertConditionalTablePropertiesToTableProperties(...),
        TrPr = ConvertStyleTableRowPropertiesToTableRowProperties(...),
        TcPr = ConvertConditionalTableCellPropertiesToTableCellProperties(...)
    };
    
    styleDef.ConditionalStyles[type] = conditionalStyle;
}
```

### 4.7.5.4 TableLook Flags

TableLook (w:tblLook) mengontrol conditional styles mana yang aktif untuk table tertentu. Flags: FirstRow, LastRow, FirstColumn, LastColumn (enable corresponding conditional styles), NoHBand dan NoVBand (disable banding). Dalam `TableLookFlags.FromTableLook()` lines 71-97, flags di-parse dari element. Support untuk legacy Val attribute (hex bitmask) juga tersedia. Default values: FirstRow=true, FirstColumn=true, NoVBand=true. Flags ini memungkinkan user mengaktifkan/menonaktifkan aspects dari table style tanpa mengubah style definition.

```csharp
// TableStyleResolverModels.cs lines 71-97
public static TableLookFlags FromTableLook(TableLook? tableLook)
{
    var defaults = new TableLookFlags
    {
        FirstRow = true,
        LastRow = false,
        FirstColumn = true,
        LastColumn = false,
        NoHBand = false,
        NoVBand = true
    };

    if (tableLook == null)
        return defaults;

    return new TableLookFlags
    {
        FirstRow = tableLook.FirstRow?.Value ?? defaults.FirstRow,
        LastRow = tableLook.LastRow?.Value ?? defaults.LastRow,
        // ... other flags
    };
}
```

### 4.7.5.5 Cell Position Conditions

`CellConditions` class menyimpan semua position-based conditions untuk cell. Dalam `CalculateCellConditions()` lines 271-295, conditions dihitung berdasarkan rowIndex, colIndex, totalRows, totalCols, dan band sizes. IsFirstRow/IsLastRow/IsFirstCol/IsLastCol untuk edge detection. IsBand1Horz/IsBand2Horz untuk horizontal banding (alternating row colors). IsBand1Vert/IsBand2Vert untuk vertical banding. IsNwCell/IsNeCell/IsSwCell/IsSeCell untuk corner detection. Band calculation uses integer division by band size: `(rowIndex / rowBandSize) % 2 == 0` for band1.

```csharp
// TableStyleResolver.cs lines 271-295
private CellConditions CalculateCellConditions(
    int rowIndex, int colIndex, int totalRows, int totalCols,
    TableLookFlags tableLook, int rowBandSize, int colBandSize)
{
    return new CellConditions
    {
        IsFirstRow = rowIndex == 0,
        IsLastRow = rowIndex == totalRows - 1,
        IsFirstCol = colIndex == 0,
        IsLastCol = colIndex == totalCols - 1,
        IsBand1Horz = (rowIndex / rowBandSize) % 2 == 0,
        IsBand2Horz = (rowIndex / rowBandSize) % 2 == 1,
        IsBand1Vert = (colIndex / colBandSize) % 2 == 0,
        IsBand2Vert = (colIndex / colBandSize) % 2 == 1,
        IsNwCell = rowIndex == 0 && colIndex == 0,
        IsNeCell = rowIndex == 0 && colIndex == totalCols - 1,
        IsSwCell = rowIndex == totalRows - 1 && colIndex == 0,
        IsSeCell = rowIndex == totalRows - 1 && colIndex == totalCols - 1
    };
}
```

### 4.7.5.6 Conditional Style Application Order

Order of conditional style application matters karena more specific conditions override general ones. Dalam `ApplyConditionalCellStyles()` lines 324-391, application order: wholeTable → banding → firstRow/lastRow → firstCol/lastCol → corners. Corners (nwCell, neCell, swCell, seCell) applied last karena mereka paling specific. Setiap condition dicheck terhadap TableLookFlags untuk memastikan conditional style enabled sebelum application. Merge menggunakan `TablePropertyMerger` dengan "last non-null wins" semantics.

```csharp
// TableStyleResolver.cs lines 324-391 (simplified)
private TableCellProperties ApplyConditionalCellStyles(
    TableCellProperties effective, TableStyleDefinition styleDef,
    CellConditions conditions, TableLookFlags tableLook)
{
    // 1. Banding (if enabled)
    if (!tableLook.NoHBand && conditions.IsBand1Horz)
        if (styleDef.ConditionalStyles.TryGetValue("band1Horz", out var band1H))
            effective = TablePropertyMerger.MergeTableCellProperties(effective, band1H.TcPr);
    
    // 2. First/Last Row (if enabled)
    if (tableLook.FirstRow && conditions.IsFirstRow)
        if (styleDef.ConditionalStyles.TryGetValue("firstRow", out var firstRow))
            effective = TablePropertyMerger.MergeTableCellProperties(effective, firstRow.TcPr);
    
    // 3. First/Last Column (if enabled)
    // ... similar pattern
    
    // 4. Corners (most specific - applied last)
    if (conditions.IsNwCell)
        if (styleDef.ConditionalStyles.TryGetValue("nwCell", out var nwCell))
            effective = TablePropertyMerger.MergeTableCellProperties(effective, nwCell.TcPr);
    
    return effective;
}
```

---

## 4.7.6 Table Property Merging

### 4.7.6.1 TablePropertyMerger Overview

`TablePropertyMerger` menyediakan static methods untuk merging table properties dengan proper override semantics. Three merge methods tersedia: `MergeTableProperties()`, `MergeTableRowProperties()`, dan `MergeTableCellProperties()`. Strategy: "last non-null wins" untuk scalar properties dan per-side merging untuk complex properties (borders, margins). Dalam each merge method, base properties di-clone terlebih dahulu, kemudian overlay properties applied. Return value adalah new merged object, preserving immutability.

```csharp
// TablePropertyMerger.cs lines 10-11, 20-28
public static class TablePropertyMerger
{
    public static TableProperties MergeTableProperties(TableProperties? baseProps, TableProperties? overlay)
    {
        var result = new TableProperties();
        
        if (baseProps != null)
            result = (TableProperties)baseProps.CloneNode(true);
        
        if (overlay == null)
            return result;
        
        // ... merge individual properties
    }
}
```

### 4.7.6.2 Scalar Property Merging

Scalar properties (single-value properties) menggunakan simple "last non-null wins". Dalam `MergeTableProperties()` lines 31-58, scalar properties seperti TableStyle, TableWidth, TableJustification, dll. di-replace jika overlay has non-null value. Helper method `SetOrReplace()` lines 355-362 removes existing element of same type before appending new one. Pattern ini ensures proper XML structure where each element type appears at most once.

```csharp
// TablePropertyMerger.cs lines 30-58
// Scalar properties - last non-null wins
if (overlay.TableStyle != null)
    SetOrReplace(result, overlay.TableStyle.CloneNode(true));

if (overlay.TableWidth != null)
    SetOrReplace(result, overlay.TableWidth.CloneNode(true));

if (overlay.TableJustification != null)
    SetOrReplace(result, overlay.TableJustification.CloneNode(true));
// ... other scalar properties
```

### 4.7.6.3 Complex Property Merging - Borders

Complex properties seperti TableBorders memerlukan per-side merging. In `MergeTableBorders()` lines 208-235, setiap border side (top, bottom, left, right, insideH, insideV) di-merge independently. Jika overlay memiliki TopBorder, only TopBorder di-replace; other borders preserved from base. Ini memungkinkan partial override - e.g., firstRow style hanya override top border tanpa affecting other borders. Similar pattern digunakan untuk TableCellBorders dengan additional diagonal borders (TopLeftToBottomRight, TopRightToBottomLeft).

```csharp
// TablePropertyMerger.cs lines 208-235
private static TableBorders MergeTableBorders(TableBorders? baseBorders, TableBorders overlay)
{
    var result = new TableBorders();
    
    if (baseBorders != null)
        result = (TableBorders)baseBorders.CloneNode(true);
    
    // Merge each border side independently
    if (overlay.TopBorder != null)
        SetOrReplace(result, overlay.TopBorder.CloneNode(true));
    if (overlay.LeftBorder != null)
        SetOrReplace(result, overlay.LeftBorder.CloneNode(true));
    if (overlay.BottomBorder != null)
        SetOrReplace(result, overlay.BottomBorder.CloneNode(true));
    if (overlay.RightBorder != null)
        SetOrReplace(result, overlay.RightBorder.CloneNode(true));
    // ... inside borders
    
    return result;
}
```

### 4.7.6.4 Complex Property Merging - Margins

Cell margins juga menggunakan per-side merging. `MergeCellMargins()` lines 282-302 handles TableCellMarginDefault (uses StartMargin/EndMargin), dan `MergeCellMarginsIndividual()` lines 307-333 handles TableCellMargin (uses LeftMargin/RightMargin dan StartMargin/EndMargin). Distinction penting karena different element types digunakan di different contexts. Per-side merging memungkinkan style define hanya specific margins tanpa affecting others.

```csharp
// TablePropertyMerger.cs lines 307-333
private static TableCellMargin MergeCellMarginsIndividual(TableCellMargin? baseMargins, TableCellMargin overlay)
{
    var result = new TableCellMargin();
    
    if (baseMargins != null)
        result = (TableCellMargin)baseMargins.CloneNode(true);
    
    if (overlay.TopMargin != null)
        SetOrReplace(result, overlay.TopMargin.CloneNode(true));
    if (overlay.LeftMargin != null)
        SetOrReplace(result, overlay.LeftMargin.CloneNode(true));
    if (overlay.BottomMargin != null)
        SetOrReplace(result, overlay.BottomMargin.CloneNode(true));
    if (overlay.RightMargin != null)
        SetOrReplace(result, overlay.RightMargin.CloneNode(true));
    // ... start/end margins
    
    return result;
}
```

---

## 4.7.7 Penyimpanan Format Tabel

### 4.7.7.1 Model DokumenFormatTable

Model `DokumenFormatTable` (mapped ke `dokumen_format_table`) menyimpan table-level properties. Kolom meliputi: `dft_tbl_style_id` untuk style reference, `dft_tbl_w_type` dan `dft_tbl_w_twips`/`dft_tbl_w_pct50` untuk width, `dft_jc` untuk justification, `dft_tbl_ind_*` untuk indentation, `dft_tbl_layout_type` untuk layout mode, `dft_tbl_borders_json` untuk borders (JSON), `dft_tblppr_json` untuk positioning (JSON), dan `dft_raw_tblpr_xml` untuk debugging. Design menggunakan separate columns untuk different width types untuk type-safe storage.

### 4.7.7.2 Model DokumenFormatTableRow

Model `DokumenFormatTableRow` (mapped ke `dokumen_format_table_row`) menyimpan row-level properties. Kolom utama: `dftr_tbl_header` (boolean) untuk header row flag, `dftr_cant_split` (boolean) untuk cant-split flag, dan `dftr_raw_trpr_xml` untuk debugging. Design intentionally minimal karena row properties yang commonly used terbatas. Additional row properties seperti height bisa ditambahkan jika diperlukan untuk validasi lebih detail.

### 4.7.7.3 Model DokumenFormatTableCell

Model `DokumenFormatTableCell` (mapped ke `dokumen_format_table_cell`) menyimpan cell-level properties. Kolom: `dftc_grid_span` (ushort) untuk horizontal merge, `dftc_v_merge` (enum) untuk vertical merge state, `dftc_v_align` (enum) untuk vertical alignment, dan `dftc_raw_tcpr_xml` untuk debugging. Untuk validasi tugas akhir, properties ini cukup untuk mendeteksi merged cells dan alignment, yang adalah aspects paling penting dari table structure validation.

### 4.7.7.4 JSON Output Structure

JSON output untuk table mengikuti struktur hierarkis yang mirroring XML structure.  Format: `{dft_id: N, content: {rows: [{dftr_id: N, cells: [{dftc_id: N, content: [...]}]}]}}`. Setiap level menyimpan format ID-nya dan nested content. Cell content berisi array of paragraph/table items. Structure ini memungkinkan navigation dan validation pada setiap level secara independen.

---

## Kesimpulan Subbab 4.7

Ekstraksi Tabel merupakan komponen kompleks yang menangani struktur hierarkis dan conditional formatting dalam dokumen Word. Implementasi dalam proyek ini mencakup:

1. **Struktur Table Element**: Pemahaman w:tbl, w:tblPr, w:tblGrid, w:tr, w:tc dan relationship antara mereka.

2. **Table Properties**: Ekstraksi width (dengan multiple unit types), justification, indentation, layout, borders (JSON serialization), dan positioning untuk floating tables.

3. **Row dan Cell Properties**: Ekstraksi header row flag, cant-split, GridSpan untuk horizontal merge, VerticalMerge untuk vertical merge, dan vertical alignment.

4. **Cell Content**: Recursive extraction untuk paragraphs dan nested tables dalam cells.

5. **Table Style Resolution**: Full inheritance chain dengan basedOn relationships, conditional formatting dengan 13 condition types, TableLook flags untuk conditional enabling, dan position-based condition calculation.

6. **Property Merging**: Sophisticated merging dengan "last non-null wins" untuk scalars dan per-side merging untuk borders dan margins.

7. **Database Storage**: Three separate models untuk table, row, dan cell dengan appropriate data types dan JSON for complex properties.

Pemahaman detail tentang ekstraksi tabel ini menjadi dasar untuk validasi format tabel dalam dokumen tugas akhir, memungkinkan pemeriksaan terhadap strukturtabel, cell merging, borders, dan consistency formatting yang seringkali menjadi requirements dalam pedoman penulisan tugas akhir.

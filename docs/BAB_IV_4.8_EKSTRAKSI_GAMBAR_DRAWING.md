# BAB IV - Subbab 4.8: Ekstraksi Gambar dan Drawing

## Ringkasan Subbab
Subbab ini membahas secara teknis proses ekstraksi gambar dan drawing dari dokumen Word menggunakan OpenXML SDK. Drawing dalam WordprocessingML dapat berupa inline (mengikuti flow teks) atau anchor (floating, diposisikan independen). Sistem ekstraksi dalam proyek ini melibatkan beberapa komponen: `DrawingExtractor` sebagai orchestrator untuk mendeteksi jenis konten (image, chart, textbox, shape) dan mengekstrak content, `DrawingFormatExtractor` untuk mengekstrak format properties, dan `FloatingElementHelper` untuk menangani elemen-elemen floating dan reordering berdasarkan posisi Y. Proses ini mencakup penanganan format modern DrawingML (w:drawing) dan legacy VML (w:pict), berbagai jenis graphic (picture, chart, smartart, textbox, shape), nested content dalam shapes dan groups, serta text wrapping configurations. Hasil ekstraksi disimpan dalam model `DokumenFormatDrawing`, sementara item konten mempertahankan referensi `rId` ke media pada package DOCX.

---

## 4.8.1 Struktur Drawing Element

### 4.8.1.1 w:drawing sebagai Container

Elemen w:drawing dalam WordprocessingML adalah container untuk semua konten grafis modern. Drawing element dapat muncul di dalam Run element dan berisi salah satu dari dua child elements utama: `wp:inline` untuk inline graphics atau `wp:anchor` untuk floating graphics. Dalam `DrawingExtractor.ExtractDrawingContent()` lines 21-173, drawing diproses untuk mendeteksi jenis konten berdasarkan descendant elements. Akses ke drawing dilakukan melalui `run.Elements<Drawing>()` dalam paragraph extraction. Drawing element menggunakan DrawingML namespace (http://schemas.openxmlformats.org/drawingml/2006/main) yang berbeda dari WordprocessingML.

```csharp
// DrawingExtractor.cs lines 21-26
public JObject? ExtractDrawingContent(
    Drawing drawing,
    Func<OpenXmlElement, NumberingDefinitionsPart?, Dictionary<int, Dictionary<int, int>>?, JArray> extractTextBoxAsItems,
    NumberingDefinitionsPart? numberingPart = null,
    Dictionary<int, Dictionary<int, int>>? numberingCounters = null)
{
    var (shapeId, shapeName) = GetShapeIdentity(drawing);
```

### 4.8.1.2 wp:inline untuk Inline Images  

Elemen wp:inline digunakan untuk graphics yang flow dengan text, layaknya karakter besar dalam paragraph. Inline element berisi `Extent` untuk ukuran, `EffectExtent` untuk margin tambahan effects, dan `Graphic` untuk konten grafis aktual. Dalam `DrawingFormatExtractor.ExtractFormat()` lines 29-52, inline dideteksi terlebih dahulu sebelum anchor. Properties inline lebih sederhana dari anchor karena tidak memerlukan positioning kompleks. Inline images umum digunakan untuk gambar yang harus tetap di posisi relatif terhadap teks sekitarnya.

```csharp
// DrawingFormatExtractor.cs lines 36-52
var inline = drawing.Inline;
var anchor = drawing.Anchor;

if (inline != null)
{
    format.DfdrIsInline = true;
    ExtractInlineProperties(inline, format);
}
else if (anchor != null)
{
    format.DfdrIsInline = false;
    ExtractAnchorProperties(anchor, format);
}
```

### 4.8.1.3 wp:anchor untuk Floating Images

Elemen wp:anchor digunakan untuk graphics dengan positioning independen dari text flow. Anchor memiliki properties kompleks: `HorizontalPosition` dan `VerticalPosition` untuk positioning, `SimplePosition` untuk absolute coordinates, wrapping elements, dan behavior flags. Dalam `DrawingFormatExtractor.ExtractAnchorProperties()` lines 71-161, semua properties ini diekstrak dan di-serialize ke JSON. Anchor images sering digunakan untuk figure placement yang memerlukan text wrapping, seperti gambar di margin dengan text mengalir di sekitarnya.

```csharp
// DrawingFormatExtractor.cs lines 71-82
private void ExtractAnchorProperties(Anchor anchor, DokumenFormatDrawing format)
{
    // Extent (size in EMUs)
    var extent = anchor.Extent;
    if (extent != null)
    {
        format.DfdrCxEmu = (ulong?)extent.Cx?.Value;
        format.DfdrCyEmu = (ulong?)extent.Cy?.Value;
    }
    
    // Anchor positioning as JSON
    var anchorJson = new JObject();
```

### 4.8.1.4 a:blip untuk Image Reference

Elemen a:blip (BLock Image Pack) dalam DrawingML menyimpan referensi ke actual image dalam package. Blip memiliki atribut `Embed` yang berisi relationship ID (rId) ke ImagePart. Dalam `DrawingExtractor.ExtractDrawingContent()` lines 46-50, semua blips diekstrak dari drawing dan relationship IDs dikumpulkan. Multiple blips mungkin ada dalam satu drawing (misalnya, group dengan multiple images). Relationship ID kemudian digunakan untuk mengakses binary image data dari MainDocumentPart.

```csharp
// DrawingExtractor.cs lines 46-50
var blips = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>()
    .Where(b => b.Embed?.Value != null)
    .Select(b => b.Embed!.Value)
    .Distinct()
    .ToList();
```

---

## 4.8.2 Ekstraksi Image Properties

### 4.8.2.1 Extent: cx dan cy dalam EMUs

Extent element (a:ext) mendefinisikan ukuran graphic dalam English Metric Units (EMUs). EMU adalah unit yang tidak bergantung pada resolusi: 1 inch = 914400 EMUs, 1 cm = 360000 EMUs. Dalam `DrawingFormatExtractor` lines 56-62 dan 73-79, extent diekstrak dari inline atau anchor. Values disimpan sebagai ulong karena dapat sangat besar untuk high-resolution images. Untuk validasi tugas akhir, extent dapat dikonversi ke cm untuk memverifikasi ukuran gambar sesuai pedoman.

```csharp
// DrawingFormatExtractor.cs lines 54-62
private void ExtractInlineProperties(Inline inline, DokumenFormatDrawing format)
{
    // Extent (size in EMUs)
    var extent = inline.Extent;
    if (extent != null)
    {
        format.DfdrCxEmu = (ulong?)extent.Cx?.Value;
        format.DfdrCyEmu = (ulong?)extent.Cy?.Value;
    }
    // ...
}
```

### 4.8.2.2 Embed Relationship ID

Relationship ID (rId) pada blip menghubungkan drawing ke actual image file dalam package. RId adalah string seperti "rId5" yang kemudian resolved melalui relationships. Dalam extractor drawing, nilai ini diambil dari Blip element (baik Embed untuk embedded images maupun Link untuk linked images) dan dipertahankan di output JSON. Penyimpanan rId dalam output memungkinkan tracking gambar mana yang digunakan di mana tanpa perlu menyimpan binary media secara terpisah.

```csharp
// DrawingFormatExtractor.cs lines 222-228
if (string.Equals(uri, PictureUri, StringComparison.OrdinalIgnoreCase))
{
    format.DfdrGraphicType = "picture";
    
    // Get relationship ID from picture
    var blip = graphicData.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
    format.DfdrRelId = blip?.Embed?.Value ?? blip?.Link?.Value;
}
```

### 4.8.2.3 DocProperties: id dan name

DocProperties (wp:docPr) menyimpan identitas unik drawing dalam dokumen. Properties meliputi: `Id` (unique numeric ID), `Name` (descriptive name seperti "Picture 1"), dan `Description` (alt text untuk accessibility). Dalam `DrawingExtractor.GetShapeIdentity()` lines 184-191, DocProperties diakses untuk logging dan identification. ID unik penting untuk tracking graphics across document, sementara Name dan Description berguna untuk accessibility validation dalam tugas akhir.

```csharp
// DrawingExtractor.cs lines 184-191
public (string? id, string? name) GetShapeIdentity(Drawing drawing)
{
    var docPr = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().FirstOrDefault();
    if (docPr != null)
        return (docPr.Id?.ToString(), docPr.Name?.Value);
    
    return (null, null);
}
```

### 4.8.2.4 Effect Extent untuk Shadows dan Effects

EffectExtent (wp:effectExtent) mendefinisikan additional margins yang diperlukan untuk visual effects seperti shadows, reflections, dan soft edges. Properties l, t, r, b (left, top, right, bottom) dalam EMUs menentukan berapa banyak space ekstra yang diperlukan di setiap sisi. Dalam `DrawingFormatExtractor.BuildEffectExtentJson()` lines 361-384, effect extent di-serialize ke JSON dengan default values 0 jika tidak ada. Effect extent penting untuk layout calculations agar effects tidak terpotong.

```csharp
// DrawingFormatExtractor.cs lines 361-384
private static JObject? BuildEffectExtentJson(EffectExtent? effectExtent, bool includeDefault)
{
    if (effectExtent == null)
    {
        if (!includeDefault)
            return null;

        return new JObject
        {
            ["l"] = 0L,
            ["t"] = 0L,
            ["r"] = 0L,
            ["b"] = 0L
        };
    }

    return new JObject
    {
        ["l"] = effectExtent.LeftEdge?.Value ?? 0L,
        ["t"] = effectExtent.TopEdge?.Value ?? 0L,
        ["r"] = effectExtent.RightEdge?.Value ?? 0L,
        ["b"] = effectExtent.BottomEdge?.Value ?? 0L
    };
}
```

---

## 4.8.3 Ekstraksi Anchor Properties

### 4.8.3.1 SimplePosition vs Relative Position

Anchor dapat menggunakan absolute positioning (SimplePos) atau relative positioning. Atribut `simplePos` boolean menentukan mode mana yang aktif. Jika simplePos=true, SimplePosition element (x, y dalam EMUs) digunakan. Jika false (default), HorizontalPosition dan VerticalPosition digunakan dengan relative positioning. Dalam `DrawingFormatExtractor.ExtractAnchorProperties()` lines 122-131, kedua mode diekstrak. Relative positioning lebih umum karena lebih flexible terhadap perubahan layout.

```csharp
// DrawingFormatExtractor.cs lines 122-131
// Simple positioning
var simplePos = anchor.SimplePosition;
if (simplePos != null)
{
    anchorJson["simplePos"] = new JObject
    {
        ["x"] = simplePos.X?.Value,
        ["y"] = simplePos.Y?.Value
    };
}
```

### 4.8.3.2 HorizontalPosition dan VerticalPosition

HorizontalPosition dan VerticalPosition elements mendefinisikan positioning relatif untuk anchored graphics. Setiap position memiliki: `RelativeFrom` (anchor reference seperti Column, Page, Margin), `PositionOffset` (offset dalam EMUs), `Alignment` (left, center, right untuk horizontal; top, center, bottom untuk vertical), atau `PercentOffset` (percentage-based positioning). Dalam `DrawingFormatExtractor.ExtractAnchorProperties()` lines 84-120, positions diekstrak ke JSON objects. RelativeFrom menentukan titik referensi positioning.

```csharp
// DrawingFormatExtractor.cs lines 85-102
var posH = anchor.HorizontalPosition;
var posV = anchor.VerticalPosition;
if (posH != null)
{
    var posHJson = new JObject
    {
        ["relativeFrom"] = posH.RelativeFrom?.Value.ToString()
    };
    var posOffset = posH.PositionOffset?.Text;
    if (!string.IsNullOrWhiteSpace(posOffset))
        posHJson["posOffset"] = posOffset;
    var align = posH.HorizontalAlignment?.Text;
    if (!string.IsNullOrWhiteSpace(align))
        posHJson["align"] = align;
    anchorJson["horizontalPosition"] = posHJson;
}
```

### 4.8.3.3 Distance from Text

Distance properties (distT, distB, distL, distR) menentukan minimum spacing antara anchored graphic dan surrounding text dalam EMUs. Dalam `DrawingFormatExtractor.ExtractAnchorProperties()` lines 133-137, distances diekstrak dengan default 0. Distances bekerja bersama dengan wrap type untuk mengontrol bagaimana text mengalir di sekitar graphic. Untuk tugas akhir, distances dapat digunakan untuk memvalidasi spacing requirements untuk figures dan gambar.

```csharp
// DrawingFormatExtractor.cs lines 133-137
// Distance from text
anchorJson["distT"] = anchor.DistanceFromTop?.Value ?? 0U;
anchorJson["distB"] = anchor.DistanceFromBottom?.Value ?? 0U;
anchorJson["distL"] = anchor.DistanceFromLeft?.Value ?? 0U;
anchorJson["distR"] = anchor.DistanceFromRight?.Value ?? 0U;
```

### 4.8.3.4 Behavior Flags

Anchor memiliki beberapa behavior flags yang mengontrol interaksi dengan document: `BehindDoc` (behind text layer vs in front), `Locked` (position locked), `LayoutInCell` (layout within table cell boundaries), `AllowOverlap` (can overlap with other anchored objects), dan `Hidden` (not displayed). RelativeHeight menentukan Z-order untuk overlapping objects. Dalam lines 145-151, flags ini diekstrak ke JSON. Flags penting untuk understanding layout behavior.

```csharp
// DrawingFormatExtractor.cs lines 145-151
// Behavior flags
anchorJson["simplePosFlag"] = anchor.SimplePos?.Value ?? false;
anchorJson["relativeHeight"] = anchor.RelativeHeight?.Value ?? 0U;
anchorJson["behindDoc"] = anchor.BehindDoc?.Value ?? false;
anchorJson["locked"] = anchor.Locked?.Value ?? false;
anchorJson["layoutInCell"] = anchor.LayoutInCell?.Value ?? true;
anchorJson["allowOverlap"] = anchor.AllowOverlap?.Value ?? true;
anchorJson["hidden"] = anchor.Hidden?.Value ?? false;
```

### 4.8.3.5 Text Wrapping Types

Text wrapping mengontrol bagaimana text mengalir di sekitar floating graphic. Lima wrap types tersedia: `WrapNone` (no wrapping, text overlaps), `WrapSquare` (rectangular wrap dengan distances), `WrapTight` (wrap mengikuti shape dengan polygon), `WrapThrough` (wrap through transparent areas), dan `WrapTopBottom` (text above dan below, tidak di samping). Dalam `DrawingFormatExtractor.ExtractWrapInfo()` lines 163-208, wrap type dideteksi dan properties di-serialize ke JSON termasuk wrap polygon untuk tight/through.

```csharp
// DrawingFormatExtractor.cs lines 163-208
private void ExtractWrapInfo(Anchor anchor, DokumenFormatDrawing format)
{
    var wrapJson = new JObject();
    
    if (anchor.GetFirstChild<WrapNone>() != null)
    {
        wrapJson["type"] = "none";
    }
    else if (anchor.GetFirstChild<WrapSquare>() is WrapSquare wrapSquare)
    {
        wrapJson["type"] = "square";
        AddWrapText(wrapJson, wrapSquare.WrapText, WrapTextValues.BothSides);
        AddWrapDistance(wrapJson, "distT", wrapSquare.DistanceFromTop);
        // ... distances and effect extent
    }
    else if (anchor.GetFirstChild<WrapTight>() is WrapTight wrapTight)
    {
        wrapJson["type"] = "tight";
        AddWrapPolygon(wrapJson, wrapTight.WrapPolygon);
    }
    // ... other wrap types
}
```

### 4.8.3.6 WrapPolygon untuk Tight/Through

WrapPolygon mendefinisikan custom shape untuk text wrapping dalam tight dan through modes. Polygon terdiri dari StartPoint dan sequence of LineTo points, membentuk closed shape. Coordinates dalam 1/21600 of extent. `Edited` flag menandakan apakah polygon diedit manually oleh user. Dalam `DrawingFormatExtractor.AddWrapPolygon()` lines 325-359, polygon di-serialize ke JSON dengan start point dan array of line points.

```csharp
// DrawingFormatExtractor.cs lines 325-359
private static void AddWrapPolygon(JObject wrapJson, WrapPolygon? wrapPolygon)
{
    if (wrapPolygon == null)
        return;

    var polygonJson = new JObject
    {
        ["edited"] = wrapPolygon.Edited?.Value ?? false
    };

    var start = wrapPolygon.StartPoint;
    if (start != null)
    {
        polygonJson["start"] = new JObject
        {
            ["x"] = start.X?.Value ?? 0L,
            ["y"] = start.Y?.Value ?? 0L
        };
    }

    var lines = new JArray();
    foreach (var line in wrapPolygon.Elements<LineTo>())
    {
        lines.Add(new JObject
        {
            ["x"] = line.X?.Value ?? 0L,
            ["y"] = line.Y?.Value ?? 0L
        });
    }

    if (lines.Count > 0)
        polygonJson["lines"] = lines;

    wrapJson["polygon"] = polygonJson;
}
```

---

## 4.8.4 Graphic Type Detection

### 4.8.4.1 GraphicData URI untuk Type Detection

GraphicData element (a:graphicData) menyimpan konten grafis aktual dan memiliki URI yang mengidentifikasi jenis konten. URI values standar: Picture (picture namespace), Chart (chart namespace), Diagram/SmartArt, OLE object, dan WordprocessingShape. Dalam `DrawingFormatExtractor.ExtractGraphicInfo()` lines 210-248, URI digunakan untuk menentukan graphic type. Constants untuk URIs didefinisikan di lines 18-24. Type detection penting untuk different processing paths.

```csharp
// DrawingFormatExtractor.cs lines 18-24, 210-248
private const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";
private const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
private const string DiagramUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
private const string OleUri = "http://schemas.openxmlformats.org/drawingml/2006/ole";
private const string WpsUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
private const string WpgUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
private const string WpcUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas";

private void ExtractGraphicInfo(DocumentFormat.OpenXml.Drawing.Graphic? graphic, DokumenFormatDrawing format)
{
    var uri = graphicData.Uri?.Value;
    
    if (string.Equals(uri, PictureUri, StringComparison.OrdinalIgnoreCase))
        format.DfdrGraphicType = "picture";
    else if (string.Equals(uri, ChartUri, StringComparison.OrdinalIgnoreCase))
        format.DfdrGraphicType = "chart";
    // ... other types
}
```

### 4.8.4.2 Picture Type Extraction

Picture graphics (gambar) diidentifikasi oleh Picture URI. Blip element berisi referensi ke actual image. Dalam lines 222-228, setelah mendeteksi picture type, relationship ID diekstrak dari Blip (Embed untuk embedded, Link untuk external links). Picture adalah graphic type paling umum dalam tugas akhir untuk figures, screenshots, dan ilustrasi. Validation dapat memverifikasi presence dan properties gambar.

### 4.8.4.3 Chart Type Extraction

Chart graphics diidentifikasi oleh Chart URI atau dengan mencari ChartReference element. Dalam `DrawingExtractor.ExtractDrawingContent()` lines 52-66, chart references diekstrak menggunakan dua metode: pertama dengan tipe ChartReference strongly-typed, fallback dengan XPath-like search untuk compatibility. Chart relationships point ke ChartPart yang berisi chart definition. Charts dapat digunakan untuk data visualization dalam tugas akhir.

```csharp
// DrawingExtractor.cs lines 52-66
var chartRefs = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Charts.ChartReference>()
    .Where(c => c.Id?.Value != null)
    .Select(c => c.Id!.Value)
    .Distinct()
    .ToList();

if (!chartRefs.Any())
{
    chartRefs = drawing.Descendants()
        .Where(e => e.LocalName == "chart" && e.NamespaceUri == "http://schemas.openxmlformats.org/drawingml/2006/chart")
        .Select(e => e.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships").Value)
        .Where(id => !string.IsNullOrEmpty(id))
        .Distinct()
        .ToList()!;
}
```

### 4.8.4.4 Shape dan TextBox Detection

Shapes dan textboxes diidentifikasi oleh WordprocessingShape URIs (wps, wpg, wpc). Distinction antara plain shape dan textbox dibuat berdasarkan kehadiran TextBoxContent element. Dalam lines 236-245, jika WordprocessingShape URI terdeteksi, dilakukan check untuk TextBoxContent. PresetGeometry juga diekstrak untuk shapes (rectangle, ellipse, arrow, etc.). Textboxes penting karena berisi text content yang perlu diekstrak.

```csharp
// DrawingFormatExtractor.cs lines 236-243
else if (IsWordprocessingShapeUri(uri))
{
    // Check if it's a textbox or regular shape
    format.DfdrGraphicType = graphicData.Descendants<TextBoxContent>().Any() ? "textbox" : "shape";
    
    // Extract preset shape type from a:prstGeom
    ExtractPresetShape(graphicData, format);
}
```

### 4.8.4.5 PresetGeometry untuk Shape Types

PresetGeometry (a:prstGeom) mendefinisikan predefined shape type seperti rectangle, ellipse, rightArrow, star5, flowchart shapes, dll. Dalam `DrawingFormatExtractor.ExtractPresetShape()` lines 253-259, preset value diekstrak dari PresetGeometry element. Preset shapes menggunakan standardized geometries yang sama across Office applications. Untuk tugas akhir, shape types dapat berguna untuk mendeteksi flowchart elements atau standardized diagrams.

```csharp
// DrawingFormatExtractor.cs lines 253-259
private void ExtractPresetShape(DocumentFormat.OpenXml.Drawing.GraphicData graphicData, DokumenFormatDrawing format)
{
    var presetGeometry = graphicData.Descendants<DocumentFormat.OpenXml.Drawing.PresetGeometry>().FirstOrDefault();
    var preset = presetGeometry?.Preset?.Value;
    if (preset != null)
        format.DfdrPresetShape = preset.ToString();
}
```

---

## 4.8.5 Ekstraksi Content dari Drawing

### 4.8.5.1 TextBoxContent Extraction

TextBoxContent (w:txbxContent) dalam shapes memungkinkan shapes berisi rich text content. Content ini berisi paragraphs yang perlu diproses seperti body content. Dalam `DrawingExtractor.ExtractDrawingContent()` lines 68-101, TextBoxContent elements diekstrak dan diproses menggunakan callback function `extractTextBoxAsItems`. Multiple textboxes mungkin ada dalam Group shapes. JSON output menggunakan struktur `{type: "textbox", content: [...]}` untuk setiap textbox.

```csharp
// DrawingExtractor.cs lines 68-101
var txbxContents = drawing.Descendants<TextBoxContent>().ToList();

// Handle shapes with textbox content (including Group shapes with multiple textboxes)
if (txbxContents.Count > 0)
{
    var content = new JArray();
    
    // Add any images in the group
    foreach (var rId in blips)
        content.Add(new JObject { ["type"] = "image", ["rId"] = rId });
    
    // Process ALL textbox contents (critical for Group shapes with multiple shapes)
    foreach (var txbx in txbxContents)
    {
        var textItems = extractTextBoxAsItems(txbx, numberingPart, numberingCounters);
        if (textItems.Count > 0)
        {
            content.Add(new JObject { 
                ["type"] = "textbox", 
                ["content"] = textItems 
            });
        }
    }
    
    return new JObject { ["type"] = "shape", ["content"] = content };
}
```

### 4.8.5.2 Composite Content Handling

Drawings dapat contain multiple content types: images + textboxes, charts + shapes, dll. Dalam `DrawingExtractor` lines 103-147, berbagai combinations ditangani: single image, multiple images (composite), single chart, dan mixed content. Mixed content dioutput sebagai shape dengan content array containing all items. Design ini memungkinkan preservasi struktur asli sambil tetap accessible untuk extraction.

```csharp
// DrawingExtractor.cs lines 103-146
if (blips.Count == 1 && !txbxContents.Any())
    return new JObject { ["type"] = "image", ["rId"] = blips[0], ["_sortY"] = sortYPosition };

if (blips.Count > 1 && !txbxContents.Any())
{
    var images = new JArray();
    foreach (var rId in blips)
        images.Add(new JObject { ["type"] = "image", ["rId"] = rId });
    return new JObject { ["type"] = "composite", ["content"] = images, ["_sortY"] = sortYPosition };
}

if (blips.Count > 0 || chartRefs.Count > 0 || txbxContents.Any())
{
    var content = new JArray();
    foreach (var rId in blips)
        content.Add(new JObject { ["type"] = "image", ["rId"] = rId });
    foreach (var rId in chartRefs)
        content.Add(new JObject { ["type"] = "chart", ["rId"] = rId });
    // ... textboxes
    return new JObject { ["type"] = "shape", ["content"] = content };
}
```

### 4.8.5.3 DrawingML Text Extraction

Selain TextBoxContent (WordprocessingML text), shapes juga dapat berisi DrawingML text dalam a:t elements. Ini digunakan untuk text langsung dalam shape bodies, labels, dan annotations. Dalam `DrawingExtractor` lines 149-165, jika tidak ada blips, charts, atau textboxes, a:t (Text) elements diekstrak sebagai fallback. Text ini berguna untuk capturing simple labels dan annotations yang mungkin hilang tanpa fallback ini.

```csharp
// DrawingExtractor.cs lines 149-165
var drawingTexts = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
    .Select(t => t.Text)
    .Where(t => !string.IsNullOrWhiteSpace(t))
    .ToList();

if (drawingTexts.Any())
{
    var content = new JArray();
    foreach (var txt in drawingTexts)
        content.Add(new JObject { ["type"] = "text", ["value"] = txt.Trim() });
    
    return new JObject { 
        ["type"] = "shape", 
        ["content"] = content, 
        ["_sortY"] = sortYPosition
    };
}
```

---

## 4.8.6 Legacy VML Support

### 4.8.6.1 w:pict untuk Legacy Images

Elemen w:pict (Picture) adalah legacy format untuk images sebelum DrawingML diintroduksi. VML (Vector Markup Language) digunakan untuk shape definitions. Meskipun modern documents lebih prefer w:drawing, banyak existing documents masih menggunakan w:pict. Dalam `DrawingExtractor.ExtractVmlPicture()` lines 175-182, VML pictures diproses untuk mengekstrak relationship ID dari ImageData element. Support ini memastikan backward compatibility dengan older documents.

```csharp
// DrawingExtractor.cs lines 175-182
public JObject? ExtractVmlPicture(Picture pict)
{
    var imageData = pict.Descendants<DocumentFormat.OpenXml.Vml.ImageData>().FirstOrDefault();
    if (imageData?.RelationshipId?.Value != null)
        return new JObject { ["type"] = "image", ["rId"] = imageData.RelationshipId.Value };
    
    return new JObject { ["type"] = "shape" };
}
```

### 4.8.6.2 VML Format Extraction

VML format properties berbeda dari DrawingML. Dalam `DrawingFormatExtractor.ExtractVmlFormat()` lines 264-303, VML Pictures diproses: ImageData untuk rId, Shape untuk type dan style. VML menggunakan CSS-like style strings untuk dimensions ("width:2in;height:1in") yang perlu di-parse. Shape types diidentifikasi melalui Type attribute yang references shapetype definitions.

```csharp
// DrawingFormatExtractor.cs lines 264-303
public DokumenFormatDrawing ExtractVmlFormat(Picture picture)
{
    var format = new DokumenFormatDrawing();
    
    format.DfdrRawDrawingXml = picture.OuterXml;
    format.DfdrIsInline = true;
    
    var imageData = picture.Descendants<ImageData>().FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(imageData?.RelationshipId?.Value))
    {
        format.DfdrGraphicType = "picture";
        format.DfdrRelId = imageData.RelationshipId!.Value;
    }
    else
    {
        format.DfdrGraphicType = "shape";
    }
    
    var shape = picture.Descendants<Shape>().FirstOrDefault();
    if (shape != null)
    {
        if (!string.IsNullOrWhiteSpace(shape.Type?.Value))
            format.DfdrPresetShape = shape.Type!.Value.TrimStart('#');
        
        var style = shape.Style?.Value;
        if (!string.IsNullOrWhiteSpace(style))
        {
            var (cxEmu, cyEmu) = ParseVmlStyleSize(style);
            // ... set dimensions
        }
    }
    
    return format;
}
```

### 4.8.6.3 VML Style Parsing

VML uses CSS-like style strings untuk properties. Dalam `DrawingFormatExtractor.ParseVmlStyleSize()` dan `ParseVmlStyleLength()` lines 411-440, style strings di-parse menggunakan regex untuk extract width dan height values. Unit conversions: pt → 12700 EMU, in → 914400 EMU, cm → 360000 EMU, mm → 36000 EMU, px → 9525 EMU. Parsing ini memungkinkan normalisasi VML dimensions ke format yang sama dengan DrawingML.

```csharp
// DrawingFormatExtractor.cs lines 418-440
private static double? ParseVmlStyleLength(string style, string name)
{
    var match = Regex.Match(
        style,
        $@"(?:^|;)\s*{Regex.Escape(name)}\s*:\s*([0-9.]+)\s*(pt|in|cm|mm|px)\s*(?:;|$)",
        RegexOptions.IgnoreCase);
    if (!match.Success)
        return null;

    if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        return null;

    var unit = match.Groups[2].Value.ToLowerInvariant();
    return unit switch
    {
        "pt" => value * 12700d,
        "in" => value * 914400d,
        "cm" => value * 360000d,
        "mm" => value * 36000d,
        "px" => value * 9525d,
        _ => null
    };
}
```

---

## 4.8.7 Floating Element Handling

### 4.8.7.1 Detection of Floating Elements

Floating elements adalah elements dengan positioning independen dari document flow. Dua jenis floating elements: floating tables (dengan w:tblpPr) dan anchored drawings (dengan wp:anchor). Dalam `FloatingElementHelper.DetectFloatingElement()` lines 25-71, method mendeteksi kedua jenis dan mengekstrak Y position untuk sorting. Tables checked untuk TablePositionProperties, paragraphs checked untuk anchored drawings dengan VerticalPosition offset.

```csharp
// FloatingElementHelper.cs lines 25-71
public (bool isFloating, int yPosition) DetectFloatingElement(OpenXmlElement elem)
{
    // Check for floating table
    if (elem is Table table)
    {
        var tblPr = table.GetFirstChild<TableProperties>();
        if (tblPr != null)
        {
            var tblpPr = tblPr.GetFirstChild<TablePositionProperties>();
            if (tblpPr != null)
            {
                var yPos = tblpPr.TablePositionY?.Value ?? 0;
                return (true, yPos);
            }
        }
    }
    
    // Check for paragraph containing anchored drawings
    if (elem is Paragraph para)
    {
        var drawings = para.Descendants<Drawing>().ToList();
        foreach (var drawing in drawings)
        {
            var anchor = drawing.Descendants<Anchor>().FirstOrDefault();
            if (anchor != null)
            {
                var posOffset = positionV.GetFirstChild<PositionOffset>();
                if (posOffset != null && int.TryParse(posOffset.Text, out int yPos))
                {
                    var yTwips = (int)(yPos / 635.0); // EMU to twips
                    return (true, yTwips);
                }
            }
        }
    }
    
    return (false, 0);
}
```

### 4.8.7.2 Y Position Sorting

Floating elements dalam XML mungkin tidak dalam visual order. Y position digunakan untuk menentukan visual order. Dalam `DrawingExtractor.ExtractDrawingContent()` lines 32-44, `_sortY` field ditambahkan ke output untuk anchored drawings. EMU to twips conversion (EMU / 635) dilakukan untuk normalisasi. Position sorting penting untuk memastikan extraction order matches visual reading order dalam dokumen.

```csharp
// DrawingExtractor.cs lines 32-44
int sortYPosition = 0;

var anchor = drawing.Descendants<Anchor>().FirstOrDefault();
if (anchor != null)
{
    var positionV = anchor.GetFirstChild<VerticalPosition>();
    if (positionV != null)
    {
        var posOffset = positionV.GetFirstChild<PositionOffset>();
        if (posOffset != null && int.TryParse(posOffset.Text, out int yPos))
            sortYPosition = yPos;
    }
}
```

### 4.8.7.3 Cluster-Based Reordering

Floating elements dikelompokkan dalam clusters dan di-reorder berdasarkan Y position dalam setiap cluster. Non-floating elements tetap di posisi asli dan memisahkan clusters. Dalam `FloatingElementHelper.ReorderFloatingElements()` lines 76-127, algorithm: collect floating elements into cluster, when non-floating encountered, sort cluster by Y position then original index (untuk stability), emit sorted cluster, emit non-floating element. Pattern ini memastikan correct visual order sambil minimizing disruption.

```csharp
// FloatingElementHelper.cs lines 76-127
public List<(OpenXmlElement element, int originalIndex)> ReorderFloatingElements(
    List<(OpenXmlElement element, bool isFloating, int floatYPosition, int originalIndex)> elements)
{
    var result = new List<(OpenXmlElement element, int originalIndex)>();
    var cluster = new List<(OpenXmlElement element, int yPos, int origIdx)>();

    foreach (var (element, isFloating, floatY, origIdx) in elements)
    {
        if (isFloating && floatY > 0)
        {
            cluster.Add((element, floatY, origIdx));
        }
        else
        {
            if (cluster.Count > 0)
            {
                var sortedCluster = cluster
                    .OrderBy(c => c.yPos)
                    .ThenBy(c => c.origIdx)
                    .Select(c => (c.element, c.origIdx));
                
                result.AddRange(sortedCluster);
                cluster.Clear();
            }
            result.Add((element, origIdx));
        }
    }
    // Handle trailing cluster
    // ...
    return result;
}
```

---

## 4.8.8 Media Extraction dari Package

### 4.8.8.1 ImageParts Enumeration

OpenXML package menyimpan images dalam `ImageParts`. Pada pipeline aktif, relationship ID ke image part dipertahankan melalui `rId` di item konten, sehingga enumerasi binary media dapat dilakukan hanya ketika dibutuhkan oleh proses eksternal atau analisis lanjutan.

```csharp
var blip = graphicData.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
var rId = blip?.Embed?.Value ?? blip?.Link?.Value;
return new JObject { ["type"] = "image", ["rId"] = rId };
        string ext = Path.GetExtension(imgPart.Uri.OriginalString).TrimStart('.');
        if (string.IsNullOrEmpty(ext)) ext = "png";
        
        string filename = $"{dokumenId}_{rId}.{ext}";
        string fullPath = Path.Combine(_storagePath, filename);
        
        await File.WriteAllBytesAsync(fullPath, ms.ToArray());
    }
}
```

### 4.8.8.2 Binary Data Extraction dengan GetStream()

ImagePart.GetStream() menyediakan read access ke binary image data dalam package. Stream di-copy ke MemoryStream untuk processing. Dalam lines 31-33, stream copying dilakukan asynchronously untuk better performance dengan large images. MemoryStream kemudian dapat diconvert ke byte array untuk saving atau processing. Pattern ini standard untuk extracting any binary content dari OpenXML packages.

### 4.8.8.3 ContentType dan File Extension

File extension di-determine dari original URI dalam package atau ContentType. Dalam lines 35-36, extension diekstrak dari Uri path. Common types: image/png (.png), image/jpeg (.jpg), image/gif (.gif), image/bmp (.bmp), image/tiff (.tiff). Fallback ke "png" jika extension tidak dapat ditentukan. Extension penting untuk proper file handling dan display.

### 4.8.8.4 Naming Convention dan Storage

Files disimpan dengan naming convention: `{dokumenId}_{rId}.{ext}`. Dalam lines 38-42, filename dibuild dan file disimpan ke configured storage path. Pattern ini memungkinkan: unique filenames (no collision), easy association back to source document, dan clear relationship ID tracking. Storage path dikonfigurasi via constructor injection, memungkinkan flexible storage locations.

---

## 4.8.9 Penyimpanan Format Drawing

### 4.8.9.1 Model DokumenFormatDrawing

Model `DokumenFormatDrawing` (mapped ke `dokumen_format_drawing`) menyimpan drawing format properties. Kolom meliputi: `dfdr_is_inline` (boolean) untuk inline vs anchor, `dfdr_graphic_type` (enum) untuk jenis graphic, `dfdr_cx_emu` dan `dfdr_cy_emu` (ulong) untuk dimensions dalam EMUs, `dfdr_rel_id` (string) untuk relationship ID, `dfdr_anchor_json` (longtext) untuk anchor positioning, `dfdr_wrap_json` (longtext) untuk wrapping configuration, `dfdr_preset_shape` (string) untuk preset shape type, dan `dfdr_raw_drawing_xml` untuk debugging.

### 4.8.9.2 JSON Fields untuk Complex Properties

Anchor dan wrap properties disimpan sebagai JSON untuk flexibility. JSON format memungkinkan storage of nested structures (positioning dengan relativeFrom, offset, alignment) dan variable properties (different wrap types have different attributes). Using longtext type accommodates potentially large JSON for complex configurations. JSON can be parsed later for specific validation needs.

### 4.8.9.3 JSON Output Structure

JSON output untuk drawings bervariasi berdasarkan content type:
- Single image: `{type: "image", rId: "rId5", _sortY: 0}`
- Composite: `{type: "composite", content: [{type: "image", rId: "rId5"}, ...]}`
- Shape with content: `{type: "shape", content: [...textboxes, images, charts...]}`
- Chart: `{type: "chart", rId: "rId6"}`

Structure memungkinkan flexible handling of various content combinations while maintaining relationship tracking through rIds.

---

## Kesimpulan Subbab 4.8

Ekstraksi Gambar dan Drawing merupakan komponen penting yang menangani berbagai jenis konten grafis dalam dokumen Word. Implementasi dalam proyek ini mencakup:

1. **Drawing Element Structure**: Pemahaman w:drawing sebagai container, inline untuk in-flow graphics, anchor untuk floating graphics, dan blip untuk image references.

2. **Image Properties**: Ekstraksi extent (dalam EMUs), relationship IDs, DocProperties (identity), dan effect extent untuk shadows.

3. **Anchor Properties**: Positioning (simple vs relative), distances from text, behavior flags, dan comprehensive text wrapping support (5 wrap types termasuk polygon untuk tight/through).

4. **Graphic Type Detection**: URI-based detection untuk pictures, charts, diagrams, OLE objects, dan wordprocessing shapes/textboxes.

5. **Content Extraction**: TextBoxContent processing dengan callback pattern, composite content handling, dan DrawingML text fallback.

6. **Legacy VML Support**: Backward compatibility dengan w:pict elements, VML style parsing, dan unit conversions.

7. **Floating Element Handling**: Detection, Y-position extraction, dan cluster-based reordering untuk correct visual order.

8. **Media Extraction**: Binary extraction dari ImageParts dengan proper naming dan storage.

9. **Database Storage**: `DokumenFormatDrawing` model dengan JSON fields untuk complex properties.

Pemahaman detail tentang ekstraksi gambar dan drawing ini menjadi dasar untuk validasi keberadaan, ukuran, dan positioning gambar dalam dokumen tugas akhir, memungkinkan verifikasi bahwa figures memenuhi pedoman penulisan university mengenai format gambar, dimensi, dan placement.

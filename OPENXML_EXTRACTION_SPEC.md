# Spesifikasi Ekstraksi OpenXML ke Database

## Overview
Service `DocxExtractionService` mengekstrak elemen dari file .docx (OpenXML) dan menyimpannya ke database dalam format JSON.

---

## Arsitektur Ekstraksi

### 1. Entry Point
```csharp
public async Task ExtractDocxToDatabase(string docxPath, int dokumenId)
```

**Flow:**
1. Buka dokumen DOCX (read-only)
2. Extract semua media (images) → tabel `dokumen_media`
3. Loop body elements → convert ke JSON
4. Simpan ke tabel `dokumen_elemen` dengan sequence

---

## 2. Media Extraction

### Method: `ExtractAllMedia()`

**Proses:**
1. Loop semua `ImageParts` dari `MainDocumentPart`
2. Get relationship ID (`rId`)
3. Read image bytes dari stream
4. Save ke folder: `{STORAGE_PATH}/dokumen_images/{dokumenId}/`
5. Generate filename: `{Guid}.{ext}`
6. Insert ke `dokumen_media`:
   - `dokumen_media_rid` → untuk lookup dari content
   - `dokumen_media_filename` → nama file
   - `dokumen_media_filepath` → full path
   - `dokumen_media_content_type` → MIME type

**Supported Image Types:**
- PNG: `image/png`
- JPEG: `image/jpeg`
- GIF: `image/gif`
- BMP: `image/bmp`
- TIFF: `image/tiff`

---

## 3. Body Element Conversion

### Method: `ConvertBodyElementToItems()`

**Element Types:**

| OpenXML Element | Type | Handler |
|----------------|------|---------|
| `Paragraph` | paragraph/h1-h9/list-item | `FlattenParagraph()` |
| `Table` | table | `ConvertTableRows()` |
| `SectionProperties` | sectionBreak | Empty object |
| `OfficeMath` | math | Store XML |
| `BookmarkStart/End` | - | Skip (ignored) |
| Other | {LocalName} | Store raw XML |

---

## 4. Paragraph Flattening

### Method: `FlattenParagraph()`

**Konsep:** Flatten semua descendants menjadi inline array

**Inline Elements:**

| Element | Type | Output |
|---------|------|--------|
| `Text` | text | `{"type":"text","value":"..."}` |
| `TabChar` | - | Append `\t` ke text |
| `Break` | - | Append `\n` ke text |
| `Drawing` (Image) | image | `{"type":"image","rId":"..."}` |
| `Math.Paragraph` | math | `{"type":"math","text":"..."}` |
| `OfficeMath` | math | `{"type":"math","text":"..."}` |

**Special Cases:**

1. **Pure Math Paragraph:**
   - Jika hanya 1 elemen math → type = `math`
   
2. **Pure Image Paragraph:**
   - Jika hanya 1 elemen image → type = `image`

3. **Mixed Content:**
   - Type = paragraph type (h1/h2/list-item/etc)
   - Content = array of inline elements

**Text Buffering:**
- Text, Tab, Break di-buffer dulu
- Flush saat ketemu non-text element (image/math)
- Flush di akhir paragraph
- Skip whitespace-only text

---

## 5. Paragraph Type Detection

### Method: `DetectParagraphType()`

**Priority:**

1. **Style-based:**
   - `Heading1-9` → `h1` - `h9`
   - `Title` → `title`
   - `Subtitle` → `subtitle`

2. **Numbering-based:**
   - Has `NumberingId` → `list-item-{numId}-{ilvl}`
   - Example: `list-item-1-0`, `list-item-2-1`

3. **Default:**
   - `paragraph`

**Numbering Levels:**
- `ilvl=0` → Level 1 (top)
- `ilvl=1` → Level 2 (indented)
- `ilvl=2` → Level 3 (more indented)

---

## 6. Table Conversion

### Method: `ConvertTableRows()`

**Struktur:**
```json
{
  "rows": [
    {"cells": ["Cell 1", "Cell 2", "Cell 3"]},
    {"cells": ["Row 2 Cell 1", "Row 2 Cell 2", "Row 2 Cell 3"]}
  ]
}
```

**Proses:**
1. Loop `TableRow` elements
2. Loop `TableCell` dalam row
3. Extract semua `Text` descendants
4. Join dengan space
5. Simpan sebagai string

**Limitations:**
- Tidak preserve formatting dalam cell
- Tidak handle merged cells
- Tidak handle nested tables
- Images dalam cell diabaikan

---

## 7. Math Equation Handling

**Math Elements:**
- `Math.Paragraph` → Container untuk equation
- `OfficeMath` → Equation element
- `Math.Text` → Text dalam equation

**Extraction:**
```csharp
var mathText = string.Join("", math.Descendants<Math.Text>().Select(t => t.Text));
```

**Output:**
```json
{
  "type": "math",
  "content": [
    {"type": "math", "text": "x^2 + y^2 = z^2"}
  ]
}
```

**Skip Logic:**
- Jika `Math.Paragraph` sudah diproses, skip `OfficeMath` di dalamnya
- Prevent duplicate math extraction

---

## 8. Ignored Elements

**Completely Skipped:**
- `BookmarkStart` / `BookmarkEnd` → Metadata
- `LastRenderedPageBreak` → Rendering hint
- `SoftHyphen` → Auto-hyphenation
- `NoBreakHyphen` → Formatting
- `SymbolChar` → Font-specific

**Stored as XML:**
- Unknown elements → `{"xml": "..."}`
- Preserve untuk future handling

---

## 9. Database Schema

### Tabel: `dokumen_elemen`

| Column | Type | Description |
|--------|------|-------------|
| `dokumen_elemen_id` | INT | PK, auto increment |
| `dokumen_id` | INT | FK ke dokumen |
| `dokumen_elemen_sequence` | INT | Urutan dari atas ke bawah |
| `dokumen_elemen_type` | VARCHAR | paragraph/h1/table/math/image |
| `dokumen_elemen_json_tree` | JSON | Content dalam format JSON compact |

### Tabel: `dokumen_media`

| Column | Type | Description |
|--------|------|-------------|
| `dokumen_media_id` | INT | PK, auto increment |
| `dokumen_id` | INT | FK ke dokumen |
| `dokumen_media_rid` | VARCHAR | Relationship ID (rId5) |
| `dokumen_media_filename` | VARCHAR | Generated filename |
| `dokumen_media_filepath` | VARCHAR | Full path to file |
| `dokumen_media_content_type` | VARCHAR | MIME type |

---

## 10. JSON Output Examples

### Paragraph dengan Text
```json
{
  "content": [
    {"type": "text", "value": "Ini adalah paragraf biasa."}
  ]
}
```

### Paragraph dengan Mixed Content
```json
{
  "content": [
    {"type": "text", "value": "Lihat gambar berikut:\n"},
    {"type": "image", "rId": "rId5"},
    {"type": "text", "value": "\nGambar di atas menunjukkan..."}
  ]
}
```

### Pure Math
```json
{
  "content": [
    {"type": "math", "text": "E = mc^2"}
  ]
}
```

### Pure Image
```json
{
  "content": [
    {"type": "image", "rId": "rId3"}
  ]
}
```

### Table
```json
{
  "rows": [
    {"cells": ["Header 1", "Header 2"]},
    {"cells": ["Data 1", "Data 2"]}
  ]
}
```

### List Item
```json
{
  "content": [
    {"type": "text", "value": "Item pertama dalam list"}
  ]
}
```

---

## 11. Performance Considerations

**Optimizations:**
1. **Single Pass:** Loop body elements sekali saja
2. **Text Buffering:** Gabung consecutive text nodes
3. **Media Pre-extraction:** Extract images dulu sebelum content
4. **Batch Insert:** SaveChanges sekali di akhir

**Memory:**
- Read-only document access
- Stream processing untuk images
- StringBuilder untuk text buffering

---

## 12. Error Handling

**File Not Found:**
```csharp
if (!File.Exists(fullFilePath))
    throw new FileNotFoundException($"File tidak ditemukan: {fullFilePath}");
```

**Corrupt DOCX:**
- OpenXML akan throw exception
- Catch di `PdfQueueBackgroundService`
- Set `antrian_convert_status = "failed"`

**Missing MainDocumentPart:**
- Null check dengan `!` operator
- Assume valid DOCX structure

---

## 13. Limitations

**Not Supported:**
1. Text formatting (bold, italic, color)
2. Paragraph alignment
3. Font information
4. Merged table cells
5. Nested tables
6. Headers/Footers
7. Footnotes/Endnotes
8. Comments
9. Track changes
10. Custom XML parts

**Reason:** Focus pada content structure, bukan visual formatting

---

## 14. Future Enhancements

**Possible Additions:**
1. Text styling (bold/italic) → `{"type":"text","value":"...","bold":true}`
2. Hyperlinks → `{"type":"link","url":"...","text":"..."}`
3. Table cell merging → `{"colspan":2,"rowspan":1}`
4. Paragraph alignment → `{"align":"center"}`
5. Font size → `{"fontSize":12}`

**Implementation:**
- Add properties ke inline objects
- Preserve formatting tanpa ubah struktur
- Backward compatible dengan existing data

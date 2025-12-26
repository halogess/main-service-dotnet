# Dokumentasi Format JSON - dokumen_elemen_json_tree

## Overview
Kolom `dokumen_elemen_json_tree` menyimpan konten elemen dokumen dalam format JSON compact (tanpa whitespace).
Type elemen sudah tersimpan di kolom `dokumen_elemen_type`, sehingga JSON hanya berisi data konten.

**Prinsip penyimpanan:** Setiap elemen disimpan sebagai row terpisah. Paragraph yang mengandung formula atau image akan di-split menjadi beberapa row untuk menjaga urutan yang benar.

---

## 1. Text (Paragraph & Heading)

**Type:** `paragraph`, `h1`, `h2`, `h3`, `h4`, `h5`, `h6`, `h7`, `h8`, `h9`, `title`, `subtitle`

**Format:**
```json
{"text":"Plain text content"}
```

**Contoh:**

| dokumen_elemen_type | dokumen_elemen_json_tree |
|---------------------|--------------------------|
| `paragraph` | `{"text":"Ini adalah paragraf biasa."}` |
| `h1` | `{"text":"BAB I PENDAHULUAN"}` |
| `h2` | `{"text":"1.1 Latar Belakang"}` |
| `title` | `{"text":"TUGAS AKHIR"}` |

**Note:** Line break (`\n`), tab (`\t`) disimpan langsung dalam text string.

---

## 2. Mathematical Formula

**Type:** `math`

**Format:**
```json
{"text":"x2+y2=z2","xml":"<m:oMath>...</m:oMath>"}
```

**Properties:**
- `text`: Plain text representation (tanpa formatting superscript/subscript)
- `xml`: OMML (Office Math Markup Language) lengkap untuk rendering

**Contoh:**

| dokumen_elemen_type | dokumen_elemen_json_tree |
|---------------------|--------------------------|
| `math` | `{"text":"x2","xml":"<m:oMath><m:sSup>...</m:sSup></m:oMath>"}` |
| `math` | `{"text":"a+b=c","xml":"<m:oMath>...</m:oMath>"}` |

**Paragraph dengan formula akan di-split:**

| sequence | type | json_tree |
|----------|------|-----------|
| 1 | `paragraph` | `{"text":"Rumus pythagoras adalah "}` |
| 2 | `math` | `{"text":"a2+b2=c2","xml":"..."}` |
| 3 | `paragraph` | `{"text":" yang digunakan untuk segitiga siku-siku."}` |

---

## 3. Image

**Type:** `image`

**Format:**
```json
{"rId":"rId5"}
```

**Lookup image:** Query tabel `dokumen_media` dengan `dokumen_media_rid = "rId5"`

**Paragraph dengan image akan di-split:**

| sequence | type | json_tree |
|----------|------|-----------|
| 1 | `paragraph` | `{"text":"Lihat gambar berikut: "}` |
| 2 | `image` | `{"rId":"rId5"}` |
| 3 | `paragraph` | `{"text":" adalah contoh diagram."}` |

---

## 4. Page Break & Column Break

**Type:** `pageBreak`, `columnBreak`

**Format:**
```json
{}
```

Empty object, type sudah menjelaskan jenis break.

---

## 5. List Item

**Type:** `list-item-{numId}-{ilvl}`

Contoh: `list-item-1-0`, `list-item-2-1`

**Format:**
```json
{"text":"Item pertama dalam list"}
```

**Contoh:**

| dokumen_elemen_type | dokumen_elemen_json_tree |
|---------------------|--------------------------|
| `list-item-1-0` | `{"text":"Item level 0"}` |
| `list-item-1-1` | `{"text":"Sub-item level 1"}` |

---

## 6. Table

**Type:** `table`

**Format:**
```json
{"rows":[{"cells":["Cell 1","Cell 2"]},{"cells":["Cell 3","Cell 4"]}]}
```

**Struktur:**
- `rows`: Array of row objects
- `cells`: Array of plain text strings

**Contoh tabel 2x3:**
```json
{
  "rows": [
    {"cells": ["Header 1","Header 2","Header 3"]},
    {"cells": ["Data 1","Data 2","Data 3"]}
  ]
}
```

---

## 7. Section Break

**Type:** `sectionBreak`

**Format:**
```json
{}
```

Empty object karena tidak ada konten.

---

## 8. Unknown Elements

**Type:** `{LocalName}` (nama element XML)

**Format:**
```json
{"xml":"<w:element>...</w:element>"}
```

Menyimpan raw XML untuk elemen yang tidak dikenali.

---

## 9. Element Types Summary

| Type | Properties | Description |
|------|-----------|-------------|
| `paragraph`, `h1`-`h9`, `title`, `subtitle`, `list-item-*` | `text` | Plain text content |
| `math` | `text`, `xml` | Formula (plain text + OMML) |
| `image` | `rId` | Image reference |
| `table` | `rows` | Table with cells |
| `sectionBreak`, `pageBreak`, `columnBreak` | - | Empty object |
| `{unknown}` | `xml` | Raw XML |

---

## Contoh Lengkap Dokumen

| sequence | type | json_tree |
|----------|------|-----------|
| 1 | `title` | `{"text":"LAPORAN TUGAS AKHIR"}` |
| 2 | `h1` | `{"text":"BAB I\nPENDAHULUAN"}` |
| 3 | `h2` | `{"text":"1.1 Latar Belakang"}` |
| 4 | `paragraph` | `{"text":"Penelitian ini membahas..."}` |
| 5 | `paragraph` | `{"text":"Rumus yang digunakan adalah "}` |
| 6 | `math` | `{"text":"E=mc2","xml":"<m:oMath>..."}` |
| 7 | `paragraph` | `{"text":" yang ditemukan Einstein."}` |
| 8 | `paragraph` | `{"text":"Lihat gambar: "}` |
| 9 | `image` | `{"rId":"rId3"}` |
| 10 | `list-item-1-0` | `{"text":"Poin pertama"}` |
| 11 | `list-item-1-0` | `{"text":"Poin kedua"}` |
| 12 | `table` | `{"rows":[{"cells":["A","B"]},{"cells":["C","D"]}]}` |
| 13 | `pageBreak` | `{}` |
| 14 | `sectionBreak` | `{}` |

---

## Parsing Guidelines

### Mendapatkan plain text:
```python
import json

# Text elements
if elem['dokumen_elemen_type'] in ['paragraph', 'h1', 'h2', 'title']:
    data = json.loads(elem['dokumen_elemen_json_tree'])
    text = data['text']

# Math elements
if elem['dokumen_elemen_type'] == 'math':
    data = json.loads(elem['dokumen_elemen_json_tree'])
    plain_text = data['text']  # "x2+y2=z2"
    omml_xml = data['xml']     # Full OMML for rendering
```

### Mendapatkan images:
```python
if elem['dokumen_elemen_type'] == 'image':
    data = json.loads(elem['dokumen_elemen_json_tree'])
    rid = data['rId']
    # Query: SELECT * FROM dokumen_media WHERE dokumen_media_rid = rid
```

### Render HTML:
```python
html = []
for elem in elements:
    etype = elem['dokumen_elemen_type']
    data = json.loads(elem['dokumen_elemen_json_tree'])
    
    if etype == 'h1':
        html.append(f"<h1>{data['text']}</h1>")
    elif etype == 'paragraph':
        html.append(f"<p>{data['text']}</p>")
    elif etype == 'math':
        html.append(f"<span class='math'>{data['text']}</span>")
    elif etype == 'image':
        html.append(f"<img src='/media/{data['rId']}'>")
    elif etype == 'table':
        rows = data['rows']
        html.append("<table>")
        for row in rows:
            html.append("<tr>")
            for cell in row['cells']:
                html.append(f"<td>{cell}</td>")
            html.append("</tr>")
        html.append("</table>")

return ''.join(html)
```

---

## Notes

- JSON disimpan dalam format **compact** (no whitespace) menggunakan `Formatting.None`
- Type elemen **tidak** disimpan di JSON (sudah ada di kolom `dokumen_elemen_type`)
- Sequence menentukan urutan elemen dari atas ke bawah dokumen
- **Paragraph dengan formula/image akan di-split** menjadi beberapa row untuk menjaga urutan
- Line break (`\n`) dan tab (`\t`) disimpan langsung dalam text string
- Formula disimpan dengan 2 format: `text` (plain) dan `xml` (OMML untuk rendering)
- Table cells disimpan sebagai plain text array (tidak support nested elements)

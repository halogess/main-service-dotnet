# Spesifikasi Ekstraksi OpenXML ke Database

## Overview

`DocxExtractionService` membaca file `.docx` dalam mode read-only, mengekstrak struktur dan format dokumen yang relevan untuk validasi, lalu menyimpannya ke database aplikasi.

Hasil ekstraksi dibagi ke dua bentuk utama:
- tabel relasional untuk metadata struktur dan format utama;
- JSON tree pada `dokumen_elemen` dan `dokumen_note` untuk konten bertingkat.

---

## Alur Utama

### Entry Point

```csharp
Task ExtractDocxToDatabase(string docxPath, uint dokumenId)
```

### Flow

1. Buka dokumen DOCX dalam mode baca.
2. Bentuk section dan part (`body`, `header`, `footer`) yang relevan.
3. Iterasi elemen konten dan konversi ke JSON tree.
4. Simpan format yang memang dipersist secara terpisah.
5. Simpan note dan metadata visual yang dibutuhkan modul validasi.

---

## Konten yang Diekstrak

### Paragraph

Paragraph disimpan sebagai elemen dengan:
- `delemen_type` seperti `paragraph`, `h1`, `title`, atau `list-item-*`;
- `dfp_id` bila format paragraf berhasil dipersist;
- `content` berisi item inline seperti `text`, `field`, `image`, `shape`, `chart`, atau `math`.

### Table

Table disimpan sebagai elemen bertipe `table` dengan struktur:

```json
{
  "dft_id": 10,
  "content": {
    "rows": [
      {
        "cells": [
          {
            "content": [
              {
                "type": "paragraph",
                "dfp_id": 40,
                "content": [
                  { "type": "text", "dftx_id": 41, "value": "Isi sel" }
                ]
              }
            ]
          }
        ]
      }
    ]
  }
}
```

Row dan cell tidak dipersist sebagai tabel database terpisah; keduanya dipertahankan langsung di JSON tabel.

### Drawing dan Image

Drawing menghasilkan item seperti `image`, `shape`, atau `chart`. Untuk gambar, JSON mempertahankan:
- `dfdr_id` bila format drawing berhasil dipersist;
- `rId` sebagai relationship ID ke media di package DOCX.

Tidak ada tabel media biner terpisah pada schema aktif. Resolusi gambar mengikuti artifact hasil ekstraksi atau relasi package saat diperlukan.

### Field

Field Word disimpan sebagai item JSON:

```json
{
  "type": "field",
  "field_type": "PAGE",
  "result_dftx_id": 79,
  "value": "12"
}
```

Field tidak memiliki tabel format khusus pada schema aktif.

### Math

Formula disimpan sebagai item `math` di content paragraf atau sebagai elemen khusus sesuai konteks ekstraksi.

---

## Tipe Elemen Body

| OpenXML Element | Output utama |
|----------------|--------------|
| `Paragraph` | paragraph / heading / list item |
| `Table` | table dengan `rows` dan `cells` |
| `Drawing` / `Picture` | image / shape / chart |
| `FootnoteReference` / `EndnoteReference` | relasi ke `dokumen_note` |
| `OfficeMath` | math |
| `SectionProperties` | boundary section, bukan elemen konten biasa |

---

## Persistensi Database

### Tabel Struktur

- `dokumen_section`
- `dokumen_part`
- `dokumen_elemen`
- `dokumen_note`
- `dokumen_elemen_visual`

### Tabel Format

- `dokumen_format_paragraf`
- `dokumen_format_text`
- `dokumen_format_table`
- `dokumen_format_drawing`

### Tabel Pendukung Validasi

- `aturan`
- `aturan_detail`
- `kesalahan`
- `kesalahan_detail`
- `antrian`

---

## Catatan Implementasi

- Text dan run dengan signature format yang sama dapat digabung agar JSON tidak terlalu fragmentatif.
- Nested table diekstrak secara rekursif.
- Relationship ID (`rId`) dipertahankan untuk drawing/image karena menjadi kunci lookup ke package atau artifact.
- Kegagalan pada satu elemen sebisa mungkin tidak menggagalkan seluruh pipeline ekstraksi.

---

## Ringkasan

Ekstraksi OpenXML aktif berfokus pada:
- struktur dokumen yang bisa divalidasi secara stabil;
- format yang memang dipakai validator;
- JSON tree yang cukup kaya untuk nested content tanpa memperbanyak tabel yang tidak terpakai.

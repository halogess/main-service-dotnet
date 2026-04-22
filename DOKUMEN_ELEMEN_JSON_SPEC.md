# Dokumentasi Format JSON `dokumen_elemen_json_tree`

## Overview

Kolom `dokumen_elemen_json_tree` menyimpan konten elemen dokumen dalam bentuk JSON. Tipe elemen utamanya tetap disimpan terpisah di kolom `delemen_type`, sehingga JSON fokus pada data konten dan referensi format.

Prinsip utama:
- satu elemen dokumen = satu row pada `dokumen_elemen`;
- format yang relevan direferensikan lewat ID seperti `dfp_id`, `dftx_id`, `dft_id`, atau `dfdr_id`;
- nested content, terutama tabel, direpresentasikan langsung sebagai JSON bertingkat.

---

## 1. Paragraph, Heading, dan List

**Tipe umum:** `paragraph`, `h1`-`h9`, `title`, `subtitle`, `list-item-*`

**Format umum:**

```json
{
  "dfp_id": 123,
  "content": [
    { "type": "text", "dftx_id": 456, "value": "Contoh teks" }
  ]
}
```

`dfp_id` bersifat opsional dan hanya muncul bila format paragraf berhasil dipersist.

---

## 2. Text Item

```json
{ "type": "text", "dftx_id": 456, "value": "Contoh teks" }
```

Properti penting:
- `dftx_id`: referensi ke `dokumen_format_text`;
- `value`: isi teks yang sudah dinormalisasi extractor.

---

## 3. Field Item

```json
{
  "type": "field",
  "field_type": "PAGE",
  "result_dftx_id": 457,
  "value": "12"
}
```

Properti penting:
- `field_type`: tipe field Word yang terdeteksi;
- `value`: hasil tampilan field;
- `result_dftx_id`: referensi format teks hasil field bila ada.

Tidak ada tabel field terpisah pada schema aktif.

---

## 4. Image, Shape, dan Chart

**Contoh image:**

```json
{ "type": "image", "dfdr_id": 45, "rId": "rId5" }
```

Properti penting:
- `dfdr_id`: referensi ke `dokumen_format_drawing` bila format drawing berhasil disimpan;
- `rId`: relationship ID ke media pada package DOCX.

Tidak ada tabel media biner terpisah pada schema aktif. Lookup gambar mengikuti artifact hasil ekstraksi atau relasi package saat diperlukan.

---

## 5. Math

```json
{ "type": "math", "text": "x^2 + y^2 = z^2" }
```

Extractor mempertahankan representasi teks formula yang cukup untuk kebutuhan validasi dan evidence.

---

## 6. Table

Elemen bertipe `table` menyimpan struktur bertingkat berikut:

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

Catatan:
- `dft_id` mereferensikan `dokumen_format_table`;
- row dan cell tidak memiliki tabel persistensi terpisah;
- nested table dapat muncul lagi sebagai item `type: "table"` di dalam `content` cell.

---

## 7. Contoh Dokumen Ringkas

```json
[
  {
    "type": "paragraph",
    "json": {
      "dfp_id": 10,
      "content": [
        { "type": "text", "dftx_id": 11, "value": "Pendahuluan" }
      ]
    }
  },
  {
    "type": "paragraph",
    "json": {
      "dfp_id": 12,
      "content": [
        { "type": "text", "dftx_id": 13, "value": "Lihat gambar berikut: " },
        { "type": "image", "dfdr_id": 14, "rId": "rId5" }
      ]
    }
  },
  {
    "type": "table",
    "json": {
      "dft_id": 20,
      "content": {
        "rows": [
          {
            "cells": [
              {
                "content": [
                  {
                    "type": "paragraph",
                    "dfp_id": 21,
                    "content": [
                      { "type": "text", "dftx_id": 22, "value": "Header" }
                    ]
                  }
                ]
              }
            ]
          }
        ]
      }
    }
  }
]
```

---

## 8. Ringkasan

`dokumen_elemen_json_tree` pada schema aktif dirancang untuk:
- menyimpan nested content secara eksplisit;
- mempertahankan referensi ke format yang benar-benar dipakai;
- menghindari tabel tambahan yang tidak lagi digunakan.

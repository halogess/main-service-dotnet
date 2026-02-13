# Service Validasi dan Rekomendasi

## 1. Pendahuluan

Service Validasi dan Rekomendasi merupakan komponen inti dalam sistem validasi tugas akhir yang bertanggung jawab untuk memeriksa kesesuaian format dokumen dengan aturan yang telah ditetapkan serta memberikan rekomendasi perbaikan kepada mahasiswa. Service ini berjalan sebagai **background worker** yang memproses dokumen secara asinkron setelah proses ekstraksi dan labeling selesai.

Service ini dibangun dengan arsitektur modular menggunakan **partial class** di C#, di mana setiap kategori validasi diimplementasikan dalam file terpisah untuk memudahkan pengembangan dan pemeliharaan kode.

## 2. Arsitektur Service

### 2.1 Komponen Utama

```
┌─────────────────────────────────────────────────────────────────────┐
│               SERVICE VALIDASI DAN REKOMENDASI                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │           ValidationQueueBackgroundService                     │ │
│  │  • Background worker yang memproses antrian validasi           │ │
│  │  • Orkestrasi proses validasi per dokumen                      │ │
│  │  • Penyimpanan hasil ke database                               │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                              │                                       │
│                              ▼                                       │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │                    ValidationService                           │ │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐   │ │
│  │  │ PageSettings │ │ ChapterTitle │ │ SubchapterTitle      │   │ │
│  │  └──────────────┘ └──────────────┘ └──────────────────────┘   │ │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐   │ │
│  │  │  Paragraph   │ │   ListItem   │ │      Image           │   │ │
│  │  └──────────────┘ └──────────────┘ └──────────────────────┘   │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                              │                                       │
│                              ▼                                       │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │                     GeminiService                              │ │
│  │  • Generasi rekomendasi perbaikan menggunakan AI               │ │
│  │  • Penjelasan kesalahan dalam bahasa natural                   │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 Struktur File

| File | Lines | Fungsi |
|------|-------|--------|
| `ValidationService.cs` | 1.164 | DTOs, rule classes, dan utility functions |
| `ValidationService.PageSettings.cs` | 594 | Validasi pengaturan halaman |
| `ValidationService.ChapterTitle.cs` | 1.860 | Validasi judul bab |
| `ValidationService.SubchapterTitle.cs` | 1.315 | Validasi judul sub-bab |
| `ValidationService.Paragraph.cs` | 781 | Validasi paragraf |
| `ValidationService.ListItem.cs` | 686 | Validasi item daftar/list |
| `ValidationService.Image.cs` | 1.808 | Validasi gambar dan caption |
| `ValidationQueueBackgroundService.cs` | 1.251 | Background worker orchestrator |

**Total: ~9.459 baris kode**

---

## 3. Kategori Validasi

### 3.1 Validasi Pengaturan Halaman (Page Settings)

Validasi pengaturan halaman memeriksa konfigurasi section dokumen meliputi:

| Aspek | Deskripsi | Contoh Aturan |
|-------|-----------|---------------|
| **Ukuran Kertas** | Dimensi halaman (width × height) | A4 (21 × 29.7 cm), Letter |
| **Margin** | Jarak tepi halaman (top, bottom, left, right) | Top: 3 cm, Bottom: 3 cm |
| **Header/Footer** | Jarak header dan footer dari tepi | Header: 1.5 cm |
| **Gutter** | Margin binding untuk penjilidan | Gutter: 1 cm (left) |
| **Column** | Jumlah kolom per halaman | 1 kolom |
| **Page Numbering** | Format dan posisi nomor halaman | Romawi (i, ii, iii) untuk pendahuluan |

**Fungsi Utama:**
- `ValidatePageSettingsAsync()` - Entry point validasi halaman
- `ValidatePaperSize()` - Memeriksa ukuran kertas
- `ValidateMargins()` - Memeriksa margin halaman
- `ValidateHeaderFooter()` - Memeriksa jarak header/footer
- `ValidateGutter()` - Memeriksa margin binding
- `ValidatePageNumbering()` - Memeriksa format nomor halaman

---

### 3.2 Validasi Judul Bab (Chapter Title)

Validasi judul bab memastikan format judul bab sesuai dengan ketentuan:

| Aspek | Deskripsi | Contoh Aturan |
|-------|-----------|---------------|
| **Font** | Jenis dan ukuran font | Times New Roman, 14pt |
| **Style** | Bold, italic, underline | Bold: Ya, Italic: Tidak |
| **Alignment** | Perataan teks | Center |
| **Spacing** | Jarak sebelum dan sesudah | Before: 0pt, After: 12pt |
| **Numbering** | Format penomoran bab | "BAB I", "BAB II" (Romawi uppercase) |
| **Case** | Kapitalisasi teks | UPPERCASE |

**Fungsi Utama:**
- `ValidateChapterTitleAsync()` - Validasi lengkap judul bab (~900 baris)
- `ValidateParagraphBeforeSubchapterAsync()` - Memeriksa paragraf sebelum sub-bab
- `MatchesNumberFormat()` - Memeriksa format penomoran
- `HasDisallowedWhitespace()` - Memeriksa spasi berlebihan

---

### 3.3 Validasi Judul Sub-Bab (Subchapter Title)

Validasi judul sub-bab mirip dengan judul bab dengan aturan berbeda:

| Aspek | Deskripsi | Contoh Aturan |
|-------|-----------|---------------|
| **Font** | Jenis dan ukuran font | Times New Roman, 12pt |
| **Style** | Bold, italic | Bold: Ya |
| **Alignment** | Perataan teks | Left (justify) |
| **Numbering** | Format penomoran sub-bab | "1.1", "1.1.1" (desimal) |
| **Indentation** | Indentasi berdasarkan level | Level 2: 0.5 cm |

---

### 3.4 Validasi Paragraf (Paragraph)

Validasi paragraf memeriksa format teks body dokumen:

| Aspek | Deskripsi | Contoh Aturan |
|-------|-----------|---------------|
| **Font** | Font family dan size | Times New Roman, 12pt |
| **Line Spacing** | Spasi antar baris | 1.5 atau 2.0 |
| **Alignment** | Perataan paragraf | Justify |
| **First Line Indent** | Indentasi baris pertama | 1.27 cm |
| **Spacing** | Jarak before/after paragraf | Before: 0pt, After: 0pt |

**Fungsi Utama:**
- `ValidateParagraphAsync()` - Entry point validasi paragraf
- `ValidateParagraphFont()` - Memeriksa font paragraf
- `ValidateParagraphFormat()` - Memeriksa format paragraf
- `ValidateParagraphSentenceCount()` - Memeriksa jumlah kalimat minimal

---

### 3.5 Validasi Item Daftar (List Item)

Validasi item daftar memeriksa format bullet/numbered list:

| Aspek | Deskripsi | Contoh Aturan |
|-------|-----------|---------------|
| **Font** | Font untuk item list | Times New Roman, 12pt |
| **Bullet Style** | Jenis bullet point | •, -, 1., a. |
| **Indentation** | Indentasi per level | Level 1: 0.63 cm |
| **Spacing** | Jarak antar item | Before: 0pt, After: 6pt |

**Fungsi Utama:**
- `ValidateListItemAsync()` - Entry point validasi list
- `ValidateListItemFont()` - Memeriksa font list item
- `ValidateListItemParagraph()` - Memeriksa format paragraf list

---

### 3.6 Validasi Gambar (Image)

Validasi gambar merupakan kategori paling kompleks yang meliputi:

| Aspek | Deskripsi | Contoh Aturan |
|-------|-----------|---------------|
| **Position** | Posisi gambar dalam halaman | Center, inline |
| **Size** | Ukuran gambar relatif terhadap margin | Max width: 100% margin |
| **Caption Font** | Font untuk caption | Times New Roman, 10pt |
| **Caption Format** | Format penulisan caption | "Gambar 1.1 Judul Caption" |
| **Caption Position** | Posisi caption | Di bawah gambar |
| **Caption Numbering** | Penomoran caption | "Gambar [bab].[urutan]" |
| **Caption Case** | Kapitalisasi caption | Title Case |

**Fungsi Utama:**
- `ValidateImageAsync()` - Entry point validasi gambar (~400 baris)
- `ValidateImagePosition()` - Memeriksa posisi gambar
- `ValidateCaptionFont()` - Memeriksa font caption (~300 baris)
- `ValidateCaptionParagraphFormat()` - Memeriksa format paragraf caption
- `ValidateCaptionNumbering()` - Memeriksa penomoran caption
- `MergeCaptionContinuationAsync()` - Menggabungkan caption multi-paragraf

---

## 4. Alur Kerja Validasi

### 4.1 Proses Background Worker

```
┌─────────────┐     ┌─────────────────┐     ┌────────────────┐
│   Dokumen   │────▶│  Antrian        │────▶│  Background    │
│   Diupload  │     │  Validasi       │     │  Worker        │
└─────────────┘     └─────────────────┘     └───────┬────────┘
                                                     │
                    ┌────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────────────────┐
│                        PROSES VALIDASI                           │
├─────────────────────────────────────────────────────────────────┤
│  1. Load dokumen dan aturan dari database                       │
│  2. Validasi Page Settings per section                          │
│  3. Validasi setiap elemen berdasarkan label:                   │
│     • judul_bab → ValidateChapterTitleAsync()                   │
│     • judul_subbab → ValidateSubchapterTitleAsync()             │
│     • paragraf → ValidateParagraphAsync()                       │
│     • item_daftar → ValidateListItemAsync()                     │
│     • gambar → ValidateImageAsync()                             │
│  4. Kumpulkan semua ValidationError                             │
│  5. Generate rekomendasi dengan GeminiService                   │
│  6. Simpan hasil ke tabel DokumenValidasi                       │
└─────────────────────────────────────────────────────────────────┘
                    │
                    ▼
        ┌───────────────────────┐
        │  Hasil Validasi       │
        │  + Rekomendasi        │
        └───────────────────────┘
```

### 4.2 Struktur ValidationError

Setiap kesalahan yang ditemukan direpresentasikan dalam objek `ValidationError`:

```csharp
public class ValidationError
{
    public int ElementId { get; set; }          // ID elemen dokumen
    public string Category { get; set; }         // Kategori: font, paragraph, margin
    public string Field { get; set; }            // Field yang salah: font_size, alignment
    public string Expected { get; set; }         // Nilai yang diharapkan
    public string Actual { get; set; }           // Nilai yang ditemukan
    public string Message { get; set; }          // Pesan kesalahan
    public string Evidence { get; set; }         // Bukti/teks terkait
}
```

---

## 5. Service Rekomendasi (GeminiService)

### 5.1 Integrasi AI

Service rekomendasi menggunakan **Google Gemini API** untuk menghasilkan penjelasan dan saran perbaikan yang mudah dipahami oleh mahasiswa.

| Aspek | Deskripsi |
|-------|-----------|
| **Model** | Gemini 1.5 Flash / Pro |
| **Fungsi** | Mengubah error teknis menjadi penjelasan natural |
| **Input** | List of ValidationError dengan konteks |
| **Output** | Penjelasan ramah pengguna + lokasi kesalahan |

### 5.2 Fitur Utama GeminiService

| Fitur | Fungsi |
|-------|--------|
| **Error Guidance** | Menghasilkan penjelasan untuk setiap kesalahan |
| **Batch Processing** | Memproses multiple error sekaligus |
| **Rate Limiting** | Mengelola kuota API dan retry |
| **Caching** | Menyimpan hasil untuk error serupa |
| **Fallback** | Menghasilkan penjelasan default jika AI gagal |

### 5.3 Contoh Transformasi

**Input (ValidationError):**
```json
{
  "category": "font",
  "field": "font_size",
  "expected": "12",
  "actual": "11",
  "message": "Font size tidak sesuai"
}
```

**Output (Rekomendasi AI):**
```json
{
  "judul": "Ukuran Font Tidak Sesuai",
  "penjelasan": "Paragraf ini menggunakan ukuran font 11pt, sedangkan 
                 aturan penulisan tugas akhir mengharuskan ukuran 12pt 
                 untuk paragraf isi. Silakan ubah ukuran font menjadi 12pt.",
  "lokasi": "Halaman 5, paragraf ke-3"
}
```

---

## 6. Rule System

### 6.1 Struktur Aturan

Aturan validasi disimpan dalam tabel `Aturan` dan `AturanDetail` dengan format JSON:

| Kategori | Contoh Field | Contoh Value |
|----------|--------------|--------------|
| `ukuran_kertas` | papers | `{"front_matter": {...}, "main_matter": {...}}` |
| `margin` | margins | `{"top": "3", "bottom": "3", "left": "4", "right": "3"}` |
| `judul_bab` | font, paragraph | `{"name": "Times New Roman", "size": "14"}` |
| `paragraf` | font, format | `{"alignment": "justify", "first_line_indent": "1.27"}` |
| `gambar` | caption, position | `{"font_size": "10", "alignment": "center"}` |

### 6.2 Rule Classes

Service menggunakan strongly-typed rule classes untuk parsing JSON:

```csharp
// Contoh Rule Class untuk Judul Bab
public class ChapterTitleRule
{
    public TitleFontRule? Font { get; set; }
    public TitleParagraphRule? Paragraph { get; set; }
    public TitleNumberingRule? Numbering { get; set; }
    public ChapterContentStructureRule? ContentStructure { get; set; }
}

public class TitleFontRule
{
    public string? Name { get; set; }      // "Times New Roman"
    public int? Size { get; set; }          // 14
    public TitleFontStyleRule? Style { get; set; }
}
```

---

## 7. Penyimpanan Hasil

### 7.1 Tabel DokumenValidasi

Hasil validasi disimpan dalam tabel `DokumenValidasi`:

| Kolom | Tipe | Deskripsi |
|-------|------|-----------|
| `id` | int | Primary key |
| `dokumen_id` | int | Foreign key ke dokumen |
| `element_id` | int | ID elemen yang bermasalah |
| `kategori` | string | Kategori kesalahan |
| `field` | string | Field yang salah |
| `expected` | string | Nilai yang diharapkan |
| `actual` | string | Nilai yang ditemukan |
| `message` | string | Pesan kesalahan |
| `judul` | string | Judul rekomendasi (dari AI) |
| `penjelasan` | text | Penjelasan lengkap (dari AI) |
| `lokasi` | string | Lokasi kesalahan |
| `created_at` | datetime | Waktu validasi |

---

## 8. Fitur Pendukung

### 8.1 Visual Location Tracking

Service dapat melacak lokasi visual kesalahan dalam dokumen:
- **Halaman** - Nomor halaman di mana kesalahan ditemukan
- **Bounding Box** - Koordinat visual elemen (untuk highlight di PDF preview)
- **Context Text** - Teks sekitar untuk membantu identifikasi

### 8.2 Error Aggregation

Untuk kesalahan yang berulang (seperti page settings), service menggabungkan error menjadi satu entri untuk menghindari duplikasi.

### 8.3 Toleransi

Validasi menggunakan toleransi untuk nilai numerik:
- **Default**: ±0.1 cm untuk margin dan spacing
- **Font Size**: ±0.5 pt untuk ukuran font

---

## 9. Ringkasan

| Aspek | Detail |
|-------|--------|
| **Total Lines of Code** | ~9.500 baris |
| **Kategori Validasi** | 6 (Page, Chapter, Subchapter, Paragraph, List, Image) |
| **Background Processing** | Asinkron dengan antrian |
| **AI Integration** | Google Gemini untuk rekomendasi |
| **Database Storage** | Tabel DokumenValidasi |
| **Error Tracking** | Visual location + bounding box |


# Database

Sistem validasi tugas akhir menggunakan dua database yang terpisah dengan fungsi berbeda. Database pertama adalah database kampus STTS yang sudah ada dan hanya diakses untuk membaca data master. Database kedua adalah database khusus untuk sistem korektor buku yang menyimpan semua data terkait proses validasi dokumen.

## 1. Database STTS (Read-Only)

Database STTS merupakan database kampus yang sudah ada dan dikelola oleh sistem informasi kampus. Sistem validasi tugas akhir **hanya membaca (read-only)** data dari database ini tanpa melakukan perubahan apapun.

### 1.1 Tabel `mahasiswa`

Tabel ini menyimpan data mahasiswa yang terdaftar di kampus STTS.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `mhs_nrp` | varchar(11) | **PK** - Nomor Registrasi Pokok mahasiswa | Identifikasi mahasiswa, foreign key ke buku |
| `mhs_nama` | varchar(200) | Nama lengkap mahasiswa | Ditampilkan di halaman validasi dan template |
| `mhs_email` | varchar(255) | Email mahasiswa | Notifikasi hasil validasi |
| `mhs_hp` | varchar(255) | Nomor handphone | Kontak alternatif |
| `mhs_status` | int | Status akademik (aktif/lulus/cuti) | Filter mahasiswa aktif |
| `jur_kode` | varchar(2) | Kode jurusan mahasiswa | Relasi ke tabel jurusan |
| `mhs_ipk` | decimal(3,2) | Indeks Prestasi Kumulatif | Informasi akademik |
| `mhs_lulus_tahun` | varchar(4) | Tahun lulus | Filter mahasiswa lulus |
| `mhs_angkatan` | int | Tahun angkatan masuk | Statistik dan filter |

**Fungsi dalam Sistem:**
- Autentikasi mahasiswa saat login
- Identifikasi pemilik buku/dokumen
- Pengiriman notifikasi email
- Statistik mahasiswa per angkatan/jurusan

---

### 1.2 Tabel `aka_jurusan`

Tabel ini menyimpan data jurusan/program studi di kampus STTS.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `jur_kode` | varchar(2) | **PK** - Kode jurusan | Identifikasi jurusan |
| `jur_nama` | varchar(50) | Nama lengkap jurusan | Ditampilkan di UI dan template |
| `jur_singkat` | varchar(20) | Singkatan jurusan | Label ringkas |
| `jur_gelar` | varchar(50) | Gelar yang diberikan (S.Kom, S.T) | Template dokumen pelengkap |
| `jur_fakultas` | varchar(50) | Nama fakultas | Template dokumen pelengkap |
| `jur_status` | int | Status aktif/nonaktif | Filter jurusan aktif |

**Fungsi dalam Sistem:**
- Menentukan nama program studi di template dokumen
- Statistik buku per jurusan
- Menentukan gelar dan fakultas untuk template

---

### 1.3 Tabel `aka_ta_proposal`

Tabel ini menyimpan data proposal tugas akhir mahasiswa.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `proposal_kode` | varchar(6) | **PK** - Kode proposal | Identifikasi proposal |
| `mhs_nrp` | varchar(9) | NRP mahasiswa pemilik | Relasi ke mahasiswa |
| `proposal_judul_baru` | varchar(300) | Judul tugas akhir | Ditampilkan dan divalidasi |
| `proposal_tgl_doc` | datetime | Tanggal proposal | Informasi administratif |
| `proposal_perpanjangan` | smallint | Status perpanjangan | Tracking administrasi |
| `dosen_pembimbing` | varchar(6) | Kode dosen pembimbing 1 | Template dokumen pelengkap |
| `dosen_co_pembimbing` | varchar(6) | Kode dosen co-pembimbing | Template dokumen pelengkap |

**Fungsi dalam Sistem:**
- Mengambil judul tugas akhir untuk validasi
- Menentukan dosen pembimbing untuk template
- Auto-fill data di template dokumen pelengkap

---

### 1.4 Tabel `tk_dosen`

Tabel ini menyimpan data dosen yang terdaftar di kampus.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `dosen_kode` | varchar(10) | **PK** - Kode dosen | Identifikasi dosen |
| `dosen_nama_sk` | varchar(255) | Nama lengkap dosen (sesuai SK) | Template dokumen |
| `dosen_status` | int | Status aktif/nonaktif | Filter dosen aktif |
| `karyawan_nip` | varchar(15) | NIP karyawan terkait | Join dengan tabel karyawan |

**Fungsi dalam Sistem:**
- Menampilkan nama dosen pembimbing/penguji
- Template dokumen (lembar pengesahan, dll)
- Dropdown pemilihan dosen di admin

---

### 1.5 Tabel `tk_karyawan`

Tabel ini menyimpan data karyawan (termasuk dosen) untuk validasi status aktif.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `karyawan_nip` | varchar(15) | **PK** - Nomor Induk Pegawai | Identifikasi karyawan |
| `karyawan_status` | smallint | Status aktif (1) / nonaktif (0) | Filter dosen aktif |

**Fungsi dalam Sistem:**
- Memvalidasi dosen yang masih aktif
- Join dengan tabel dosen untuk filter

---

## 2. Database Korektor Buku (Read-Write)

Database Korektor Buku adalah database utama sistem validasi tugas akhir yang menyimpan semua data terkait proses validasi. Database ini memiliki full access (read-write).

---

### 2.1 Kategori: Manajemen Buku dan Dokumen

Tabel-tabel dalam kategori ini menyimpan data utama buku tugas akhir dan dokumen (BAB) yang diupload oleh mahasiswa.

#### 2.1.1 Tabel `buku`

Tabel utama yang menyimpan data buku tugas akhir yang diupload.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `buku_id` | int | **PK** - ID auto increment | Identifikasi buku |
| `mhs_nrp` | varchar | NRP mahasiswa pemilik | Relasi ke mahasiswa di DB STTS |
| `buku_judul` | varchar | Judul buku tugas akhir | Display dan referensi |
| `buku_status` | varchar | Status: `dalam_antrian`, `sedang_divalidasi`, `selesai`, `dibatalkan` | Tracking progress |
| `buku_skor` | int | Skor validasi (0-100) | Hasil penilaian |
| `buku_jumlah_kesalahan` | int | Total kesalahan ditemukan | Statistik validasi |
| `buku_jumlah_bab` | int | Jumlah BAB yang diupload | Tracking completeness |
| `buku_created_at` | datetime | Waktu upload | Audit trail |
| `buku_updated_at` | datetime | Waktu update terakhir | Audit trail |

**Relasi:**
- 1 Buku → banyak Bab
- 1 Buku → banyak Kesalahan

---

#### 2.1.2 Tabel `bab`

Menyimpan informasi BAB dalam satu buku tugas akhir.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `bab_id` | uint | **PK** - ID BAB | Identifikasi BAB |
| `buku_id` | uint | FK ke buku | Relasi ke buku parent |
| `bab_order` | byte | Urutan BAB (1, 2, 3...) | Ordering BAB |
| `bab_filename` | varchar(255) | Nama file original | Display |
| `bab_docx_path` | varchar(255) | Path file DOCX | Akses file |
| `bab_pdf_path` | varchar(255) | Path file PDF | Preview |

**Relasi:**
- Banyak Bab → 1 Buku

---

#### 2.1.3 Tabel `dokumen`

Menyimpan data dokumen individual yang diupload untuk proses validasi.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `dokumen_id` | int | **PK** - ID dokumen | Identifikasi dokumen |
| `mhs_nrp` | varchar | NRP pemilik | Relasi ke mahasiswa |
| `dokumen_filename` | varchar | Nama file original | Display dan referensi |
| `dokumen_filesize_bytes` | bigint | Ukuran file dalam bytes | Informasi file |
| `dokumen_status` | varchar | Status validasi dokumen | Tracking progress |
| `dokumen_skor` | int | Skor validasi dokumen | Hasil penilaian |
| `dokumen_jumlah_kesalahan` | int | Total kesalahan | Statistik |
| `dokumen_docx_path` | varchar | Path file DOCX di storage | Akses file |
| `dokumen_pdf_path` | varchar | Path file PDF hasil konversi | Preview PDF |
| `dokumen_images_path` | varchar | Path folder gambar halaman | Preview per halaman |
| `dokumen_created_at` | datetime | Waktu upload | Audit trail |
| `dokumen_updated_at` | datetime | Waktu update | Audit trail |

**Relasi:**
- 1 Dokumen → banyak DokumenSection
- 1 Dokumen → banyak Kesalahan

---

### 2.2 Kategori: Struktur Dokumen (Hasil Ekstraksi)

Tabel-tabel dalam kategori ini menyimpan hasil ekstraksi struktur dokumen dari file DOCX menggunakan OpenXML SDK.

#### 2.2.1 Tabel `dokumen_section`

Menyimpan data section (bagian) dokumen dengan pengaturan halaman.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `dsec_id` | uint | **PK** - ID section | Identifikasi section |
| `dokumen_id` | uint | FK ke dokumen | Relasi ke dokumen |
| `dsec_index` | uint | Urutan section (1,2,3...) | Ordering |
| `dsec_type` | varchar(50) | Tipe break: `nextPage`, `continuous` | Validasi section |
| `dsec_has_title_page` | bool | Ada halaman pertama berbeda | Validasi header/footer |
| `dsec_different_odd_even` | bool | Odd/even berbeda | Validasi header/footer |
| `dsec_page_num_format` | varchar(32) | Format: `decimal`, `lowerRoman`, `upperRoman` | Validasi penomoran |
| `dsec_page_num_start` | uint | Nomor halaman awal | Validasi penomoran |
| `dsec_page_width_twips` | uint | Lebar halaman (twips) | Validasi ukuran kertas |
| `dsec_page_height_twips` | uint | Tinggi halaman (twips) | Validasi ukuran kertas |
| `dsec_orientation` | varchar(10) | `portrait` / `landscape` | Validasi orientasi |
| `dsec_margin_top_twips` | uint | Margin atas | Validasi margin |
| `dsec_margin_bottom_twips` | uint | Margin bawah | Validasi margin |
| `dsec_margin_left_twips` | uint | Margin kiri | Validasi margin |
| `dsec_margin_right_twips` | uint | Margin kanan | Validasi margin |
| `dsec_header_margin_twips` | uint | Jarak header | Validasi header |
| `dsec_footer_margin_twips` | uint | Jarak footer | Validasi footer |
| `dsec_gutter_twips` | uint | Margin binding | Validasi gutter |
| `dsec_gutter_position` | varchar(10) | Posisi gutter: `left`, `right`, `top` | Validasi gutter |
| `dsec_column_count` | uint | Jumlah kolom (default: 1) | Validasi kolom |

**Relasi:**
- 1 Section → banyak DokumenPart (body, header, footer)

---

#### 2.2.2 Tabel `dokumen_part`

Menyimpan bagian-bagian dalam section (body, header, footer).

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `dpart_id` | uint | **PK** - ID part | Identifikasi part |
| `dsec_id` | uint | FK ke section | Relasi ke section |
| `dpart_type` | varchar(20) | Tipe: `body`, `header`, `footer` | Kategorisasi |
| `dpart_position` | varchar(10) | Posisi: `default`, `first`, `even` | Untuk header/footer |

**Relasi:**
- 1 Part → banyak DokumenElemen

---

#### 2.2.3 Tabel `dokumen_elemen`

Menyimpan elemen-elemen dokumen (paragraf, tabel, gambar, dll).

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `delemen_id` | ulong | **PK** - ID elemen | Identifikasi elemen |
| `dpart_id` | uint | FK ke part | Relasi ke part |
| `delemen_sequence` | uint | Urutan dalam dokumen | Ordering elemen |
| `delemen_type` | varchar(100) | Tipe: `paragraph`, `table`, `image` | Kategorisasi |
| `delemen_json_tree` | longtext | Konten terstruktur (JSON) | Data ekstraksi lengkap |
| `delemen_xml` | longtext | Raw XML dari OpenXML | Debugging/referensi |

**Relasi:**
- 1 Elemen → 1 DokumenFormatParagraf (jika paragraph)
- 1 Elemen → 1 DokumenFormatTable (jika table)
- 1 Elemen → banyak DokumenFormatText (runs dalam paragraph)
- 1 Elemen → banyak DokumenNote (footnote/endnote)

---

#### 2.2.4 Tabel `dokumen_note`

Menyimpan footnote dan endnote dalam dokumen.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `dnote_id` | uint | **PK** - ID note | Identifikasi note |
| `dokumen_id` | uint | FK ke dokumen | Relasi ke dokumen |
| `delemen_id` | ulong | FK ke elemen reference | Relasi ke elemen yang mereferensi |
| `dnote_kind` | varchar(10) | Jenis: `footnote` / `endnote` | Kategorisasi |
| `dnote_type` | varchar(30) | Tipe: `normal`, `separator`, `continuationSeparator` | Tipe note |
| `dnote_json_tree` | longtext | Konten terstruktur (JSON) | Data note |
| `dnote_xml` | longtext | Raw XML | Debugging |

---

#### 2.2.5 Tabel `dokumen_media`

Menyimpan file media (gambar) yang terdapat dalam dokumen.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `dokumen_media_id` | long | **PK** - ID media | Identifikasi media |
| `dokumen_id` | int | FK ke dokumen | Relasi ke dokumen |
| `dokumen_media_rid` | varchar(50) | Relationship ID dari OpenXML | Referensi internal |
| `dokumen_media_filename` | varchar(255) | Nama file asli | Display |
| `dokumen_media_filepath` | varchar(255) | Path file tersimpan | Akses file |
| `dokumen_media_content_type` | varchar(100) | MIME type: `image/png`, `image/jpeg` | Tipe konten |

---

### 2.3 Kategori: Format Dokumen (Detail Ekstraksi)

Tabel-tabel dalam kategori ini menyimpan detail format dari setiap elemen dokumen sesuai spesifikasi OpenXML.

#### 2.3.1 Tabel `dokumen_format_paragraf`

Menyimpan format paragraf dari OpenXML (w:pPr). Tabel dengan kolom terbanyak untuk menyimpan semua properti paragraf.

| Kolom Penting | Tipe | Deskripsi |
|---------------|------|-----------|
| `dfp_id` | uint | **PK** - ID format |
| `dfp_p_style_id` | varchar(128) | Style ID (Heading1, Normal, dll) |
| `dfp_is_list` | bool | Apakah list item |
| `dfp_jc` | varchar(15) | Alignment: `left`, `right`, `center`, `both` |
| `dfp_spacing_before_twips` | uint | Spasi sebelum paragraf |
| `dfp_spacing_after_twips` | uint | Spasi sesudah paragraf |
| `dfp_spacing_line_twips` | uint | Spasi antar baris |
| `dfp_spacing_line_rule` | varchar(10) | Rule: `auto`, `atLeast`, `exact` |
| `dfp_ind_left_twips` | uint | Indentasi kiri |
| `dfp_ind_first_line_twips` | uint | Indentasi baris pertama |
| `dfp_ind_hanging_twips` | uint | Hanging indent |
| `dfp_numpr_json` | longtext | Numbering properties (JSON) |
| `dfp_list_numId` | uint | ID numbering definition |
| `dfp_list_ilvl` | uint | Level list (0-8) |

**Total: 40+ kolom untuk menyimpan semua properti paragraf OpenXML**

---

#### 2.3.2 Tabel `dokumen_format_text`

Menyimpan format teks/run dari OpenXML (w:rPr).

| Kolom | Tipe | Deskripsi |
|-------|------|-----------|
| `dftx_id` | uint | **PK** - ID format text |
| `dftx_font_ascii` | varchar(128) | Nama font (Times New Roman, Arial) |
| `dftx_size_halfpt` | ushort | Ukuran font dalam half-points (24 = 12pt) |
| `dftx_bold` | bool | Bold atau tidak |
| `dftx_italic` | bool | Italic atau tidak |
| `dftx_underline` | varchar(10) | Style underline: `none`, `single`, `double` |
| `dftx_raw_rpr_xml` | longtext | Raw XML untuk debugging |

---

#### 2.3.3 Tabel `dokumen_format_table`

Menyimpan format tabel dari OpenXML (w:tblPr).

| Kolom | Tipe | Deskripsi |
|-------|------|-----------|
| `dft_id` | uint | **PK** - ID format table |
| `dft_tbl_style_id` | varchar(128) | Style ID tabel |
| `dft_tbl_w_type` | varchar(10) | Tipe lebar: `auto`, `dxa`, `pct` |
| `dft_tbl_w_twips` | uint | Lebar dalam twips |
| `dft_jc` | varchar(10) | Alignment: `left`, `center`, `right` |
| `dft_tbl_ind_twips` | int | Indentasi tabel |
| `dft_tbl_layout_type` | varchar(10) | Layout: `fixed`, `autofit` |
| `dft_tbl_borders_json` | longtext | Border properties (JSON) |
| `dft_raw_tblpr_xml` | longtext | Raw XML |

---

#### 2.3.4 Tabel `dokumen_format_table_row`

Menyimpan format baris tabel dari OpenXML (w:trPr).

| Kolom | Tipe | Deskripsi |
|-------|------|-----------|
| `dftr_id` | uint | **PK** - ID format row |
| `dftr_raw_trpr_xml` | longtext | Raw XML properties baris |

---

#### 2.3.5 Tabel `dokumen_format_table_cell`

Menyimpan format sel tabel dari OpenXML (w:tcPr).

| Kolom | Tipe | Deskripsi |
|-------|------|-----------|
| `dftc_id` | uint | **PK** - ID format cell |
| `dftc_raw_tcpr_xml` | longtext | Raw XML properties sel |

---

#### 2.3.6 Tabel `dokumen_format_drawing`

Menyimpan format gambar/drawing dari OpenXML (w:drawing).

| Kolom | Tipe | Deskripsi |
|-------|------|-----------|
| `dfdr_id` | ulong | **PK** - ID format drawing |
| `dfdr_is_inline` | bool | True jika inline, false jika floating |
| `dfdr_graphic_type` | varchar(20) | Tipe: `picture`, `shape`, `chart` |
| `dfdr_cx_emu` | ulong | Lebar dalam EMUs |
| `dfdr_cy_emu` | ulong | Tinggi dalam EMUs |
| `dfdr_rel_id` | varchar(64) | Relationship ID ke media |
| `dfdr_anchor_json` | longtext | Anchor positioning (JSON) |
| `dfdr_wrap_json` | longtext | Text wrapping properties (JSON) |
| `dfdr_preset_shape` | varchar(50) | Preset shape type |
| `dfdr_raw_drawing_xml` | longtext | Raw XML |

---

#### 2.3.7 Tabel `dokumen_format_field`

Menyimpan format field khusus dari OpenXML (w:fldChar, w:instrText).

| Kolom | Tipe | Deskripsi |
|-------|------|-----------|
| `dffd_id` | ulong | **PK** - ID format field |
| `dffd_field_type` | varchar(20) | Tipe: `PAGE`, `NUMPAGES`, `TOC`, `REF` |
| `dffd_instr_text` | text | Field instruction code |
| `dffd_result_text` | text | Displayed value |
| `dffd_is_locked` | bool | Field terkunci |
| `dffd_is_dirty` | bool | Field perlu recalculate |

---

### 2.4 Kategori: Aturan Validasi

Tabel-tabel dalam kategori ini menyimpan konfigurasi aturan validasi format dokumen.

#### 2.4.1 Tabel `aturan`

Menyimpan versi aturan validasi yang dapat dikonfigurasi.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `aturan_id` | uint | **PK** - ID aturan | Identifikasi aturan |
| `aturan_versi` | varchar(255) | Versi aturan (unique) | "2024-v1", "2025-v1" |
| `aturan_status` | tinyint | Status aktif (1/0) | Hanya 1 yang aktif |
| `aturan_skor_minimum` | uint | Skor minimum lulus (default: 80) | Threshold kelulusan |
| `aturan_template_file_path` | varchar(255) | Path file template aturan | Referensi |
| `aturan_created_at` | datetime | Waktu pembuatan | Audit trail |
| `aturan_updated_at` | datetime | Waktu update | Audit trail |

**Relasi:**
- 1 Aturan → banyak AturanDetail

---

#### 2.4.2 Tabel `aturan_detail`

Menyimpan detail konfigurasi aturan dalam format JSON.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `aturan_detail_id` | uint | **PK** - ID detail | Identifikasi detail |
| `aturan_id` | uint | FK ke aturan | Relasi ke aturan |
| `aturan_detail_kategori` | varchar(255) | Kategori: `ukuran_kertas`, `margin`, `judul_bab`, `paragraf`, dll | Pengelompokan |
| `aturan_detail_key` | varchar(255) | Key spesifik: `font`, `paragraph`, `spacing` | Sub-kategori |
| `aturan_detail_json_value` | longtext | Nilai aturan dalam JSON | Konfigurasi lengkap |
| `aturan_detail_status` | tinyint | Status aktif (1/0) | On/off per detail |
| `aturan_detail_catatan` | varchar(255) | Catatan/keterangan | Dokumentasi |

**Contoh JSON Value:**
```json
{
  "name": "Times New Roman",
  "size": "12",
  "style": {
    "bold": false,
    "italic": false
  }
}
```

---

### 2.5 Kategori: Hasil Validasi

Tabel-tabel dalam kategori ini menyimpan hasil validasi dokumen beserta rekomendasi perbaikan dari AI.

#### 2.5.1 Tabel `kesalahan`

Menyimpan kesalahan yang ditemukan per kategori.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `kesalahan_id` | uint | **PK** - ID kesalahan | Identifikasi kesalahan |
| `kesalahan_kategori` | varchar(100) | Kategori: `font`, `margin`, `paragraph`, `image` | Pengelompokan |
| `kesalahan_ref_tipe` | enum | `bab` atau `dokumen` | Referensi target |
| `kesalahan_ref_id` | uint | ID bab atau dokumen | FK dinamis |
| `kesalahan_lokasi` | varchar(255) | Lokasi kesalahan (halaman, baris) | Navigasi user |

**Relasi:**
- 1 Kesalahan → banyak KesalahanDetail

---

#### 2.5.2 Tabel `kesalahan_detail`

Menyimpan detail kesalahan dengan penjelasan dan rekomendasi dari AI.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `kesalahan_detail_id` | uint | **PK** - ID detail | Identifikasi |
| `kesalahan_id` | uint | FK ke kesalahan | Relasi ke parent |
| `kesalahan_detail_judul` | varchar(255) | Judul kesalahan (dari AI) | Display header |
| `kesalahan_detail_penjelasan` | varchar(255) | Penjelasan lengkap (dari AI) | Panduan user |
| `kesalahan_detail_steps` | longtext | Langkah perbaikan (JSON) | Tutorial perbaikan |
| `kesalahan_is_required` | bool | Wajib diperbaiki atau tidak | Prioritas |

---

### 2.6 Kategori: Template Dokumen

Tabel-tabel dalam kategori ini menyimpan template dokumen pelengkap buku tugas akhir.

#### 2.6.1 Tabel `template`

Menyimpan template dokumen pelengkap buku.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `template_id` | uint | **PK** - ID template | Identifikasi |
| `template_name` | varchar(255) | Nama template | Display |
| `template_status` | varchar(50) | Status: `draft`, `active` | Filter template tersedia |
| `template_docx_path` | varchar(500) | Path file DOCX | Generate dokumen |
| `template_pdf_path` | varchar(500) | Path file PDF preview | Preview user |
| `template_created_at` | datetime | Waktu pembuatan | Audit trail |

**Relasi:**
- 1 Template → banyak TemplateDetail

---

#### 2.6.2 Tabel `template_detail`

Menyimpan mapping field dalam template dokumen.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `template_detail_id` | uint | **PK** - ID detail | Identifikasi |
| `template_id` | uint | FK ke template | Relasi ke template |
| `template_detail_text` | varchar(100) | Teks placeholder yang di-highlight | Identifikasi placeholder |
| `template_detail_field` | varchar(100) | Field mapping: `nama`, `nrp`, `judul` | Sumber data |
| `template_detail_catatan` | varchar(100) | Catatan/keterangan | Dokumentasi |
| `template_detail_optional` | bool | Opsional atau wajib | Validasi |

---

#### 2.6.3 Tabel `template_generation`

Menyimpan history dokumen yang di-generate dari template.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `template_generation_id` | uint | **PK** - ID generation | Identifikasi |
| `template_id` | uint | FK ke template | Template yang digunakan |
| `mhs_nrp` | uint | NRP mahasiswa | Pemilik dokumen |
| `template_generation_docx_filepath` | varchar(255) | Path file DOCX hasil | Download |
| `template_generation_pdf_filepath` | varchar(255) | Path file PDF hasil | Preview |
| `template_generation_created_at` | datetime | Waktu generate | Audit trail |
| `template_generation_updated_at` | datetime | Waktu update | Audit trail |

---

### 2.7 Kategori: Antrian dan Processing

Tabel-tabel dalam kategori ini menyimpan data antrian dan status processing dokumen.

#### 2.7.1 Tabel `antrian`

Menyimpan antrian dokumen untuk proses ekstraksi, labeling, dan validasi.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `antrian_id` | uint | **PK** - ID antrian | Identifikasi |
| `antrian_tipe` | varchar | Tipe: `dokumen` atau `buku` | Kategorisasi |
| `buku_id` | uint | FK ke buku (opsional) | Referensi buku |
| `bab_id` | uint | FK ke bab (opsional) | Referensi bab |
| `dokumen_id` | uint | FK ke dokumen (opsional) | Referensi dokumen |
| `antrian_extraction_status` | varchar | Status ekstraksi: `in_queue`, `processing`, `completed`, `failed` | Tracking |
| `antrian_labeling_status` | varchar | Status labeling | Tracking |
| `antrian_validation_status` | varchar | Status validasi | Tracking |
| `antrian_error_message` | varchar(255) | Pesan error jika gagal | Debugging |
| `antrian_created_at` | datetime | Waktu masuk antrian | Audit trail |
| `antrian_updated_at` | datetime | Waktu update terakhir | Audit trail |

---

### 2.8 Kategori: Credentials dan API Keys

Tabel-tabel dalam kategori ini menyimpan credentials untuk integrasi dengan layanan eksternal.

#### 2.8.1 Tabel `credential_adobe`

Menyimpan credentials untuk Adobe PDF Services API.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `adobe_credentials_id` | int | **PK** - ID credential | Identifikasi |
| `adobe_client_id` | varchar(100) | Client ID dari Adobe | Autentikasi |
| `adobe_client_secret` | varchar(100) | Client Secret | Autentikasi |
| `adobe_credentials_status` | varchar(8) | Status: `active`, `inactive` | Filter aktif |
| `adobe_credentials_quota_used` | int | Quota yang sudah dipakai | Tracking usage |
| `adobe_credentials_quota_limit` | int | Limit quota (default: 500) | Rate limiting |
| `adobe_credentials_reset_date` | datetime | Tanggal reset quota | Quota management |
| `adobe_credentials_created_at` | datetime | Waktu pembuatan | Audit trail |
| `adobe_credentials_updated_at` | datetime | Waktu update | Audit trail |

---

#### 2.8.2 Tabel `credential_gemini`

Menyimpan API keys untuk Google Gemini AI.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `gemini_api_key_id` | uint | **PK** - ID API key | Identifikasi |
| `gemini_api_key_value` | varchar(512) | API key value | Autentikasi |
| `gemini_api_key_tier` | varchar(10) | Tier: `free`, `paid` | Rate limiting |
| `gemini_api_key_status` | tinyint | Status aktif (1/0) | Filter aktif |
| `gemini_api_key_usage` | uint | Jumlah penggunaan | Tracking |
| `gemini_api_key_created_at` | datetime | Waktu pembuatan | Audit trail |
| `gemini_api_key_updated_at` | datetime | Waktu update | Audit trail |

---

### 2.9 Kategori: Logging dan Monitoring

Tabel-tabel dalam kategori ini menyimpan log penggunaan API untuk monitoring dan debugging.

#### 2.9.1 Tabel `adobe_api_logs`

Menyimpan log setiap request ke Adobe PDF Services API.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `adobe_api_logs_id` | int | **PK** - ID log | Identifikasi |
| `adobe_credentials_id` | int | FK ke credential | Credential yang digunakan |
| `antrian_id` | uint | FK ke antrian | Konteks request |
| `activity` | varchar(100) | Aktivitas: `convert_to_pdf`, `extract_images` | Tipe operasi |
| `endpoint` | varchar(255) | URL endpoint | Debugging |
| `method` | varchar(10) | HTTP method | Debugging |
| `status_code` | int | HTTP status code response | Monitoring |
| `response_time_ms` | int | Response time dalam ms | Performance |
| `error_message` | varchar | Pesan error jika ada | Debugging |
| `created_at` | datetime | Waktu request | Audit trail |

---

#### 2.9.2 Tabel `llm_api_logs`

Menyimpan log penggunaan Gemini AI untuk labeling dan rekomendasi.

| Kolom | Tipe | Deskripsi | Penggunaan |
|-------|------|-----------|------------|
| `log_id` | uint | **PK** - ID log | Identifikasi |
| `log_error_code` | int | Error code jika ada | Debugging |
| `log_message` | varchar(50) | Pesan singkat | Status tracking |
| `antrian_id` | uint | FK ke antrian | Konteks request |
| `api_key_id` | uint | FK ke API key | Key yang digunakan |
| `log_tokens_used` | int | Jumlah token yang digunakan | Cost tracking |
| `log_batch_number` | int | Nomor batch (untuk batch processing) | Tracking |
| `log_total_batches` | int | Total batch | Tracking |
| `log_error_count` | int | Jumlah error dalam batch | Quality |
| `log_key_tokens_used` | int | Token per key | Rate limiting |
| `log_created_at` | datetime | Waktu log | Audit trail |

---

## 3. Diagram Relasi Database

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          DATABASE STTS (Read-Only)                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌─────────────┐     ┌─────────────────┐     ┌─────────────┐           │
│  │  mahasiswa  │────▶│  aka_ta_proposal │     │ aka_jurusan │           │
│  │  (mhs_nrp)  │     │                 │     │ (jur_kode)  │           │
│  └──────┬──────┘     └────────┬────────┘     └─────────────┘           │
│         │                     │                                          │
│         │            ┌────────▼────────┐     ┌─────────────┐           │
│         │            │    tk_dosen     │────▶│ tk_karyawan │           │
│         │            │  (dosen_kode)   │     │             │           │
│         │            └─────────────────┘     └─────────────┘           │
│         │                                                               │
└─────────┼───────────────────────────────────────────────────────────────┘
          │ (Reference only, no FK)
┌─────────▼───────────────────────────────────────────────────────────────┐
│                     DATABASE KOREKTOR BUKU (Read-Write)                  │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ MANAJEMEN BUKU & DOKUMEN                                          │  │
│  │  ┌──────────┐     ┌──────────┐     ┌──────────────┐              │  │
│  │  │   buku   │────▶│   bab    │     │   dokumen    │              │  │
│  │  └────┬─────┘     └──────────┘     └──────┬───────┘              │  │
│  └───────┼──────────────────────────────────┼────────────────────────┘  │
│          │                                   │                           │
│  ┌───────▼───────────────────────────────────▼───────────────────────┐  │
│  │ STRUKTUR DOKUMEN (EKSTRAKSI)                                      │  │
│  │  ┌────────────────┐   ┌─────────────┐   ┌──────────────────┐     │  │
│  │  │dokumen_section │──▶│dokumen_part │──▶│ dokumen_elemen   │     │  │
│  │  └────────────────┘   └─────────────┘   └────────┬─────────┘     │  │
│  │                                                   │               │  │
│  │  ┌────────────────────────────────────────────────┼─────────────┐│  │
│  │  │ FORMAT DOKUMEN                                 │             ││  │
│  │  │ ┌─────────────────┐  ┌───────────────────┐    │             ││  │
│  │  │ │format_paragraf  │  │ format_text       │◄───┘             ││  │
│  │  │ └─────────────────┘  └───────────────────┘                  ││  │
│  │  │ ┌─────────────────┐  ┌───────────────────┐                  ││  │
│  │  │ │format_table     │  │ format_drawing    │                  ││  │
│  │  │ └─────────────────┘  └───────────────────┘                  ││  │
│  │  │ ┌─────────────────┐  ┌───────────────────┐                  ││  │
│  │  │ │format_table_row │  │ format_field      │                  ││  │
│  │  │ └─────────────────┘  └───────────────────┘                  ││  │
│  │  │ ┌─────────────────┐                                         ││  │
│  │  │ │format_table_cell│                                         ││  │
│  │  │ └─────────────────┘                                         ││  │
│  │  └─────────────────────────────────────────────────────────────┘│  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  ┌────────────────────────────┐   ┌─────────────────────────────────┐  │
│  │ ATURAN VALIDASI            │   │ HASIL VALIDASI                  │  │
│  │  ┌────────┐  ┌────────────┐│   │  ┌───────────┐  ┌─────────────┐│  │
│  │  │aturan  │─▶│aturan_    ││   │  │kesalahan  │─▶│kesalahan_   ││  │
│  │  └────────┘  │detail      ││   │  └───────────┘  │detail       ││  │
│  │              └────────────┘│   │                 └─────────────┘│  │
│  └────────────────────────────┘   └─────────────────────────────────┘  │
│                                                                          │
│  ┌────────────────────────────┐   ┌─────────────────────────────────┐  │
│  │ TEMPLATE DOKUMEN           │   │ ANTRIAN & PROCESSING            │  │
│  │  ┌────────┐  ┌────────────┐│   │  ┌───────────┐                 │  │
│  │  │template│─▶│template_   ││   │  │  antrian  │                 │  │
│  │  └───┬────┘  │detail      ││   │  └───────────┘                 │  │
│  │      │       └────────────┘│   └─────────────────────────────────┘  │
│  │      ▼                     │                                         │
│  │  ┌────────────┐           │   ┌─────────────────────────────────┐  │
│  │  │template_   │           │   │ CREDENTIALS & LOGGING           │  │
│  │  │generation  │           │   │  ┌──────────┐  ┌──────────────┐ │  │
│  │  └────────────┘           │   │  │cred_adobe│  │adobe_api_logs│ │  │
│  └────────────────────────────┘   │  └──────────┘  └──────────────┘ │  │
│                                    │  ┌──────────┐  ┌──────────────┐ │  │
│                                    │  │cred_     │  │llm_api_logs  │ │  │
│                                    │  │gemini    │  │              │ │  │
│                                    │  └──────────┘  └──────────────┘ │  │
│                                    └─────────────────────────────────┘  │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Ringkasan

### 4.1 Database STTS (Read-Only)

| # | Tabel | Kolom | Fungsi |
|---|-------|-------|--------|
| 1 | `mahasiswa` | 9 | Data mahasiswa |
| 2 | `aka_jurusan` | 6 | Data jurusan |
| 3 | `aka_ta_proposal` | 7 | Proposal TA |
| 4 | `tk_dosen` | 4 | Data dosen |
| 5 | `tk_karyawan` | 2 | Status karyawan |

**Total: 5 Tabel**

---

### 4.2 Database Korektor Buku (Read-Write)

| Kategori | Tabel | Jumlah |
|----------|-------|--------|
| **Manajemen Buku & Dokumen** | `buku`, `bab`, `dokumen` | 3 |
| **Struktur Dokumen** | `dokumen_section`, `dokumen_part`, `dokumen_elemen`, `dokumen_note`, `dokumen_media` | 5 |
| **Format Dokumen** | `dokumen_format_paragraf`, `dokumen_format_text`, `dokumen_format_table`, `dokumen_format_table_row`, `dokumen_format_table_cell`, `dokumen_format_drawing`, `dokumen_format_field` | 7 |
| **Aturan Validasi** | `aturan`, `aturan_detail` | 2 |
| **Hasil Validasi** | `kesalahan`, `kesalahan_detail` | 2 |
| **Template Dokumen** | `template`, `template_detail`, `template_generation` | 3 |
| **Antrian & Processing** | `antrian` | 1 |
| **Credentials & API Keys** | `credential_adobe`, `credential_gemini` | 2 |
| **Logging & Monitoring** | `adobe_api_logs`, `llm_api_logs` | 2 |

**Total: 27 Tabel dalam 9 Kategori**


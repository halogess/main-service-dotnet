# BAB 4 - EKSTRAKSI DOKUMEN DENGAN OPEN XML SDK

## Pendahuluan Bab
Bab ini membahas proses ekstraksi dokumen Microsoft Word (`.docx`) pada sistem secara menyeluruh, mulai dari landasan format Office Open XML, desain algoritma ekstraksi konten, pemetaan data ke skema basis data, hingga strategi penyimpanan hasil ekstraksi untuk mendukung modul validasi format dokumen tugas akhir.

Secara implementatif, proses ekstraksi pada proyek ini dipusatkan pada layanan `DocxExtractionService` yang berada pada lapisan service backend. Layanan tersebut menerima path dokumen, membuka file menggunakan Open XML SDK dalam mode baca, melakukan normalisasi struktur konten dokumen, mengekstrak elemen-elemen penting (section, part, paragraf, daftar, tabel, gambar, field, dan catatan), serta menyimpannya ke model relasional dan JSON yang siap dikonsumsi modul validasi berikutnya.

Arsitektur ekstraksi dalam proyek ini bersifat:
- deterministik, karena urutan elemen dijaga melalui `delemen_sequence`;
- terstruktur, karena pemisahan `section -> part -> element`;
- extensible, karena setiap domain ekstraksi (paragraf, tabel, gambar, style) dipisahkan ke extractor khusus;
- auditable, karena XML mentah (`delemen_xml`) dan format mentah pada tabel format disimpan untuk kebutuhan pelacakan.

Dengan pendekatan tersebut, ekstraksi tidak hanya menghasilkan teks datar, tetapi juga representasi format, struktur, serta konteks visual-logis dokumen. Hal ini menjadi prasyarat utama agar validasi aturan penulisan dapat dilakukan secara presisi.

Apabila dijabarkan sebagai alur eksekusi sistem, pipeline ekstraksi pada backend berjalan dalam urutan makro berikut:
1. dokumen masuk ke antrian ekstraksi;
2. worker antrian menandai status `processing`;
3. service ekstraksi membuka dokumen dan membangun konteks resolver;
4. section dan part dibentuk;
5. body, header, footer, serta note diekstrak;
6. format-format pendukung disimpan;
7. status antrian diperbarui, kemudian dokumen melanjutkan ke tahap konversi PDF dan validasi.

Urutan tersebut menunjukkan bahwa ekstraksi diposisikan sebagai tahap transformatif utama: dari artefak dokumen biner menjadi data analitis yang siap divalidasi secara otomatis.

---

## 4.1 Arsitektur OpenXML dan Struktur DOCX

### 4.1.1 Office Open XML (OOXML) dan Format DOCX
Office Open XML (OOXML) adalah standar terbuka untuk dokumen perkantoran yang disusun dalam kumpulan berkas XML di dalam container ZIP. Pada dokumen Word, ekstensi `.docx` merepresentasikan paket yang memuat berbagai part, seperti isi utama dokumen, style, numbering, tema, relasi antar part, header/footer, footnote/endnote, dan media.

Dari sudut pandang rekayasa perangkat lunak, penggunaan OOXML memberikan beberapa keuntungan utama untuk ekstraksi:
1. Struktur data bersifat eksplisit dan dapat ditelusuri elemen per elemen.
2. Hubungan antar komponen dokumen dinyatakan melalui relationship ID (`rId`) yang konsisten.
3. Format presentasi (style, numbering, layout) dipisahkan dari konten mentah, sehingga memungkinkan resolusi properti efektif secara sistematis.
4. Dokumen dapat diproses tanpa otomasi Microsoft Word, sehingga cocok untuk pipeline backend server.

Dalam proyek ini, pendekatan ekstraksi memanfaatkan karakteristik OOXML tersebut dengan memisahkan:
- konten blok (`paragraph`, `table`, `shape`, dan lain-lain),
- metadata layout (`section`, `header/footer part`),
- metadata format (`dokumen_format_*`),
- representasi JSON konten (`delemen_json_tree`),
- dan referensi silang ke elemen sumber (`delemen_xml`).

### 4.1.2 Struktur Internal File DOCX
Secara internal, file DOCX dapat dipandang sebagai paket berisi:
- `word/document.xml` sebagai sumber utama body dokumen;
- `word/styles.xml` dan `word/stylesWithEffects.xml` untuk style;
- `word/numbering.xml` untuk definisi daftar/penomoran;
- `word/settings.xml` untuk pengaturan dokumen (misalnya `evenAndOddHeaders`, `defaultTabStop`, kompatibilitas numbering);
- `word/theme/theme1.xml` untuk tema font;
- part tambahan seperti `header*.xml`, `footer*.xml`, `footnotes.xml`, `endnotes.xml`, dan part media.

Implementasi ekstraksi pada proyek ini mengakses komponen tersebut melalui:
- `MainDocumentPart.Document.Body` sebagai sumber blok konten utama;
- `StyleDefinitionsPart` dan `StylesWithEffectsPart` untuk resolusi style;
- `NumberingDefinitionsPart` untuk generate label list;
- `DocumentSettingsPart` untuk konfigurasi pagination dan numbering behavior;
- `ThemePart` untuk pemetaan theme font;
- `FootnotesPart` dan `EndnotesPart` untuk catatan.

Dengan kata lain, ekstraksi tidak berhenti pada `document.xml`, tetapi menggabungkan beberapa part OOXML agar hasil akhir mencerminkan perilaku tampilan Word secara lebih akurat.

### 4.1.3 Komponen XML yang Menjadi Sumber Data Ekstraksi
Komponen XML kunci yang diekstrak di sistem ini dapat dipetakan sebagai berikut:

1. `w:sectPr` (Section Properties)
   - Sumber: paragraf yang mengandung section break dan `sectPr` level body.
   - Hasil: record `dokumen_section`.

2. `w:p` (Paragraph)
   - Sumber: body, header/footer, textbox, cell tabel, note.
   - Hasil: `dokumen_elemen` bertipe paragraf/list/title heading + `dokumen_format_paragraf`.

3. `w:r` (Run) dan `w:rPr` (Run Properties)
   - Sumber: konten inline di dalam paragraf.
   - Hasil: item konten JSON bertipe `text` dengan referensi `dftx_id` (format teks).

4. `w:fldSimple`, `w:fldChar`, `w:instrText`
   - Sumber: field sederhana dan field kompleks.
   - Hasil: item konten JSON bertipe `field` dengan metadata seperti `field_type`, `value`, dan `result_dftx_id`.

5. `w:tbl`, `w:tr`, `w:tc`
   - Sumber: tabel pada body atau nested.
   - Hasil: elemen bertipe `table` dengan struktur baris-sel hierarkis + format tabel level-utama.

6. `w:drawing`, `w:pict`, `w:txbxContent`
   - Sumber: gambar, shape, chart, textbox, termasuk objek floating.
   - Hasil: item konten `image/shape/chart/composite` + `dokumen_format_drawing`.

7. `w:footnoteReference`, `w:endnoteReference`, `footnotes.xml`, `endnotes.xml`
   - Sumber: referensi note pada paragraf dan isi note pada part note.
   - Hasil: `dokumen_note` yang tertaut ke `delemen_id` pemanggil.

8. `w:bookmarkStart`, `w:bookmarkEnd`, `m:oMath`
   - Sumber: marker bookmark dan formula.
   - Hasil: item `bookmarkStart`, `bookmarkEnd`, `math`.

Pemilihan komponen-komponen ini didasarkan pada kebutuhan validasi akademik: margin, heading, paragraf, daftar, tabel, gambar, dan struktur pagination.

### 4.1.4 Library Open XML SDK
Proses ekstraksi menggunakan `DocumentFormat.OpenXml` (Open XML SDK) sebagai library inti. Keunggulan praktis library ini dalam konteks proyek:

1. Strongly typed object model:
   - Elemen OOXML direpresentasikan sebagai class C# (`Paragraph`, `Table`, `SectionProperties`, dll), sehingga traversal lebih aman dan mudah dipelihara.

2. Integrasi langsung dengan part dokumen:
   - Akses ke `MainDocumentPart`, `HeaderPart`, `FooterPart`, `FootnotesPart`, dan lainnya bersifat natural.

3. Dukungan parser untuk WordprocessingML:
   - Mendukung kebutuhan ekstraksi kompleks seperti numbering, fields, drawing, dan style inheritance.

4. Efisien untuk mode baca:
   - Dokumen dibuka dalam mode `read-only` saat ekstraksi, mengurangi risiko perubahan tidak sengaja pada file sumber.

Dalam implementasi aktual, pipeline ekstraksi dibangun di atas Open XML SDK dan dikombinasikan dengan:
- `Newtonsoft.Json.Linq` untuk pembentukan JSON tree;
- Entity Framework Core untuk persistensi relasional;
- helper extractor terpisah agar logic domain modular.

Secara metodologis, kombinasi library tersebut menghasilkan pemisahan tanggung jawab yang jelas:
- Open XML SDK bertanggung jawab pada pembacaan struktur dokumen;
- extractor domain bertanggung jawab pada normalisasi dan translasi semantik elemen;
- ORM bertanggung jawab pada persistensi konsisten antar-entitas;
- serializer JSON bertanggung jawab pada representasi konten semi-terstruktur.

Model ini selaras dengan prinsip arsitektur layanan ilmiah: reproducibility, traceability, dan maintainability.

---

## 4.2 Ekstraksi Section Properties (Pengaturan Halaman)

### 4.2.1 Lokasi SectionProperties dalam Dokumen
Penentuan section merupakan tahap fondasional karena seluruh elemen body harus dimapping ke section yang benar. Pada WordprocessingML, `SectionProperties` dapat berada di dua lokasi:
1. di dalam `ParagraphProperties` (menandai akhir suatu section),
2. pada level body sebagai `sectPr` final dokumen.

Implementasi pada `DocxExtractionService` melakukan iterasi seluruh elemen body dengan aturan penting:
- elemen `SectionProperties` level body tidak dihitung sebagai indeks elemen konten;
- setiap paragraf yang memiliki `sectPr` disimpan sebagai batas section;
- `sectPr` level body ditambahkan sebagai section terakhir dengan indeks batas `int.MaxValue`.

Pendekatan ini menghasilkan daftar batas section (`sectionInfos`) yang konsisten terhadap indeks elemen body aktual. Keputusan ini krusial untuk menghindari mismatch mapping section-elemen, terutama pada dokumen multi-section kompleks.

Dalam konteks validitas data, konsistensi indexing section memiliki dampak langsung pada:
1. ketepatan join `section -> part -> element`,
2. akurasi page-setting validation per segmen dokumen,
3. akurasi evidence kesalahan yang mengacu elemen tertentu,
4. kestabilan hasil ketika dokumen mengandung section break berurutan.

Secara konseptual, algoritma deteksi section dapat dirumuskan:
1. inisialisasi `elemIndex = 0`;
2. iterasi semua child body;
3. jika elemen adalah `SectionProperties` level body, skip index;
4. jika elemen paragraf mengandung `sectPr`, simpan tuple `(elemIndex, sectPr)`;
5. increment index;
6. setelah loop, tambahkan body-level `sectPr` sebagai batas akhir.

### 4.2.2 Ekstraksi Ukuran dan Orientasi Halaman
Setelah section terdeteksi, setiap `sectPr` diproses oleh `SectionExtractor`. Komponen halaman yang diekstrak meliputi:
- `PageSize`: lebar/tinggi halaman dalam twips,
- `PageOrientation`: portrait atau landscape,
- normalisasi orientasi (swap width-height bila diperlukan agar konsisten dengan orientasi logis).

Dengan normalisasi tersebut, model `DokumenSection` menyimpan dimensi halaman yang dapat dipakai langsung pada validasi page settings tanpa perlu interpretasi ulang di tahap validasi.

Secara teknis, hasil ekstraksi ukuran/orientasi disimpan pada:
- `dsec_page_width_twips`,
- `dsec_page_height_twips`,
- `dsec_orientation`.

Format unit twips dipertahankan karena:
1. merupakan unit native WordprocessingML;
2. meminimalkan kehilangan presisi;
3. dapat dikonversi ke cm/pt di layer validasi sesuai kebutuhan aturan.

### 4.2.3 Ekstraksi Properti Section yang Mempengaruhi Pagination
Selain ukuran halaman, pagination dipengaruhi sejumlah properti section lain yang juga diekstrak:
- margin atas/bawah/kiri/kanan (`PageMargin`);
- jarak header dan footer dari tepi halaman;
- gutter dan posisi gutter;
- format nomor halaman (`PageNumberType.Format`);
- awal nomor halaman (`PageNumberType.Start`);
- jumlah kolom teks (`Columns.ColumnCount`);
- tipe break section (`SectionType`, misal `nextPage`, `continuous`, `evenPage`, `oddPage`);
- `TitlePage` (header/footer halaman pertama berbeda);
- `DifferentOddEven` yang disinkronkan dari pengaturan dokumen (`evenAndOddHeaders`).

Pengambilan properti ini menghasilkan model section yang kaya konteks dan langsung relevan untuk:
- validasi ukuran kertas,
- validasi margin,
- validasi header/footer offset,
- validasi aturan penomoran halaman tiap jenis section (`awal`, `isi`, `akhir`, `lampiran`).

### 4.2.4 Pembentukan DokumenPart (Body, Header, Footer)
Setiap section yang berhasil diekstrak menjadi parent untuk beberapa part:
1. `body` (wajib, satu per section),
2. `header` (`default`, `first`, `even`) jika direferensikan,
3. `footer` (`default`, `first`, `even`) jika direferensikan.

Pada implementasi, service membangun `partMap`:
- key: `dsec_id`,
- value: dictionary `partKey -> dpart_id`.

Langkah pembentukan part:
1. Buat `body` part untuk semua section.
2. Baca `HeaderReference` dan `FooterReference` pada `sectPr`.
3. Konversi tipe OpenXML (`First/Even/Default`) menjadi posisi part.
4. Buat `dokumen_part` bila belum ada kombinasi part yang sama.
5. Ambil `HeaderPart`/`FooterPart` via `GetPartById`.
6. Ekstrak isi part dengan method `ExtractPartContent`.

Desain ini memberi dua manfaat ilmiah-praktis:
1. konten header/footer tidak bercampur dengan body;
2. validasi berbasis konteks section dapat dilakukan dengan join `section -> part -> element`.

---

## 4.3 Ekstraksi Paragraf dan Konten Teks

### 4.3.1 Struktur Paragraph Element
Paragraf (`w:p`) adalah satuan konten paling dominan pada dokumen ilmiah. Dalam pipeline ekstraksi ini, paragraf diperlakukan sebagai block element yang dapat menghasilkan:
- satu record elemen bertipe paragraf/list/heading/title/subtitle,
- sekumpulan item inline di `content`,
- satu record format paragraf (`dokumen_format_paragraf`),
- dan beberapa record format inline tambahan (teks, drawing, field) sesuai isi.

Klasifikasi tipe paragraf dilakukan melalui `DetectParagraphType` dengan prioritas:
1. style heading/title/subtitle,
2. numbering efektif (direct atau style chain),
3. fallback `paragraph`.

Jika paragraf berisi numbering, tipe dapat menjadi `list-item-{numId}-{ilvl}`. Jika style heading terdeteksi, tipe menjadi `h1`, `h2`, dst.

### 4.3.2 Ekstraksi Paragraf Properties
Format paragraf diekstrak menggunakan `ParagraphFormatExtractor` dengan pendekatan effective properties:
- menggabungkan default dokumen, style inheritance, numbering-level properties, dan direct paragraph properties;
- menormalisasi properti spacing, indentasi, alignment, outline, serta toggle layout.

Properti penting yang disimpan antara lain:
- `dfp_is_list`, `dfp_list_numId`, `dfp_list_ilvl`;
- `dfp_spacing_*` (before/after/line/rule);
- `dfp_ind_*` (left/right/firstLine/hanging/start/end);
- `dfp_jc` (alignment);
- flag pagination seperti `keep_next`, `keep_lines`, `page_break_before`, `widow_control`;
- raw XML `dfp_raw_ppr_xml` untuk audit.

Implementasi juga memasukkan penyesuaian hanging indent efektif untuk list dengan mempertimbangkan:
- label numbering aktual yang digenerate,
- level numbering,
- default tab stop,
- kompatibilitas dokumen terhadap virtual tab stop.

Hal ini penting karena dokumen Word riil sering menampilkan indentation list yang berbeda dari direct XML jika kompatibilitas tertentu aktif.

Secara kualitas data, ekstraksi properti paragraf dengan mekanisme effective resolution memiliki implikasi:
- mengurangi false positive validasi yang muncul akibat membaca direct formatting saja;
- mengurangi false negative pada dokumen template yang heavily style-driven;
- meningkatkan konsistensi penilaian antar dokumen berbeda sumber (manual style vs direct format).

Dengan kata lain, resolusi properti efektif merupakan syarat epistemik agar kesimpulan validasi benar-benar merepresentasikan tampilan dokumen final.

### 4.3.3 Ekstraksi Konten Paragraf
Konten paragraf diekstrak oleh `ExtractParagraphContentSorted` menjadi array item inline. Jenis konten yang dapat dihasilkan:
- `text`,
- `field`,
- `math`,
- `image`,
- `shape`,
- `chart`,
- `table` (untuk nested table dalam textbox),
- item lain yang diturunkan dari drawing/pict/textbox.

Fitur-fitur utama ekstraksi konten paragraf:
1. Numbering text generation:
   - Label numbering ditambahkan sebagai item `text` awal, kecuali untuk paragraf caption tertentu.

2. Caption-aware logic:
   - Paragraf caption yang mengandung field `SEQ Gambar/Tabel` tidak dipaksa prepend numbering list agar tidak menggandakan nomor.

3. Run aggregation:
   - Run berturut-turut dengan signature format yang sama digabung, lalu disimpan sebagai satu item `text` dengan satu `dftx_id`, sehingga output lebih kompak namun tetap melacak format.

4. Nested complex field handling:
   - Menggunakan stack konteks field (`begin/separate/end`) untuk menjaga hasil field kompleks termasuk `instrText`, result text, lock/dirty state.

5. Anchored drawing sorting:
   - Item anchored (floating) dengan koordinat vertikal dipisahkan sementara, lalu diurutkan menurut `Y` agar urutan baca lebih representatif.

6. Textbox content extraction:
   - Teks dan tabel di dalam `TextBoxContent` diekstrak recursively.

Hasil akhir berupa `content` JSON terurut, kemudian dibungkus menjadi objek elemen dan disimpan ke `dokumen_elemen`.

Pada lapisan ketahanan proses, ekstraksi elemen body juga menerapkan isolasi kesalahan per elemen:
1. jika satu elemen gagal diproses (misalnya struktur tabel tidak terduga), exception ditangkap lokal;
2. ringkasan XML elemen dicatat pada log untuk diagnosa;
3. pipeline melanjutkan ke elemen berikutnya.

Strategi ini menjaga rasio keberhasilan ekstraksi pada dokumen heterogen dan mencegah kegagalan total akibat satu elemen anomali.

---

## 4.4 Ekstraksi Run dan Format Teks

### 4.4.1 Struktur Run Element
Run (`w:r`) adalah unit inline terkecil yang membawa teks dan format karakter. Dalam satu paragraf, run dapat bercampur dengan:
- teks biasa,
- tab, line break,
- field marker,
- drawing inline,
- dan elemen lain.

Pipeline ekstraksi memproses run secara detail karena validasi ilmiah sering menuntut ketelitian format karakter (font family, font size, bold, italic, underline).

Setiap run dianalisis terhadap konteks style paragraf agar format efektif dapat ditentukan. Dengan demikian, format yang disimpan tidak hanya direct run property, tetapi dapat mencerminkan inheritance style.

### 4.4.2 Ekstraksi Font Properties
Format teks run disimpan pada model `DokumenFormatText` dengan atribut inti:
- `dftx_font_ascii`,
- `dftx_size_halfpt`,
- `dftx_bold`,
- `dftx_italic`,
- `dftx_underline`,
- `dftx_raw_rpr_xml`.

Ukuran font disimpan dalam half-point mengikuti standar Word (`24` berarti `12pt`). Pendekatan ini menjaga kompatibilitas dengan sumber XML dan memudahkan konversi ke satuan point saat validasi.

Proses ekstraksi font mempertimbangkan:
1. direct run properties,
2. style inheritance,
3. theme font mapping,
4. language/script context pada theme font.

Dengan strategi tersebut, nilai font yang didapat lebih mendekati hasil render Word aktual dibanding hanya membaca `w:rPr` secara langsung.

Pada praktiknya, perbedaan antara direct dan effective font sering muncul pada:
- judul bab/subbab yang diwarisi dari style template;
- isi daftar pustaka dengan style karakter khusus;
- field otomatis pada footer (misalnya page number) yang formatnya diwarisi dari style part.

Karena alasan tersebut, pencatatan format teks sebagai entitas terpisah (`dokumen_format_text`) menjadi landasan penting untuk validasi presisi tinggi pada level karakter.

### 4.4.3 Ekstraksi Text Styling
Selain font name/size, styling penting lain yang diekstrak meliputi:
- bold,
- italic,
- underline (termasuk style underline),
- serta indikator format result field (`result_dftx_id` pada item `field`).

Untuk field:
- format field code dan result dipisahkan;
- `field` item menyimpan metadata tipe field dan value result;
- format teks hasil field dapat direferensikan melalui `result_dftx_id`.

Pendekatan ini relevan untuk kasus penomoran halaman, caption sequence, cross-reference, dan field otomatis lain yang umum pada dokumen ilmiah.

---

## 4.5 Ekstraksi Numbering dan List

### 4.5.1 Struktur NumberingDefinitionsPart
`NumberingDefinitionsPart` memuat:
- `AbstractNum` (template pola numbering),
- `NumberingInstance` (`numId` sebagai instance nyata),
- level-level (`ilvl`) dengan format, start, suffix, dan teks pola (`lvlText`).

Pada dokumen nyata, beberapa paragraf dapat menggunakan style numbering, direct numbering, atau kombinasi keduanya. Oleh karena itu, ekstraksi list tidak dapat mengandalkan satu sumber tunggal.

Pipeline proyek ini memadukan:
- resolusi numbering efektif dari style resolver,
- pembacaan `numPr` direct,
- dan state continuation antar paragraf.

### 4.5.2 Ekstraksi Level Properties
Saat generate label list, sistem mengambil properti level efektif:
- `start numbering value`,
- `number format` (`decimal`, `decimalZero`, `lowerLetter`, `upperRoman`, dsb),
- `level text pattern`,
- `level suffix` (`tab`, `space`, `nothing`),
- `level override` dan `start override` pada numbering instance.

Counter numbering disimpan dalam dictionary bertingkat:
- key pertama `numId`,
- key kedua `ilvl`,
- value counter saat ini.

Setiap kali item list baru diproses:
1. counter level aktif dinaikkan atau diinisialisasi;
2. counter level lebih dalam di-reset sesuai aturan restart;
3. placeholder `%1`, `%2`, dst pada `lvlText` diganti berdasarkan counter.

### 4.5.3 Ekstraksi Numbering dari Paragraf
Deteksi numbering paragraf dilakukan berjenjang:
1. cek `numPr` direct pada paragraf;
2. jika tidak ada, cek numbering efektif dari style chain;
3. jika style numbering restart tetapi abstrak sama, dapat menggunakan continuation `numId` sebelumnya berdasarkan state.

Implementasi `ParagraphExtractor` menyimpan state continuation:
- style id terakhir,
- numId terakhir,
- level terakhir,
- abstract num terakhir,
- sumber numbering (direct/continuation).

Aturan penting yang dipakai:
- paragraf netral tanpa numbering tidak selalu mereset continuation;
- paragraf disabled numbering yang kosong dapat diperlakukan netral;
- paragraf disabled numbering dengan konten terlihat akan mereset continuation.

Aturan tersebut sangat penting untuk dokumen akademik yang sering memiliki separator atau caption di antara blok list.

### 4.5.4 Generate Numbering Text
`NumberingExtractor.GetNumberingText` menghasilkan label list final untuk ditampilkan di konten JSON. Dukungan format meliputi:
- decimal,
- decimal zero-padded,
- lower/upper letter,
- lower/upper roman,
- bullet.

Untuk bullet, sistem melakukan normalisasi karakter dari font Symbol/Wingdings ke Unicode agar konsisten lintas perangkat. Suffix level (`tab/space/nothing`) juga diterapkan agar representasi teks mendekati tampilan Word.

Label yang dihasilkan disisipkan sebagai item `text` awal pada konten paragraf list. Nilai ini kemudian dipakai juga dalam beberapa validasi struktur dan penomoran.

Sebagai ilustrasi konseptual:
1. jika `lvlText = "%1.%2"` pada level 1,
2. counter level 0 = 3 dan level 1 = 2,
3. maka label yang dibentuk adalah `3.2` ditambah suffix level.

Untuk kasus `decimalZero`, angka dipadatkan ke format dua digit (misal `01`, `02`, ...), sehingga dokumen yang mensyaratkan pola numerik tetap dapat divalidasi secara eksplisit.

---

## 4.6 Ekstraksi Tabel

### 4.6.1 Struktur Tabel Elemen
Tabel diekstrak dari elemen `w:tbl` melalui `TableExtractor`. Struktur keluaran JSON pada level elemen berbentuk:
- `dft_id` (referensi format tabel),
- `content.rows` berisi daftar baris,
- setiap baris memiliki daftar sel,
- setiap sel memiliki konten bertingkat.

Konten sel dapat berupa:
- paragraf (dengan tipe dan `dfp_id`),
- nested table (rekursif).

Struktur ini menjaga hirarki logis tabel sekaligus memungkinkan validasi format pada tiap level.

Selain itu, desain `rows -> cells -> content` memungkinkan pemisahan dua dimensi analisis:
1. analisis struktural tabel (merge, alignment, border, posisi caption);
2. analisis semantik isi tabel (jenis konten, ketepatan font, dan paragraf di dalam sel).

Keduanya dibutuhkan untuk aturan format ilmiah yang tidak hanya menilai tampilan tabel, tetapi juga konsistensi isi tabel terhadap aturan paragraf.

### 4.6.2 Ekstraksi Table Properties
Format tabel diekstrak oleh `TableFormatExtractor` dan disimpan pada `dokumen_format_table`. Properti yang ditangkap meliputi:
- table style id,
- lebar tabel dan tipe lebar,
- alignment tabel,
- indentation tabel,
- layout type (`fixed`/`autofit`),
- border tabel (JSON),
- properti posisi floating tabel (`tblpPr`) dalam JSON,
- raw XML table properties.

Extractor tabel bekerja bersama `TableStyleResolver` untuk mendapatkan effective properties, sehingga style inheritance tabel turut diperhitungkan.

### 4.6.3 Ekstraksi Row dan Cell
Pada skema aktif, baris dan sel tabel dipertahankan sebagai struktur JSON bertingkat di dalam elemen tabel:
- setiap row berisi array `cells`,
- setiap cell berisi array `content`,
- setiap item di dalam cell tetap dapat mereferensikan `dfp_id` dan `dftx_id` bila kontennya berupa paragraf atau teks berformat.

Konversi indeks baris-kolom tetap dipertahankan selama proses ekstraksi agar traversal nested table dan pembacaan isi sel tetap stabil.

### 4.6.4 Ekstraksi Konten Cell
Konten sel diproses sebagai mini-document block:
1. jika elemen sel adalah paragraf:
   - tipe paragraf dideteksi,
   - konten inline diekstrak,
   - format paragraf diekstrak dan ditautkan melalui `dfp_id`.
2. jika elemen sel adalah tabel nested:
   - `ConvertTableToJsonAsync` dipanggil rekursif.

Dengan pendekatan ini, tabel kompleks (termasuk nested table) tetap dapat direpresentasikan dalam JSON terstruktur tanpa kehilangan konteks format.

Namun demikian, nested table meningkatkan kompleksitas traversal dan kedalaman JSON. Oleh sebab itu, extractor menggunakan pendekatan rekursif yang tetap menjaga tipe elemen dan referensi format di tiap level, agar parser validasi tidak kehilangan konteks ketika menelusuri konten multi-level.

---

## 4.7 Ekstraksi Gambar dan Drawing

### 4.7.1 Struktur Drawing Elemen
Objek gambar dan shape modern pada Word disimpan dalam `w:drawing`, sedangkan beberapa dokumen lama dapat memakai `w:pict` (VML). `DrawingExtractor` memproses `w:drawing` dan mengidentifikasi konten menjadi:
- `image`,
- `chart`,
- `shape`,
- `composite`,
- atau `textbox` sebagai konten shape.

Selain itu, extractor membaca identitas shape (`docPr`) dan memeriksa apakah drawing bersifat inline atau anchored.

Pada dokumen akademik modern, shape sering digunakan untuk:
- diagram arsitektur,
- flowchart metodologi,
- anotasi visual dalam pembahasan.

Karena itu, ekstraksi drawing tidak dibatasi pada gambar raster saja, melainkan juga mencakup struktur shape dan textbox agar isi textual pada diagram tetap terambil sebagai data yang dapat divalidasi.

### 4.7.2 Ekstraksi Image Properties
Format drawing disimpan pada `dokumen_format_drawing` melalui `DrawingFormatExtractor`, dengan atribut seperti:
- `dfdr_is_inline`,
- `dfdr_graphic_type`,
- ukuran extent (`cx/cy` dalam EMU),
- relationship id media,
- properti anchor/wrapping (JSON),
- preset shape,
- raw XML drawing.

Pada sisi konten elemen, item `image` umumnya menyimpan `rId`. Relasi ini memungkinkan proses lanjutan untuk pemetaan ke media nyata.

### 4.7.3 Ekstraksi Anchor Properties
Untuk objek floating (anchor), extractor membaca posisi vertikal (`positionV/posOffset`) dan menyimpan nilai `_sortY` internal. Nilai ini digunakan untuk mengurutkan item anchored agar urutan baca lebih stabil.

Pada level body, `FloatingElementHelper` juga mendeteksi elemen floating:
- tabel dengan `tblpPr`,
- paragraf yang berisi drawing anchor.

Elemen-elemen floating dalam cluster lokal diurutkan berdasarkan posisi `Y` untuk mengurangi ketidaksesuaian urutan XML terhadap urutan visual dokumen.

Perlu dicatat bahwa urutan XML bawaan Word tidak selalu identik dengan urutan baca visual pada halaman saat objek floating digunakan. Dengan demikian, strategi reordering berbasis posisi vertikal adalah kompromi praktis untuk meningkatkan konsistensi interpretasi dokumen pada layer validasi otomatis.

### 4.7.4 Referensi Media dari Package
Dalam pipeline ekstraksi inti saat ini, elemen konten menyimpan referensi media (`rId`) dan format drawing. Pendekatan ini menjaga jalur ekstraksi utama tetap ringan, sementara hubungan ke image part di dalam package DOCX tetap dapat ditelusuri saat dibutuhkan untuk analisis atau rendering lanjutan.

---

## 4.8 Resolusi Style dan Inheritance

### 4.8.1 Struktur StylesPart
`StylesPart` memuat:
- `docDefaults` sebagai default global,
- style definitions per kategori (paragraph, run, table),
- relasi `basedOn` antar style.

Selain `StylesPart`, proyek juga memanfaatkan:
- `StylesWithEffectsPart`,
- `ThemePart`,
- `NumberingDefinitionsPart`,
untuk mengonstruksi properti efektif yang lebih representatif.

Karena banyak dokumen akademik mengandalkan style template resmi kampus, resolusi style menjadi komponen kritikal agar validasi tidak bias oleh direct formatting semata.

### 4.8.2 Algoritma Style Resolution
Secara konseptual, algoritma resolusi style yang dipakai mengikuti urutan:
1. mulai dari `docDefaults`,
2. telusuri rantai `basedOn` dari style aktif,
3. gabungkan properti style secara berurutan (parent ke child),
4. terapkan properti numbering-level jika relevan,
5. terapkan direct properties dari elemen aktual sebagai prioritas tertinggi.

Untuk tabel, mekanisme serupa dijalankan oleh `TableStyleResolver` dan `TablePropertyMerger`, termasuk conditional style berbasis posisi.

Algoritma ini memberikan dua keuntungan utama:
- hasil validasi lebih mendekati tampilan Word aktual,
- robust terhadap dokumen yang minim direct formatting namun kaya style template.

Dalam perspektif rekayasa kualitas, style resolution juga berfungsi sebagai mekanisme standardisasi input. Dokumen dari sumber berbeda (template lama, template baru, atau hasil salin-tempel) dapat direduksi ke representasi properti efektif yang relatif seragam, sehingga aturan validasi dapat diterapkan secara konsisten.

### 4.8.3 Effective Properties Calculation
Effective properties dihitung untuk dua domain utama:
1. paragraf (`EffectiveParagraphProperties`),
2. run teks (`EffectiveRunProperties`).

Nilai hasil kalkulasi kemudian dipetakan ke model persistensi:
- `DokumenFormatParagraf`,
- `DokumenFormatText`.

Contoh dampak praktis:
- paragraf yang tidak punya `w:jc` direct tetap bisa dikenali alignment-nya dari style;
- ukuran font run dapat diturunkan dari style/theme bila tidak ada `w:sz` direct;
- indentasi list dapat mencerminkan definisi numbering-level.

Pendekatan effective properties ini sangat penting untuk menjaga validitas evaluasi aturan format ilmiah yang biasanya berbasis style institusional.

Secara konseptual, prioritas merge properti dapat diringkas:
1. `docDefaults` sebagai baseline;
2. `basedOn` chain dari style aktif;
3. kontribusi numbering level (jika elemen list);
4. direct properties pada elemen.

Properti pada prioritas lebih tinggi menimpa nilai sebelumnya bila terjadi konflik. Aturan precedence ini menjaga determinisme hasil ekstraksi.

### 4.8.4 Theme Font Resolution
Theme font resolver memetakan referensi font bertema (major/minor) menjadi nama font konkret. Proses ini diperkaya dengan language resolver agar pemilihan font dapat menyesuaikan script/locale teks.

Tanpa theme resolution, ekstraksi font berisiko menghasilkan nilai referensi abstrak yang tidak mewakili font render aktual. Dengan resolver ini, output format teks menjadi lebih operasional untuk:
- validasi kesesuaian font,
- perbandingan lintas run,
- dan pembuatan evidence kesalahan yang presisi.

---

## 4.9 Skema Database untuk Hasil Ekstraksi

### 4.9.1 Skema Database untuk Hasil Ekstraksi
Hasil ekstraksi dipetakan ke skema relasional dengan prinsip pemisahan concern:

1. Struktur dokumen:
   - `dokumen_section`
   - `dokumen_part`
   - `dokumen_elemen`

2. Catatan:
   - `dokumen_note` (footnote/endnote)

3. Format:
   - `dokumen_format_paragraf`
   - `dokumen_format_text`
   - `dokumen_format_table`
   - `dokumen_format_drawing`

Relasi inti:
- `section` memiliki banyak `part`;
- `part` memiliki banyak `element`;
- `note` dapat menaut ke `element` sumber referensi;
- setiap elemen menyimpan JSON konten dan XML mentah;
- format disimpan pada tabel masing-masing lalu direferensikan melalui ID di JSON konten elemen.

Desain ini menyeimbangkan normalisasi data dengan fleksibilitas JSON untuk konten semi-terstruktur.

Dalam praktik query validasi, pola relasi tersebut memudahkan operasi berikut:
1. filter elemen body untuk satu dokumen/bab tertentu;
2. ambil seluruh format paragraf yang direferensikan subset elemen;
3. hitung distribusi elemen per section;
4. telusuri note berdasarkan elemen referensi.

Dengan demikian, skema ini bukan hanya tempat simpan, tetapi juga fondasi performa analitik validasi.

### 4.9.2 Format JSON untuk Konten Elemen
Kolom `delemen_json_tree` menyimpan struktur konten elemen dalam JSON. Pola umum:

1. Paragraf/list/heading:
```json
{
  "dfp_id": 123,
  "content": [
    { "type": "text", "dftx_id": 456, "value": "Contoh teks" },
    { "type": "field", "field_type": "PAGE", "result_dftx_id": 457, "value": "1" }
  ]
}
```

2. Tabel:
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
                "content": [{ "type": "text", "value": "Isi sel" }]
              }
            ]
          }
        ]
      }
    ]
  }
}
```

3. Drawing:
```json
{
  "type": "image",
  "dfdr_id": 999,
  "rId": "rId12"
}
```

Strategi ini memungkinkan parser validasi membaca konten lintas tipe tanpa kehilangan referensi format.

### 4.9.3 Sequence dan Ordering
Urutan elemen disimpan pada `delemen_sequence` dengan prinsip:
- body: sequence global per part body dalam alur ekstraksi yang telah diproses reordering floating;
- header/footer: sequence lokal per part (dimulai dari 1 pada tiap part).

Selain sequence numerik, kestabilan ordering dijaga oleh:
1. mapping elemen ke section berdasarkan indeks body non-`SectionProperties`;
2. reordering cluster floating berdasarkan posisi vertikal;
3. sorting item anchored di dalam paragraf berdasarkan `_sortY`.

Konsistensi ordering sangat penting karena:
- validasi konten bergantung pada urutan logis elemen;
- deteksi konteks tetangga (sebelum/sesudah) membutuhkan sequence yang stabil;
- pengaitan note ke elemen referensi dilakukan melalui sequence-to-id map pasca persistensi.

Pada implementasi, pengaitan note dilakukan dengan strategi dua tahap:
1. saat ekstraksi body, referensi footnote/endnote dipetakan ke sequence elemen pemanggil;
2. setelah elemen tersimpan, sequence dipetakan ke `delemen_id` aktual untuk membentuk foreign key `dokumen_note`.

Pendekatan ini menghindari ketergantungan terhadap ID database yang belum tersedia pada saat traversal XML awal.

### 4.9.4 Optimisasi Penyimpanan
Optimisasi pada layer penyimpanan dan keandalan ekstraksi dilakukan melalui beberapa strategi:

1. Pemisahan tabel format:
   - Menghindari duplikasi kolom format di tabel elemen utama.
   - Memudahkan query validasi spesifik domain (misal hanya format paragraf atau tabel).

2. Penyimpanan JSON + XML mentah:
   - JSON mempermudah konsumsi validator.
   - XML mentah mempertahankan jejak sumber untuk audit/debug.

3. Error isolation pada ekstraksi elemen:
   - Jika satu elemen gagal diekstrak, sistem melanjutkan elemen lain dan menulis log error terstruktur.
   - Pendekatan ini meningkatkan fault tolerance pada dokumen heterogen.

4. Modular extractor:
   - Perubahan pada satu domain (misal tabel atau drawing) tidak merusak keseluruhan pipeline.
   - Memudahkan evolusi bertahap.

5. Integrasi antrian background:
   - Ekstraksi dijalankan melalui queue (`in_queue -> processing -> completed/failed`), sehingga beban proses tidak menahan request upload.
   - Cocok untuk dokumen besar dan skenario multi-pengguna.

6. Normalisasi referensi dokumen:
   - Dukungan `ref_tipe` (`dokumen`/`bab`) membuat pipeline dapat dipakai untuk dua mode validasi tanpa duplikasi arsitektur.

Secara keseluruhan, desain penyimpanan ini mendukung tujuan ilmiah sistem: ketelitian validasi format, keterlacakan sumber data, dan skalabilitas operasional.

Sebagai catatan pengembangan lanjutan, optimisasi berikut dapat dipertimbangkan tanpa mengubah model ilmiah ekstraksi:
1. batching `SaveChanges` pada subset operasi format untuk mengurangi round-trip database;
2. normalisasi tambahan pada item JSON yang sangat besar agar biaya parsing validasi lebih rendah;
3. metadata checksum elemen untuk mendukung incremental re-extraction.

Meskipun demikian, implementasi saat ini telah menunjukkan keseimbangan yang baik antara ketelitian data, keterbacaan arsitektur, dan keandalan proses batch.

---

## Penutup Bab
Bab ini telah menguraikan proses ekstraksi dokumen berbasis Open XML SDK secara komprehensif, mulai dari fondasi format OOXML hingga pemodelan data hasil ekstraksi di basis data. Implementasi pada proyek menunjukkan bahwa ekstraksi dokumen akademik yang andal membutuhkan kombinasi:
- parsing struktur XML,
- resolusi style dan inheritance,
- normalisasi ordering konten,
- serta desain penyimpanan yang mempertahankan konteks format dan struktur.

Dengan pipeline `section -> part -> element -> format`, sistem mampu menyediakan representasi dokumen yang kaya dan siap untuk tahap validasi aturan penulisan. Representasi tersebut tidak hanya menyimpan teks, tetapi juga konteks tipografi, struktur daftar, konfigurasi halaman, tabel, gambar, dan relasi catatan, sehingga modul validasi dapat bekerja dengan tingkat presisi yang dibutuhkan pada domain penulisan ilmiah.

Bab ini sekaligus menjadi landasan metodologis bahwa kualitas validasi sangat ditentukan oleh kualitas ekstraksi. Oleh karena itu, pengembangan lanjutan sistem sebaiknya tetap mempertahankan prinsip modularitas extractor, konsistensi model data, dan keterlacakan antara output ekstraksi dengan sumber XML dokumen.

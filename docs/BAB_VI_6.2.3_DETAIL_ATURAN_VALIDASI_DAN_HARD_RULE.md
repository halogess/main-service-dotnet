# BAB VI - DETAIL ATURAN VALIDASI DAN HARD RULE

## 6.2.3 Aturan Validasi Aktif

Aturan validasi aktif adalah sumber acuan yang dipakai sistem untuk memeriksa kesesuaian format dokumen. Pada implementasi saat ini, satu versi pedoman disimpan pada tabel `aturan`, sedangkan rincian parameter pemeriksaan disimpan pada tabel `aturan_detail`. Worker validasi mengambil `aturan` yang berstatus aktif, kemudian membaca `aturan_detail_json_value` dari setiap aturan detail yang relevan. Setiap aturan detail dibedakan dengan key canonical runtime seperti `page_settings`, `nomor_halaman`, `judul_bab`, `judul_subbab`, `paragraf`, `item_daftar`, `gambar`, `tabel`, `kode`, `rumus`, dan `footnote`. Key tersebut digunakan oleh backend untuk menentukan model aturan mana yang harus dipakai oleh modul validasi.

Nilai aturan disimpan dalam bentuk JSON agar setiap jenis aturan dapat memiliki struktur parameter yang berbeda. Parameter yang dapat dikonfigurasi umumnya memakai pola `value`, `is_editable`, dan `is_hard_constraint`. Field `value` berisi nilai acuan yang dibandingkan dengan data hasil ekstraksi dan analisis visual. Field `is_editable` menunjukkan apakah nilai tersebut dapat diubah melalui pengelolaan aturan. Field `is_hard_constraint` menunjukkan apakah pelanggaran terhadap parameter tersebut dianggap sebagai pelanggaran wajib yang dapat membuat hasil validasi tidak lolos walaupun skor memenuhi nilai minimum.

### 6.2.3.1 Aturan Pengaturan Halaman

Aturan Pengaturan Halaman menggunakan key runtime `page_settings`. Aturan ini memeriksa konfigurasi global halaman yang berasal dari section dokumen. Pemeriksaan dilakukan lebih awal karena pengaturan halaman menjadi konteks dasar bagi elemen lain yang muncul pada halaman.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Paper | Ukuran kertas dan orientasi halaman | `dokumen_section` dan `aturan_detail_json_value` |
| Margin | Margin atas, bawah, kiri, dan kanan | `dokumen_section` |
| Header/footer | Jarak header dari atas dan footer dari bawah | `dokumen_section`, header part, footer part |
| Gutter | Ukuran gutter dan posisi gutter | `dokumen_section` |
| Column | Jumlah kolom halaman | `dokumen_section` |
| Akhir halaman | Maksimal baris kosong akhir halaman dan pencegahan halaman kosong | Data section, elemen halaman, dan hasil visual |

Runtime membaca nilai `page_settings` dari `aturan_detail_json_value`, lalu membandingkannya dengan properti section hasil ekstraksi. Jika admin menandai parameter seperti margin atau ukuran kertas sebagai hard rule, pelanggaran terhadap parameter tersebut ditandai sebagai hard constraint pada hasil validasi. Aturan ini tidak bergantung pada teks isi bab, tetapi pada konfigurasi halaman yang melekat pada section.

### 6.2.3.2 Aturan Nomor Halaman

Aturan Nomor Halaman menggunakan key runtime `nomor_halaman`. Aturan ini memeriksa keberadaan, posisi, dan format nomor halaman pada header atau footer. Pemeriksaan nomor halaman dilakukan bersama pengaturan halaman karena keduanya bergantung pada section dan part header/footer.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Numbering | Format nomor halaman, misalnya decimal | Header/footer part dan `aturan_detail_json_value` |
| Font | Nama font, ukuran font, bold, italic, dan underline nomor halaman | `dokumen_format_text` |
| Paragraph | Indentasi, spacing, dan format paragraf nomor halaman | `dokumen_format_paragraf` |
| Posisi default | Lokasi nomor pada header/footer dan alignment | Header/footer part, section, dan data visual |
| Different first page | Pengaturan halaman pertama berbeda, lokasi, dan alignment halaman pertama | Section properties dan header/footer |
| Different odd/even | Pengaturan halaman genap/ganjil bila tersedia | Section properties dan header/footer |
| Struktur konten | Pencegahan baris atau konten tambahan pada area nomor halaman | Elemen header/footer dan data visual |

Key `nomor_halaman` membantu sistem membedakan aturan nomor halaman dari aturan halaman umum. Jika format nomor, posisi, atau style teks nomor halaman tidak sesuai, sistem membentuk error sesuai parameter yang dilanggar. Jika parameter tersebut memiliki `is_hard_constraint: true`, error nomor halaman dapat menjadi penyebab status validasi tidak lolos.

### 6.2.3.3 Aturan Judul Bab

Aturan Judul Bab menggunakan key runtime `judul_bab`. Aturan ini memeriksa elemen yang diklasifikasikan sebagai judul bab setelah proses klasifikasi elemen selesai. Pemeriksaan dilakukan terhadap bentuk teks, format paragraf, numbering, dan struktur konten setelah judul bab.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Font | Nama font dan ukuran font judul bab | `dokumen_elemen` dan `dokumen_format_text` |
| Font style | Bold, italic, dan underline | `dokumen_format_text` |
| Paragraph | Alignment, indentasi, first line indent, spacing before, spacing after, dan line spacing | `dokumen_format_paragraf` |
| Numbering | Format penomoran `BAB I` | Teks elemen, numbering metadata, dan aturan |
| Kapitalisasi | Bentuk huruf judul bab, seperti uppercase | Teks hasil ekstraksi |
| Enter setelah nomor | Pemisahan nomor bab dan judul | Struktur paragraf dan teks |
| Struktur konten | Jumlah baris kosong setelah judul dan minimal paragraf sebelum subbab | Urutan `dokumen_elemen` dan data visual |

Aturan ini memastikan judul bab tidak hanya terbaca sebagai teks biasa, tetapi mengikuti struktur bab yang diharapkan. Sistem memakai label dari `dokumen_elemen_visual` untuk membantu menemukan kandidat judul bab, kemudian membandingkan detail formatnya dengan JSON `judul_bab`. Hard rule pada judul bab dapat dipasang pada parameter seperti font, alignment, penomoran, atau struktur konten.

### 6.2.3.4 Aturan Judul Subbab

Aturan Judul Subbab menggunakan key runtime `judul_subbab`. Aturan ini memeriksa elemen heading di bawah judul bab, termasuk hierarki dan penomoran bertingkat. Pemeriksaan judul subbab dilakukan setelah judul bab agar konteks bab dan urutan heading sudah tersedia.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Font | Nama font dan ukuran font judul subbab | `dokumen_format_text` |
| Font style | Bold, italic, dan underline | `dokumen_format_text` |
| Paragraph | Alignment, left indent, right indent, spacing before, spacing after, dan line spacing | `dokumen_format_paragraf` |
| Hanging range | Batas minimal dan maksimal hanging indent untuk numbering bertingkat | `dokumen_format_paragraf` |
| Numbering | Format numbering bertingkat seperti `1.1` atau `1.1.1` | Numbering metadata, teks elemen, dan konteks heading |
| Kapitalisasi | Title case judul subbab | Teks hasil ekstraksi |
| Struktur konten | Minimal paragraf setelah subbab, pencegahan posisi paling bawah halaman, minimal subbab level sama, dan baris kosong sebelum subbab | Urutan elemen dan `dokumen_elemen_visual` |

Key `judul_subbab` berfungsi untuk menjaga konsistensi struktur heading di dalam bab. Sistem memeriksa apakah subbab memiliki format yang sesuai dan tidak berdiri sendiri tanpa isi yang cukup. Jika admin menandai aturan struktur subbab sebagai hard rule, pelanggaran seperti subbab terlalu bawah pada halaman atau struktur subbab tidak sesuai dapat memengaruhi status akhir validasi.

### 6.2.3.5 Aturan Paragraf

Aturan Paragraf menggunakan key runtime `paragraf`. Aturan ini memeriksa paragraf isi yang sudah dipisahkan dari judul, daftar, caption, tabel, gambar, rumus, dan kode. Pemeriksaan paragraf dilakukan setelah klasifikasi elemen agar elemen yang bukan paragraf isi tidak ikut divalidasi sebagai paragraf biasa.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Font | Nama font dan ukuran font paragraf | `dokumen_format_text` |
| Paragraph | Alignment paragraf | `dokumen_format_paragraf` |
| Indentation | Left indent, right indent, dan first line indent | `dokumen_format_paragraf` |
| Spacing | Line spacing, spacing before, dan spacing after | `dokumen_format_paragraf` |
| Struktur konten | Minimal jumlah kalimat dalam paragraf | Teks `dokumen_elemen` |

Aturan ini menjadi dasar pemeriksaan isi utama naskah. Sistem membandingkan format paragraf aktual dengan nilai pada `aturan_detail_json_value` untuk `paragraf`. Hard rule pada paragraf biasanya digunakan untuk parameter yang dianggap wajib, seperti font, ukuran font, alignment, line spacing, atau first line indent.

### 6.2.3.6 Aturan Item Daftar

Aturan Item Daftar menggunakan key runtime `item_daftar`. Aturan ini memeriksa elemen yang diklasifikasikan sebagai list item, baik yang memiliki metadata numbering maupun yang dikenali melalui pola visual. Pemisahan item daftar dari paragraf penting agar daftar tidak keliru diperiksa dengan aturan paragraf isi biasa.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Font | Nama font dan ukuran font item daftar | `dokumen_format_text` |
| Paragraph | Alignment item daftar | `dokumen_format_paragraf` |
| Indentation | Left indent, right indent, dan hanging indent | `dokumen_format_paragraf` dan numbering metadata |
| Spacing | Line spacing, spacing before, dan spacing after | `dokumen_format_paragraf` |
| Struktur konten | Pencegahan item daftar pada posisi paling bawah halaman dan pencegahan daftar tunggal | Urutan elemen dan `dokumen_elemen_visual` |

Key `item_daftar` membuat sistem dapat menerapkan aturan khusus untuk daftar. Pemeriksaan dapat melihat pola indentasi gantung dan konsistensi spacing antar item. Jika parameter seperti hanging indent atau pencegahan daftar tunggal ditandai sebagai hard rule, pelanggaran pada daftar dapat disimpan sebagai kesalahan hard constraint.

### 6.2.3.7 Aturan Gambar dan Caption Gambar

Aturan Gambar dan Caption Gambar menggunakan key runtime `gambar`. Satu aturan ini memuat konfigurasi untuk objek gambar dan caption gambar karena keduanya harus divalidasi sebagai satu relasi. Pemeriksaan gambar dilakukan setelah elemen teks utama dan footnote agar konteks gambar dan caption dapat dibaca dengan lebih jelas.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Gambar paragraph | Alignment, indentasi, dan spacing gambar | `dokumen_format_paragraf` |
| Gambar position | Layout inline, pencegahan melebihi margin, dan pencegahan memenuhi halaman | `dokumen_format_drawing`, visual halaman, dan margin section |
| Struktur gambar | Baris kosong sebelum/sesudah gambar dan pengabaian jika di awal halaman | Urutan elemen dan `dokumen_elemen_visual` |
| Caption font | Nama font, ukuran, bold, italic, dan underline caption | `dokumen_format_text` |
| Caption paragraph | Alignment, indentasi, dan spacing caption | `dokumen_format_paragraf` |
| Caption numbering | Format nomor gambar, kapitalisasi, dan enter setelah numbering | Teks caption dan aturan numbering |
| Caption position | Posisi caption terhadap gambar | Relasi elemen gambar dan caption |

Sistem menggunakan key `gambar` untuk memastikan gambar tidak hanya ada, tetapi juga berada dalam posisi dan format yang sesuai. Caption gambar diperiksa melalui relasi dengan elemen gambar sehingga sistem dapat mendeteksi caption yang hilang, salah posisi, atau salah format. Hard rule dapat dipasang pada parameter gambar maupun caption, misalnya posisi caption, layout inline, atau pencegahan gambar melebihi margin.

### 6.2.3.8 Aturan Tabel dan Caption Tabel

Aturan Tabel dan Caption Tabel menggunakan key runtime `tabel`. Aturan ini memeriksa properti tabel, konten tabel, serta caption tabel yang terhubung dengan tabel. Pemeriksaan tabel dilakukan setelah gambar karena kedua jenis elemen ini sama-sama membutuhkan relasi visual dan caption.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Posisi tabel | Alignment, indent from left, pencegahan melebihi margin, dan pencegahan memenuhi halaman | `dokumen_format_table`, section, dan data visual |
| Konten tabel | Font konten tabel dan spacing paragraf di dalam tabel | `dokumen_format_table`, `dokumen_format_text`, dan `dokumen_format_paragraf` |
| Larangan gambar tabel | Pencegahan tabel disajikan sebagai gambar | Elemen tabel, gambar, dan label visual |
| Struktur tabel | Baris kosong sebelum/sesudah tabel dan pengabaian jika di awal halaman | Urutan elemen dan visual halaman |
| Caption font | Nama font, ukuran, bold, italic, dan underline caption tabel | `dokumen_format_text` |
| Caption paragraph | Alignment, indentasi, dan spacing caption tabel | `dokumen_format_paragraf` |
| Caption numbering | Format nomor tabel, kapitalisasi, dan enter setelah numbering | Teks caption dan aturan numbering |
| Caption lanjutan | Kewajiban caption lanjutan jika tabel lintas halaman | Relasi tabel, halaman, dan data visual |

Key `tabel` menyatukan pemeriksaan tabel dengan caption tabel karena caption adalah bagian dari penyajian tabel. Sistem dapat memeriksa apakah caption berada sebelum atau sesudah tabel sesuai aturan. Jika caption lanjutan lintas halaman atau larangan tabel berupa gambar ditandai sebagai hard rule, pelanggarannya menjadi kesalahan yang dapat menggagalkan hasil validasi.

### 6.2.3.9 Aturan Algoritma dan Segmen Program

Aturan Algoritma dan Segmen Program menggunakan key runtime `kode`. Aturan ini memeriksa blok kode, algoritma, segmen program, dan judul kode yang menyertainya. Pemeriksaan kode dilakukan setelah rumus dan elemen media lain karena blok kode memiliki struktur khusus yang dapat menyerupai tabel atau gambar jika dokumen tidak ditulis sesuai pedoman.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Font kode | Font Courier New, ukuran font, bold, italic, dan underline | `dokumen_format_text` |
| Paragraph kode | Alignment, left indent, right indent, hanging indent, dan spacing | `dokumen_format_paragraf` |
| Numbering kode | Penggunaan numbering dan format numbering kode | Teks elemen dan numbering metadata |
| Larangan penyajian | Pencegahan kode dalam bentuk gambar atau tabel | Elemen gambar, elemen tabel, dan label visual |
| Struktur kode | Baris kosong sebelum/sesudah kode dan pengabaian jika di awal halaman | Urutan elemen dan visual halaman |
| Judul kode | Font, style, paragraph, numbering judul, posisi judul, dan caption lanjutan | Elemen judul kode, format teks, dan relasi dengan blok kode |

Key `kode` dipakai untuk membedakan pemeriksaan blok algoritma dan segmen program dari paragraf biasa. Aturan ini dapat menilai apakah kode memakai font monospaced, apakah judul kode muncul pada posisi yang tepat, dan apakah kode tidak dimasukkan sebagai gambar atau tabel. Hard rule pada larangan gambar kode atau tabel kode penting karena penyajian tersebut membuat isi kode sulit diperiksa sebagai teks.

### 6.2.3.10 Aturan Rumus

Aturan Rumus menggunakan key runtime `rumus`. Aturan ini memeriksa formula atau persamaan yang memiliki format khusus dalam naskah akademik. Pemeriksaan rumus mencakup bentuk teks matematika, paragraf, tabulasi, numbering, dan struktur halaman.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Font | Font Cambria Math dan ukuran font rumus | Elemen math/formula dan `dokumen_format_text` |
| Paragraph | Alignment, first line indent, left indent, right indent, dan spacing | `dokumen_format_paragraf` |
| Left tab | Jarak dari persamaan, alignment tab, leader style, dan ketergantungan pada panjang persamaan | Properti paragraf dan tabulasi |
| Right tab | Posisi tab kanan, alignment, leader style, dan ketergantungan pada panjang persamaan | Properti paragraf dan tabulasi |
| Numbering | Format nomor rumus seperti `([nomor_bab].[nomor_rumus])` | Teks rumus dan numbering |
| Position | Pencegahan rumus memenuhi halaman dan overall indent | Data visual dan format paragraf |
| Struktur halaman | Minimal satu paragraf di halaman yang sama bila aturan diaktifkan | Urutan elemen dan visual halaman |

Key `rumus` membantu sistem menerapkan aturan khusus pada persamaan yang tidak dapat diperlakukan sebagai paragraf biasa. Sistem memeriksa relasi antara rumus, tab kiri, tab kanan, dan nomor rumus. Jika parameter tabulasi, numbering, atau struktur halaman ditandai sebagai hard rule, pelanggaran rumus dapat memengaruhi status validasi.

### 6.2.3.11 Aturan Footnote

Aturan Footnote menggunakan key runtime `footnote`. Aturan ini memeriksa catatan kaki dan separator footnote yang memiliki aturan berbeda dari teks utama. Pemeriksaan footnote dilakukan setelah modul teks utama karena data footnote berada pada part dan note yang berbeda.

| Kelompok Parameter | Yang Diperiksa | Sumber Data Utama |
| --- | --- | --- |
| Numbering | Format nomor footnote dan tipe penomoran | `dokumen_note` dan footnote part |
| Separator paragraph | Alignment, indentasi, spacing before, spacing after, dan line spacing separator | `dokumen_format_paragraf` |
| Separator rule | Pencegahan tab awal pada separator | Struktur footnote dan teks |
| Footnote text font | Nama font dan ukuran font footnote | `dokumen_format_text` |
| Footnote text paragraph | Alignment dan spacing teks footnote | `dokumen_format_paragraf` |
| Struktur konten | Satu enter sebelum footnote | Urutan note dan struktur part |

Key `footnote` memastikan catatan kaki tidak diperlakukan sebagai paragraf isi biasa. Sistem memeriksa nomor, separator, dan teks footnote sesuai parameter aturan aktif. Jika admin menandai format footnote atau separator sebagai hard rule, pelanggaran akan diteruskan sebagai kesalahan hard constraint.

### 6.2.3.12 Pengelolaan Aktivasi Aturan dan Hard Rule oleh Admin

Pengelolaan aturan dilakukan oleh admin melalui halaman `Admin > Template Panduan` pada frontend. Pada halaman tersebut, admin dapat melihat daftar template aturan, memilih template, melihat detail aturan format, melakukan review, mengubah parameter, mengatur skor minimum, dan mengaktifkan template aturan. Aktivasi dilakukan pada level versi `aturan`, bukan pada level parameter atau aturan detail satu per satu.

Ketika admin menekan tombol aktivasi pada template, frontend memanggil endpoint `PUT /aturan/{id}/activate`. Backend kemudian memeriksa status template aturan tersebut. Template dengan status `diproses`, `menunggu_review`, atau `gagal` belum dapat diaktifkan. Jika template valid untuk diaktifkan, backend mengubah template tersebut menjadi status aktif dan mengubah aturan aktif lain menjadi tidak aktif. Dengan demikian, worker validasi hanya memakai satu versi `aturan` aktif pada saat memproses dokumen.

Pengubahan parameter aturan dilakukan melalui kartu aturan pada `FormatRulesSection`. Admin membuka dialog sesuai jenis aturan, misalnya Aturan Judul Bab, Aturan Paragraf, Aturan Tabel, atau Aturan Rumus. Di dalam dialog, admin dapat mengubah nilai parameter yang memiliki `is_editable: true`. Untuk menjadikan sebuah parameter sebagai hard rule, admin mencentang ikon `!` pada parameter tersebut. Centang tersebut disimpan sebagai `is_hard_constraint: true` pada node parameter di dalam `aturan_detail_json_value`.

Perubahan detail aturan disimpan melalui endpoint `PATCH /aturan/{id}/detail`. Payload yang dikirim berisi `aturan_detail_id`, key canonical aturan, dan `json_value` yang sudah memuat nilai terbaru. Backend melakukan canonicalization dan validasi bentuk JSON sebelum menyimpannya kembali ke `aturan_detail_json_value`. Dengan alur ini, hard rule tidak disimpan sebagai tabel terpisah, melainkan sebagai metadata pada parameter aturan yang bersangkutan.

Pada saat validasi berjalan, backend membaca nilai `is_hard_constraint` dari parameter yang diperiksa. Jika parameter hard rule dilanggar, error yang terbentuk diberi penanda hard constraint. Penanda tersebut kemudian disimpan pada `kesalahan_detail.kesalahan_is_hard_constraint`. Kesalahan hard constraint juga digunakan dalam penentuan status, sehingga bab atau dokumen dapat dinyatakan tidak lolos walaupun skor validasi memenuhi skor minimum. Dengan mekanisme ini, admin dapat membedakan aturan biasa dan aturan wajib tanpa mengubah kode validasi.

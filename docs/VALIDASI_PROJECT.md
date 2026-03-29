# Validasi Format Buku

Catatan: aturan numerik (margin, font size, spacing, dll) diambil dari Aturan/AturanDetail aktif sehingga bersifat konfigurasi.

| Kategori | SubKategori | Aturan | Sumber |
| --- | --- | --- | --- |
| Pengaturan Halaman | section | Dokumen harus memiliki minimal 1 section. | hardcoded/derived |
| Pengaturan Halaman | paper | Ukuran kertas + orientasi tiap section harus sesuai daftar `paper.section` berdasarkan tipe section (awal/isi/akhir/lampiran). | diambil dari aturan.txt |
| Pengaturan Halaman | margin | Margin top/bottom/left/right harus sesuai `margin.paper` untuk ukuran kertas & orientasi (toleransi ~0.5 mm). | diambil dari aturan.txt |
| Pengaturan Halaman | header_from_top | Jarak header dari atas harus sesuai `header_footer.header_from_top` (cm). | diambil dari aturan.txt |
| Pengaturan Halaman | footer_from_bottom | Jarak footer dari bawah harus sesuai `header_footer.footer_from_bottom` (cm). | diambil dari aturan.txt |
| Pengaturan Halaman | different_odd_even | Header/footer ganjil-genap harus nonaktif (false). | hardcoded/derived |
| Pengaturan Halaman | gutter | Ukuran gutter harus 0 cm (default jika tidak ada rule di DB; bisa di-override). | hardcoded/derived |
| Pengaturan Halaman | gutter_position | Posisi gutter harus sesuai `gutter.position` jika diset. | diambil dari aturan.txt |
| Pengaturan Halaman | column_count | Jumlah kolom harus 1. | hardcoded/derived |
| Pengaturan Halaman | page_number_format | Format nomor halaman harus sesuai `page_numbering.section.*.format` untuk tipe section. | hardcoded/derived |
| Pengaturan Halaman | page_number_start | Nomor halaman awal harus sesuai `page_numbering.section.*.start` (nilai 0 diterima sebagai lanjutan ketika start=1). | hardcoded/derived |
| Judul Bab | eksistensi | Minimal 1 judul bab harus ada. | hardcoded/derived |
| Judul Bab | posisi | Judul bab harus berada di elemen pertama body. | hardcoded/derived |
| Judul Bab | nomor_bab_whitespace | Nomor bab tidak boleh memiliki spasi di awal, tab, double space, atau >1 spasi di akhir. | hardcoded/derived |
| Judul Bab | format_nomor | Format nomor bab harus sesuai `numbering.number_format` + aturan `numbering.case` jika diisi. | diambil dari aturan.txt |
| Judul Bab | judul_setelah_nomor | Judul bab harus ada setelah nomor bab. | hardcoded/derived |
| Judul Bab | enter_after_number | Jika `numbering.enter_after_number` true, judul bab harus di baris berikutnya dan tidak ada baris kosong di antara nomor dan judul. | diambil dari aturan.txt |
| Judul Bab | paragraf_kosong_setelah | Jika `struktur_konten.satu_baris_kosong_setelah` true, harus ada tepat 1 paragraf kosong setelah judul, lalu diikuti paragraf non-kosong. | diambil dari aturan.txt |
| Judul Bab | format_paragraf | Setiap baris judul bab harus memiliki paragraph format (tidak boleh ada baris tanpa format). | hardcoded/derived |
| Judul Bab | alignment | Alignment judul bab sesuai `paragraph.alignment`. | diambil dari aturan.txt |
| Judul Bab | indentation | Jika `paragraph.indentation` = none, semua indent (left/right/special) harus 0. | diambil dari aturan.txt |
| Judul Bab | line_spacing | Line spacing judul bab sesuai `paragraph.spacing.line_spacing`. | diambil dari aturan.txt |
| Judul Bab | spacing_before_after | Spacing before/after judul bab sesuai `paragraph.spacing.before/after` dan autospacing harus false. | diambil dari aturan.txt |
| Judul Bab | format_teks | Format teks judul bab harus ditemukan jika ada aturan font. | hardcoded/derived |
| Judul Bab | font | Font name/size/bold/italic/underline sesuai `font`. | diambil dari aturan.txt |
| Judul Bab | paragraf_kosong_font_size | Ukuran font paragraf kosong setelah judul harus sama dengan judul (non-required/saran). | hardcoded/derived |
| Judul Bab | paragraf_sebelum_subbab | Jika `struktur_konten.min_satu_paragraf_sebelum_subbab` true, harus ada minimal 1 paragraf antara judul bab dan subbab pertama. | diambil dari aturan.txt |
| Judul Subbab | font | Font name/size/bold/italic/underline sesuai `font`. | diambil dari aturan.txt |
| Judul Subbab | alignment | Alignment judul subbab sesuai `paragraph.alignment`. | diambil dari aturan.txt |
| Judul Subbab | left_right_indent | Left/right indent judul subbab sesuai `paragraph.indentation.left_indent/right_indent`. | diambil dari aturan.txt |
| Judul Subbab | hanging_indent | Hanging indent harus berada dalam rentang `paragraph.hanging_min_cm` - `paragraph.hanging_max_cm` (toleransi ~0.5 mm). | diambil dari aturan.txt |
| Judul Subbab | numbering_multilevel | Jika aturan hanging aktif, judul subbab harus menggunakan numbering (multilevel list). | hardcoded/derived |
| Judul Subbab | line_spacing | Line spacing judul subbab sesuai `paragraph.spacing.line_spacing`. | diambil dari aturan.txt |
| Judul Subbab | spacing_before_after | Spacing before/after judul subbab sesuai `paragraph.spacing.before/after`. | diambil dari aturan.txt |
| Judul Subbab | case | Judul subbab harus Title Case/UPPERCASE/lowercase sesuai `numbering.case` (jika diisi). | diambil dari aturan.txt |
| Judul Subbab | paragraf_setelah | Jika `struktur_konten.minimal_satu_paragraf_setelah` true, harus ada minimal 1 paragraf setelah judul subbab. | diambil dari aturan.txt |
| Judul Subbab | posisi_bawah_halaman | Jika `struktur_konten.cegah_posisi_paling_bawah` true, judul subbab tidak boleh berada di paling bawah halaman. | diambil dari aturan.txt |
| Judul Subbab | urutan_parent | Jika ada subbab bertingkat (mis. 1.2.1), parent (1.2) harus ada. | hardcoded/derived |
| Judul Subbab | urutan_sibling | Tidak boleh loncat nomor sibling (mis. 1.3 tanpa 1.2). | hardcoded/derived |
| Judul Subbab | subbab_tunggal | Jika `struktur_konten.cegah_subbab_tunggal` true, subbab tidak boleh berdiri sendiri (harus ada minimal dua sibling). | diambil dari aturan.txt |
| Paragraf | font_name | Font paragraf sesuai `font.font_name`. | diambil dari aturan.txt |
| Paragraf | font_size | Ukuran font paragraf sesuai `font.font_size`. | diambil dari aturan.txt |
| Paragraf | alignment | Alignment paragraf sesuai `paragraph.alignment`. | diambil dari aturan.txt |
| Paragraf | left_indent_after_list | Paragraf setelah list harus memiliki left indent sesuai aturan list/hanging yang dihitung dari item daftar terakhir. | hardcoded/derived |
| Paragraf | indentation | Left/right/first line indent sesuai `paragraph.indentation.left_indent/right_indent/first_line_indent` (atau override setelah list). | diambil dari aturan.txt |
| Paragraf | hanging_indent_after_list | Jika override setelah list aktif, hanging indent harus sesuai nilai yang dihitung. | hardcoded/derived |
| Paragraf | line_spacing | Line spacing paragraf sesuai `paragraph.spacing.line_spacing`. | diambil dari aturan.txt |
| Paragraf | spacing_before_after | Spacing before/after paragraf sesuai `paragraph.spacing.before/after` dan autospacing harus false. | diambil dari aturan.txt |
| Paragraf | sentence_count | Saran: minimal 3 kalimat per paragraf (IsRequired=false). | hardcoded/derived |
| Item Daftar | font_name | Font item daftar sesuai `font.font_name`. | diambil dari aturan.txt |
| Item Daftar | font_size | Ukuran font item daftar sesuai `font.font_size`. | diambil dari aturan.txt |
| Item Daftar | alignment | Alignment item daftar sesuai `paragraph.alignment`. | diambil dari aturan.txt |
| Item Daftar | hanging_indent | Hanging indent item daftar sesuai `paragraph.indentation.hanging`. | diambil dari aturan.txt |
| Item Daftar | left_indent | Left indent item daftar sesuai `paragraph.indentation.left_indent` + penyesuaian level (jika ada). | diambil dari aturan.txt |
| Item Daftar | first_line_indent | First line indent item daftar harus 0 cm. | hardcoded/derived |
| Item Daftar | line_spacing | Line spacing item daftar sesuai `paragraph.spacing.line_spacing`. | diambil dari aturan.txt |
| Item Daftar | spacing_before_after | Spacing before/after item daftar sesuai `paragraph.spacing.before/after` dan autospacing harus false. | diambil dari aturan.txt |
| Gambar | paragraph_alignment | Alignment paragraf gambar sesuai `gambar.paragraph.alignment`. | diambil dari aturan.txt |
| Gambar | paragraph_indentation | Indentasi paragraf gambar harus none (left/right/special = 0) jika diatur. | diambil dari aturan.txt |
| Gambar | line_spacing | Line spacing paragraf gambar sesuai `gambar.paragraph.spacing.line_spacing`. | diambil dari aturan.txt |
| Gambar | spacing_before_after | Spacing before/after paragraf gambar sesuai `gambar.paragraph.spacing.before/after`. | diambil dari aturan.txt |
| Gambar | layout_option | Jika `gambar.position.layout_option` = inline_with_text, semua gambar harus inline (bukan floating). | diambil dari aturan.txt |
| Gambar | lebar_vs_margin | Jika `gambar.position.cegah_melebihi_margin` true, lebar gambar tidak boleh melebihi area teks (margin). | diambil dari aturan.txt |
| Gambar | halaman_penuh | Jika `gambar.position.cegah_memenuhi_halaman` true, halaman gambar tidak boleh hanya berisi gambar + caption (harus ada paragraf lain). | diambil dari aturan.txt |
| Caption Gambar | posisi | Jika `caption_gambar.position` = before/after, caption wajib berada di posisi tersebut relatif terhadap gambar. | diambil dari aturan.txt |
| Caption Gambar | eksistensi | Jika aturan posisi ditetapkan, caption harus ada di sekitar gambar. | hardcoded/derived |
| Caption Gambar | font | Font name/size/bold/italic/underline caption sesuai `caption_gambar.font`. | diambil dari aturan.txt |
| Caption Gambar | alignment | Alignment caption sesuai `caption_gambar.paragraph.alignment`. | diambil dari aturan.txt |
| Caption Gambar | indentation | Indentasi caption harus none (left/right/special = 0) jika diatur. | diambil dari aturan.txt |
| Caption Gambar | line_spacing | Line spacing caption sesuai `caption_gambar.paragraph.spacing.line_spacing`. | diambil dari aturan.txt |
| Caption Gambar | spacing_before_after | Spacing before/after caption sesuai `caption_gambar.paragraph.spacing.before/after`. | diambil dari aturan.txt |
| Caption Gambar | format_nomor | Format nomor caption harus sesuai `caption_gambar.numbering.number_format` (prefix + X.Y). | diambil dari aturan.txt |
| Caption Gambar | judul_setelah_nomor | Jika `caption_gambar.numbering.enter_after_numbering` true, caption harus memiliki judul setelah nomor. | diambil dari aturan.txt |
| Caption Gambar | title_case | Jika `caption_gambar.numbering.case` = Title Case, judul caption harus Title Case. | diambil dari aturan.txt |

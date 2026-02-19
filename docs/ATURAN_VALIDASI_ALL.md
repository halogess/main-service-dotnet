# Aturan Validasi (DB + Hardcoded)

Catatan: Jika kolom Value berisi format `aturan.txt=...; rule.txt=...`, itu menandakan konflik nilai antar sumber DB snapshot.

| Kategori | Kriteria | Subkriteria | Value | Editable | Sumber |
| --- | --- | --- | --- | --- | --- |
| Caption Gambar | eksistensi | kondisi | Jika aturan posisi ditetapkan, caption harus ada di sekitar gambar. | fixed | hardcoded |
| Caption Tabel | eksistensi | kondisi | Jika aturan posisi caption diterapkan, caption tabel harus ada di sekitar tabel (before/after sesuai aturan). | fixed | hardcoded |
| Isi Buku | gambar | caption_gambar.font.font_name | Times New Roman | true | db |
| Isi Buku | gambar | caption_gambar.font.font_size | 12 | true | db |
| Isi Buku | gambar | caption_gambar.font.font_style.bold | True | true | db |
| Isi Buku | gambar | caption_gambar.font.font_style.italic | False | true | db |
| Isi Buku | gambar | caption_gambar.font.font_style.underline | False | true | db |
| Isi Buku | gambar | caption_gambar.numbering.case | Title Case | true | db |
| Isi Buku | gambar | caption_gambar.numbering.enter_after_numbering | True | true | db |
| Isi Buku | gambar | caption_gambar.numbering.number_format | Gambar [nomor_bab].[nomor_gambar] | false | db |
| Isi Buku | gambar | caption_gambar.paragraph.alignment | center | false | db |
| Isi Buku | gambar | caption_gambar.paragraph.indentation | none | false | db |
| Isi Buku | gambar | caption_gambar.paragraph.spacing.after | 0 | true | db |
| Isi Buku | gambar | caption_gambar.paragraph.spacing.before | 0 | true | db |
| Isi Buku | gambar | caption_gambar.paragraph.spacing.line_spacing | 1 | true | db |
| Isi Buku | gambar | caption_gambar.position | after | true | db |
| Isi Buku | gambar | gambar.paragraph.alignment | center | true | db |
| Isi Buku | gambar | gambar.paragraph.indentation | none | false | db |
| Isi Buku | gambar | gambar.paragraph.spacing.after | 0 | true | db |
| Isi Buku | gambar | gambar.paragraph.spacing.before | 0 | true | db |
| Isi Buku | gambar | gambar.paragraph.spacing.line_spacing | 1 | true | db |
| Isi Buku | gambar | gambar.position.cegah_melebihi_margin | True | true | db |
| Isi Buku | gambar | gambar.position.cegah_memenuhi_halaman | True | true | db |
| Isi Buku | gambar | gambar.position.layout_option | inline_with_text | false | db |
| Isi Buku | item_daftar | font.font_name | Times New Roman | true | db |
| Isi Buku | item_daftar | font.font_size | 12 | true | db |
| Isi Buku | item_daftar | paragraph.alignment | justify | true | db |
| Isi Buku | item_daftar | paragraph.indentation.hanging | 0.75 | true | db |
| Isi Buku | item_daftar | paragraph.indentation.left_indent | 0 | true | db |
| Isi Buku | item_daftar | paragraph.spacing.after | 0 | true | db |
| Isi Buku | item_daftar | paragraph.spacing.before | 0 | true | db |
| Isi Buku | item_daftar | paragraph.spacing.line_spacing | 1.5 | true | db |
| Isi Buku | item_daftar | penggunaan_poin.bullets | Digunakan untuk menyajikan daftar yang seluruh itemnya bersifat setara dan tidak memerlukan urutan tertentu. | false | db |
| Isi Buku | item_daftar | penggunaan_poin.numbering | Digunakan untuk menyajikan daftar yang item-itemnya memiliki urutan atau tahapan yang jelas. | false | db |
| Isi Buku | item_daftar | struktur_konten.cegah_daftar_tunggal | True | true | db |
| Isi Buku | item_daftar | struktur_konten.cegah_posisi_paling_bawah | True | true | db |
| Isi Buku | judul_bab | font.font_name | Times New Roman | true | db |
| Isi Buku | judul_bab | font.font_size | 16 | true | db |
| Isi Buku | judul_bab | font.font_style.bold | True | true | db |
| Isi Buku | judul_bab | font.font_style.italic | False | true | db |
| Isi Buku | judul_bab | font.font_style.underline | False | true | db |
| Isi Buku | judul_bab | numbering.case | UPPERCASE | true | db |
| Isi Buku | judul_bab | numbering.enter_after_number | True | true | db |
| Isi Buku | judul_bab | numbering.number_format | BAB I | false | db |
| Isi Buku | judul_bab | paragraph.alignment | center | false | db |
| Isi Buku | judul_bab | paragraph.indentation | none | false | db |
| Isi Buku | judul_bab | paragraph.spacing.after | 0 | true | db |
| Isi Buku | judul_bab | paragraph.spacing.before | 0 | true | db |
| Isi Buku | judul_bab | paragraph.spacing.line_spacing | 1.5 | true | db |
| Isi Buku | judul_bab | struktur_konten.min_satu_paragraf_sebelum_subbab | True | true | db |
| Isi Buku | judul_bab | struktur_konten.satu_baris_kosong_setelah | True | true | db |
| Isi Buku | judul_subbab | font.font_name | Times New Roman | true | db |
| Isi Buku | judul_subbab | font.font_size | 14 | true | db |
| Isi Buku | judul_subbab | font.font_style.bold | True | true | db |
| Isi Buku | judul_subbab | font.font_style.italic | False | true | db |
| Isi Buku | judul_subbab | font.font_style.underline | False | true | db |
| Isi Buku | judul_subbab | numbering.case | Title Case | true | db |
| Isi Buku | judul_subbab | numbering.number_format | 1.1, 1.1.1, 1.1.1.1 | false | db |
| Isi Buku | judul_subbab | paragraph.alignment | justify | true | db |
| Isi Buku | judul_subbab | paragraph.hanging_max_cm | 2.5 | true | db |
| Isi Buku | judul_subbab | paragraph.hanging_min_cm | 1.27 | true | db |
| Isi Buku | judul_subbab | paragraph.spacing.after | 0 | true | db |
| Isi Buku | judul_subbab | paragraph.spacing.before | 0 | true | db |
| Isi Buku | judul_subbab | paragraph.spacing.line_spacing | 1.5 | true | db |
| Isi Buku | judul_subbab | struktur_konten.cegah_posisi_paling_bawah | True | true | db |
| Isi Buku | judul_subbab | struktur_konten.cegah_subbab_tunggal | True | true | db |
| Isi Buku | judul_subbab | struktur_konten.minimal_satu_paragraf_setelah | True | true | db |
| Isi Buku | kode | judul_kode.font.font_name | Times New Roman | true | db |
| Isi Buku | kode | judul_kode.font.font_size | 12 | true | db |
| Isi Buku | kode | judul_kode.font.font_style.bold | True | true | db |
| Isi Buku | kode | judul_kode.font.font_style.italic | False | true | db |
| Isi Buku | kode | judul_kode.font.font_style.underline | False | true | db |
| Isi Buku | kode | judul_kode.numbering.case | Title Case | true | db |
| Isi Buku | kode | judul_kode.numbering.enter_after_numbering | False | true | db |
| Isi Buku | kode | judul_kode.numbering.number_format | ["Algoritma [nomor_bab].[nomor_algo]", "Segmen Program [nomor_bab].[nomor_segpro]"] | false | db |
| Isi Buku | kode | judul_kode.paragraph.alignment | left | false | db |
| Isi Buku | kode | judul_kode.paragraph.indentation | none | false | db |
| Isi Buku | kode | judul_kode.paragraph.spacing.after | 0 | true | db |
| Isi Buku | kode | judul_kode.paragraph.spacing.before | 0 | true | db |
| Isi Buku | kode | judul_kode.paragraph.spacing.line_spacing | 1 | true | db |
| Isi Buku | kode | judul_kode.position | before | true | db |
| Isi Buku | kode | kode.cegah_gambar_kode | True | true | db |
| Isi Buku | kode | kode.font.font_name | Courier New | true | db |
| Isi Buku | kode | kode.font.font_size | 10 | true | db |
| Isi Buku | kode | kode.font.font_style.bold | False | true | db |
| Isi Buku | kode | kode.font.font_style.italic | False | true | db |
| Isi Buku | kode | kode.font.font_style.underline | False | true | db |
| Isi Buku | kode | kode.numbering.number_format | %1 | true | db |
| Isi Buku | kode | kode.numbering.use_numbering | True | true | db |
| Isi Buku | kode | kode.paragraph.alignment | left | true | db |
| Isi Buku | kode | kode.paragraph.indentation.hanging | 1 | true | db |
| Isi Buku | kode | kode.paragraph.indentation.left_indent | 0 | true | db |
| Isi Buku | kode | kode.paragraph.spacing.after | 0 | true | db |
| Isi Buku | kode | kode.paragraph.spacing.before | 0 | true | db |
| Isi Buku | kode | kode.paragraph.spacing.line_spacing | 1 | true | db |
| Isi Buku | paragraf | font.font_name | Times New Roman | true | db |
| Isi Buku | paragraf | font.font_size | 12 | true | db |
| Isi Buku | paragraf | paragraph.alignment | justify | true | db |
| Isi Buku | paragraf | paragraph.first_line_indent | 1.27 | true | db |
| Isi Buku | paragraf | paragraph.spacing.after | 0 | true | db |
| Isi Buku | paragraf | paragraph.spacing.before | 0 | true | db |
| Isi Buku | paragraf | paragraph.spacing.line_spacing | 1.5 | true | db |
| Isi Buku | tabel | caption_tabel.font.font_name | Times New Roman | true | db |
| Isi Buku | tabel | caption_tabel.font.font_size | 12 | true | db |
| Isi Buku | tabel | caption_tabel.font.font_style.bold | True | true | db |
| Isi Buku | tabel | caption_tabel.font.font_style.italic | False | true | db |
| Isi Buku | tabel | caption_tabel.font.font_style.underline | False | true | db |
| Isi Buku | tabel | caption_tabel.numbering.case | Title Case | true | db |
| Isi Buku | tabel | caption_tabel.numbering.enter_after_number | True | true | db |
| Isi Buku | tabel | caption_tabel.numbering.enter_after_numbering | True | true | db |
| Isi Buku | tabel | caption_tabel.numbering.number_format | Tabel [nomor_bab].[nomor_tabel] | false | db |
| Isi Buku | tabel | caption_tabel.paragraph.alignment | center | mixed(aturan.txt=false; rule.txt=true) | db |
| Isi Buku | tabel | caption_tabel.paragraph.indentation | none | false | db |
| Isi Buku | tabel | caption_tabel.paragraph.spacing.after | 0 | true | db |
| Isi Buku | tabel | caption_tabel.paragraph.spacing.before | 0 | true | db |
| Isi Buku | tabel | caption_tabel.paragraph.spacing.line_spacing | 1 | true | db |
| Isi Buku | tabel | caption_tabel.position | before | true | db |
| Isi Buku | tabel | tabel.cegah_gambar_tabel | True | true | db |
| Isi Buku | tabel | tabel.konten_tabel.font.font_name | Times New Roman | true | db |
| Isi Buku | tabel | tabel.konten_tabel.font.font_size | 12 | true | db |
| Isi Buku | tabel | tabel.konten_tabel.paragraph.spacing.after | 0 | true | db |
| Isi Buku | tabel | tabel.konten_tabel.paragraph.spacing.before | 0 | true | db |
| Isi Buku | tabel | tabel.konten_tabel.paragraph.spacing.line_spacing | 1 | true | db |
| Isi Buku | tabel | tabel.position.alignment | center | mixed(aturan.txt=false; rule.txt=true) | db |
| Isi Buku | tabel | tabel.position.cegah_melebihi_margin | True | true | db |
| Isi Buku | tabel | tabel.position.cegah_memenuhi_halaman | True | true | db |
| Isi Buku | tabel | tabel.position.indent_from_left | aturan.txt=0; rule.txt=1 | mixed(aturan.txt=false; rule.txt=true) | db |
| Item Daftar | first_line_indent | kondisi | First line indent item daftar harus 0 cm. | fixed | hardcoded |
| Judul Bab | eksistensi | kondisi | Minimal 1 judul bab harus ada. | fixed | hardcoded |
| Judul Bab | format_paragraf | kondisi | Setiap baris judul bab harus memiliki paragraph format (tidak boleh ada baris tanpa format). | fixed | hardcoded |
| Judul Bab | format_teks | kondisi | Format teks judul bab harus ditemukan jika ada aturan font. | fixed | hardcoded |
| Judul Bab | judul_setelah_nomor | kondisi | Judul bab harus ada setelah nomor bab. | fixed | hardcoded |
| Judul Bab | nomor_bab_whitespace | kondisi | Nomor bab tidak boleh memiliki spasi di awal, tab, double space, atau >1 spasi di akhir. | fixed | hardcoded |
| Judul Bab | paragraf_kosong_font_size | kondisi | Ukuran font paragraf kosong setelah judul harus sama dengan judul (non-required/saran). | fixed | hardcoded |
| Judul Bab | posisi | kondisi | Judul bab harus berada di elemen pertama body. | fixed | hardcoded |
| Judul Kode | eksistensi | kondisi | Jika aturan posisi judul kode diterapkan, judul kode harus ada di sekitar blok kode (before/after sesuai aturan). | fixed | hardcoded |
| Judul Subbab | numbering_multilevel | kondisi | Jika aturan hanging aktif, judul subbab harus menggunakan numbering (multilevel list). | fixed | hardcoded |
| Judul Subbab | urutan_parent | kondisi | Jika ada subbab bertingkat (mis. 1.2.1), parent (1.2) harus ada. | fixed | hardcoded |
| Judul Subbab | urutan_sibling | kondisi | Tidak boleh loncat nomor sibling (mis. 1.3 tanpa 1.2). | fixed | hardcoded |
| Nomor Halaman | nomor_halaman_akhir | continue | True | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.allow_other_content | False | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.position.alignment | center | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.position.indentation | None | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.position.location | footer | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.text_style.font_name | Times New Roman | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.text_style.font_size | 12 | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.text_style.line_spacing | 1 | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.text_style.spacing_after |  | fixed | db |
| Nomor Halaman | nomor_halaman_awal | default_page.text_style.spacing_before | 0 | fixed | db |
| Nomor Halaman | nomor_halaman_awal | different_first_page | True | fixed | db |
| Nomor Halaman | nomor_halaman_awal | first_page.allow_other_content | False | fixed | db |
| Nomor Halaman | nomor_halaman_awal | first_page.is_empty | True | fixed | db |
| Nomor Halaman | nomor_halaman_awal | number_format.type | arab | true | db |
| Nomor Halaman | nomor_halaman_isi | continue | False | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.allow_other_content | False | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.number_format.prefix | None | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.number_format.type | arabic | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.position.alignment | center | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.position.indentation | None | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.position.location | footer | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.text_style.font_name | Times New Roman | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.text_style.font_size | 12 | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.text_style.line_spacing | 1 | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.text_style.spacing_after |  | fixed | db |
| Nomor Halaman | nomor_halaman_isi | default_page.text_style.spacing_before | 0 | fixed | db |
| Nomor Halaman | nomor_halaman_isi | different_first_page | True | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.allow_other_content | False | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.is_empty | False | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.number_format.prefix | None | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.number_format.type | arabic | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.position.alignment | right | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.position.indentation | None | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.position.location | header | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.text_style.font_name | Times New Roman | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.text_style.font_size | 12 | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.text_style.line_spacing | 1 | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.text_style.spacing_after |  | fixed | db |
| Nomor Halaman | nomor_halaman_isi | first_page.text_style.spacing_before | 0 | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | continue | False | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.allow_other_content | False | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.number_format.prefix | None | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.number_format.type | arabic | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.position.alignment | center | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.position.indentation | None | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.position.location | footer | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.text_style.font_name | Times New Roman | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.text_style.font_size | 12 | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.text_style.line_spacing | 1 | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.text_style.spacing_after |  | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | default_page.text_style.spacing_before | 0 | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | different_first_page | True | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.allow_other_content | False | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.is_empty | False | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.number_format.prefix | None | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.number_format.type | arabic | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.position.alignment | right | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.position.indentation | None | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.position.location | header | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.text_style.font_name | Times New Roman | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.text_style.font_size | 12 | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.text_style.line_spacing | 1 | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.text_style.spacing_after |  | fixed | db |
| Nomor Halaman | nomor_halaman_lampiran | first_page.text_style.spacing_before | 0 | fixed | db |
| Paragraf | hanging_indent_after_list | kondisi | Jika override setelah list aktif, hanging indent harus sesuai nilai yang dihitung. | fixed | hardcoded |
| Paragraf | left_indent_after_list | kondisi | Paragraf setelah list harus memiliki left indent sesuai aturan list/hanging yang dihitung dari item daftar terakhir. | fixed | hardcoded |
| Paragraf | sentence_count | kondisi | Saran: minimal 3 kalimat per paragraf (IsRequired=false). | fixed | hardcoded |
| Pengaturan Halaman | column_count | kondisi | Jumlah kolom harus 1. | fixed | hardcoded |
| Pengaturan Halaman | different_odd_even | kondisi | Header/footer ganjil-genap harus nonaktif (false). | fixed | hardcoded |
| Pengaturan Halaman | gutter | kondisi | Ukuran gutter harus 0 cm (default jika tidak ada rule di DB; bisa di-override). | fixed | hardcoded |
| Pengaturan Halaman | header_footer | footer_from_bottom | aturan.txt=1.5; rule.txt=2.5 | true | db |
| Pengaturan Halaman | header_footer | header_from_top | aturan.txt=2.5; rule.txt=1.5 | true | db |
| Pengaturan Halaman | margin | paper.a3_landscape | {"top": 4, "left": 4, "bottom": 3, "right": 3} | true | db |
| Pengaturan Halaman | margin | paper.a4_landscape | {"top": 4, "left": 3, "bottom": 3, "right": 4} | true | db |
| Pengaturan Halaman | margin | paper.a4_portrait | {"top": 4, "left": 4, "bottom": 3, "right": 3} | true | db |
| Pengaturan Halaman | page_number_format | kondisi | Format nomor halaman harus sesuai `page_numbering.section.*.format` untuk tipe section. | fixed | hardcoded |
| Pengaturan Halaman | page_number_start | kondisi | Nomor halaman awal harus sesuai `page_numbering.section.*.start` (nilai 0 diterima sebagai lanjutan ketika start=1). | fixed | hardcoded |
| Pengaturan Halaman | paper | section.akhir | [{"size": "A4", "orientation": "PORTRAIT"}] | true | db |
| Pengaturan Halaman | paper | section.awal | [{"size": "A4", "orientation": "PORTRAIT"}] | false | db |
| Pengaturan Halaman | paper | section.isi | [{"size": "A4", "orientation": "PORTRAIT"}] | true | db |
| Pengaturan Halaman | paper | section.lampiran | [{"size": "A4", "orientation": "PORTRAIT"}, {"size": "A4", "orientation": "LANDSCAPE"}, {"size": "A3", "orientation": "LANDSCAPE"}] | true | db |
| Pengaturan Halaman | section | kondisi | Dokumen harus memiliki minimal 1 section. | fixed | hardcoded |
# Aturan Validasi (Struktur: Kategori/Kriteria/Subkriteria)

> Catatan 15 April 2026:
> `aturan.txt` / `rule.txt` legacy tidak lagi authoritative untuk `is_editable`.
> Sumber kanonik runtime adalah FE shape pada `AturanExportCatalog`, policy `AturanDetailEditablePolicy`, dan seed FE `E:\\cek-ta-react\\default-aturan-seed.json`.

Sumber nilai DB historis di bawah tetap dipertahankan sebagai dokumentasi audit, bukan kontrak editability runtime. Hardcoded diambil dari `docs/VALIDASI_SUMBER.md` + inspeksi kode.

## DB (rule.txt)

| Kategori | Kriteria | Subkriteria | Value | Editable | Sumber |
| --- | --- | --- | --- | --- | --- |
| Gambar | Caption Gambar | font.font name | Times New Roman | True | Db |
| Gambar | Caption Gambar | font.font size | 12 | True | Db |
| Gambar | Caption Gambar | font.font style.bold | True | True | Db |
| Gambar | Caption Gambar | font.font style.italic | False | True | Db |
| Gambar | Caption Gambar | font.font style.underline | False | True | Db |
| Gambar | Caption Gambar | numbering.case | Title Case | True | Db |
| Gambar | Caption Gambar | numbering.enter after numbering | True | True | Db |
| Gambar | Caption Gambar | numbering.number format | Gambar [nomor_bab].[nomor_gambar] | False | Db |
| Gambar | Caption Gambar | paragraph.alignment | center | False | Db |
| Gambar | Caption Gambar | paragraph.indentation | none | False | Db |
| Gambar | Caption Gambar | paragraph.spacing.after | 0 | True | Db |
| Gambar | Caption Gambar | paragraph.spacing.before | 0 | True | Db |
| Gambar | Caption Gambar | paragraph.spacing.line spacing | 1 | True | Db |
| Gambar | Caption Gambar | position | after | True | Db |
| Gambar | Gambar | paragraph.alignment | center | True | Db |
| Gambar | Gambar | paragraph.indentation | none | False | Db |
| Gambar | Gambar | paragraph.spacing.after | 0 | True | Db |
| Gambar | Gambar | paragraph.spacing.before | 0 | True | Db |
| Gambar | Gambar | paragraph.spacing.line spacing | 1 | True | Db |
| Gambar | Gambar | position.cegah melebihi margin | True | True | Db |
| Gambar | Gambar | position.cegah memenuhi halaman | True | True | Db |
| Gambar | Gambar | position.layout option | inline_with_text | False | Db |
| Item Daftar | Font | font name | Times New Roman | True | Db |
| Item Daftar | Font | font size | 12 | True | Db |
| Item Daftar | Paragraph | alignment | justify | True | Db |
| Item Daftar | Paragraph | indentation.hanging | 0.75 | True | Db |
| Item Daftar | Paragraph | indentation.left indent | 0 | True | Db |
| Item Daftar | Paragraph | spacing.after | 0 | True | Db |
| Item Daftar | Paragraph | spacing.before | 0 | True | Db |
| Item Daftar | Paragraph | spacing.line spacing | 1.5 | True | Db |
| Item Daftar | Struktur Konten | cegah daftar tunggal | True | True | Db |
| Item Daftar | Struktur Konten | cegah posisi paling bawah | True | True | Db |
| Judul Bab | Font | font name | Times New Roman | True | Db |
| Judul Bab | Font | font size | 16 | True | Db |
| Judul Bab | Font | font style.bold | True | True | Db |
| Judul Bab | Font | font style.italic | False | True | Db |
| Judul Bab | Font | font style.underline | False | True | Db |
| Judul Bab | Numbering | case | UPPERCASE | True | Db |
| Judul Bab | Numbering | enter after number | True | True | Db |
| Judul Bab | Numbering | number format | BAB I | False | Db |
| Judul Bab | Paragraph | alignment | center | False | Db |
| Judul Bab | Paragraph | indentation | none | False | Db |
| Judul Bab | Paragraph | spacing.after | 0 | True | Db |
| Judul Bab | Paragraph | spacing.before | 0 | True | Db |
| Judul Bab | Paragraph | spacing.line spacing | 1.5 | True | Db |
| Judul Bab | Struktur Konten | min satu paragraf sebelum subbab | True | True | Db |
| Judul Bab | Struktur Konten | satu baris kosong setelah | True | True | Db |
| Judul Subbab | Font | font name | Times New Roman | True | Db |
| Judul Subbab | Font | font size | 14 | True | Db |
| Judul Subbab | Font | font style.bold | True | True | Db |
| Judul Subbab | Font | font style.italic | False | True | Db |
| Judul Subbab | Font | font style.underline | False | True | Db |
| Judul Subbab | Numbering | case | Title Case | True | Db |
| Judul Subbab | Numbering | number format | 1.1, 1.1.1, 1.1.1.1 | False | Db |
| Judul Subbab | Paragraph | alignment | justify | True | Db |
| Judul Subbab | Paragraph | indentation.left indent | 0 | True | Db |
| Judul Subbab | Paragraph | indentation.right indent | 0 | True | Db |
| Judul Subbab | Paragraph | hanging max cm | 2.5 | True | Db |
| Judul Subbab | Paragraph | hanging min cm | 1.27 | True | Db |
| Judul Subbab | Paragraph | spacing.after | 0 | True | Db |
| Judul Subbab | Paragraph | spacing.before | 0 | True | Db |
| Judul Subbab | Paragraph | spacing.line spacing | 1.5 | True | Db |
| Judul Subbab | Struktur Konten | cegah posisi paling bawah | True | True | Db |
| Judul Subbab | Struktur Konten | cegah subbab tunggal | True | True | Db |
| Judul Subbab | Struktur Konten | minimal satu paragraf setelah | True | True | Db |
| Kode | Judul Kode | font.font name | Times New Roman | True | Db |
| Kode | Judul Kode | font.font size | 12 | True | Db |
| Kode | Judul Kode | font.font style.bold | True | True | Db |
| Kode | Judul Kode | font.font style.italic | False | True | Db |
| Kode | Judul Kode | font.font style.underline | False | True | Db |
| Kode | Judul Kode | numbering.case | Title Case | True | Db |
| Kode | Judul Kode | numbering.enter after numbering | False | True | Db |
| Kode | Judul Kode | numbering.number format | ["Algoritma [nomor_bab].[nomor_algo]", "Segmen Program [nomor_bab].[nomor_segpro]"] | False | Db |
| Kode | Judul Kode | paragraph.alignment | left | False | Db |
| Kode | Judul Kode | paragraph.indentation | none | False | Db |
| Kode | Judul Kode | paragraph.spacing.after | 0 | True | Db |
| Kode | Judul Kode | paragraph.spacing.before | 0 | True | Db |
| Kode | Judul Kode | paragraph.spacing.line spacing | 1 | True | Db |
| Kode | Judul Kode | position | before | True | Db |
| Kode | Kode | cegah gambar kode | True | True | Db |
| Kode | Kode | font.font name | Courier New | True | Db |
| Kode | Kode | font.font size | 10 | True | Db |
| Kode | Kode | font.font style.bold | False | True | Db |
| Kode | Kode | font.font style.italic | False | True | Db |
| Kode | Kode | font.font style.underline | False | True | Db |
| Kode | Kode | numbering.number format | %1 | True | Db |
| Kode | Kode | numbering.use numbering | True | True | Db |
| Kode | Kode | paragraph.alignment | left | True | Db |
| Kode | Kode | paragraph.indentation.hanging | 1 | True | Db |
| Kode | Kode | paragraph.indentation.left indent | 0 | True | Db |
| Kode | Kode | paragraph.spacing.after | 0 | True | Db |
| Kode | Kode | paragraph.spacing.before | 0 | True | Db |
| Kode | Kode | paragraph.spacing.line spacing | 1 | True | Db |
| Nomor Halaman | Nomor Halaman Akhir | continue | True | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.allow other content | False | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.position.alignment | center | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.position.indentation | None | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.position.location | footer | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.text style.font name | Times New Roman | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.text style.font size | 12 | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.text style.line spacing | 1 | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.text style.spacing after |  | False | Db |
| Nomor Halaman | Nomor Halaman Awal | default page.text style.spacing before | 0 | False | Db |
| Nomor Halaman | Nomor Halaman Awal | different first page | True | False | Db |
| Nomor Halaman | Nomor Halaman Awal | first page.allow other content | False | False | Db |
| Nomor Halaman | Nomor Halaman Awal | first page.is empty | True | False | Db |
| Nomor Halaman | Nomor Halaman Awal | number format.type | arab | True | Db |
| Nomor Halaman | Nomor Halaman Isi | continue | False | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.allow other content | False | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.number format.prefix | None | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.number format.type | arabic | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.position.alignment | center | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.position.indentation | None | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.position.location | footer | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.text style.font name | Times New Roman | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.text style.font size | 12 | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.text style.line spacing | 1 | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.text style.spacing after |  | False | Db |
| Nomor Halaman | Nomor Halaman Isi | default page.text style.spacing before | 0 | False | Db |
| Nomor Halaman | Nomor Halaman Isi | different first page | True | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.allow other content | False | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.is empty | False | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.number format.prefix | None | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.number format.type | arabic | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.position.alignment | right | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.position.indentation | None | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.position.location | header | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.text style.font name | Times New Roman | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.text style.font size | 12 | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.text style.line spacing | 1 | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.text style.spacing after |  | False | Db |
| Nomor Halaman | Nomor Halaman Isi | first page.text style.spacing before | 0 | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | continue | False | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.allow other content | False | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.number format.prefix | None | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.number format.type | arabic | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.position.alignment | center | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.position.indentation | None | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.position.location | footer | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.text style.font name | Times New Roman | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.text style.font size | 12 | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.text style.line spacing | 1 | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.text style.spacing after |  | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | default page.text style.spacing before | 0 | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | different first page | True | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.allow other content | False | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.is empty | False | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.number format.prefix | None | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.number format.type | arabic | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.position.alignment | right | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.position.indentation | None | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.position.location | header | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.text style.font name | Times New Roman | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.text style.font size | 12 | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.text style.line spacing | 1 | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.text style.spacing after |  | False | Db |
| Nomor Halaman | Nomor Halaman Lampiran | first page.text style.spacing before | 0 | False | Db |
| Paragraf | Font | font name | Times New Roman | True | Db |
| Paragraf | Font | font size | 12 | True | Db |
| Paragraf | Paragraph | alignment | justify | True | Db |
| Paragraf | Paragraph | indentation.left indent | 0 | True | Db |
| Paragraf | Paragraph | indentation.right indent | 0 | True | Db |
| Paragraf | Paragraph | indentation.first line indent | 1.27 | True | Db |
| Paragraf | Paragraph | spacing.after | 0 | True | Db |
| Paragraf | Paragraph | spacing.before | 0 | True | Db |
| Paragraf | Paragraph | spacing.line spacing | 1.5 | True | Db |
| Pengaturan Halaman | Header Footer | footer from bottom | aturan.txt=1.5; rule.txt=2.5 | True | Db (conflict) |
| Pengaturan Halaman | Header Footer | header from top | aturan.txt=2.5; rule.txt=1.5 | True | Db (conflict) |
| Pengaturan Halaman | Margin | paper.a3 landscape | {"top": 4, "left": 4, "bottom": 3, "right": 3} | True | Db |
| Pengaturan Halaman | Margin | paper.a4 landscape | {"top": 4, "left": 3, "bottom": 3, "right": 4} | True | Db |
| Pengaturan Halaman | Margin | paper.a4 portrait | {"top": 4, "left": 4, "bottom": 3, "right": 3} | True | Db |
| Pengaturan Halaman | Paper | section.akhir | [{"size": "A4", "orientation": "PORTRAIT"}] | True | Db |
| Pengaturan Halaman | Paper | section.awal | [{"size": "A4", "orientation": "PORTRAIT"}] | False | Db |
| Pengaturan Halaman | Paper | section.isi | [{"size": "A4", "orientation": "PORTRAIT"}] | True | Db |
| Pengaturan Halaman | Paper | section.lampiran | [{"size": "A4", "orientation": "PORTRAIT"}, {"size": "A4", "orientation": "LANDSCAPE"}, {"size": "A3", "orientation": "LANDSCAPE"}] | True | Db |
| Tabel | Caption Tabel | font.font name | Times New Roman | True | Db |
| Tabel | Caption Tabel | font.font size | 12 | True | Db |
| Tabel | Caption Tabel | font.font style.bold | True | True | Db |
| Tabel | Caption Tabel | font.font style.italic | False | True | Db |
| Tabel | Caption Tabel | font.font style.underline | False | True | Db |
| Tabel | Caption Tabel | numbering.case | Title Case | True | Db |
| Tabel | Caption Tabel | numbering.enter after number | True | True | Db |
| Tabel | Caption Tabel | numbering.enter after numbering | True | True | Db |
| Tabel | Caption Tabel | numbering.number format | Tabel [nomor_bab].[nomor_tabel] | False | Db |
| Tabel | Caption Tabel | paragraph.alignment | center | Mixed(aturan.txt=False; rule.txt=True) | Db |
| Tabel | Caption Tabel | paragraph.indentation | none | False | Db |
| Tabel | Caption Tabel | paragraph.spacing.after | 0 | True | Db |
| Tabel | Caption Tabel | paragraph.spacing.before | 0 | True | Db |
| Tabel | Caption Tabel | paragraph.spacing.line spacing | 1 | True | Db |
| Tabel | Caption Tabel | position | before | True | Db |
| Tabel | Tabel | cegah gambar tabel | True | True | Db |
| Tabel | Tabel | konten tabel.font.font name | Times New Roman | True | Db |
| Tabel | Tabel | konten tabel.font.font size | 12 | True | Db |
| Tabel | Tabel | konten tabel.paragraph.spacing.after | 0 | True | Db |
| Tabel | Tabel | konten tabel.paragraph.spacing.before | 0 | True | Db |
| Tabel | Tabel | konten tabel.paragraph.spacing.line spacing | 1 | True | Db |
| Tabel | Tabel | position.alignment | center | Mixed(aturan.txt=False; rule.txt=True) | Db |
| Tabel | Tabel | position.cegah melebihi margin | True | True | Db |
| Tabel | Tabel | position.cegah memenuhi halaman | True | True | Db |
| Tabel | Tabel | position.indent from left | aturan.txt=0; rule.txt=1 | Mixed(aturan.txt=False; rule.txt=True) | Db (conflict) |

## Hardcoded

| Kategori | Kriteria | Subkriteria | Value | Editable | Sumber |
| --- | --- | --- | --- | --- | --- |
| Caption Gambar | Eksistensi | kondisi | Jika aturan posisi ditetapkan, caption harus ada di sekitar gambar. | False | Hardcoded |
| Caption Tabel | Eksistensi | kondisi | Jika aturan posisi caption diterapkan, caption tabel harus ada di sekitar tabel (before/after sesuai aturan). | False | Hardcoded |
| Item Daftar | First Line Indent | kondisi | First line indent item daftar harus 0 cm. | False | Hardcoded |
| Judul Bab | Eksistensi | kondisi | Minimal 1 judul bab harus ada. | False | Hardcoded |
| Judul Bab | Format Paragraf | kondisi | Setiap baris judul bab harus memiliki paragraph format (tidak boleh ada baris tanpa format). | False | Hardcoded |
| Judul Bab | Format Teks | kondisi | Format teks judul bab harus ditemukan jika ada aturan font. | False | Hardcoded |
| Judul Bab | Judul Setelah Nomor | kondisi | Judul bab harus ada setelah nomor bab. | False | Hardcoded |
| Judul Bab | Nomor Bab Whitespace | kondisi | Nomor bab tidak boleh memiliki spasi di awal, tab, double space, atau >1 spasi di akhir. | False | Hardcoded |
| Judul Bab | Paragraf Kosong Font Size | kondisi | Ukuran font paragraf kosong setelah judul harus sama dengan judul (non-required/saran). | False | Hardcoded |
| Judul Bab | Posisi | kondisi | Judul bab harus berada di elemen pertama body. | False | Hardcoded |
| Judul Kode | Eksistensi | kondisi | Jika aturan posisi judul kode diterapkan, judul kode harus ada di sekitar blok kode (before/after sesuai aturan). | False | Hardcoded |
| Judul Subbab | Numbering Multilevel | kondisi | Jika aturan hanging aktif, judul subbab harus menggunakan numbering (multilevel list). | False | Hardcoded |
| Judul Subbab | Urutan Parent | kondisi | Jika ada subbab bertingkat (mis. 1.2.1), parent (1.2) harus ada. | False | Hardcoded |
| Judul Subbab | Urutan Sibling | kondisi | Tidak boleh loncat nomor sibling (mis. 1.3 tanpa 1.2). | False | Hardcoded |
| Paragraf | Hanging Indent After List | kondisi | Jika override setelah list aktif, hanging indent harus sesuai nilai yang dihitung. | False | Hardcoded |
| Paragraf | Left Indent After List | kondisi | Paragraf setelah list harus memiliki left indent sesuai aturan list/hanging yang dihitung dari item daftar terakhir. | False | Hardcoded |
| Paragraf | Sentence Count | kondisi | Saran: minimal 3 kalimat per paragraf (non-hard-constraint). | False | Hardcoded |
| Pengaturan Halaman | Column Count | kondisi | Jumlah kolom harus 1. | False | Hardcoded |
| Pengaturan Halaman | Different Odd Even | kondisi | Header/footer ganjil-genap harus nonaktif (false). | False | Hardcoded |
| Pengaturan Halaman | Gutter | kondisi | Ukuran gutter harus 0 cm (default jika tidak ada rule di DB; bisa di-override). | False | Hardcoded |
| Pengaturan Halaman | Page Number Format | kondisi | Format nomor halaman harus sesuai `page_numbering.section.*.format` untuk tipe section. | False | Hardcoded |
| Pengaturan Halaman | Page Number Start | kondisi | Nomor halaman awal harus sesuai `page_numbering.section.*.start` (nilai 0 diterima sebagai lanjutan ketika start=1). | False | Hardcoded |
| Pengaturan Halaman | Section | kondisi | Dokumen harus memiliki minimal 1 section. | False | Hardcoded |

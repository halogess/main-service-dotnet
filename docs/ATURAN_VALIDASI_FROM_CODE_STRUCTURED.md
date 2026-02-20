# Aturan Validasi (Struktur: Kategori/Kriteria/Subkriteria)

Sumber nilai DB diambil dari `rule.txt` (terbaru). Hardcoded diambil dari `docs/VALIDASI_SUMBER.md` + inspeksi kode.

## DB (rule.txt)

| Kategori | Kriteria | Subkriteria | Value | Editable | Sumber |
| --- | --- | --- | --- | --- | --- |

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
| Paragraf | Sentence Count | kondisi | Saran: minimal 3 kalimat per paragraf (IsRequired=false). | False | Hardcoded |
| Pengaturan Halaman | Column Count | kondisi | Jumlah kolom harus 1. | False | Hardcoded |
| Pengaturan Halaman | Different Odd Even | kondisi | Header/footer ganjil-genap harus nonaktif (false). | False | Hardcoded |
| Pengaturan Halaman | Gutter | kondisi | Ukuran gutter harus 0 cm (default jika tidak ada rule di DB; bisa di-override). | False | Hardcoded |
| Pengaturan Halaman | Page Number Format | kondisi | Format nomor halaman harus sesuai `page_numbering.section.*.format` untuk tipe section. | False | Hardcoded |
| Pengaturan Halaman | Page Number Start | kondisi | Nomor halaman awal harus sesuai `page_numbering.section.*.start` (nilai 0 diterima sebagai lanjutan ketika start=1). | False | Hardcoded |
| Pengaturan Halaman | Section | kondisi | Dokumen harus memiliki minimal 1 section. | False | Hardcoded |
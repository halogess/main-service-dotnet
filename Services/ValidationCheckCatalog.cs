using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

internal static class ValidationCheckCatalog
{
    private sealed record ValidationCheckDefinition(
        string Elemen,
        string FieldError,
        string Sumber,
        string AturanPath,
        bool AktifTanpaDb,
        bool BisaErrorTanpaDb,
        string YangDicek,
        params string[] RuleKeys);

    private static readonly IReadOnlyList<ValidationCheckDefinition> Definitions =
    [
        new("section", "section", "logika kode", "-", true, true, "Dokumen harus memiliki minimal 1 section."),
        new("paper", "paper", "DB", "paper.section.*", false, false, "Ukuran kertas dan orientasi tiap section harus sesuai aturan paper.", "paper"),
        new("margin", "margin_top/bottom/left/right", "DB", "margin.paper.*", false, false, "Margin top, bottom, left, right diperiksa per ukuran/orientasi kertas.", "margin"),
        new("header_footer", "header_from_top", "DB", "header_footer.header_from_top", false, false, "Jarak header dari atas diperiksa jika diatur.", "header_footer"),
        new("header_footer", "footer_from_bottom", "DB", "header_footer.footer_from_bottom", false, false, "Jarak footer dari bawah diperiksa jika diatur.", "header_footer"),
        new("different_odd_even", "different_odd_even", "logika kode", "-", true, true, "Header/footer ganjil-genap harus nonaktif."),
        new("gutter", "gutter", "DB / default kode", "gutter.gutter", true, true, "Ukuran gutter diperiksa; jika tidak ada di DB, validator memakai default 0 cm.", "gutter"),
        new("gutter", "gutter_position", "DB", "gutter.position", false, false, "Posisi gutter diperiksa jika diatur.", "gutter"),
        new("column", "column_count", "DB / default kode", "column.count", true, true, "Jumlah kolom section harus 1; jika tidak ada di DB, validator memakai default 1.", "column"),
        new("page_numbering", "page_number_format", "DB", "page_numbering.section.*.format", false, false, "Format nomor halaman per section diperiksa dari page_numbering.", "page_numbering"),
        new("page_numbering", "page_number_start", "DB", "page_numbering.section.*.start", false, false, "Nomor awal halaman per section diperiksa dari page_numbering.", "page_numbering"),
        new("nomor_halaman", "page_number_continue", "DB", "nomor_halaman_*.continue", false, false, "Lanjutan/restart nomor halaman diperiksa jika aturan nomor_halaman_* tersedia.", "nomor_halaman_awal", "nomor_halaman_isi", "nomor_halaman_akhir", "nomor_halaman_lampiran"),
        new("nomor_halaman", "different_first_page", "DB", "nomor_halaman_*.different_first_page", false, false, "Pengaturan different first page diperiksa jika aturan nomor_halaman_* tersedia.", "nomor_halaman_awal", "nomor_halaman_isi", "nomor_halaman_akhir", "nomor_halaman_lampiran"),
        new("nomor_halaman", "first_page_is_empty", "DB", "nomor_halaman_*.first_page.is_empty", false, false, "Kekosongan halaman pertama diperiksa jika diatur di nomor_halaman_*.", "nomor_halaman_awal", "nomor_halaman_isi", "nomor_halaman_akhir", "nomor_halaman_lampiran"),
        new("nomor_halaman", "page_number_format", "DB", "nomor_halaman_*.*.number_format.type", false, false, "Format nomor halaman dari nomor_halaman_* dipakai bila tersedia.", "nomor_halaman_awal", "nomor_halaman_isi", "nomor_halaman_akhir", "nomor_halaman_lampiran"),

        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab", false, false, "Eksistensi minimal satu judul bab diperiksa saat aturan judul_bab aktif.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab", false, false, "Judul bab harus berada di elemen pertama body.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab.numbering.number_format", false, false, "Nomor bab tidak boleh punya whitespace/tab/double space yang tidak valid.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.numbering.number_format + numbering.case", false, false, "Format nomor bab diperiksa terhadap number_format dan case.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab", false, false, "Judul bab harus ada setelah nomor bab.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab.numbering.enter_after_number", false, false, "Jika enter_after_number aktif, judul harus di baris berikutnya tanpa baris kosong di tengah.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab.struktur_konten.satu_baris_kosong_setelah", false, false, "Jika aktif, harus ada tepat 1 paragraf kosong setelah judul lalu diikuti paragraf non-kosong.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab", false, false, "Format paragraf judul bab harus lengkap untuk semua baris judul.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.paragraph.alignment", false, false, "Alignment judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.paragraph.indentation", false, false, "Indentasi judul bab diperiksa sesuai aturan.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.paragraph.spacing.line_spacing", false, false, "Line spacing judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.paragraph.spacing.before", false, false, "Spacing before judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.paragraph.spacing.after", false, false, "Spacing after judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab.font.*", false, false, "Format teks judul bab harus bisa ditemukan saat aturan font diaktifkan.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.font.font_name", false, false, "Font name judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.font.font_size", false, false, "Ukuran font judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.font.font_style.bold", false, false, "Bold judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.font.font_style.italic", false, false, "Italic judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB", "judul_bab.font.font_style.underline", false, false, "Underline judul bab diperiksa.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab.struktur_konten.min_satu_paragraf_sebelum_subbab", false, false, "Jika aktif, harus ada minimal satu paragraf sebelum subbab pertama.", "judul_bab"),
        new("judul_bab", "judul_bab", "DB + logika kode", "judul_bab", false, false, "Ukuran font paragraf kosong setelah judul dibandingkan dengan judul sebagai saran/non-required.", "judul_bab"),

        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.font.font_name", false, false, "Font name judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.font.font_size", false, false, "Ukuran font judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.font.font_style.bold/italic/underline", false, false, "Style font judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.paragraph.alignment", false, false, "Alignment judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB + logika kode", "judul_subbab.paragraph.hanging_min_cm + hanging_max_cm", false, false, "Hanging indent judul subbab diperiksa; bila aturan hanging aktif, numbering multilevel juga harus ada.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.paragraph.spacing.line_spacing", false, false, "Line spacing judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.paragraph.spacing.before", false, false, "Spacing before judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.paragraph.spacing.after", false, false, "Spacing after judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB", "judul_subbab.numbering.case", false, false, "Case judul subbab diperiksa.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB + logika kode", "judul_subbab.struktur_konten.minimal_satu_paragraf_setelah", false, false, "Jika aktif, harus ada minimal satu paragraf setelah judul subbab.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB + logika kode", "judul_subbab.struktur_konten.cegah_posisi_paling_bawah", false, false, "Jika aktif, judul subbab tidak boleh berada paling bawah halaman.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB + logika kode", "judul_subbab", false, false, "Urutan subbab diperiksa: nomor unik, sesuai bab, parent ada, dan sibling tidak loncat.", "judul_subbab"),
        new("judul_subbab", "judul_subbab", "DB + logika kode", "judul_subbab.struktur_konten.cegah_subbab_tunggal", false, false, "Jika aktif, subbab tunggal pada level yang sama dianggap error.", "judul_subbab"),

        new("paragraf", "paragraf", "DB", "paragraf.font.font_name", false, false, "Font name paragraf diperiksa.", "paragraf"),
        new("paragraf", "paragraf", "DB", "paragraf.font.font_size", false, false, "Ukuran font paragraf diperiksa.", "paragraf"),
        new("paragraf", "paragraf", "DB", "paragraf.paragraph.alignment", false, false, "Alignment paragraf diperiksa.", "paragraf"),
        new("paragraf", "paragraf", "DB + logika kode", "paragraf.paragraph.first_line_indent", false, false, "First line indent paragraf diperiksa; setelah list bisa dioverride oleh hasil hitung list.", "paragraf", "item_daftar"),
        new("paragraf", "paragraf", "logika kode", "-", false, false, "Left indent paragraf harus 0; setelah list bisa dioverride dari item daftar terakhir.", "paragraf", "item_daftar"),
        new("paragraf", "paragraf", "logika kode", "-", false, false, "Hanging indent paragraf setelah list diperiksa bila override aktif.", "paragraf", "item_daftar"),
        new("paragraf", "paragraf", "logika kode", "-", false, false, "Right indent paragraf harus 0.", "paragraf"),
        new("paragraf", "paragraf", "DB", "paragraf.paragraph.spacing.line_spacing", false, false, "Line spacing paragraf diperiksa.", "paragraf"),
        new("paragraf", "paragraf", "DB", "paragraf.paragraph.spacing.before", false, false, "Spacing before paragraf diperiksa.", "paragraf"),
        new("paragraf", "paragraf", "DB", "paragraf.paragraph.spacing.after", false, false, "Spacing after paragraf diperiksa.", "paragraf"),
        new("paragraf", "paragraf", "logika kode", "-", false, false, "Jumlah kalimat paragraf minimal 3 diperiksa sebagai saran/non-required.", "paragraf"),

        new("item_daftar", "item_daftar", "DB", "item_daftar.font.font_name", false, false, "Font name item daftar diperiksa.", "item_daftar"),
        new("item_daftar", "item_daftar", "DB", "item_daftar.font.font_size", false, false, "Ukuran font item daftar diperiksa.", "item_daftar"),
        new("item_daftar", "item_daftar", "DB", "item_daftar.paragraph.alignment", false, false, "Alignment item daftar diperiksa.", "item_daftar"),
        new("item_daftar", "item_daftar", "DB", "item_daftar.paragraph.indentation.hanging", false, false, "Hanging indent item daftar diperiksa.", "item_daftar"),
        new("item_daftar", "item_daftar", "DB + logika kode", "item_daftar.paragraph.indentation.left_indent", false, false, "Left indent item daftar diperiksa dan disesuaikan per level list.", "item_daftar"),
        new("item_daftar", "item_daftar", "logika kode", "-", false, false, "First line indent item daftar harus 0.", "item_daftar"),
        new("item_daftar", "item_daftar", "logika kode", "-", false, false, "Right indent item daftar harus 0.", "item_daftar"),
        new("item_daftar", "item_daftar", "DB", "item_daftar.paragraph.spacing.line_spacing", false, false, "Line spacing item daftar diperiksa.", "item_daftar"),
        new("item_daftar", "item_daftar", "DB", "item_daftar.paragraph.spacing.before", false, false, "Spacing before item daftar diperiksa.", "item_daftar"),
        new("item_daftar", "item_daftar", "DB", "item_daftar.paragraph.spacing.after", false, false, "Spacing after item daftar diperiksa.", "item_daftar"),

        new("gambar", "gambar", "DB", "gambar.paragraph.alignment", false, false, "Alignment paragraf gambar diperiksa.", "gambar"),
        new("gambar", "gambar", "DB", "gambar.paragraph.indentation", false, false, "Indentasi paragraf gambar diperiksa.", "gambar"),
        new("gambar", "gambar", "DB", "gambar.paragraph.spacing.line_spacing/before/after", false, false, "Line spacing dan spacing paragraf gambar diperiksa.", "gambar"),
        new("gambar", "gambar", "DB", "gambar.position.layout_option", false, false, "Layout option gambar diperiksa, misalnya inline_with_text.", "gambar"),
        new("gambar", "gambar", "DB + logika kode", "gambar.position.cegah_melebihi_margin", false, false, "Jika aktif, lebar gambar tidak boleh melebihi area teks.", "gambar"),
        new("gambar", "gambar", "DB + logika kode", "gambar.position.cegah_memenuhi_halaman", false, false, "Jika aktif, halaman gambar tidak boleh hanya berisi gambar dan caption.", "gambar"),
        new("caption_gambar", "caption_gambar", "DB + logika kode", "caption_gambar.position", false, false, "Posisi caption gambar diperiksa dan caption wajib ada di before/after sesuai aturan.", "gambar", "caption_gambar"),
        new("caption_gambar", "caption_gambar", "DB", "caption_gambar.font.*", false, false, "Font name, size, dan style caption gambar diperiksa.", "gambar", "caption_gambar"),
        new("caption_gambar", "caption_gambar", "DB", "caption_gambar.paragraph.alignment + indentation + spacing", false, false, "Format paragraf caption gambar diperiksa.", "gambar", "caption_gambar"),
        new("caption_gambar", "caption_gambar", "DB + logika kode", "caption_gambar.numbering.number_format + enter_after_numbering + case", false, false, "Format nomor, keberadaan judul setelah nomor, dan Title Case caption gambar diperiksa.", "gambar", "caption_gambar"),

        new("tabel", "tabel", "DB", "tabel.position.alignment", false, false, "Alignment tabel diperiksa.", "tabel"),
        new("tabel", "tabel", "DB", "tabel.position.indent_from_left", false, false, "Indentasi tabel dari kiri diperiksa.", "tabel"),
        new("tabel", "tabel", "DB + logika kode", "tabel.position.cegah_melebihi_margin", false, false, "Jika aktif, lebar tabel tidak boleh melebihi area teks.", "tabel"),
        new("tabel", "tabel", "DB + logika kode", "tabel.position.cegah_memenuhi_halaman", false, false, "Jika aktif, halaman tabel tidak boleh hanya berisi tabel dan caption.", "tabel"),
        new("tabel", "tabel", "DB + logika kode", "tabel.cegah_gambar_tabel", false, false, "Tabel tidak boleh berupa gambar dan tidak boleh berisi gambar jika rule aktif.", "tabel"),
        new("tabel", "tabel", "DB", "tabel.konten_tabel.font.*", false, false, "Font konten tabel diperiksa.", "tabel"),
        new("tabel", "tabel", "DB", "tabel.konten_tabel.paragraph.spacing.*", false, false, "Spacing konten tabel diperiksa.", "tabel"),
        new("caption_tabel", "caption_tabel", "DB + logika kode", "caption_tabel.position", false, false, "Posisi caption tabel diperiksa dan caption wajib ada di before/after sesuai aturan.", "tabel", "caption_tabel"),
        new("caption_tabel", "caption_tabel", "DB", "caption_tabel.font.*", false, false, "Font name, size, dan style caption tabel diperiksa.", "tabel", "caption_tabel"),
        new("caption_tabel", "caption_tabel", "DB", "caption_tabel.paragraph.alignment + indentation + spacing", false, false, "Format paragraf caption tabel diperiksa.", "tabel", "caption_tabel"),
        new("caption_tabel", "caption_tabel", "DB + logika kode", "caption_tabel.numbering.number_format + enter_after_numbering + case", false, false, "Format nomor, keberadaan judul setelah nomor, dan Title Case caption tabel diperiksa.", "tabel", "caption_tabel"),

        new("kode", "kode", "DB + logika kode", "kode.cegah_tabel_kode", false, false, "Kode tidak boleh berupa tabel jika rule aktif.", "kode"),
        new("kode", "kode", "DB + logika kode", "kode.cegah_gambar_kode", false, false, "Kode tidak boleh berupa gambar/objek non-teks jika rule aktif.", "kode"),
        new("kode", "kode", "DB", "kode.font.*", false, false, "Font name, size, dan style kode diperiksa.", "kode"),
        new("kode", "kode", "DB", "kode.paragraph.alignment + indentation + spacing", false, false, "Format paragraf kode diperiksa, termasuk right indent harus 0.", "kode"),
        new("kode", "kode", "DB", "kode.numbering.use_numbering + number_format", false, false, "Penggunaan numbering dan format numbering kode diperiksa.", "kode"),
        new("judul_kode", "judul_kode", "DB + logika kode", "judul_kode.position", false, false, "Posisi judul kode diperiksa dan judul wajib ada di before/after sesuai aturan.", "kode", "judul_kode"),
        new("judul_kode", "judul_kode", "DB", "judul_kode.font.*", false, false, "Font name, size, dan style judul kode diperiksa.", "kode", "judul_kode"),
        new("judul_kode", "judul_kode", "DB", "judul_kode.paragraph.alignment + indentation + spacing", false, false, "Format paragraf judul kode diperiksa.", "kode", "judul_kode"),
        new("judul_kode", "judul_kode", "DB + logika kode", "judul_kode.numbering.number_format + enter_after_numbering + case", false, false, "Format nomor, keberadaan judul setelah nomor, dan Title Case judul kode diperiksa.", "kode", "judul_kode"),

        new("rumus", "rumus", "DB", "rumus.font.*", false, false, "Font name dan ukuran font rumus diperiksa.", "rumus"),
        new("rumus", "rumus", "DB", "rumus.paragraph.alignment + position.paragraph_alignment + position.equation_alignment", false, false, "Alignment paragraf/equation rumus diperiksa.", "rumus"),
        new("rumus", "rumus", "DB", "rumus.paragraph.indentation.* + position.overall_indent_cm", false, false, "First line indent, left indent, overall indent, dan right indent 0 diperiksa.", "rumus"),
        new("rumus", "rumus", "DB", "rumus.paragraph.spacing.*", false, false, "Line spacing dan spacing rumus diperiksa.", "rumus"),
        new("rumus", "rumus", "DB + logika kode", "rumus.tabs.left_tab + tabs.right_tab", false, false, "Tab stop kiri/kanan diperiksa: keberadaan, alignment, leader, dan posisi/jarak minimum.", "rumus"),
        new("rumus", "rumus", "DB + logika kode", "rumus.numbering.number_format", false, false, "Nomor rumus harus ada dan sesuai format.", "rumus"),
        new("rumus", "rumus", "DB + logika kode", "rumus.position.cegah_memenuhi_halaman + struktur_halaman.*", false, false, "Halaman rumus tidak boleh hanya berisi rumus bila rule struktur_halaman/position aktif.", "rumus"),

        new("footnote", "footnote_numbering_format", "DB + logika kode", "footnote.numbering.number_format", false, false, "Format nomor footnote diperiksa, misalnya arabic.", "footnote"),
        new("footnote", "footnote_numbering_type", "DB + logika kode", "footnote.numbering.type", false, false, "Penomoran footnote continuous diperiksa.", "footnote"),
        new("footnote", "footnote_separator_*", "DB + logika kode", "footnote.separator.paragraph.* + separator.cegah_tab_awal", false, false, "Separator footnote diperiksa: alignment, indentasi, spacing, dan larangan tab awal.", "footnote"),
        new("footnote", "footnote_font_*", "DB", "footnote.footnote_text.font.*", false, false, "Font name dan ukuran font footnote diperiksa.", "footnote"),
        new("footnote", "footnote_alignment/spacing_*", "DB", "footnote.footnote_text.paragraph.*", false, false, "Alignment dan spacing paragraf footnote diperiksa.", "footnote"),
        new("footnote", "footnote_struktur_konten", "DB + logika kode", "footnote.footnote_text.struktur_konten.satu_enter_sebelum", false, false, "Best-effort: konten footnote tidak boleh diawali tab saat rule satu_enter_sebelum aktif.", "footnote"),
        new("footnote", "footnote_sumber", "DB + logika kode", "footnote.sumber.wajib_berisi_sumber", false, false, "Footnote wajib berisi sumber jika rule aktif.", "footnote"),
        new("footnote", "footnote_sumber_format", "DB + logika kode", "footnote.sumber.format_penulisan", false, false, "Format penulisan sumber footnote diperiksa sebagai saran/non-required.", "footnote"),

        new("daftar_pustaka", "daftar_pustaka", "DB", "daftar_pustaka.font.*", false, false, "Font name dan ukuran font daftar pustaka diperiksa.", "daftar_pustaka"),
        new("daftar_pustaka", "daftar_pustaka", "DB", "daftar_pustaka.paragraph.alignment + spacing", false, false, "Alignment dan spacing daftar pustaka diperiksa.", "daftar_pustaka"),
        new("daftar_pustaka", "daftar_pustaka_urut_abjad", "DB + logika kode", "daftar_pustaka.urut_abjad", false, false, "Urutan daftar pustaka A-Z diperiksa jika rule aktif.", "daftar_pustaka"),
        new("daftar_pustaka", "daftar_pustaka_struktur_konten", "DB + logika kode", "daftar_pustaka.struktur_konten.satu_enter_antar_sumber", false, false, "Jika aktif, harus ada tepat 1 baris kosong antar sumber.", "daftar_pustaka"),
    ];

    public static IReadOnlyList<ValidationCheckExportRow> BuildRows(IReadOnlyList<AturanDetail> details)
    {
        var existingKeys = details
            .Select(detail => NormalizeKey(detail.AturanDetailKey))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Definitions
            .Select(definition =>
            {
                var activeFromDb = definition.RuleKeys.Any(existingKeys.Contains);
                return new ValidationCheckExportRow(
                    definition.Elemen,
                    definition.FieldError,
                    definition.Sumber,
                    definition.AturanPath,
                    definition.AktifTanpaDb || activeFromDb,
                    definition.BisaErrorTanpaDb,
                    definition.YangDicek);
            })
            .ToList();
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}

internal sealed record ValidationCheckExportRow(
    string Elemen,
    string FieldError,
    string Sumber,
    string AturanPath,
    bool AktifSaatIni,
    bool BisaErrorTanpaDb,
    string YangDicek);

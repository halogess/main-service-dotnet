using System.Text.Json;
using System.Text.Json.Nodes;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

internal static class AturanExportCatalog
{
    private const string TemplateNote = "Template export default; aturan ini belum ada di DB.";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private sealed record RuleTemplate(string Kategori, string Key, string JsonValue);

    private static readonly IReadOnlyList<RuleTemplate> ValidationRuleTemplates =
    [
        new(
            "Pengaturan Halaman",
            "page_settings",
            """
            {"paper":{"size":{"value":"A4","is_editable":true},"orientation":{"value":"PORTRAIT","is_editable":true}},"margin":{"top":{"value":4,"is_editable":true},"bottom":{"value":3,"is_editable":true},"left":{"value":4,"is_editable":true},"right":{"value":3,"is_editable":true}},"header_footer":{"header_from_top":{"value":2.5,"is_editable":true},"footer_from_bottom":{"value":1.5,"is_editable":true}},"gutter":{"size":{"value":0,"is_editable":true},"position":{"value":"left","is_editable":true}},"column":{"value":1,"is_editable":true},"akhir_halaman":{"max_baris_kosong":{"value":3,"is_editable":true},"cegah_halaman_kosong":{"value":true,"is_editable":true}}}
            """),
        new(
            "Nomor Halaman",
            "nomor_halaman",
            """
            {"numbering":{"number_format":{"value":"decimal","is_editable":false}},"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":12,"is_editable":true},"font_style":{"bold":{"value":false,"is_editable":true},"italic":{"value":false,"is_editable":true},"underline":{"value":false,"is_editable":true}}},"paragraph":{"indentation":{"left_indent":{"value":0,"is_editable":true},"right_indent":{"value":0,"is_editable":true},"first_line_indent":{"value":0,"is_editable":true}},"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"struktur_konten":{"cegah_baris_tambahan":{"value":true,"is_editable":true}},"variation":{"default":{"position":{"location":{"value":"footer","is_editable":true},"alignment":{"value":"center","is_editable":true}}},"different_first_page":{"enabled":{"value":true,"is_editable":true},"first":{"position":{"location":{"value":"header","is_editable":true},"alignment":{"value":"right","is_editable":true}}}},"different_odd_even":{"enabled":{"value":false,"is_editable":true},"even":{"position":{"location":{"value":"footer","is_editable":true},"alignment":{"value":"left","is_editable":true}}}}}}
            """),
        new(
            "Isi Buku",
            "judul_bab",
            """
            {"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":16,"is_editable":true},"font_style":{"bold":{"value":true,"is_editable":true},"italic":{"value":false,"is_editable":true},"underline":{"value":false,"is_editable":true}}},"paragraph":{"alignment":{"value":"center","is_editable":false},"indentation":{"left_indent":{"value":0,"is_editable":true},"right_indent":{"value":0,"is_editable":true},"first_line_indent":{"value":0,"is_editable":true}},"spacing":{"line_spacing":{"value":1.5,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"numbering":{"number_format":{"value":"BAB I","is_editable":false},"case":{"value":"UPPERCASE","is_editable":true},"enter_after_number":{"value":true,"is_editable":true}},"struktur_konten":{"jumlah_baris_kosong_setelah":{"value":1,"is_editable":true},"minimal_paragraf_sebelum_subbab":{"value":1,"is_editable":true}}}
            """),
        new(
            "Isi Buku",
            "judul_subbab",
            """
            {"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":14,"is_editable":true},"font_style":{"bold":{"value":true,"is_editable":true},"italic":{"value":false,"is_editable":true},"underline":{"value":false,"is_editable":true}}},"paragraph":{"alignment":{"value":"justify","is_editable":true},"indentation":{"left_indent":{"value":0,"is_editable":true},"right_indent":{"value":0,"is_editable":true}},"hanging_min_cm":{"value":1.27,"is_editable":true},"hanging_max_cm":{"value":2.5,"is_editable":true},"spacing":{"line_spacing":{"value":1.5,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"numbering":{"number_format":{"value":"1.1, 1.1.1, 1.1.1.1","is_editable":false},"case":{"value":"Title Case","is_editable":true}},"struktur_konten":{"minimal_paragraf_setelah":{"value":1,"is_editable":true},"cegah_posisi_paling_bawah":{"value":true,"is_editable":true},"minimal_subbab_level_sama":{"value":2,"is_editable":true},"jumlah_baris_kosong_sebelum":{"value":1,"is_editable":true},"abaikan_jika_di_awal_halaman":{"value":true,"is_editable":true}}}
            """),
        new(
            "Isi Buku",
            "paragraf",
            """
            {"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":12,"is_editable":true}},"paragraph":{"alignment":{"value":"justify","is_editable":true},"indentation":{"left_indent":{"value":0,"is_editable":true},"right_indent":{"value":0,"is_editable":true},"first_line_indent":{"value":1.27,"is_editable":true}},"spacing":{"line_spacing":{"value":1.5,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"struktur_konten":{"minimal_kalimat":{"value":3,"is_editable":true}}}
            """),
        new(
            "Isi Buku",
            "item_daftar",
            """
            {"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":12,"is_editable":true}},"paragraph":{"alignment":{"value":"justify","is_editable":true},"indentation":{"left_indent":{"value":0,"is_editable":true},"right_indent":{"value":0,"is_editable":true},"hanging":{"value":0.75,"is_editable":true}},"spacing":{"line_spacing":{"value":1.5,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"struktur_konten":{"cegah_posisi_paling_bawah":{"value":true,"is_editable":true},"cegah_daftar_tunggal":{"value":true,"is_editable":true}}}
            """),
        new(
            "Isi Buku",
            "gambar",
            """
            {"gambar":{"paragraph":{"alignment":{"value":"center","is_editable":true},"indentation":{"is_editable":false,"is_hard_constraint":false,"left_indent":{"value":0,"is_editable":false},"right_indent":{"value":0,"is_editable":false},"first_line_indent":{"value":0,"is_editable":false}},"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"position":{"layout_option":{"value":"inline_with_text","is_editable":false},"cegah_melebihi_margin":{"value":true,"is_editable":true},"cegah_memenuhi_halaman":{"value":true,"is_editable":true}},"struktur_konten":{"jumlah_baris_kosong_sebelum":{"value":1,"is_editable":true},"jumlah_baris_kosong_setelah":{"value":1,"is_editable":true},"abaikan_jika_di_awal_halaman":{"value":true,"is_editable":true}}},"caption_gambar":{"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":12,"is_editable":true},"font_style":{"bold":{"value":true,"is_editable":true},"italic":{"value":false,"is_editable":true},"underline":{"value":false,"is_editable":true}}},"paragraph":{"alignment":{"value":"center","is_editable":false},"indentation":{"is_editable":false,"is_hard_constraint":false,"left_indent":{"value":0,"is_editable":false},"right_indent":{"value":0,"is_editable":false},"first_line_indent":{"value":0,"is_editable":false}},"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"numbering":{"number_format":{"value":"Gambar [nomor_bab].[nomor_gambar]","is_editable":false},"case":{"value":"Title Case","is_editable":true},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"after","is_editable":true}}}
            """),
        new(
            "Isi Buku",
            "tabel",
            """
            {"tabel":{"position":{"alignment":{"value":"center","is_editable":true},"indent_from_left":{"value":1,"is_editable":true},"cegah_melebihi_margin":{"value":true,"is_editable":true},"cegah_memenuhi_halaman":{"value":true,"is_editable":true}},"konten_tabel":{"font":{"font_name":{"value":"Times New Roman","is_editable":true}},"paragraph":{"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}}},"cegah_gambar_tabel":{"value":true,"is_editable":true},"struktur_konten":{"jumlah_baris_kosong_sebelum":{"value":1,"is_editable":true},"jumlah_baris_kosong_setelah":{"value":1,"is_editable":true},"abaikan_jika_di_awal_halaman":{"value":true,"is_editable":true}}},"caption_tabel":{"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":12,"is_editable":true},"font_style":{"bold":{"value":true,"is_editable":true},"italic":{"value":false,"is_editable":true},"underline":{"value":false,"is_editable":true}}},"paragraph":{"alignment":{"value":"center","is_editable":true},"indentation":{"is_editable":false,"is_hard_constraint":false,"left_indent":{"value":0,"is_editable":false},"right_indent":{"value":0,"is_editable":false},"first_line_indent":{"value":0,"is_editable":false}},"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"numbering":{"number_format":{"value":"Tabel [nomor_bab].[nomor_tabel]","is_editable":false},"case":{"value":"Title Case","is_editable":true},"enter_after_numbering":{"value":true,"is_editable":true}},"position":{"value":"before","is_editable":true},"wajib_caption_lanjutan_jika_lintas_halaman":{"value":true,"is_editable":true}}}
            """),
        new(
            "Isi Buku",
            "kode",
            """
            {"kode":{"font":{"font_name":{"value":"Courier New","is_editable":true},"font_size":{"value":10,"is_editable":true},"font_style":{"bold":{"value":false,"is_editable":true},"italic":{"value":false,"is_editable":true},"underline":{"value":false,"is_editable":true}}},"paragraph":{"alignment":{"value":"left","is_editable":true},"indentation":{"left_indent":{"value":0,"is_editable":true},"right_indent":{"value":0,"is_editable":true},"hanging":{"value":1,"is_editable":true}},"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"numbering":{"use_numbering":{"value":true,"is_editable":true},"number_format":{"value":"%1","is_editable":true}},"cegah_gambar_kode":{"value":true,"is_editable":true},"cegah_tabel_kode":{"value":true,"is_editable":true},"struktur_konten":{"jumlah_baris_kosong_sebelum":{"value":1,"is_editable":true},"jumlah_baris_kosong_setelah":{"value":1,"is_editable":true},"abaikan_jika_di_awal_halaman":{"value":true,"is_editable":true}}},"judul_kode":{"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":12,"is_editable":true},"font_style":{"bold":{"value":true,"is_editable":true},"italic":{"value":false,"is_editable":true},"underline":{"value":false,"is_editable":true}}},"paragraph":{"alignment":{"value":"left","is_editable":false},"indentation":{"is_editable":false,"is_hard_constraint":false,"left_indent":{"value":0,"is_editable":false},"right_indent":{"value":0,"is_editable":false},"first_line_indent":{"value":0,"is_editable":false}},"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"numbering":{"number_format":{"value":["Algoritma [nomor_bab].[nomor_algo]","Segmen Program [nomor_bab].[nomor_segpro]"],"is_editable":false},"case":{"value":"Title Case","is_editable":true},"enter_after_numbering":{"value":false,"is_editable":true}},"position":{"value":"before","is_editable":true},"wajib_caption_lanjutan_jika_lintas_halaman":{"value":true,"is_editable":true}}}
            """),
        new(
            "Isi Buku",
            "rumus",
            """
            {"font":{"font_name":{"value":"Cambria Math","is_editable":true},"font_size":{"value":12,"is_editable":true}},"paragraph":{"alignment":{"value":"justify","is_editable":true},"indentation":{"first_line_indent":{"value":1.27,"is_editable":true},"left_indent":{"value":0,"is_editable":true},"right_indent":{"value":0,"is_editable":true}},"spacing":{"line_spacing":{"value":1.5,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"tabs":{"left_tab":{"distance_from_equation_cm":{"value":1.27,"is_editable":true},"alignment":{"value":"left","is_editable":true},"leader_style":{"value":"none","is_editable":true},"depends_on_equation_length":{"value":false,"is_editable":true}},"right_tab":{"position_cm":{"value":13.5,"is_editable":true},"alignment":{"value":"right","is_editable":true},"leader_style":{"value":"dots","is_editable":true},"depends_on_equation_length":{"value":false,"is_editable":true}}},"numbering":{"number_format":{"value":"([nomor_bab].[nomor_rumus])","is_editable":false}},"position":{"cegah_memenuhi_halaman":{"value":true,"is_editable":true},"overall_indent_cm":{"value":0,"is_editable":true}},"struktur_halaman":{"minimal_satu_paragraf_di_halaman":{"value":false,"is_editable":true}}}
            """),
        new(
            "Referensi",
            "footnote",
            """
            {"numbering":{"number_format":{"value":"arabic","is_editable":true},"type":{"value":"continuous","is_editable":true}},"separator":{"paragraph":{"alignment":{"value":"left","is_editable":false},"indentation":{"left_indent":{"value":0,"is_editable":false},"first_line_indent":{"value":0,"is_editable":false}},"spacing":{"before":{"value":0,"is_editable":false},"after":{"value":0,"is_editable":false},"line_spacing":{"value":1,"is_editable":true}}},"cegah_tab_awal":{"value":true,"is_editable":true}},"footnote_text":{"font":{"font_name":{"value":"Times New Roman","is_editable":true},"font_size":{"value":10,"is_editable":true}},"paragraph":{"alignment":{"value":"left","is_editable":true},"spacing":{"line_spacing":{"value":1,"is_editable":true},"before":{"value":0,"is_editable":true},"after":{"value":0,"is_editable":true}}},"struktur_konten":{"satu_enter_sebelum":{"value":true,"is_editable":true}}}}
            """)
    ];

    public static IReadOnlyList<AturanDetail> CreateDefaultDetails(uint aturanId, bool appendTemplateNote = false)
    {
        return ValidationRuleTemplates
            .Select(template => new AturanDetail
            {
                AturanId = aturanId,
                AturanDetailKategori = template.Kategori,
                AturanDetailKey = template.Key,
                AturanDetailJsonValue = CreateCanonicalTemplateJson(template),
                AturanDetailCatatan = appendTemplateNote ? TemplateNote : null
            })
            .ToList();
    }

    public static IReadOnlyList<AturanDetail> MergeValidationTemplates(IReadOnlyList<AturanDetail> details)
    {
        var merged = new List<AturanDetail>(details);
        var existingKeys = details
            .Select(detail => NormalizeKey(detail.AturanDetailKey))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var syntheticId = 0u;
        var aturanId = details.FirstOrDefault()?.AturanId ?? 0u;

        foreach (var template in ValidationRuleTemplates)
        {
            if (existingKeys.Contains(template.Key))
                continue;

            var detail = CreateDefaultDetails(aturanId, appendTemplateNote: true)
                .First(item => string.Equals(item.AturanDetailKey, template.Key, StringComparison.OrdinalIgnoreCase));
            detail.AturanDetailId = syntheticId++;
            merged.Add(detail);
        }

        return merged;
    }

    public static IReadOnlySet<string> GetSyntheticElementKeys(IReadOnlyList<AturanDetail> details)
    {
        var existingKeys = details
            .Select(detail => NormalizeKey(detail.AturanDetailKey))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var syntheticElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in ValidationRuleTemplates)
        {
            if (existingKeys.Contains(template.Key))
                continue;

            syntheticElements.Add(template.Key);
            switch (template.Key)
            {
                case "gambar":
                    if (!existingKeys.Contains("caption_gambar"))
                        syntheticElements.Add("caption_gambar");
                    break;
                case "tabel":
                    if (!existingKeys.Contains("caption_tabel"))
                        syntheticElements.Add("caption_tabel");
                    break;
                case "kode":
                    if (!existingKeys.Contains("judul_kode"))
                        syntheticElements.Add("judul_kode");
                    break;
            }
        }

        return syntheticElements;
    }

    public static string AppendTemplateNote(string note)
    {
        return AppendNote(note, TemplateNote);
    }

    private static string AppendNote(string note, string extra)
    {
        if (string.IsNullOrWhiteSpace(note))
            return extra;

        return $"{note} | {extra}";
    }

    private static string CreateCanonicalTemplateJson(RuleTemplate template)
    {
        if (JsonNode.Parse(template.JsonValue) is not JsonObject root)
            throw new InvalidOperationException($"Template aturan `{template.Key}` tidak valid.");

        AturanDetailEditablePolicy.Apply(template.Key, root);
        return root.ToJsonString(SerializerOptions);
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}

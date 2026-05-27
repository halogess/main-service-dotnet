using System.Data;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

#region DTOs for Aturan JSON Values

public class RuleValue<T>
{
    [JsonPropertyName("value")]
    public T? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }

    [JsonPropertyName("is_hard_constraint")]
    public bool IsHardConstraint { get; set; }
}

public class DecimalRuleValue
{
    [JsonPropertyName("value")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }

    [JsonPropertyName("is_hard_constraint")]
    public bool IsHardConstraint { get; set; }
}

public class FlexibleDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                raw = raw.Trim().Replace(',', '.');
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }

        throw new JsonException("Invalid decimal value.");
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

public class StringOrStringListConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return string.IsNullOrWhiteSpace(value) ? new List<string>() : new List<string> { value };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;

                if (reader.TokenType == JsonTokenType.String)
                {
                    var item = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(item))
                        list.Add(item);
                }
            }
            return list;
        }

        throw new JsonException("Invalid string list value.");
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}

// Page Settings Rule
public class PageSettingsRule
{
    [JsonPropertyName("paper")]
    public PagePaperRule? Paper { get; set; }

    [JsonPropertyName("margin")]
    public MarginRule? Margin { get; set; }

    [JsonPropertyName("header_footer")]
    public HeaderFooterRule? HeaderFooter { get; set; }

    [JsonPropertyName("gutter")]
    public GutterRule? Gutter { get; set; }

    [JsonPropertyName("column")]
    public RuleValue<int>? Column { get; set; }

    [JsonPropertyName("akhir_halaman")]
    public PageEndRule? AkhirHalaman { get; set; }
}

public class PageEndRule
{
    [JsonPropertyName("max_baris_kosong")]
    public DecimalRuleValue? MaxBarisKosong { get; set; }

    [JsonPropertyName("cegah_halaman_kosong")]
    public RuleValue<bool>? CegahHalamanKosong { get; set; }
}

public class PagePaperRule
{
    [JsonPropertyName("size")]
    public RuleValue<string>? Size { get; set; }

    [JsonPropertyName("orientation")]
    public RuleValue<string>? Orientation { get; set; }
}

public class MarginRule
{
    [JsonPropertyName("top")]
    public DecimalRuleValue? Top { get; set; }

    [JsonPropertyName("bottom")]
    public DecimalRuleValue? Bottom { get; set; }

    [JsonPropertyName("left")]
    public DecimalRuleValue? Left { get; set; }

    [JsonPropertyName("right")]
    public DecimalRuleValue? Right { get; set; }
}

public class HeaderFooterRule
{
    [JsonPropertyName("header_from_top")]
    public DecimalRuleValue? HeaderFromTop { get; set; }

    [JsonPropertyName("footer_from_bottom")]
    public DecimalRuleValue? FooterFromBottom { get; set; }
}

public class GutterRule
{
    [JsonPropertyName("size")]
    public DecimalRuleValue? Size { get; set; }

    [JsonPropertyName("position")]
    public RuleValue<string>? Position { get; set; }
}

public class PaperSpec
{
    public string? Size { get; set; }

    public string? Orientation { get; set; }
}

// Page Numbering Rule
public class PageNumberingRule
{
    [JsonPropertyName("section")]
    public PageNumberingSectionRules? Section { get; set; }
}

public class PageNumberingSectionRules
{
    [JsonPropertyName("awal")]
    public PageNumberingSpec? Awal { get; set; }

    [JsonPropertyName("isi")]
    public PageNumberingSpec? Isi { get; set; }

    [JsonPropertyName("akhir")]
    public PageNumberingSpec? Akhir { get; set; }

    [JsonPropertyName("lampiran")]
    public PageNumberingSpec? Lampiran { get; set; }
}

public class PageNumberingSpec
{
    [JsonPropertyName("format")]
    public string? Format { get; set; } // "decimal", "lowerRoman", "upperRoman", "lowerLetter", "upperLetter"

    [JsonPropertyName("start")]
    public int? Start { get; set; } // Starting page number (null means continue)
}

// Chapter Title Rule (judul_bab)
public class ChapterTitleRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public TitleNumberingRule? Numbering { get; set; }

    [JsonPropertyName("struktur_konten")]
    public ChapterContentStructureRule? StrukturKonten { get; set; }
}

public class TitleFontRule
{
    [JsonPropertyName("font_name")]
    public RuleValue<string>? FontName { get; set; }

    [JsonPropertyName("font_size")]
    public DecimalRuleValue? FontSize { get; set; }

    [JsonPropertyName("font_style")]
    public TitleFontStyleRule? FontStyle { get; set; }
}

public class TitleFontStyleRule
{
    [JsonPropertyName("bold")]
    public RuleValue<bool>? Bold { get; set; }

    [JsonPropertyName("italic")]
    public RuleValue<bool>? Italic { get; set; }

    [JsonPropertyName("underline")]
    public RuleValue<bool>? Underline { get; set; }
}

public class TitleParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public TitleParagraphIndentationRule? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class TitleParagraphIndentationRule
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }

    [JsonPropertyName("is_hard_constraint")]
    public bool IsHardConstraint { get; set; }

    [JsonPropertyName("left_indent")]
    public DecimalRuleValue? LeftIndent { get; set; }

    [JsonPropertyName("right_indent")]
    public DecimalRuleValue? RightIndent { get; set; }

    [JsonPropertyName("first_line_indent")]
    public DecimalRuleValue? FirstLineIndent { get; set; }

    [JsonPropertyName("hanging")]
    public DecimalRuleValue? Hanging { get; set; }
}

public class TitleParagraphSpacingRule
{
    [JsonPropertyName("line_spacing")]
    public DecimalRuleValue? LineSpacing { get; set; }

    [JsonPropertyName("before")]
    public DecimalRuleValue? Before { get; set; }

    [JsonPropertyName("after")]
    public DecimalRuleValue? After { get; set; }
}

public class TitleNumberingRule
{
    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }

    [JsonPropertyName("case")]
    public RuleValue<string>? Case { get; set; }

    [JsonPropertyName("enter_after_number")]
    public RuleValue<bool>? EnterAfterNumber { get; set; }
}

public class ChapterContentStructureRule
{
    [JsonPropertyName("satu_baris_kosong_setelah")]
    public RuleValue<bool>? SatuBarisKosongSetelah { get; set; }

    [JsonPropertyName("min_satu_paragraf_sebelum_subbab")]
    public RuleValue<bool>? MinSatuParagrafSebelumSubbab { get; set; }

    [JsonPropertyName("jumlah_baris_kosong_setelah")]
    public DecimalRuleValue? JumlahBarisKosongSetelah { get; set; }

    [JsonPropertyName("minimal_paragraf_sebelum_subbab")]
    public DecimalRuleValue? MinimalParagrafSebelumSubbab { get; set; }
}

// Subchapter Title Rule (judul_subbab)
public class SubchapterTitleRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public SubchapterParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public TitleNumberingRule? Numbering { get; set; }

    [JsonPropertyName("struktur_konten")]
    public SubchapterContentStructureRule? StrukturKonten { get; set; }
}

public class SubchapterParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public ParagraphIndentationRule? Indentation { get; set; }

    [JsonPropertyName("hanging_min_cm")]
    public DecimalRuleValue? HangingMinCm { get; set; }

    [JsonPropertyName("hanging_max_cm")]
    public DecimalRuleValue? HangingMaxCm { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class SubchapterContentStructureRule
{
    [JsonPropertyName("minimal_satu_paragraf_setelah")]
    public RuleValue<bool>? MinimalSatuParagrafSetelah { get; set; }

    [JsonPropertyName("cegah_posisi_paling_bawah")]
    public RuleValue<bool>? CegahPosisiPalingBawah { get; set; }

    [JsonPropertyName("cegah_subbab_tunggal")]
    public RuleValue<bool>? CegahSubbabTunggal { get; set; }

    [JsonPropertyName("minimal_paragraf_setelah")]
    public DecimalRuleValue? MinimalParagrafSetelah { get; set; }

    [JsonPropertyName("minimal_subbab_level_sama")]
    public DecimalRuleValue? MinimalSubbabLevelSama { get; set; }

    [JsonPropertyName("jumlah_baris_kosong_sebelum")]
    public DecimalRuleValue? JumlahBarisKosongSebelum { get; set; }

    [JsonPropertyName("abaikan_jika_di_awal_halaman")]
    public RuleValue<bool>? AbaikanJikaDiAwalHalaman { get; set; }
}

// Paragraph Rule (paragraf)
public class ParagraphRule
{
    [JsonPropertyName("font")]
    public ParagraphFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public ParagraphFormatRule? Paragraph { get; set; }

    [JsonPropertyName("struktur_konten")]
    public ParagraphContentStructureRule? StrukturKonten { get; set; }
}

public class ParagraphFontRule
{
    [JsonPropertyName("font_name")]
    public RuleValue<string>? FontName { get; set; }

    [JsonPropertyName("font_size")]
    public DecimalRuleValue? FontSize { get; set; }
}

public class ParagraphFormatRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public ParagraphIndentationRule? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class ParagraphIndentationRule
{
    [JsonPropertyName("left_indent")]
    public DecimalRuleValue? LeftIndent { get; set; }

    [JsonPropertyName("right_indent")]
    public DecimalRuleValue? RightIndent { get; set; }

    [JsonPropertyName("first_line_indent")]
    public DecimalRuleValue? FirstLineIndent { get; set; }
}

public class ParagraphContentStructureRule
{
    [JsonPropertyName("minimal_kalimat")]
    public DecimalRuleValue? MinimalKalimat { get; set; }
}

// List Item Rule (item_daftar)
public class ListItemRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public ListItemParagraphRule? Paragraph { get; set; }
}

public class ListItemParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public ListItemIndentationRule? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class ListItemIndentationRule
{
    [JsonPropertyName("left_indent")]
    public DecimalRuleValue? LeftIndent { get; set; }

    [JsonPropertyName("right_indent")]
    public DecimalRuleValue? RightIndent { get; set; }

    [JsonPropertyName("hanging")]
    public DecimalRuleValue? Hanging { get; set; }
}

// Image Rule (gambar)
public class ImageRule
{
    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("position")]
    public ImagePositionRule? Position { get; set; }

    [JsonPropertyName("struktur_konten")]
    public MediaContentStructureRule? StrukturKonten { get; set; }
}

public class ImagePositionRule
{
    [JsonPropertyName("layout_option")]
    public RuleValue<string>? LayoutOption { get; set; }

    [JsonPropertyName("cegah_melebihi_margin")]
    public RuleValue<bool>? CegahMelebihiMargin { get; set; }

    [JsonPropertyName("cegah_memenuhi_halaman")]
    public RuleValue<bool>? CegahMemenuhiHalaman { get; set; }
}

// Caption Image Rule (caption_gambar)
public class CaptionImageRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public CaptionNumberingRule? Numbering { get; set; }

    [JsonPropertyName("position")]
    public RuleValue<string>? Position { get; set; }
}

public class CaptionNumberingRule
{
    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }

    [JsonPropertyName("case")]
    public RuleValue<string>? Case { get; set; }

    [JsonPropertyName("enter_after_numbering")]
    public RuleValue<bool>? EnterAfterNumbering { get; set; }
}

public class FlexibleStringListRuleValue
{
    [JsonPropertyName("value")]
    [JsonConverter(typeof(StringOrStringListConverter))]
    public List<string>? Value { get; set; }

    [JsonPropertyName("is_editable")]
    public bool IsEditable { get; set; }

    [JsonPropertyName("is_hard_constraint")]
    public bool IsHardConstraint { get; set; }
}

public class FlexibleCaptionNumberingRule
{
    [JsonPropertyName("number_format")]
    public FlexibleStringListRuleValue? NumberFormat { get; set; }

    [JsonPropertyName("case")]
    public RuleValue<string>? Case { get; set; }

    [JsonPropertyName("enter_after_numbering")]
    public RuleValue<bool>? EnterAfterNumbering { get; set; }
}

// Table Rule (tabel)
public class TableRule
{
    [JsonPropertyName("position")]
    public TablePositionRule? Position { get; set; }

    [JsonPropertyName("konten_tabel")]
    public TableContentRule? KontenTabel { get; set; }

    [JsonPropertyName("cegah_gambar_tabel")]
    public RuleValue<bool>? CegahGambarTabel { get; set; }

    [JsonPropertyName("struktur_konten")]
    public MediaContentStructureRule? StrukturKonten { get; set; }
}

public class TablePositionRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indent_from_left")]
    public DecimalRuleValue? IndentFromLeft { get; set; }

    [JsonPropertyName("cegah_melebihi_margin")]
    public RuleValue<bool>? CegahMelebihiMargin { get; set; }

    [JsonPropertyName("cegah_memenuhi_halaman")]
    public RuleValue<bool>? CegahMemenuhiHalaman { get; set; }
}

public class TableContentRule
{
    [JsonPropertyName("font")]
    public ParagraphFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TableContentParagraphRule? Paragraph { get; set; }
}

public class TableContentParagraphRule
{
    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class TableCaptionRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public FlexibleCaptionNumberingRule? Numbering { get; set; }

    [JsonPropertyName("position")]
    public RuleValue<string>? Position { get; set; }

    [JsonPropertyName("wajib_caption_lanjutan_jika_lintas_halaman")]
    public RuleValue<bool>? WajibCaptionLanjutanJikaLintasHalaman { get; set; }
}

// Code Rule (kode)
public class CodeRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public CodeParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public CodeNumberingRule? Numbering { get; set; }

    [JsonPropertyName("cegah_gambar_kode")]
    public RuleValue<bool>? CegahGambarKode { get; set; }

    [JsonPropertyName("cegah_tabel_kode")]
    public RuleValue<bool>? CegahTabelKode { get; set; }

    [JsonPropertyName("struktur_konten")]
    public MediaContentStructureRule? StrukturKonten { get; set; }
}

public class MediaContentStructureRule
{
    [JsonPropertyName("jumlah_baris_kosong_sebelum")]
    public DecimalRuleValue? JumlahBarisKosongSebelum { get; set; }

    [JsonPropertyName("jumlah_baris_kosong_setelah")]
    public DecimalRuleValue? JumlahBarisKosongSetelah { get; set; }

    [JsonPropertyName("abaikan_jika_di_awal_halaman")]
    public RuleValue<bool>? AbaikanJikaDiAwalHalaman { get; set; }
}

public class CodeParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public ListItemIndentationRule? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class CodeNumberingRule
{
    [JsonPropertyName("use_numbering")]
    public RuleValue<bool>? UseNumbering { get; set; }

    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }
}

public class CodeTitleRule
{
    [JsonPropertyName("font")]
    public TitleFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public TitleParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("numbering")]
    public FlexibleCaptionNumberingRule? Numbering { get; set; }

    [JsonPropertyName("position")]
    public RuleValue<string>? Position { get; set; }

    [JsonPropertyName("wajib_caption_lanjutan_jika_lintas_halaman")]
    public RuleValue<bool>? WajibCaptionLanjutanJikaLintasHalaman { get; set; }
}

// Formula Rule (rumus)
public class FormulaRule
{
    [JsonPropertyName("font")]
    public ParagraphFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public FormulaParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("tabs")]
    public FormulaTabsRule? Tabs { get; set; }

    [JsonPropertyName("numbering")]
    public FormulaNumberingRule? Numbering { get; set; }

    [JsonPropertyName("position")]
    public FormulaPositionRule? Position { get; set; }

    [JsonPropertyName("struktur_halaman")]
    public FormulaPageStructureRule? StrukturHalaman { get; set; }
}

public class FormulaParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public FormulaIndentationRule? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class FormulaIndentationRule
{
    [JsonPropertyName("first_line_indent")]
    public DecimalRuleValue? FirstLineIndent { get; set; }

    [JsonPropertyName("first_line_indent_cm")]
    public DecimalRuleValue? FirstLineIndentCm { get; set; }

    [JsonPropertyName("left_indent")]
    public DecimalRuleValue? LeftIndent { get; set; }

    [JsonPropertyName("left_indent_cm")]
    public DecimalRuleValue? LeftIndentCm { get; set; }

    [JsonPropertyName("left_cm")]
    public DecimalRuleValue? LeftCm { get; set; }

    [JsonPropertyName("right_indent")]
    public DecimalRuleValue? RightIndent { get; set; }
}

public class FormulaTabsRule
{
    [JsonPropertyName("left_tab")]
    public FormulaTabRule? LeftTab { get; set; }

    [JsonPropertyName("right_tab")]
    public FormulaTabRule? RightTab { get; set; }
}

public class FormulaTabRule
{
    [JsonPropertyName("distance_from_equation_cm")]
    public DecimalRuleValue? DistanceFromEquationCm { get; set; }

    [JsonPropertyName("position_cm")]
    public DecimalRuleValue? PositionCm { get; set; }

    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("leader_style")]
    public RuleValue<string>? LeaderStyle { get; set; }

    [JsonPropertyName("depends_on_equation_length")]
    public RuleValue<bool>? DependsOnEquationLength { get; set; }
}

public class FormulaNumberingRule
{
    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }
}

public class FormulaPositionRule
{
    [JsonPropertyName("cegah_memenuhi_halaman")]
    public RuleValue<bool>? CegahMemenuhiHalaman { get; set; }

    [JsonPropertyName("equation_alignment")]
    public RuleValue<string>? EquationAlignment { get; set; }

    [JsonPropertyName("overall_indent_cm")]
    public DecimalRuleValue? OverallIndentCm { get; set; }

    [JsonPropertyName("paragraph_alignment")]
    public RuleValue<string>? ParagraphAlignment { get; set; }
}

public class FormulaPageStructureRule
{
    [JsonPropertyName("cegah_memenuhi_halaman")]
    public RuleValue<bool>? CegahMemenuhiHalaman { get; set; }

    [JsonPropertyName("minimal_satu_paragraf_di_halaman")]
    public RuleValue<bool>? MinimalSatuParagrafDiHalaman { get; set; }
}

public class FootnoteRule
{
    [JsonPropertyName("numbering")]
    public FootnoteNumberingRule? Numbering { get; set; }

    [JsonPropertyName("separator")]
    public FootnoteSeparatorRule? Separator { get; set; }

    [JsonPropertyName("footnote_text")]
    public FootnoteTextRule? FootnoteText { get; set; }
}

public class FootnoteNumberingRule
{
    [JsonPropertyName("number_format")]
    public RuleValue<string>? NumberFormat { get; set; }

    [JsonPropertyName("type")]
    public RuleValue<string>? Type { get; set; }
}

public class FootnoteSeparatorRule
{
    [JsonPropertyName("paragraph")]
    public FootnoteSeparatorParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("cegah_tab_awal")]
    public RuleValue<bool>? CegahTabAwal { get; set; }
}

public class FootnoteSeparatorParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("indentation")]
    public FootnoteSeparatorIndentationRule? Indentation { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class FootnoteSeparatorIndentationRule
{
    [JsonPropertyName("left_indent")]
    public DecimalRuleValue? LeftIndent { get; set; }

    [JsonPropertyName("first_line_indent")]
    public DecimalRuleValue? FirstLineIndent { get; set; }
}

public class FootnoteTextRule
{
    [JsonPropertyName("font")]
    public ParagraphFontRule? Font { get; set; }

    [JsonPropertyName("paragraph")]
    public FootnoteTextParagraphRule? Paragraph { get; set; }

    [JsonPropertyName("struktur_konten")]
    public FootnoteContentStructureRule? StrukturKonten { get; set; }
}

public class FootnoteTextParagraphRule
{
    [JsonPropertyName("alignment")]
    public RuleValue<string>? Alignment { get; set; }

    [JsonPropertyName("spacing")]
    public TitleParagraphSpacingRule? Spacing { get; set; }
}

public class FootnoteContentStructureRule
{
    [JsonPropertyName("satu_enter_sebelum")]
    public RuleValue<bool>? SatuEnterSebelum { get; set; }
}

#endregion

#region Validation Result DTOs

public class ValidationResult
{
    public sealed class ValidationErrorCollection : Collection<ValidationError>
    {
        private readonly ValidationResult _owner;
        private bool _suppressPrepareErrorForInsert;

        public ValidationErrorCollection(ValidationResult owner)
        {
            _owner = owner;
        }

        internal void ReplaceTail(int startIndex, IReadOnlyList<ValidationError> replacement)
        {
            _suppressPrepareErrorForInsert = true;
            try
            {
                while (Count > startIndex)
                    RemoveAt(Count - 1);

                foreach (var error in replacement)
                    Add(error);
            }
            finally
            {
                _suppressPrepareErrorForInsert = false;
            }
        }

        protected override void InsertItem(int index, ValidationError item)
        {
            if (!_suppressPrepareErrorForInsert)
                _owner.PrepareErrorForInsert(item);
            base.InsertItem(index, item);
        }

        protected override void SetItem(int index, ValidationError item)
        {
            if (!_suppressPrepareErrorForInsert)
                _owner.PrepareErrorForInsert(item);
            base.SetItem(index, item);
        }
    }

    public ValidationResult()
    {
        Errors = new ValidationErrorCollection(this);
    }

    public bool IsValid => Errors.Count == 0;
    public ValidationErrorCollection Errors { get; }
    public int TotalChecks { get; set; }
    public int PassedChecks { get; set; }

    // Unique check tracking: each check statement line is treated as one check key.
    private readonly HashSet<string> _uniqueCheckKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedUniqueCheckKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedHardConstraintCheckKeys = new(StringComparer.Ordinal);
    private string? _pendingCheckKey;
    private bool _pendingCheckPassed;
    private bool _pendingCheckIsHardConstraint;

    public bool HasHardConstraintViolation
    {
        get
        {
            FinalizePendingCheck();
            return _failedHardConstraintCheckKeys.Count > 0 || Errors.Any(error => error.IsHardConstraint);
        }
    }

    public int UniqueTotalChecks
    {
        get
        {
            FinalizePendingCheck();
            return _uniqueCheckKeys.Count;
        }
    }

    public int UniqueFailedChecks
    {
        get
        {
            FinalizePendingCheck();
            return _failedUniqueCheckKeys.Count;
        }
    }

    public int UniquePassedChecks
    {
        get
        {
            FinalizePendingCheck();
            return Math.Max(0, _uniqueCheckKeys.Count - _failedUniqueCheckKeys.Count);
        }
    }

    public decimal Score => UniqueTotalChecks > 0 ? (decimal)UniquePassedChecks / UniqueTotalChecks * 100 : 100;

    public decimal GetEffectiveScore(IReadOnlyCollection<ValidationError> effectiveErrors)
    {
        FinalizePendingCheck();

        var failedCheckKeys = new HashSet<string>(StringComparer.Ordinal);
        var keylessErrorCount = 0;

        foreach (var error in effectiveErrors)
        {
            var hasKnownCheckKey = false;
            foreach (var checkKey in error.ValidationCheckKeys)
            {
                if (string.IsNullOrWhiteSpace(checkKey) || !_uniqueCheckKeys.Contains(checkKey))
                    continue;

                failedCheckKeys.Add(checkKey);
                hasKnownCheckKey = true;
            }

            if (!hasKnownCheckKey)
                keylessErrorCount++;
        }

        var effectiveFailedChecks = failedCheckKeys.Count + keylessErrorCount;
        var effectiveTotalChecks = Math.Max(_uniqueCheckKeys.Count, effectiveFailedChecks);

        if (effectiveTotalChecks == 0)
            return 100;

        var effectivePassedChecks = Math.Max(0, effectiveTotalChecks - effectiveFailedChecks);
        return (decimal)effectivePassedChecks / effectiveTotalChecks * 100;
    }

    public bool HasEffectiveHardConstraintViolation(IReadOnlyCollection<ValidationError> effectiveErrors)
        => effectiveErrors.Any(error => error.IsHardConstraint);

    public void IncrementTotalChecks(
        bool isHardConstraint = false,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        FinalizePendingCheck();
        TotalChecks++;

        var checkKey = BuildCheckKey(filePath, lineNumber, memberName);
        _uniqueCheckKeys.Add(checkKey);
        _pendingCheckKey = checkKey;
        _pendingCheckPassed = false;
        _pendingCheckIsHardConstraint = isHardConstraint;
    }

    public void IncrementPassedChecks(
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        PassedChecks++;

        // Normal flow: pass belongs to the latest started check.
        if (!string.IsNullOrWhiteSpace(_pendingCheckKey))
        {
            _pendingCheckPassed = true;
            return;
        }

        // Defensive fallback: if called without pending check, still register key.
        var checkKey = BuildCheckKey(filePath, lineNumber, memberName);
        _uniqueCheckKeys.Add(checkKey);
    }

    public void MergeFrom(ValidationResult other)
    {
        if (other == null)
            return;

        FinalizePendingCheck();
        other.FinalizePendingCheck();

        foreach (var error in other.Errors)
            Errors.Add(error);
        TotalChecks += other.TotalChecks;
        PassedChecks += other.PassedChecks;
        _uniqueCheckKeys.UnionWith(other._uniqueCheckKeys);
        _failedUniqueCheckKeys.UnionWith(other._failedUniqueCheckKeys);
        _failedHardConstraintCheckKeys.UnionWith(other._failedHardConstraintCheckKeys);
    }

    private void FinalizePendingCheck()
    {
        if (string.IsNullOrWhiteSpace(_pendingCheckKey))
            return;

        if (!_pendingCheckPassed)
        {
            _failedUniqueCheckKeys.Add(_pendingCheckKey);
            if (_pendingCheckIsHardConstraint)
                _failedHardConstraintCheckKeys.Add(_pendingCheckKey);
        }

        _pendingCheckKey = null;
        _pendingCheckPassed = false;
        _pendingCheckIsHardConstraint = false;
    }

    private void PrepareErrorForInsert(ValidationError? error)
    {
        if (error == null)
            return;

        if (!_pendingCheckPassed)
            error.AddValidationCheckKey(_pendingCheckKey);

        if (!error.IsHardConstraint && _pendingCheckIsHardConstraint && !_pendingCheckPassed)
            error.IsHardConstraint = true;

        if (error.IsHardConstraint && !string.IsNullOrWhiteSpace(_pendingCheckKey))
            _failedHardConstraintCheckKeys.Add(_pendingCheckKey);
    }

    private static string BuildCheckKey(string filePath, int lineNumber, string memberName)
    {
        var fileName = string.IsNullOrWhiteSpace(filePath) ? "unknown" : Path.GetFileName(filePath);
        return $"{fileName}:{lineNumber}:{memberName}";
    }
}

public class ErrorLocation
{
    public int HalamanKe { get; set; }
    public ErrorBbox? Bbox { get; set; }
}

public class ErrorBbox
{
    public decimal X0 { get; set; }
    public decimal Y0 { get; set; }
    public decimal X1 { get; set; }
    public decimal Y1 { get; set; }
}

public class ValidationError
{
    private readonly List<string> _validationCheckKeys = new();

    public string Category { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public int? SectionIndex { get; set; }
    public List<ErrorLocation> Locations { get; set; } = new();
    public string? DiffType { get; set; }
    public string? Cause { get; set; }
    public bool? HasNumbering { get; set; }
    public string? StyleName { get; set; }
    public string? StyleId { get; set; }
    public string? Evidence { get; set; }
    public string? ToolRequirement { get; set; }
    public string? FeatureName { get; set; }
    public List<string>? AllowedActions { get; set; }
    public List<string>? DisallowedActions { get; set; }
    public string? ScopeHint { get; set; }
    public string? PageRange { get; set; }
    public string? PrevElementText { get; set; }
    public string? PrevElementLabel { get; set; }
    public string? NextElementText { get; set; }
    public string? NextElementLabel { get; set; }
    public decimal? PageMarginTopCm { get; set; }
    public decimal? PageMarginBottomCm { get; set; }
    public decimal? PageMarginLeftCm { get; set; }
    public decimal? PageMarginRightCm { get; set; }
    public ulong? DokumenElemenId { get; set; }
    public bool IsHardConstraint { get; set; }

    internal IReadOnlyList<string> ValidationCheckKeys => _validationCheckKeys;

    internal void AddValidationCheckKey(string? checkKey)
    {
        if (string.IsNullOrWhiteSpace(checkKey))
            return;

        if (!_validationCheckKeys.Contains(checkKey, StringComparer.Ordinal))
            _validationCheckKeys.Add(checkKey);
    }

    internal void AddValidationCheckKeys(IEnumerable<string> checkKeys)
    {
        foreach (var checkKey in checkKeys)
            AddValidationCheckKey(checkKey);
    }
}

#endregion

public interface IValidationService
{
    Task<ValidationResult> ValidateDokumenAsync(int dokumenId, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateBabAsync(uint babId, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePageSettingsAsync(int dokumenId, CancellationToken cancellationToken = default);
}

public partial class ValidationService : IValidationService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<ValidationService> _logger;

    // Conversion constants
    private const decimal TwipsPerCm = 566.929m; // 1 cm = 566.929 twips
    private const decimal TwipsTolerance = 28.35m; // ~0.5mm tolerance
    private const decimal EmusPerCm = 360000m; // 1 cm = 360,000 EMUs

    // Standard paper sizes in twips (width x height for portrait)
    private static readonly Dictionary<string, (uint Width, uint Height)> PaperSizes = new()
    {
        { "A4", (11906, 16838) },  // 210mm x 297mm
        { "F4", (11906, 18709) }   // 210mm x 330mm
    };

    public ValidationService(KorektorBukuDbContext db, ILogger<ValidationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private sealed class ValidationTargetContext
    {
        public string SectionRefType { get; init; } = "dokumen";
        public uint SectionRefId { get; init; }
        public uint? BabId { get; init; }
    }

    private sealed class ValidationTargetResolution
    {
        public bool Exists { get; init; }
    }

    private ValidationTargetContext? _activeValidationTarget;

    private async Task<ValidationTargetResolution> ResolveValidationTargetAsync(
        int dokumenId,
        CancellationToken cancellationToken)
    {
        if (_activeValidationTarget != null)
        {
            return new ValidationTargetResolution
            {
                Exists = true
            };
        }

        var dokumenExists = await _db.Dokumens
            .AsNoTracking()
            .AnyAsync(d => d.DokumenId == dokumenId, cancellationToken);

        return new ValidationTargetResolution
        {
            Exists = dokumenExists
        };
    }

    private (string RefType, uint RefId) ResolveSectionRefForValidation(int dokumenId)
    {
        if (_activeValidationTarget != null)
            return (_activeValidationTarget.SectionRefType, _activeValidationTarget.SectionRefId);

        return ("dokumen", (uint)dokumenId);
    }

    private bool IsBabScopedValidation()
        => string.Equals(_activeValidationTarget?.SectionRefType, "bab", StringComparison.OrdinalIgnoreCase);

    public async Task<ValidationResult> ValidateBabAsync(uint babId, CancellationToken cancellationToken = default)
    {
        var babExists = await _db.Babs
            .AsNoTracking()
            .AnyAsync(b => b.BabId == babId, cancellationToken);

        if (!babExists)
        {
            var missingResult = new ValidationResult();
            missingResult.Errors.Add(new ValidationError
            {
                Category = "Buku",
                Field = "bab_id",
                Message = "Bab tidak ditemukan"
            });
            return missingResult;
        }

        var previousContext = _activeValidationTarget;
        _activeValidationTarget = new ValidationTargetContext
        {
            SectionRefType = "bab",
            SectionRefId = babId,
            BabId = babId
        };

        try
        {
            return await ValidateCoreAsync((int)babId, cancellationToken);
        }
        finally
        {
            _activeValidationTarget = previousContext;
        }
    }

    public async Task<ValidationResult> ValidateDokumenAsync(int dokumenId, CancellationToken cancellationToken = default)
    {
        var dokumenExists = await _db.Dokumens
            .AsNoTracking()
            .AnyAsync(d => d.DokumenId == dokumenId, cancellationToken);

        if (!dokumenExists)
        {
            var missingResult = new ValidationResult();
            missingResult.Errors.Add(new ValidationError
            {
                Category = "Dokumen",
                Field = "dokumen_id",
                Message = "Dokumen tidak ditemukan"
            });
            return missingResult;
        }

        var previousContext = _activeValidationTarget;
        _activeValidationTarget = new ValidationTargetContext
        {
            SectionRefType = "dokumen",
            SectionRefId = (uint)dokumenId
        };

        try
        {
            return await ValidateCoreAsync(dokumenId, cancellationToken);
        }
        finally
        {
            _activeValidationTarget = previousContext;
        }
    }

    private async Task<ValidationResult> ValidateCoreAsync(int dokumenId, CancellationToken cancellationToken)
    {
        var result = new ValidationResult();

        // Validate page settings
        var pageResult = await ValidatePageSettingsAsync(dokumenId, cancellationToken);
        result.MergeFrom(pageResult);

        var classification = await ClassifyElementsAsync(dokumenId, cancellationToken);

        // Validate chapter title
        var titleResult = await ValidateChapterTitleAsync(dokumenId, classification.ChapterTitleIds, cancellationToken);
        result.MergeFrom(titleResult);

        // Validate subchapter title
        var subchapterResult = await ValidateSubchapterTitleAsync(dokumenId, classification.SubchapterIds, cancellationToken);
        result.MergeFrom(subchapterResult);

        // Validate paragraphs
        var paragraphResult = await ValidateParagraphAsync(
            dokumenId,
            classification.ParagraphIds,
            classification.ListItemIds,
            cancellationToken);
        result.MergeFrom(paragraphResult);

        // Validate list items
        var listItemResult = await ValidateListItemAsync(dokumenId, classification.ListItemIds, cancellationToken);
        result.MergeFrom(listItemResult);

        var footnoteResult = await ValidateFootnoteAsync(dokumenId, cancellationToken);
        result.MergeFrom(footnoteResult);

        // Validate images
        var imageResult = await ValidateImageAsync(dokumenId, cancellationToken);
        result.MergeFrom(imageResult);

        // Validate tables
        var tableResult = await ValidateTableAsync(dokumenId, cancellationToken);
        result.MergeFrom(tableResult);

        // Validate formulas
        var formulaResult = await ValidateFormulaAsync(dokumenId, cancellationToken);
        result.MergeFrom(formulaResult);

        // Validate code blocks
        var codeResult = await ValidateCodeAsync(dokumenId, cancellationToken);
        result.MergeFrom(codeResult);

        NormalizeContentErrorCategories(result.Errors);
        return result;
    }

    private static void NormalizeContentErrorCategories(IList<ValidationError> errors)
    {
        if (errors.Count == 0)
            return;

        foreach (var error in errors)
        {
            if (!string.Equals(error.Category, "Isi Buku", StringComparison.OrdinalIgnoreCase))
                continue;

            error.Category = BuildCategoryFromField(error.Field);
        }
    }

    private static string BuildCategoryFromField(string field)
    {
        var cleaned = field.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Konten";

        var tokens = cleaned
            .Replace('-', '_')
            .Replace(' ', '_')
            .Split('_', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return "Konten";

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrWhiteSpace(token))
                continue;

            tokens[i] = char.ToUpperInvariant(token[0]) + (token.Length > 1 ? token[1..] : string.Empty);
        }

        return string.Join(' ', tokens);
    }

    private sealed class ElementClassification
    {
        public HashSet<ulong> ChapterTitleIds { get; } = new();
        public HashSet<ulong> SubchapterIds { get; } = new();
        public HashSet<ulong> ListItemIds { get; } = new();
        public HashSet<ulong> ParagraphIds { get; } = new();
    }

    private async Task<ElementClassification> ClassifyElementsAsync(
        int dokumenId,
        CancellationToken cancellationToken)
    {
        var classification = new ElementClassification();
        var (sectionRefType, sectionRefId) = ResolveSectionRefForValidation(dokumenId);

        var bodyElements = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where s.DsecRefTipe == sectionRefType && s.DsecRefId == sectionRefId && p.DpartType == "body"
            orderby s.DsecIndex, e.DelemenSequence
            select new BodyElementInfo { DelemenId = e.DelemenId, DelemenType = e.DelemenType, DelemenJsonTree = e.DelemenJsonTree })
            .ToListAsync(cancellationToken);

        if (bodyElements.Count == 0)
            return classification;

        var labelMap = await LoadVisualLabelsAsync(
            bodyElements.Select(e => e.DelemenId),
            cancellationToken);

        var listCandidates = new List<(BodyElementInfo Element, uint? ParagraphFormatId)>();

        foreach (var elem in bodyElements)
        {
            if (!labelMap.TryGetValue(elem.DelemenId, out var rawLabel))
                continue;

            var normalized = NormalizeLabel(rawLabel);
            if (normalized == "judul_bab")
            {
                classification.ChapterTitleIds.Add(elem.DelemenId);
                continue;
            }

            if (normalized == "judul_subbab")
            {
                classification.SubchapterIds.Add(elem.DelemenId);
                continue;
            }

            if (IsListLabel(normalized))
            {
                var content = ParseElementContent(elem.DelemenJsonTree);
                listCandidates.Add((elem, content.ParagraphFormatId));
                continue;
            }

            if (normalized == "paragraf")
            {
                classification.ParagraphIds.Add(elem.DelemenId);
            }
        }

        if (listCandidates.Count > 0)
        {
            foreach (var (element, _) in listCandidates)
            {
                // Structural label list (list_item/list_level_*) is the strongest signal.
                // Some DOCX files store manual list text (e.g. "a. ...") as plain paragraph
                // without numbering metadata, and downgrading them to paragraf causes
                // paragraph-only checks (like min sentence) to trigger incorrectly.
                classification.ListItemIds.Add(element.DelemenId);
            }
        }

        return classification;
    }

    private async Task PersistStructureLabelsAsync(
        IReadOnlyList<BodyElementInfo> bodyElements,
        ElementClassification classification,
        Dictionary<ulong, ElementContentInfo> contentById,
        Dictionary<uint, DokumenFormatParagraf> paragraphFormats,
        CancellationToken cancellationToken)
    {
        if (bodyElements.Count == 0)
            return;

        var labelById = BuildStructureLabelMap(bodyElements, classification, contentById, paragraphFormats);
        if (labelById.Count == 0)
            return;

        var (idColumn, labelColumn) = await ResolveVisualColumnsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(idColumn))
            return;

        if (!string.IsNullOrWhiteSpace(labelColumn) &&
            labelColumn.Equals("dev_label_struktural", StringComparison.OrdinalIgnoreCase))
            return;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS " +
                                       "WHERE TABLE_SCHEMA = DATABASE() " +
                                       "AND TABLE_NAME = 'dokumen_elemen_visual' " +
                                       "AND COLUMN_NAME = 'dev_label_struktural' " +
                                       "LIMIT 1";

                var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);
                if (exists == null || exists == DBNull.Value)
                    return;
            }

            const int batchSize = 200;
            foreach (var chunk in labelById.Chunk(batchSize))
            {
                using var cmd = connection.CreateCommand();
                var sb = new StringBuilder();
                sb.Append("UPDATE `dokumen_elemen_visual` ");
                sb.Append("SET `dev_label_struktural` = CASE `")
                  .Append(idColumn)
                  .Append("` ");

                var ids = new List<string>();
                var index = 0;
                foreach (var (id, label) in chunk)
                {
                    var paramName = "@label" + index;
                    sb.Append("WHEN ").Append(id).Append(" THEN ").Append(paramName).Append(' ');

                    var param = cmd.CreateParameter();
                    param.ParameterName = paramName;
                    param.Value = label ?? (object)DBNull.Value;
                    cmd.Parameters.Add(param);

                    ids.Add(id.ToString(CultureInfo.InvariantCulture));
                    index++;
                }

                sb.Append("ELSE `dev_label_struktural` END ");
                sb.Append("WHERE `").Append(idColumn).Append("` IN (")
                  .Append(string.Join(",", ids))
                  .Append(')');

                cmd.CommandText = sb.ToString();
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update dev_label_struktural");
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    private static Dictionary<ulong, string?> BuildStructureLabelMap(
        IReadOnlyList<BodyElementInfo> bodyElements,
        ElementClassification classification,
        Dictionary<ulong, ElementContentInfo> contentById,
        Dictionary<uint, DokumenFormatParagraf> paragraphFormats)
    {
        var labels = new Dictionary<ulong, string?>(bodyElements.Count);
        foreach (var elem in bodyElements)
        {
            string? label = null;

            if (classification.ChapterTitleIds.Contains(elem.DelemenId))
            {
                label = "judul_bab";
            }
            else if (classification.SubchapterIds.Contains(elem.DelemenId))
            {
                label = "judul_subbab";
            }
            else if (classification.ListItemIds.Contains(elem.DelemenId))
            {
                label = BuildListStructureLabel(elem, contentById, paragraphFormats);
            }
            else if (classification.ParagraphIds.Contains(elem.DelemenId))
            {
                label = "paragraf";
            }

            labels[elem.DelemenId] = label;
        }

        return labels;
    }

    private static string BuildListStructureLabel(
        BodyElementInfo element,
        Dictionary<ulong, ElementContentInfo> contentById,
        Dictionary<uint, DokumenFormatParagraf> paragraphFormats)
    {
        DokumenFormatParagraf? format = null;
        if (contentById.TryGetValue(element.DelemenId, out var content) &&
            content.ParagraphFormatId.HasValue)
        {
            paragraphFormats.TryGetValue(content.ParagraphFormatId.Value, out format);
        }

        var level = TryParseListItemLevel(element.DelemenType, format);
        if (level.HasValue)
            return $"list_level_{level.Value + 1}";

        return "list_item";
    }

    /// <summary>
    /// Creates a list of ErrorLocation from a page number and merged bbox string.
    /// This is the legacy overload - prefer using the Dictionary overload for multi-page support.
    /// </summary>
    protected static List<ErrorLocation> CreateLocations(int? pageNumber, string? mergedBbox)
    {
        var locations = new List<ErrorLocation>();
        
        if (!pageNumber.HasValue)
            return locations;

        var loc = new ErrorLocation { HalamanKe = pageNumber.Value };

        if (!string.IsNullOrWhiteSpace(mergedBbox))
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(mergedBbox);
                var root = doc.RootElement;
                
                loc.Bbox = new ErrorBbox
                {
                    X0 = root.TryGetProperty("x0", out var x0) ? x0.GetDecimal() : 0,
                    Y0 = root.TryGetProperty("y0", out var y0) ? y0.GetDecimal() : 0,
                    X1 = root.TryGetProperty("x1", out var x1) ? x1.GetDecimal() : 0,
                    Y1 = root.TryGetProperty("y1", out var y1) ? y1.GetDecimal() : 0
                };
            }
            catch
            {
                // Ignore parsing errors, add location without bbox
            }
        }

        locations.Add(loc);
        return locations;
    }

    /// <summary>
    /// Creates a list of ErrorLocation from a dictionary of page numbers to bounding boxes.
    /// Supports elements that span multiple pages.
    /// </summary>
    /// <param name="pageBboxMap">Dictionary where key is page number and value is the merged bbox for that page</param>
    protected static List<ErrorLocation> CreateLocations(Dictionary<int, ErrorBbox> pageBboxMap)
    {
        var locations = new List<ErrorLocation>();
        
        if (pageBboxMap == null || pageBboxMap.Count == 0)
            return locations;

        foreach (var (pageNumber, bbox) in pageBboxMap.OrderBy(kv => kv.Key))
        {
            locations.Add(new ErrorLocation
            {
                HalamanKe = pageNumber,
                Bbox = bbox
            });
        }

        return locations;
    }

    /// <summary>
    /// Creates a list of ErrorLocation from page numbers and optional per-page bounding boxes.
    /// </summary>
    protected static List<ErrorLocation> CreateLocations(
        IEnumerable<int> pageNumbers,
        Dictionary<int, ErrorBbox>? pageBboxMap)
    {
        var pageSet = new HashSet<int>();
        if (pageNumbers != null)
        {
            foreach (var page in pageNumbers)
                pageSet.Add(page);
        }

        if (pageBboxMap != null)
        {
            foreach (var page in pageBboxMap.Keys)
                pageSet.Add(page);
        }

        var pages = pageSet
            .OrderBy(p => p)
            .ToList();

        var locations = new List<ErrorLocation>(pages.Count);
        if (pages.Count == 0)
            return locations;

        foreach (var page in pages)
        {
            ErrorBbox? bbox = null;
            if (pageBboxMap != null)
                pageBboxMap.TryGetValue(page, out bbox);

            locations.Add(new ErrorLocation
            {
                HalamanKe = page,
                Bbox = bbox
            });
        }

        return locations;
    }

    private sealed class ElementNeighborContext
    {
        public string? PrevText { get; init; }
        public string? PrevLabel { get; init; }
        public string? NextText { get; init; }
        public string? NextLabel { get; init; }
        public decimal? MarginTopCm { get; init; }
        public decimal? MarginBottomCm { get; init; }
        public decimal? MarginLeftCm { get; init; }
        public decimal? MarginRightCm { get; init; }
    }

    private sealed class PageMarginSnapshot
    {
        public decimal? TopCm { get; init; }
        public decimal? BottomCm { get; init; }
        public decimal? LeftCm { get; init; }
        public decimal? RightCm { get; init; }
    }

    private static void ApplyContextToErrors(IList<ValidationError> errors, int startIndex, ElementNeighborContext? context)
    {
        if (context == null)
            return;

        for (var i = startIndex; i < errors.Count; i++)
            ApplyContext(errors[i], context);
    }

    private static void ApplyElementIdToErrors(IList<ValidationError> errors, int startIndex, ulong elementId)
    {
        for (var i = startIndex; i < errors.Count; i++)
        {
            if (!errors[i].DokumenElemenId.HasValue)
                errors[i].DokumenElemenId = elementId;
        }
    }

    private static void ReplaceErrorTail(IList<ValidationError> errors, int startIndex, IReadOnlyList<ValidationError> replacement)
    {
        if (errors is ValidationResult.ValidationErrorCollection collection)
        {
            collection.ReplaceTail(startIndex, replacement);
            return;
        }

        while (errors.Count > startIndex)
            errors.RemoveAt(errors.Count - 1);

        foreach (var error in replacement)
            errors.Add(error);
    }

    private static void ApplyContext(ValidationError error, ElementNeighborContext context)
    {
        if (string.IsNullOrWhiteSpace(error.PrevElementText))
            error.PrevElementText = context.PrevText;
        if (string.IsNullOrWhiteSpace(error.PrevElementLabel))
            error.PrevElementLabel = context.PrevLabel;
        if (string.IsNullOrWhiteSpace(error.NextElementText))
            error.NextElementText = context.NextText;
        if (string.IsNullOrWhiteSpace(error.NextElementLabel))
            error.NextElementLabel = context.NextLabel;

        if (!error.PageMarginTopCm.HasValue)
            error.PageMarginTopCm = context.MarginTopCm;
        if (!error.PageMarginBottomCm.HasValue)
            error.PageMarginBottomCm = context.MarginBottomCm;
        if (!error.PageMarginLeftCm.HasValue)
            error.PageMarginLeftCm = context.MarginLeftCm;
        if (!error.PageMarginRightCm.HasValue)
            error.PageMarginRightCm = context.MarginRightCm;
    }

    private static Dictionary<ulong, ElementNeighborContext> BuildNeighborContexts(
        IReadOnlyList<ulong> orderedElementIds,
        Dictionary<ulong, string?> elementJsonById,
        Dictionary<ulong, string> labelMap,
        Dictionary<ulong, PageMarginSnapshot> marginsById)
    {
        var contexts = new Dictionary<ulong, ElementNeighborContext>(orderedElementIds.Count);
        var contentCache = new Dictionary<ulong, ElementContentInfo>();

        string? GetText(ulong id)
        {
            if (!elementJsonById.TryGetValue(id, out var json))
                return null;

            if (!contentCache.TryGetValue(id, out var content))
            {
                content = ParseElementContent(json);
                contentCache[id] = content;
            }

            return NormalizeContextText(content.PlainText);
        }

        for (var i = 0; i < orderedElementIds.Count; i++)
        {
            var id = orderedElementIds[i];
            var prevId = i > 0 ? orderedElementIds[i - 1] : (ulong?)null;
            var nextId = i + 1 < orderedElementIds.Count ? orderedElementIds[i + 1] : (ulong?)null;

            string? prevLabel = null;
            string? nextLabel = null;
            if (prevId.HasValue)
                labelMap.TryGetValue(prevId.Value, out prevLabel);
            if (nextId.HasValue)
                labelMap.TryGetValue(nextId.Value, out nextLabel);

            var margin = marginsById.TryGetValue(id, out var found) ? found : null;

            contexts[id] = new ElementNeighborContext
            {
                PrevText = prevId.HasValue ? GetText(prevId.Value) : null,
                PrevLabel = prevLabel,
                NextText = nextId.HasValue ? GetText(nextId.Value) : null,
                NextLabel = nextLabel,
                MarginTopCm = margin?.TopCm,
                MarginBottomCm = margin?.BottomCm,
                MarginLeftCm = margin?.LeftCm,
                MarginRightCm = margin?.RightCm
            };
        }

        return contexts;
    }

    private static string? NormalizeContextText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        const int maxLength = 160;
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength] + "...";
    }

    private static string NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        return label.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    }

    private async Task<Dictionary<ulong, PageMarginSnapshot>> LoadPageMarginsAsync(
        IEnumerable<ulong> elementIds,
        CancellationToken cancellationToken)
    {
        var ids = elementIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<ulong, PageMarginSnapshot>();

        var margins = await (from e in _db.DokumenElemens
            join p in _db.DokumenParts on e.DpartId equals p.DpartId
            join s in _db.DokumenSections on p.DsecId equals s.DsecId
            where ids.Contains(e.DelemenId)
            select new
            {
                e.DelemenId,
                s.DsecMarginTopTwips,
                s.DsecMarginBottomTwips,
                s.DsecMarginLeftTwips,
                s.DsecMarginRightTwips
            }).ToListAsync(cancellationToken);

        return margins
            .GroupBy(m => m.DelemenId)
            .ToDictionary(
                g => g.Key,
                g => new PageMarginSnapshot
                {
                    TopCm = TwipsToCm(g.First().DsecMarginTopTwips),
                    BottomCm = TwipsToCm(g.First().DsecMarginBottomTwips),
                    LeftCm = TwipsToCm(g.First().DsecMarginLeftTwips),
                    RightCm = TwipsToCm(g.First().DsecMarginRightTwips)
                });
    }

    private static decimal? TwipsToCm(uint? twips)
    {
        if (!twips.HasValue)
            return null;

        return Math.Round(twips.Value / TwipsPerCm, 2);
    }

    private static decimal? TwipsToCm(long? twips)
    {
        if (!twips.HasValue)
            return null;

        return Math.Round(twips.Value / TwipsPerCm, 2);
    }

    private static decimal? TwipsToPoints(long? twips)
    {
        return twips.HasValue ? twips.Value / 20m : null;
    }

    private static string NormalizeAlignmentValue(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return string.Empty;

        var normalized = alignment.Trim().ToLowerInvariant();
        if (normalized == "both")
            return "justify";
        if (normalized == "start")
            return "left";
        if (normalized == "end")
            return "right";

        return normalized;
    }

    private sealed class AlignmentValidationContext
    {
        public string? Text { get; init; }
        public IReadOnlyList<ErrorLocation>? Locations { get; init; }
        public PageLayoutSnapshot? PageLayout { get; init; }
        public decimal? FontSizePt { get; init; }
    }

    private static AlignmentValidationContext? CreateAlignmentContext(
        string? text,
        IReadOnlyList<ErrorLocation>? locations,
        PageLayoutSnapshot? pageLayout,
        decimal? fontSizePt = null)
    {
        if (string.IsNullOrWhiteSpace(text) &&
            (locations == null || locations.Count == 0) &&
            pageLayout == null &&
            !fontSizePt.HasValue)
        {
            return null;
        }

        return new AlignmentValidationContext
        {
            Text = text,
            Locations = locations,
            PageLayout = pageLayout,
            FontSizePt = fontSizePt
        };
    }

    private static bool AreAlignmentsEquivalent(
        string? actual,
        string? expected,
        AlignmentValidationContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        var normalizedActual = NormalizeAlignmentValue(actual);
        var normalizedExpected = NormalizeAlignmentValue(expected);
        if (string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IsLeftJustifyPair(normalizedActual, normalizedExpected))
            return false;

        if (context == null)
            return false;

        if (normalizedExpected == "left" && normalizedActual == "justify")
            return IsJustifyVisuallyEquivalentToLeft(context);

        if (normalizedExpected == "justify" && normalizedActual == "left")
            return IsLeftVisuallyEquivalentToJustify(context);

        return false;
    }

    private static bool IsLeftJustifyPair(string normalizedActual, string normalizedExpected)
    {
        return (normalizedActual == "left" && normalizedExpected == "justify") ||
               (normalizedActual == "justify" && normalizedExpected == "left");
    }

    private static bool IsJustifyVisuallyEquivalentToLeft(AlignmentValidationContext context)
    {
        if (context.PageLayout == null)
            return false;

        if (!TryGetTextAreaHorizontalBoundsRatio(context.PageLayout, out var _textAreaLeft, out var textAreaRight))
            return false;

        if (!TryGetBboxHorizontalBoundsRatio(context.Locations, context.PageLayout, out var _bboxLeft, out var bboxRight))
            return false;

        // Consider "touching right margin" if the box right edge is within ~1% page width from text boundary.
        return bboxRight < textAreaRight - 0.01m;
    }

    private static bool IsLeftVisuallyEquivalentToJustify(AlignmentValidationContext context)
    {
        if (context.PageLayout == null)
            return false;

        var text = NormalizeWhitespace(context.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryGetTextAreaWidthPoints(context.PageLayout, out var availableWidthPt))
            return false;

        if (availableWidthPt <= 0m)
            return false;

        var estimatedLines = EstimateWrappedLineCount(text, availableWidthPt, context.FontSizePt);
        return estimatedLines <= 1;
    }

    private static bool TryGetTextAreaHorizontalBoundsRatio(
        PageLayoutSnapshot layout,
        out decimal leftRatio,
        out decimal rightRatio)
    {
        leftRatio = 0m;
        rightRatio = 0m;

        if (!layout.WidthCm.HasValue || layout.WidthCm.Value <= 0m)
            return false;

        var pageWidthCm = layout.WidthCm.Value;
        var marginLeftCm = Math.Max(0m, layout.MarginLeftCm ?? 0m);
        var marginRightCm = Math.Max(0m, layout.MarginRightCm ?? 0m);

        leftRatio = ClampRatio(marginLeftCm / pageWidthCm);
        rightRatio = ClampRatio(1m - (marginRightCm / pageWidthCm));
        return rightRatio > leftRatio;
    }

    private static bool TryGetBboxHorizontalBoundsRatio(
        IReadOnlyList<ErrorLocation>? locations,
        PageLayoutSnapshot layout,
        out decimal leftRatio,
        out decimal rightRatio)
    {
        leftRatio = 0m;
        rightRatio = 0m;

        if (locations == null || locations.Count == 0)
            return false;

        decimal? minX0 = null;
        decimal? maxX1 = null;
        foreach (var location in locations)
        {
            var bbox = location.Bbox;
            if (bbox == null)
                continue;

            minX0 = !minX0.HasValue ? bbox.X0 : Math.Min(minX0.Value, bbox.X0);
            maxX1 = !maxX1.HasValue ? bbox.X1 : Math.Max(maxX1.Value, bbox.X1);
        }

        if (!minX0.HasValue || !maxX1.HasValue)
            return false;

        if (!TryNormalizeHorizontalCoordinate(minX0.Value, layout, out leftRatio))
            return false;

        if (!TryNormalizeHorizontalCoordinate(maxX1.Value, layout, out rightRatio))
            return false;

        return rightRatio >= leftRatio;
    }

    private static bool TryNormalizeHorizontalCoordinate(
        decimal coordinate,
        PageLayoutSnapshot layout,
        out decimal ratio)
    {
        ratio = 0m;
        if (coordinate < 0m || !layout.WidthCm.HasValue || layout.WidthCm.Value <= 0m)
            return false;

        if (coordinate <= 1.5m)
        {
            ratio = ClampRatio(coordinate);
            return true;
        }

        var pageWidthCm = layout.WidthCm.Value;
        var pageWidthPt = CmToPoints(pageWidthCm);
        var pageWidthTwips = pageWidthCm * TwipsPerCm;
        var pageWidthEmu = pageWidthCm * EmusPerCm;

        decimal rawRatio;
        if (coordinate <= pageWidthCm * 1.2m)
        {
            rawRatio = coordinate / pageWidthCm;
        }
        else if (coordinate <= pageWidthPt * 1.2m)
        {
            rawRatio = coordinate / pageWidthPt;
        }
        else if (coordinate <= pageWidthTwips * 1.2m)
        {
            rawRatio = coordinate / pageWidthTwips;
        }
        else if (coordinate <= pageWidthEmu * 1.2m)
        {
            rawRatio = coordinate / pageWidthEmu;
        }
        else
        {
            var candidates = new[]
            {
                coordinate / pageWidthCm,
                coordinate / pageWidthPt,
                coordinate / pageWidthTwips,
                coordinate / pageWidthEmu
            };

            var validCandidates = candidates
                .Where(c => c >= 0m && c <= 1.5m)
                .ToList();

            if (validCandidates.Count == 0)
                return false;

            rawRatio = validCandidates.OrderBy(c => Math.Abs(1m - c)).First();
        }

        ratio = ClampRatio(rawRatio);
        return true;
    }

    private static bool TryGetTextAreaWidthPoints(PageLayoutSnapshot layout, out decimal widthPt)
    {
        widthPt = 0m;
        if (!layout.WidthCm.HasValue || layout.WidthCm.Value <= 0m)
            return false;

        var leftCm = Math.Max(0m, layout.MarginLeftCm ?? 0m);
        var rightCm = Math.Max(0m, layout.MarginRightCm ?? 0m);
        var textAreaCm = layout.WidthCm.Value - leftCm - rightCm;
        if (textAreaCm <= 0m)
            return false;

        widthPt = CmToPoints(textAreaCm);
        return widthPt > 0m;
    }

    private static int EstimateWrappedLineCount(string text, decimal availableWidthPt, decimal? fontSizePt)
    {
        if (availableWidthPt <= 0m)
            return int.MaxValue;

        var normalizedText = text?.Replace('\r', '\n') ?? string.Empty;
        var paragraphs = normalizedText.Split('\n', StringSplitOptions.None);
        if (paragraphs.Length == 0)
            return 0;

        var effectiveFontSize = fontSizePt.HasValue && fontSizePt.Value > 0m
            ? fontSizePt.Value
            : 12m;

        var totalLines = 0;
        foreach (var paragraph in paragraphs)
        {
            var normalizedParagraph = NormalizeWhitespace(paragraph);
            if (string.IsNullOrWhiteSpace(normalizedParagraph))
            {
                totalLines += 1;
                continue;
            }

            totalLines += EstimateWrappedLineCountSingleParagraph(
                normalizedParagraph,
                availableWidthPt,
                effectiveFontSize);
        }

        return Math.Max(totalLines, 1);
    }

    private static int EstimateWrappedLineCountSingleParagraph(
        string paragraph,
        decimal availableWidthPt,
        decimal fontSizePt)
    {
        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return 1;

        var lines = 1;
        var currentLineWidth = 0m;
        var spaceWidth = fontSizePt * 0.33m;

        foreach (var word in words)
        {
            var wordWidth = EstimateWordWidthPoints(word, fontSizePt);
            if (wordWidth <= 0m)
                continue;

            if (currentLineWidth <= 0m)
            {
                if (wordWidth <= availableWidthPt)
                {
                    currentLineWidth = wordWidth;
                }
                else
                {
                    var wrappedLines = (int)Math.Ceiling(wordWidth / availableWidthPt);
                    lines += Math.Max(0, wrappedLines - 1);
                    var remainder = wordWidth % availableWidthPt;
                    currentLineWidth = remainder > 0m ? remainder : availableWidthPt;
                }
                continue;
            }

            var candidateWidth = currentLineWidth + spaceWidth + wordWidth;
            if (candidateWidth <= availableWidthPt)
            {
                currentLineWidth = candidateWidth;
                continue;
            }

            lines++;
            if (wordWidth <= availableWidthPt)
            {
                currentLineWidth = wordWidth;
            }
            else
            {
                var wrappedLines = (int)Math.Ceiling(wordWidth / availableWidthPt);
                lines += Math.Max(0, wrappedLines - 1);
                var remainder = wordWidth % availableWidthPt;
                currentLineWidth = remainder > 0m ? remainder : availableWidthPt;
            }
        }

        return Math.Max(lines, 1);
    }

    private static decimal EstimateWordWidthPoints(string word, decimal fontSizePt)
    {
        var width = 0m;
        foreach (var ch in word)
        {
            decimal factor;
            if (char.IsUpper(ch))
                factor = 0.62m;
            else if (char.IsLower(ch))
                factor = 0.53m;
            else if (char.IsDigit(ch))
                factor = 0.55m;
            else if (char.IsPunctuation(ch))
                factor = 0.28m;
            else
                factor = 0.5m;

            width += factor * fontSizePt;
        }

        return width;
    }

    private static decimal ClampRatio(decimal ratio)
    {
        if (ratio < 0m)
            return 0m;
        if (ratio > 1.5m)
            return 1.5m;
        return ratio;
    }

    private static bool IsContinuationCaptionRequired(RuleValue<bool>? rule)
    {
        return rule?.Value ?? true;
    }

    private static decimal CmToPoints(decimal cm)
    {
        return cm / 2.54m * 72m;
    }

}


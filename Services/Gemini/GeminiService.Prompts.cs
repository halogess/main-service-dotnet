using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// GeminiService partial - Prompt building methods
/// </summary>
public partial class GeminiService
{
    private static string BuildErrorGuidancePrompt(
        IReadOnlyList<EnhancedValidationError> errors,
        IReadOnlyList<RuleDefinitionPayload> ruleDefinitions,
        IReadOnlyDictionary<int, OpenXmlContextPayload>? openXmlContexts,
        IReadOnlyList<PageImageInfo>? pageImages,
        bool strictJson = false)
    {
        var imageIndexByPage = new Dictionary<int, int>();
        if (pageImages != null)
        {
            foreach (var info in pageImages)
                imageIndexByPage[info.Page] = info.ImageIndex;
        }

        var errorPayloadWithContext = errors
            .Select(err => new
            {
                index = err.Index,
                message = err.Error.Message,
                expected = err.Error.Expected,
                actual = err.Error.Actual,
                evidence = err.Error.Evidence,
                category = err.Error.Category,
                field = err.Error.Field,
                section_index = err.Error.SectionIndex,
                diff_type = err.DiffType,
                cause = err.Cause,
                has_numbering = err.HasNumbering,
                tool_requirement = err.ToolRequirement,
                feature_name = err.FeatureName,
                style_id = err.StyleId,
                style_name = err.StyleName,
                allowed_actions = err.AllowedActions,
                disallowed_actions = err.DisallowedActions,
                rule_key = err.RuleKey,
                rule_context = err.RuleDefinition?.LlmContext,
                scope_hint = err.ScopeHint,
                page_range = err.PageRange,
                prev_element_text = err.Error.PrevElementText,
                prev_element_label = err.Error.PrevElementLabel,
                next_element_text = err.Error.NextElementText,
                next_element_label = err.Error.NextElementLabel,
                page_margin_top_cm = err.Error.PageMarginTopCm,
                page_margin_bottom_cm = err.Error.PageMarginBottomCm,
                page_margin_left_cm = err.Error.PageMarginLeftCm,
                page_margin_right_cm = err.Error.PageMarginRightCm,
                image_page = err.Error.Locations.FirstOrDefault()?.HalamanKe,
                image_index = err.Error.Locations.FirstOrDefault()?.HalamanKe is int page &&
                              imageIndexByPage.TryGetValue(page, out var idx)
                    ? idx
                    : (int?)null,
                openxml_context = openXmlContexts != null &&
                                  openXmlContexts.TryGetValue(err.Index, out var openXml)
                    ? openXml
                    : null,
                location = err.Error.Locations.Count > 0
                    ? new
                    {
                        halaman_ke = err.Error.Locations[0].HalamanKe,
                        bbox = err.Error.Locations[0].Bbox != null
                            ? new
                            {
                                x0 = err.Error.Locations[0].Bbox!.X0,
                                y0 = err.Error.Locations[0].Bbox!.Y0,
                                x1 = err.Error.Locations[0].Bbox!.X1,
                                y1 = err.Error.Locations[0].Bbox!.Y1
                            }
                            : null
                    }
                    : null
            })
            .ToList();

        var usedRuleKeys = new HashSet<string>(
            errors.Select(e => e.RuleKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k!),
            StringComparer.OrdinalIgnoreCase);

        var filteredRules = ruleDefinitions
            .Where(r => !string.IsNullOrWhiteSpace(r.Key) && usedRuleKeys.Contains(r.Key!))
            .Select(r => new { key = r.Key, category = r.Category, llm_context = r.LlmContext })
            .ToList();

        var promptJsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        var sb = new StringBuilder();
        sb.AppendLine("ATURAN_AKTIF_JSON:");
        sb.AppendLine(JsonSerializer.Serialize(filteredRules, promptJsonOptions));
        sb.AppendLine("IMAGE_INDEX_MAP_JSON:");
        sb.AppendLine(JsonSerializer.Serialize(pageImages ?? Array.Empty<PageImageInfo>(), promptJsonOptions));
        sb.AppendLine("Catatan: Gambar halaman penuh dikirim sebagai inline data sesuai IMAGE_INDEX_MAP_JSON.");
        sb.AppendLine("KESALAHAN_JSON:");
        sb.AppendLine(JsonSerializer.Serialize(errorPayloadWithContext, promptJsonOptions));

        if (strictJson)
        {
            sb.AppendLine("Output JSON valid saja, tanpa teks lain, tanpa markdown/backticks, tanpa trailing comma.");
            sb.AppendLine("Jangan tambah field di luar format yang diminta.");
            sb.AppendLine("Wajib isi is_error dan skip_reason (skip_reason boleh kosong jika is_error=true).");
            sb.AppendLine("Explanation sebutkan yang ditemukan vs seharusnya; langkah sebut objek dan gunakan istilah MS Word bahasa Inggris.");
        }

        return sb.ToString();
    }
}

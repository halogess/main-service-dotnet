using System.Text;
using System.Text.Json;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// GeminiService partial - Error analysis and classification methods
/// </summary>
public partial class GeminiService
{
    private static (List<EnhancedValidationError> Errors, List<RuleDefinitionPayload> Rules) BuildEnhancedErrors(
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<AturanDetail> activeRules)
    {
        var ruleDefinitions = activeRules
            .Where(r => !string.IsNullOrWhiteSpace(r.AturanDetailKey))
            .Select(BuildRuleDefinitionPayload)
            .ToList();

        var ruleByKey = ruleDefinitions
            .Where(r => !string.IsNullOrWhiteSpace(r.Key))
            .GroupBy(r => r.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var enhanced = new List<EnhancedValidationError>(errors.Count);

        for (var i = 0; i < errors.Count; i++)
        {
            var error = errors[i];
            var ruleKey = ResolveRuleKey(error.Field);
            RuleDefinitionPayload? ruleDef = null;
            if (!string.IsNullOrWhiteSpace(ruleKey) && ruleByKey.TryGetValue(ruleKey, out var found))
                ruleDef = found;

            var llmContext = ruleDef?.LlmContext;
            var diff = ClassifyDiff(error.Expected, error.Actual, error.Message);

            var diffType = error.DiffType ?? diff.DiffType;
            var cause = error.Cause ?? diff.Cause;
            var evidence = !string.IsNullOrWhiteSpace(error.Evidence) ? error.Evidence : diff.Evidence;

            var hasNumbering = error.HasNumbering ?? ExtractBoolValue(llmContext, "has_numbering");
            var toolRequirement = error.ToolRequirement
                ?? ExtractStringValue(llmContext, "tool_requirement")
                ?? DeriveToolRequirement(error.Field, error.Message, diffType, cause);
            var featureName = error.FeatureName
                ?? ExtractStringValue(llmContext, "feature_name")
                ?? DeriveFeatureName(toolRequirement, error.Field, cause);

            var allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var disallowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (error.AllowedActions != null)
            {
                foreach (var action in error.AllowedActions)
                    AddActionToken(allowedActions, action);
            }

            if (error.DisallowedActions != null)
            {
                foreach (var action in error.DisallowedActions)
                    AddActionToken(disallowedActions, action);
            }

            foreach (var action in ExtractStringArray(llmContext, "allowed_actions"))
                AddActionToken(allowedActions, action);

            foreach (var action in ExtractStringArray(llmContext, "disallowed_actions"))
                AddActionToken(disallowedActions, action);

            if (ExtractBoolValue(llmContext, "not_list") == true)
            {
                AddActionToken(disallowedActions, "numbering");
                AddActionToken(disallowedActions, "multilevel_list");
            }

            var policy = DeriveActionPolicy(diffType, cause, hasNumbering, toolRequirement, featureName);
            foreach (var action in policy.AllowedActions)
                AddActionToken(allowedActions, action);
            foreach (var action in policy.DisallowedActions)
                AddActionToken(disallowedActions, action);

            var allowedList = allowedActions.Count > 0 ? allowedActions.OrderBy(a => a).ToList() : null;
            var disallowedList = disallowedActions.Count > 0 ? disallowedActions.OrderBy(a => a).ToList() : null;

            enhanced.Add(new EnhancedValidationError
            {
                Index = i,
                Error = error,
                RuleKey = ruleKey,
                RuleDefinition = ruleDef,
                DiffType = diffType,
                Cause = cause,
                Evidence = evidence,
                HasNumbering = hasNumbering,
                ToolRequirement = toolRequirement,
                FeatureName = featureName,
                StyleId = error.StyleId,
                StyleName = error.StyleName,
                AllowedActions = allowedList,
                DisallowedActions = disallowedList,
                ScopeHint = error.ScopeHint,
                PageRange = error.PageRange
            });
        }

        return (enhanced, ruleDefinitions);
    }

    private static DiffClassification ClassifyDiff(string? expected, string? actual, string? message)
    {
        var result = new DiffClassification();

        if (!string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(actual))
        {
            if (expected == actual)
            {
                result.DiffType = "unknown";
                result.Cause = "unknown";
            }
            else if (NormalizeWhitespaceForDiff(expected) == NormalizeWhitespaceForDiff(actual))
            {
                result.DiffType = "whitespace_only";
                result.Cause = DetectWhitespaceCause(expected, actual);
            }
            else if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                result.DiffType = "case_only";
                result.Cause = "wrong_case";
            }
            else if (StripPunctuationForDiff(expected) == StripPunctuationForDiff(actual))
            {
                result.DiffType = "punctuation_only";
                result.Cause = "punctuation_mismatch";
            }
            else
            {
                result.DiffType = "value_mismatch";
                result.Cause = DeriveCauseFromField(null) ?? "value_mismatch";
            }
        }
        else if (!string.IsNullOrWhiteSpace(actual))
        {
            var normalized = NormalizeWhitespaceForDiff(actual);
            if (normalized == actual.Trim())
            {
                result.DiffType = "whitespace_only";
                result.Cause = DetectWhitespaceCause(null, actual);
            }
        }
        else if (!string.IsNullOrWhiteSpace(expected))
        {
            result.DiffType = "structure_mismatch";
            result.Cause = "missing_value";
        }
        else if (!string.IsNullOrWhiteSpace(message) && message.Contains("tidak ditemukan", StringComparison.OrdinalIgnoreCase))
        {
            result.DiffType = "structure_mismatch";
            result.Cause = "missing_element";
        }

        result.Evidence = BuildEvidence(expected, actual, result.Cause);
        return result;
    }

    private static string DetectWhitespaceCause(string? expected, string actual)
    {
        if (!string.IsNullOrEmpty(expected))
        {
            if (expected.TrimEnd() == actual.TrimEnd() && actual.Length > expected.Length)
                return "trailing_whitespace";
            if (expected.TrimStart() == actual.TrimStart() && actual.Length > expected.Length)
                return "leading_whitespace";
        }

        if (actual.Contains("\t"))
            return "tab";
        if (actual.Contains("  "))
            return "double_space";
        if (actual != actual.TrimEnd())
            return "trailing_whitespace";
        if (actual != actual.TrimStart())
            return "leading_whitespace";

        return "whitespace_mismatch";
    }

    private static string BuildEvidence(string? expected, string? actual, string? cause)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(expected))
            parts.Add($"expected='{expected}'");
        if (!string.IsNullOrWhiteSpace(actual))
            parts.Add($"actual='{actual}'");
        if (!string.IsNullOrWhiteSpace(cause) && cause != "unknown")
            parts.Add($"cause={cause}");
        return string.Join("; ", parts);
    }

    private static string? DeriveCauseFromField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        var normalized = field.ToLowerInvariant();
        if (normalized.Contains("margin"))
            return "wrong_margin";
        if (normalized.Contains("spacing"))
            return "wrong_paragraph_spacing";
        if (normalized.Contains("alignment"))
            return "wrong_alignment";
        if (normalized.Contains("page_number"))
            return "missing_page_number_field";
        return null;
    }

    private static string? DeriveToolRequirement(string? field, string? message, string? diffType, string? cause)
    {
        if (string.Equals(diffType, "whitespace_only", StringComparison.OrdinalIgnoreCase))
            return "must_not_use_feature";

        var normalizedField = field?.ToLowerInvariant() ?? string.Empty;
        if (normalizedField.Contains("page_number") || string.Equals(cause, "missing_page_number_field", StringComparison.OrdinalIgnoreCase))
            return "must_use_word_feature";

        if (!string.IsNullOrWhiteSpace(message) && message.Contains("tidak ditemukan", StringComparison.OrdinalIgnoreCase))
            return "must_use_word_feature";

        return "optional";
    }

    private static string? DeriveFeatureName(string? toolRequirement, string? field, string? cause)
    {
        if (!string.Equals(toolRequirement, "must_use_word_feature", StringComparison.OrdinalIgnoreCase))
            return null;

        var normalizedField = field?.ToLowerInvariant() ?? string.Empty;
        if (normalizedField.Contains("page_number") || string.Equals(cause, "missing_page_number_field", StringComparison.OrdinalIgnoreCase))
            return "Page Number";

        return null;
    }

    private static ActionPolicy DeriveActionPolicy(string? diffType, string? cause, bool? hasNumbering, string? toolRequirement, string? featureName)
    {
        var policy = new ActionPolicy();
        var diff = diffType?.ToLowerInvariant() ?? "unknown";
        var causeNormalized = cause?.ToLowerInvariant() ?? "unknown";

        if (diff == "whitespace_only")
        {
            policy.AllowedActions.AddRange(new[] { "edit_text", "find_replace", "show_hide_marks" });
            policy.DisallowedActions.AddRange(new[] { "numbering", "multilevel_list", "section_break", "modify_style", "apply_style" });
        }
        else if (diff == "case_only")
        {
            policy.AllowedActions.AddRange(new[] { "edit_text", "change_case", "find_replace" });
        }

        if (causeNormalized.Contains("spacing"))
            policy.AllowedActions.Add("paragraph_spacing");
        if (causeNormalized.Contains("margin"))
            policy.AllowedActions.Add("layout_margins");
        if (causeNormalized.Contains("alignment"))
            policy.AllowedActions.Add("paragraph_alignment");
        if (causeNormalized.Contains("page_number"))
            policy.AllowedActions.Add("page_number");

        if (hasNumbering == false)
        {
            policy.DisallowedActions.Add("numbering");
            policy.DisallowedActions.Add("multilevel_list");
        }

        if (string.Equals(toolRequirement, "must_not_use_feature", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(featureName))
                policy.DisallowedActions.Add(featureName);
        }

        return policy;
    }

    private static string NormalizeWhitespaceForDiff(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder();
        var inWhitespace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(ch);
                inWhitespace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static string StripPunctuationForDiff(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                sb.Append(ch);
        }

        return NormalizeWhitespaceForDiff(sb.ToString());
    }

    private static bool? ExtractBoolValue(JsonElement? context, string property)
    {
        if (!context.HasValue || context.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (context.Value.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True)
            return true;
        if (context.Value.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.False)
            return false;

        return null;
    }

    private static string? ExtractStringValue(JsonElement? context, string property)
    {
        if (!context.HasValue || context.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (context.Value.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();

        return null;
    }

    private static List<string> ExtractStringArray(JsonElement? context, string property)
    {
        var result = new List<string>();
        if (!context.HasValue || context.Value.ValueKind != JsonValueKind.Object)
            return result;

        if (!context.Value.TryGetProperty(property, out var value))
            return result;

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString();
            if (!string.IsNullOrWhiteSpace(single))
                result.Add(single);
            return result;
        }

        if (value.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
            }
        }

        return result;
    }

    private static void AddActionToken(HashSet<string> target, string? value)
    {
        var normalized = NormalizeActionToken(value);
        if (!string.IsNullOrWhiteSpace(normalized))
            target.Add(normalized);
    }

    private static string? NormalizeActionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        normalized = normalized.Replace(" ", "_").Replace("-", "_");
        return normalized;
    }

    private static void ApplyGuardrails(List<EnhancedValidationError> contextErrors, List<GeminiErrorDetail> results)
    {
        if (contextErrors.Count == 0 || results.Count == 0)
            return;

        var contextByIndex = contextErrors.ToDictionary(e => e.Index, e => e);
        foreach (var result in results)
        {
            if (!contextByIndex.TryGetValue(result.Index, out var context))
                continue;

            if (ViolatesActionPolicy(context, result.Steps))
            {
                result.Steps = BuildFallbackSteps(context);
            }
        }
    }

    private static bool ViolatesActionPolicy(EnhancedValidationError context, List<string> steps)
    {
        if (steps.Count == 0)
            return false;

        var stepText = string.Join(" ", steps).ToLowerInvariant();

        if (context.DisallowedActions != null)
        {
            foreach (var action in context.DisallowedActions)
            {
                if (action == "numbering" && ContainsAny(stepText, new[] { "numbering", "multilevel list", "define new" }))
                    return true;
                if (action == "multilevel_list" && ContainsAny(stepText, new[] { "multilevel", "define new" }))
                    return true;
                if (action == "section_break" && ContainsAny(stepText, new[] { "section break", "pemisah section" }))
                    return true;
                if ((action == "modify_style" || action == "apply_style") && ContainsAny(stepText, new[] { "style", "styles", "heading" }))
                    return true;
                if (action == "page_number" && ContainsAny(stepText, new[] { "page number", "nomor halaman", "header", "footer" }))
                    return true;
            }
        }

        if (string.Equals(context.DiffType, "whitespace_only", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(stepText, new[] { "numbering", "multilevel", "define new", "section break", "header", "footer", "page number", "style", "heading" }))
                return true;
        }

        return false;
    }

    private static bool ContainsAny(string haystack, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static List<string> BuildFallbackSteps(EnhancedValidationError context)
    {
        var steps = new List<string>();
        var allowed = context.AllowedActions?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (allowed.Contains("show_hide_marks"))
            steps.Add("Tampilkan tanda spasi/paragraf: Home -> ? (Show/Hide)." );

        switch (context.Cause)
        {
            case "trailing_whitespace":
                steps.Add("Hapus spasi di akhir teks yang bermasalah.");
                break;
            case "leading_whitespace":
                steps.Add("Hapus spasi di awal teks yang bermasalah.");
                break;
            case "double_space":
                if (allowed.Contains("find_replace"))
                {
                    steps.Add("Tekan Ctrl+H, ganti dua spasi dengan satu spasi pada bagian terkait.");
                }
                else
                {
                    steps.Add("Ganti spasi ganda menjadi satu secara manual.");
                }
                break;
            case "wrong_case":
                steps.Add("Ubah kapitalisasi teks agar sesuai (gunakan Shift+F3 jika diperlukan).");
                break;
            default:
                steps.Add("Perbaiki teks agar sesuai expected.");
                break;
        }

        if (steps.Count < 3)
            steps.Insert(0, "Pilih teks yang bermasalah.");
        if (!steps.Any(s => s.Contains("Simpan", StringComparison.OrdinalIgnoreCase)))
            steps.Add("Simpan dokumen (Ctrl+S)." );

        while (steps.Count < 3)
            steps.Add("Periksa ulang hasil perbaikan.");

        if (steps.Count > 6)
            steps = steps.Take(6).ToList();

        return steps;
    }

    private static string? ResolveRuleKey(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        var normalized = field.Trim().ToLowerInvariant();

        if (normalized.StartsWith("margin_"))
            return "margin";
        if (normalized == "header_from_top" || normalized == "footer_from_bottom" || normalized == "different_odd_even")
            return "header_footer";
        if (normalized.StartsWith("gutter"))
            return "gutter";
        if (normalized.StartsWith("column"))
            return "column";
        if (normalized.StartsWith("page_number"))
            return "page_numbering";
        if (normalized.StartsWith("paper"))
            return "paper";

        return normalized;
    }

    /// <summary>
    /// Returns a human-readable description for common rule fields.
    /// </summary>
    private static string? GetRuleDescription(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        return field.ToLowerInvariant() switch
        {
            "judul_bab" => "Aturan format judul bab (font, spacing, alignment, numbering)",
            "paper" => "Aturan ukuran kertas dan orientasi",
            "paper_size" => "Aturan ukuran kertas",
            "margin" => "Aturan margin halaman",
            "margin_top" => "Aturan margin atas halaman",
            "margin_bottom" => "Aturan margin bawah halaman",
            "margin_left" => "Aturan margin kiri halaman",
            "margin_right" => "Aturan margin kanan halaman",
            "header_footer" => "Aturan header dan footer",
            "header_from_top" => "Aturan jarak header dari atas",
            "footer_from_bottom" => "Aturan jarak footer dari bawah",
            "different_odd_even" => "Aturan header/footer ganjil-genap",
            "gutter" => "Aturan gutter halaman",
            "gutter_position" => "Aturan posisi gutter",
            "column" => "Aturan jumlah kolom",
            "column_count" => "Aturan jumlah kolom",
            "page_numbering" => "Aturan penomoran halaman",
            "page_number_format" => "Aturan format nomor halaman",
            "page_number_start" => "Aturan awal nomor halaman",
            "font_name" => "Aturan jenis font",
            "font_size" => "Aturan ukuran font",
            "line_spacing" => "Aturan spasi antar baris",
            "spacing_before" => "Aturan spasi sebelum paragraf",
            "spacing_after" => "Aturan spasi setelah paragraf",
            "alignment" => "Aturan perataan paragraf (center, left, right, justify)",
            "indentation" => "Aturan indentasi paragraf",
            "judul_subbab" => "Aturan format judul subbab",
            "paragraf" => "Aturan format paragraf",
            _ => null
        };
    }
}

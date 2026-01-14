using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// GeminiService partial - Prompt building methods
/// </summary>
public partial class GeminiService
{
    private string BuildAnalysisPrompt(string documentContent, List<string>? errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Berikut potongan dokumen dan/atau daftar kesalahan validasi.");
        sb.AppendLine();
        sb.AppendLine("=== KONTEN_DOKUMEN (POTONGAN) ===");
        sb.AppendLine(SmartTruncate(documentContent, 5000, errors));
        sb.AppendLine();

        sb.AppendLine("=== KESALAHAN_YANG_DITEMUKAN (maks 20) ===");
        if (errors != null && errors.Count > 0)
        {
            var cappedErrors = errors.Take(20).ToList();
            for (var i = 0; i < cappedErrors.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {cappedErrors[i]}");
            }
        }
        else
        {
            sb.AppendLine("- (tidak ada)");
        }
        sb.AppendLine();

        sb.AppendLine("Format jawaban:");
        sb.AppendLine("1) Ringkasan masalah utama (maks 5 poin)");
        sb.AppendLine("   - Jika merujuk kesalahan format, sebutkan contoh nomor index error / kategori.");
        sb.AppendLine("2) Rekomendasi perbaikan spesifik (step-by-step)");
        sb.AppendLine("   - Prioritaskan perbaikan massal (Styles/Template), lalu perbaikan manual.");
        sb.AppendLine("3) Saran meningkatkan kualitas dokumen");
        sb.AppendLine("   - Pisahkan saran kualitas isi/struktur dari perbaikan format (jangan dicampur).");

        return sb.ToString();
    }

    /// <summary>
    /// Smart truncation that prioritizes content around error keywords
    /// </summary>
    private static string SmartTruncate(string content, int maxLength, List<string>? errors)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        if (errors == null || errors.Count == 0)
            return content[..maxLength] + "...";

        // Extract keywords from errors
        var keywords = errors
            .SelectMany(e => e.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        // Find sections containing keywords
        var lines = content.Split('\n');
        var relevantLines = new List<(int index, string line)>();
        
        for (int i = 0; i < lines.Length; i++)
        {
            if (keywords.Any(k => lines[i].Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                // Include context: 2 lines before and after
                for (int j = Math.Max(0, i - 2); j <= Math.Min(lines.Length - 1, i + 2); j++)
                {
                    if (!relevantLines.Any(r => r.index == j))
                        relevantLines.Add((j, lines[j]));
                }
            }
        }

        if (relevantLines.Count > 0)
        {
            var prioritized = string.Join("\n", relevantLines.OrderBy(r => r.index).Select(r => r.line));
            if (prioritized.Length <= maxLength)
            {
                // Append beginning of document for context
                var remaining = maxLength - prioritized.Length - 50;
                if (remaining > 100)
                {
                    return content[..remaining] + "\n...[PRIORITAS]...\n" + prioritized;
                }
                return prioritized;
            }
            return prioritized[..maxLength] + "...";
        }

        return content[..maxLength] + "...";
    }

    private static string BuildErrorGuidancePrompt(
        IReadOnlyList<EnhancedValidationError> errors,
        IReadOnlyList<RuleDefinitionPayload> ruleDefinitions)
    {
        var errorPayloadWithContext = errors
            .Select(err => new
            {
                index = err.Index,
                category = err.Error.Category,
                field = err.Error.Field,
                message = err.Error.Message,
                expected = err.Error.Expected,
                actual = err.Error.Actual,
                section_index = err.Error.SectionIndex,
                diff_type = err.DiffType,
                cause = err.Cause,
                has_numbering = err.HasNumbering,
                style_name = err.StyleName,
                style_id = err.StyleId,
                evidence = err.Evidence,
                tool_requirement = err.ToolRequirement,
                feature_name = err.FeatureName,
                allowed_actions = err.AllowedActions,
                disallowed_actions = err.DisallowedActions,
                scope_hint = err.ScopeHint,
                page_range = err.PageRange,
                rule_key = err.RuleKey,
                rule_context = err.RuleDefinition == null
                    ? null
                    : new
                    {
                        rule_key = err.RuleDefinition.Key,
                        category = err.RuleDefinition.Category,
                        llm_context = err.RuleDefinition.LlmContext
                    },
                rule_description = GetRuleDescription(err.RuleKey ?? err.Error.Field)
            })
            .ToList();

        var promptJsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        var sb = new StringBuilder();
        sb.AppendLine("Anda adalah asisten yang membantu menjelaskan kesalahan format dokumen akademik dengan bahasa Indonesia yang ramah dan mudah dipahami.");
        sb.AppendLine();
        sb.AppendLine("Konteks:");
        sb.AppendLine("- ATURAN_AKTIF_JSON adalah satu-satunya aturan yang berlaku saat ini (nilainya bisa berubah).");
        sb.AppendLine("- KESALAHAN_JSON adalah temuan dari validator; bisa saja ada false positive.");
        sb.AppendLine("- ATURAN_AKTIF_JSON dan rule_context bisa memuat llm_context per-rule.");
        sb.AppendLine();
        sb.AppendLine("Decision Ladder (Minimal Fix First):");
        sb.AppendLine("Level 1: edit teks/karakter (hapus spasi, kapitalisasi)." );
        sb.AppendLine("Level 2: Find & Replace.");
        sb.AppendLine("Level 3: pengaturan paragraf (spacing/indent/alignment).");
        sb.AppendLine("Level 4: style.");
        sb.AppendLine("Level 5: struktur (numbering/multilevel list, section break, header/footer)." );
        sb.AppendLine("Wajib pilih level paling rendah yang cukup mengubah actual -> expected.");
        sb.AppendLine();
        sb.AppendLine("Evidence Gate:");
        sb.AppendLine("- Dilarang menyarankan Level 4-5 jika tidak ada bukti di data (has_numbering, tool_requirement, evidence)." );
        sb.AppendLine("- Jika bukti tidak cukup, tulis 'Perlu verifikasi' dan berikan langkah Level 1-2 saja.");
        sb.AppendLine();
        sb.AppendLine("Tugas untuk setiap item di KESALAHAN_JSON:");
        sb.AppendLine("A) Verifikasi dulu:");
        sb.AppendLine("   - Bandingkan expected vs actual.");
        sb.AppendLine("   - Cocokkan dengan aturan terkait (jika ada).");
        sb.AppendLine("   - Jika rule_context.llm_context.not_list == true, jangan sarankan Numbering/Multilevel List.");
        sb.AppendLine("   - Jika disallowed_actions ada, hindari semua aksi di daftar tersebut.");
        sb.AppendLine("   - Jika allowed_actions hanya berisi edit_text/find_replace, jangan sarankan fitur lain.");
        sb.AppendLine("   - Jika belum bisa dipastikan salah, jangan memaksa. Gunakan kalimat 'Perlu verifikasi' di explanation.");
        sb.AppendLine();
        sb.AppendLine("B) Buat output yang membantu user:");
        sb.AppendLine("   - title: singkat, jelas, tidak menyalahkan.");
        sb.AppendLine("   - explanation: jelaskan dengan bahasa natural perbedaan kondisi saat ini vs yang diharapkan (contoh: 'Font yang digunakan adalah Aptos Display, seharusnya Times New Roman'). DILARANG menggunakan format teknis seperti expected='...' atau actual='...'.");
        sb.AppendLine("   - steps: 3-6 langkah di Microsoft Word, menu/tab jelas. Jika 'Perlu verifikasi', steps berisi cara mengecek.");
        sb.AppendLine("   - location: isi berdasarkan data input. halaman_ke = nomor halaman (integer, contoh: 1, 5, 10). section = nama bagian dokumen jika tersedia (contoh: 'BAB I', 'Pendahuluan', 'Daftar Isi'). Jika tidak diketahui, isi halaman_ke dengan 0 dan section dengan '-'.");
        sb.AppendLine("   - Jangan memprioritaskan penggunaan Styles; gunakan Styles hanya jika relevan atau disebut di aturan/llm_context.");
        sb.AppendLine();
        sb.AppendLine("BATASAN OUTPUT (WAJIB):");
        sb.AppendLine("- Jawab HANYA JSON valid, tanpa markdown, tanpa teks tambahan.");
        sb.AppendLine("- Struktur JSON HARUS persis:");
        sb.AppendLine(@"  {""errors"":[{""index"":0,""title"":""..."",""explanation"":""..."",""steps"":[""...""],""location"":{""halaman_ke"":1,""section"":""...""}}]}");
        sb.AppendLine("- Urutan output harus sama dengan urutan item pada KESALAHAN_JSON.");
        sb.AppendLine("- Jangan menambahkan field lain.");
        sb.AppendLine();
        sb.AppendLine("=== ATURAN_AKTIF_JSON ===");
        sb.AppendLine(JsonSerializer.Serialize(ruleDefinitions, promptJsonOptions));
        sb.AppendLine();
        sb.AppendLine("=== KESALAHAN_JSON ===");
        sb.AppendLine(JsonSerializer.Serialize(errorPayloadWithContext, promptJsonOptions));

        return sb.ToString();
    }
}

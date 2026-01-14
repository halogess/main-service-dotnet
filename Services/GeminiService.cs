using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiService> _logger;
    private readonly KorektorBukuDbContext _db;
    private readonly string _analysisModel;
    private readonly string _structuredModel;
    private readonly string _fallbackModel;
    private readonly string _apiBaseUrl;
    private readonly string _analysisSystemInstruction;
    private readonly string _errorGuidanceSystemInstruction;

    // Cache for error guidance responses (key: hash of error signature, value: cached response)
    private static readonly ConcurrentDictionary<string, CachedGuidance> _guidanceCache = new();
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonElement ErrorGuidanceSchema = JsonSerializer.Deserialize<JsonElement>(
        @"{
  ""type"": ""object"",
  ""properties"": {
    ""errors"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""properties"": {
          ""index"": { ""type"": ""integer"", ""minimum"": 0 },
          ""title"": { ""type"": ""string"", ""maxLength"": 100 },
          ""explanation"": { ""type"": ""string"", ""maxLength"": 500 },
          ""steps"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""string"", ""maxLength"": 200 },
            ""minItems"": 1,
            ""maxItems"": 6
          },
          ""location"": {
            ""type"": ""object"",
            ""properties"": {
              ""halaman_ke"": { ""type"": ""integer"" },
              ""section"": { ""type"": ""string"" }
            }
          }
        },
        ""required"": [""index"", ""title"", ""explanation"", ""steps"", ""location""]
      }
    }
  },
  ""required"": [""errors""]
}")!;


    public GeminiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiService> logger,
        KorektorBukuDbContext db)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _db = db;
        _analysisModel = _configuration["Gemini:Model"] ?? "gemma-3-27b";
        _structuredModel = _configuration["Gemini:StructuredModel"] ?? "gemini-2.5-flash";
        _fallbackModel = _configuration["Gemini:FallbackModel"] ?? "gemini-1.5-flash";
        _apiBaseUrl = _configuration["Gemini:ApiBaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta";

        // Load prompts from files
        _analysisSystemInstruction = LoadPromptFile("Prompts/AnalysisSystemInstruction.txt");
        _errorGuidanceSystemInstruction = LoadPromptFile("Prompts/ErrorGuidanceSystemInstruction.txt");
    }

    private string LoadPromptFile(string relativePath)
    {
        var basePath = AppContext.BaseDirectory;
        var fullPath = Path.Combine(basePath, relativePath);
        
        if (File.Exists(fullPath))
        {
            _logger.LogInformation("Loaded prompt file: {Path}", relativePath);
            return File.ReadAllText(fullPath);
        }

        // Fallback: try from current directory (development)
        var devPath = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        if (File.Exists(devPath))
        {
            _logger.LogInformation("Loaded prompt file from dev path: {Path}", devPath);
            return File.ReadAllText(devPath);
        }

        _logger.LogWarning("Prompt file not found: {Path}, using empty string", relativePath);
        return string.Empty;
    }

    public async Task<string> GenerateRecommendationAsync(string prompt)
    {
        var generationConfig = new
        {
            temperature = 0.7,
            topK = 40,
            topP = 0.95,
            maxOutputTokens = 2048
        };

        return await GenerateContentWithRetryAsync(prompt, _analysisSystemInstruction, generationConfig, modelOverride: _analysisModel);
    }

    public async Task<GeminiAnalysisResult> AnalyzeDocumentAsync(int dokumenId, string documentContent, List<string>? errors = null)
    {
        _logger.LogInformation("Analyzing document {DokumenId} with Gemini", dokumenId);

        var prompt = BuildAnalysisPrompt(documentContent, errors);
        _logger.LogDebug("Analysis prompt length: {Length} chars", prompt.Length);

        try
        {
            var response = await GenerateRecommendationAsync(prompt);
            
            return new GeminiAnalysisResult
            {
                Success = true,
                Recommendation = response,
                Suggestions = ParseSuggestions(response)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze document {DokumenId}", dokumenId);
            return new GeminiAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<List<GeminiErrorDetail>> GenerateErrorGuidanceAsync(
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<AturanDetail> activeRules,
        CancellationToken cancellationToken = default)
    {
        if (errors.Count == 0)
            return new List<GeminiErrorDetail>();

        cancellationToken.ThrowIfCancellationRequested();

        // Check cache first
        var cacheKey = GenerateCacheKey(errors);
        if (_guidanceCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.LogInformation("Cache hit for error guidance (key: {Key})", cacheKey[..8]);
            return cached.Details;
        }

        _logger.LogInformation("Cache miss for error guidance, calling Gemini API");

        var (enhancedErrors, ruleDefinitions) = BuildEnhancedErrors(errors, activeRules);
        var prompt = BuildErrorGuidancePrompt(enhancedErrors, ruleDefinitions);
        _logger.LogDebug("Error guidance prompt length: {Length} chars, error count: {Count}", prompt.Length, errors.Count);
        
        var generationConfig = new
        {
            temperature = 0.1,
            topP = 0.8,
            topK = 20,
            maxOutputTokens = 4096,
            responseMimeType = "application/json",
            responseJsonSchema = ErrorGuidanceSchema
        };

        var response = await GenerateContentWithRetryAsync(
            prompt,
            _errorGuidanceSystemInstruction,
            generationConfig,
            cancellationToken,
            _structuredModel);
        
        var results = ParseErrorGuidance(response);
        ApplyGuardrails(enhancedErrors, results);
        
        // Validate output indices match input
        ValidateErrorIndices(errors, results);
        
        // Store in cache
        _guidanceCache[cacheKey] = new CachedGuidance(results);
        
        return results;
    }

    private static string GenerateCacheKey(IReadOnlyList<ValidationError> errors)
    {
        // Create a signature based on error category, field, and message
        var signature = string.Join("|", errors.Select(e => $"{e.Category}:{e.Field}:{e.Message}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return Convert.ToHexString(bytes);
    }

    private void ValidateErrorIndices(IReadOnlyList<ValidationError> errors, List<GeminiErrorDetail> results)
    {
        var expectedIndices = Enumerable.Range(0, errors.Count).ToHashSet();
        var actualIndices = results.Select(r => r.Index).ToHashSet();
        
        var missing = expectedIndices.Except(actualIndices).ToList();
        var extra = actualIndices.Except(expectedIndices).ToList();
        
        if (missing.Count > 0)
        {
            _logger.LogWarning("Missing guidance for error indices: {Indices}", string.Join(", ", missing));
        }
        
        if (extra.Count > 0)
        {
            _logger.LogWarning("Unexpected guidance indices returned: {Indices}", string.Join(", ", extra));
        }
    }

    private async Task<string> GenerateContentWithRetryAsync(
        string prompt,
        string? systemInstruction,
        object generationConfig,
        CancellationToken cancellationToken = default,
        string? modelOverride = null)
    {
        var currentModel = modelOverride ?? _analysisModel;
        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                return await GenerateContentAsync(prompt, systemInstruction, generationConfig, cancellationToken, currentModel);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("503") || ex.Message.Contains("ServiceUnavailable") || ex.Message.Contains("TooManyRequests"))
            {
                lastException = ex;
                _logger.LogWarning("Gemini API rate limited or unavailable (attempt {Attempt}/{Max}), retrying in {Delay}s", 
                    attempt + 1, MaxRetries, RetryDelays[attempt].TotalSeconds);
                
                await Task.Delay(RetryDelays[attempt], cancellationToken);
                
                // On last retry, try fallback model
                if (attempt == MaxRetries - 2)
                {
                    _logger.LogInformation("Switching to fallback model: {Model}", _fallbackModel);
                    currentModel = _fallbackModel;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini API error on attempt {Attempt}", attempt + 1);
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException("Failed to generate content after retries");
    }

    private async Task<string> GenerateContentAsync(
        string prompt,
        string? systemInstruction,
        object generationConfig,
        CancellationToken cancellationToken = default,
        string? modelOverride = null)
    {
        var model = modelOverride ?? _analysisModel;
        var apiKey = await GetActiveApiKeyAsync();
        _logger.LogInformation("Generating Gemini content with model: {Model}, key id: {KeyId}", model, apiKey.GeminiApiKeyId);

        var endpoint = $"{_apiBaseUrl}/models/{model}:generateContent?key={apiKey.GeminiApiKeyValue}";

        var requestBody = new Dictionary<string, object?>
        {
            ["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            ["generationConfig"] = generationConfig
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            requestBody["systemInstruction"] = new
            {
                parts = new[]
                {
                    new { text = systemInstruction }
                }
            };
        }

        var json = JsonSerializer.Serialize(requestBody, JsonWriteOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                throw new HttpRequestException($"Gemini API error: {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var textContent = result
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            _logger.LogInformation("Gemini response received, length: {Length} chars", textContent?.Length ?? 0);
            await IncrementUsageAsync(apiKey);
            return textContent ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Gemini API");
            throw;
        }
    }

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

    private static List<GeminiSuggestion> ParseSuggestions(string response)
    {
        // Simple parsing - in production, consider structured output from Gemini
        var suggestions = new List<GeminiSuggestion>();
        
        // For now, return the full response as a single suggestion
        if (!string.IsNullOrEmpty(response))
        {
            suggestions.Add(new GeminiSuggestion
            {
                Category = "Analisis",
                Issue = "Hasil analisis dokumen",
                Recommendation = response
            });
        }

        return suggestions;
    }

    
    
        private sealed class EnhancedValidationError
    {
        public int Index { get; init; }
        public ValidationError Error { get; init; } = null!;
        public string? RuleKey { get; init; }
        public RuleDefinitionPayload? RuleDefinition { get; init; }
        public string? DiffType { get; init; }
        public string? Cause { get; init; }
        public bool? HasNumbering { get; init; }
        public string? Evidence { get; init; }
        public string? ToolRequirement { get; init; }
        public string? FeatureName { get; init; }
        public string? StyleId { get; init; }
        public string? StyleName { get; init; }
        public List<string>? AllowedActions { get; init; }
        public List<string>? DisallowedActions { get; init; }
        public string? ScopeHint { get; init; }
        public string? PageRange { get; init; }
    }

    private sealed class DiffClassification
    {
        public string DiffType { get; set; } = "unknown";
        public string Cause { get; set; } = "unknown";
        public string Evidence { get; set; } = string.Empty;
    }

    private sealed class ActionPolicy
    {
        public List<string> AllowedActions { get; } = new();
        public List<string> DisallowedActions { get; } = new();
    }

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

        if (actual.Contains("	"))
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

private sealed class RuleDefinitionPayload
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("rule_json")]
        public JsonElement? RuleJson { get; set; }

        [JsonPropertyName("llm_context")]
        public JsonElement? LlmContext { get; set; }
    }

    private static RuleDefinitionPayload BuildRuleDefinitionPayload(AturanDetail rule)
    {
        var ruleJson = TryParseJsonElement(rule.AturanDetailJsonValue);
        return new RuleDefinitionPayload
        {
            Category = rule.AturanDetailKategori,
            Key = rule.AturanDetailKey,
            RuleJson = ruleJson,
            LlmContext = ExtractLlmContext(ruleJson)
        };
    }

    private static JsonElement? TryParseJsonElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ExtractLlmContext(JsonElement? ruleJson)
    {
        if (!ruleJson.HasValue)
            return null;

        var root = ruleJson.Value;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("llm_context", out var llmContext))
        {
            return llmContext.Clone();
        }

        return null;
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


    private static List<GeminiErrorDetail> ParseErrorGuidance(string response)
    {
        try
        {
            var structured = JsonSerializer.Deserialize<GeminiErrorGuidancePayload>(response, JsonReadOptions);
            if (structured?.Errors != null)
                return structured.Errors;
        }
        catch (JsonException)
        {
            // Fall through to legacy parsing.
        }

        var payload = ExtractJsonPayload(response);
        if (string.IsNullOrWhiteSpace(payload))
            return new List<GeminiErrorDetail>();

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            JsonElement errorsElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                errorsElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("errors", out var errorsProp) &&
                     errorsProp.ValueKind == JsonValueKind.Array)
            {
                errorsElement = errorsProp;
            }
            else
            {
                return new List<GeminiErrorDetail>();
            }

            var results = new List<GeminiErrorDetail>();
            foreach (var item in errorsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var detail = new GeminiErrorDetail
                {
                    Index = item.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var idx) ? idx : -1,
                    Title = item.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                        ? titleEl.GetString() ?? string.Empty
                        : string.Empty,
                    Explanation = item.TryGetProperty("explanation", out var expEl) && expEl.ValueKind == JsonValueKind.String
                        ? expEl.GetString() ?? string.Empty
                        : string.Empty
                };

                if (item.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var step in stepsEl.EnumerateArray())
                    {
                        if (step.ValueKind == JsonValueKind.String)
                            detail.Steps.Add(step.GetString() ?? string.Empty);
                    }
                }

                if (item.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.Object)
                {
                    detail.Location = new GeminiErrorLocation
                    {
                        HalamanKe = locEl.TryGetProperty("halaman_ke", out var halamanEl) && halamanEl.TryGetInt32(out var halamanInt)
                            ? halamanInt
                            : null,
                        Section = locEl.TryGetProperty("section", out var sectionEl) && sectionEl.ValueKind == JsonValueKind.String
                            ? sectionEl.GetString()
                            : null
                    };
                }

                results.Add(detail);
            }

            return results;
        }
        catch (JsonException)
        {
            return new List<GeminiErrorDetail>();
        }
    }

    private async Task<GeminiApiKey> GetActiveApiKeyAsync()
    {
        var apiKey = await _db.GeminiApiKeys
            .Where(k => k.GeminiApiKeyStatus == 1)
            .OrderBy(k => k.GeminiApiKeyUsage ?? 0)
            .ThenBy(k => k.GeminiApiKeyId)
            .FirstOrDefaultAsync();

        if (apiKey == null)
            throw new InvalidOperationException("Gemini API key not configured in database");

        return apiKey;
    }

    private async Task IncrementUsageAsync(GeminiApiKey apiKey)
    {
        var currentUsage = apiKey.GeminiApiKeyUsage ?? 0;
        if (currentUsage < uint.MaxValue)
            apiKey.GeminiApiKeyUsage = currentUsage + 1;
        apiKey.GeminiApiKeyUpdatedAt = DateTime.Now;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update Gemini API key usage for id: {KeyId}", apiKey.GeminiApiKeyId);
        }
    }

    private static string? ExtractJsonPayload(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var trimmed = response.Trim();
        var objStart = trimmed.IndexOf('{');
        var arrStart = trimmed.IndexOf('[');
        if (objStart < 0 && arrStart < 0)
            return null;

        var start = objStart >= 0 && (arrStart < 0 || objStart < arrStart) ? objStart : arrStart;
        var end = start == objStart ? trimmed.LastIndexOf('}') : trimmed.LastIndexOf(']');
        if (end <= start)
            return null;

        return trimmed.Substring(start, end - start + 1);
    }

    private class GeminiErrorGuidancePayload
    {
        public List<GeminiErrorDetail> Errors { get; set; } = new();
    }

    private class CachedGuidance
    {
        public List<GeminiErrorDetail> Details { get; }
        public DateTime CreatedAt { get; }
        public bool IsExpired => DateTime.UtcNow - CreatedAt > CacheExpiry;

        public CachedGuidance(List<GeminiErrorDetail> details)
        {
            Details = details;
            CreatedAt = DateTime.UtcNow;
        }
    }
}

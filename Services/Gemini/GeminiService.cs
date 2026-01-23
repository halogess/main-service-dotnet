using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// Core GeminiService - main API communication and public methods
/// </summary>
public partial class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiService> _logger;
    private readonly KorektorBukuDbContext _db;
    private readonly string _analysisModel;
    private readonly string _structuredModel;
    private readonly string _fallbackModel;
    private readonly string _apiBaseUrl;
    private readonly string _errorGuidanceSystemInstruction;
    private readonly double _generationTemperature;
    private readonly double _generationTopP;
    private readonly int _generationTopK;
    private readonly int _generationMaxOutputTokens;
    private readonly int _generationThinkingBudget;
    private readonly bool _enableThinking;
    private readonly bool _enableLlm;
    private readonly int _maxParseAttempts;
    private readonly TimeSpan _parseRetryDelay;

    // Cache for error guidance responses (key: hash of error signature, value: cached response)
    private static readonly ConcurrentDictionary<string, CachedGuidance> _guidanceCache = new();
    private static TimeSpan CacheExpiry = TimeSpan.FromHours(24);
    private readonly int _maxRetries;
    private readonly TimeSpan[] _retryDelays;
    private readonly TimeSpan _defaultRateLimitDelay;
    private static readonly ConcurrentDictionary<uint, DateTimeOffset> RateLimitedKeys = new();

    // Per-Key Token Rate Limiter
    private readonly int _tokensPerMinutePerKeyLimit;
    private static TimeSpan TokenWindowDuration = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<uint, List<(DateTimeOffset Time, int Tokens)>> _keyTokenUsage = new();

    private sealed class GeminiRateLimitException : Exception
    {
        public TimeSpan? RetryAfter { get; }

        public GeminiRateLimitException(string message, TimeSpan? retryAfter)
            : base(message)
        {
            RetryAfter = retryAfter;
        }
    }

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
          ""is_error"": { ""type"": ""boolean"" },
          ""skip_reason"": { ""type"": ""string"", ""maxLength"": 200 },
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
        ""required"": [""index"", ""is_error"", ""skip_reason"", ""title"", ""explanation"", ""steps"", ""location""]
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
        _structuredModel = _configuration["Gemini:StructuredModel"] ?? _analysisModel;
        _fallbackModel = _configuration["Gemini:FallbackModel"] ?? _analysisModel;
        _apiBaseUrl = _configuration["Gemini:ApiBaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta";
        _generationTemperature = _configuration.GetValue("Gemini:Temperature", 0.1);
        _generationTopP = _configuration.GetValue("Gemini:TopP", 0.8);
        _generationTopK = _configuration.GetValue("Gemini:TopK", 20);
        _generationMaxOutputTokens = _configuration.GetValue("Gemini:MaxOutputTokens", 6000);
        _generationThinkingBudget = Math.Max(0, _configuration.GetValue("Gemini:ThinkingBudget", 1024));
        _enableThinking = _configuration.GetValue("Gemini:EnableThinking", _generationThinkingBudget > 0);
        _enableLlm = _configuration.GetValue("Gemini:EnableLlm", true);
        _maxParseAttempts = Math.Max(1, _configuration.GetValue("Gemini:MaxParseAttempts", 3));
        var parseDelaySeconds = Math.Max(0, _configuration.GetValue("Gemini:ParseRetryDelaySeconds", 2));
        _parseRetryDelay = TimeSpan.FromSeconds(parseDelaySeconds);
        _maxRetries = Math.Max(1, _configuration.GetValue("Gemini:MaxRetries", 3));
        _retryDelays = BuildRetryDelays(_configuration, _maxRetries);
        var defaultRateLimitDelaySeconds = Math.Max(0, _configuration.GetValue("Gemini:DefaultRateLimitDelaySeconds", 10));
        _defaultRateLimitDelay = TimeSpan.FromSeconds(defaultRateLimitDelaySeconds);
        _tokensPerMinutePerKeyLimit = Math.Max(1, _configuration.GetValue("Gemini:TokensPerMinutePerKeyLimit", 12000));
        var tokenWindowSeconds = Math.Max(1, _configuration.GetValue("Gemini:TokenWindowSeconds", 60));
        TokenWindowDuration = TimeSpan.FromSeconds(tokenWindowSeconds);
        var cacheHours = Math.Max(0, _configuration.GetValue("Gemini:CacheHours", 24));
        CacheExpiry = TimeSpan.FromHours(cacheHours);

        // Load prompt from file
        _errorGuidanceSystemInstruction = LoadPromptFile("Prompts/ErrorGuidanceSystemInstruction.txt");

        if (IsGemmaModel(_analysisModel))
        {
            if (!IsGemmaModel(_structuredModel))
            {
                _logger.LogInformation("Structured model {StructuredModel} is not Gemma; using {AnalysisModel} instead.", _structuredModel, _analysisModel);
                _structuredModel = _analysisModel;
            }

            if (!IsGemmaModel(_fallbackModel))
            {
                _logger.LogInformation("Fallback model {FallbackModel} is not Gemma; using {AnalysisModel} instead.", _fallbackModel, _analysisModel);
                _fallbackModel = _analysisModel;
            }
        }
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

    private static TimeSpan[] BuildRetryDelays(IConfiguration configuration, int maxRetries)
    {
        if (maxRetries <= 0)
            return Array.Empty<TimeSpan>();

        var delays = new List<TimeSpan>();
        var raw = configuration["Gemini:RetryDelaySeconds"];
        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var part in raw.Split(',', ';'))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                    delays.Add(TimeSpan.FromSeconds(seconds));
            }
        }

        if (delays.Count == 0)
        {
            var delaySeconds = 1.0;
            for (var i = 0; i < maxRetries; i++)
            {
                delays.Add(TimeSpan.FromSeconds(delaySeconds));
                delaySeconds *= 2;
            }
        }

        if (delays.Count < maxRetries)
        {
            var last = delays[^1];
            while (delays.Count < maxRetries)
                delays.Add(last);
        }
        else if (delays.Count > maxRetries)
        {
            delays = delays.Take(maxRetries).ToList();
        }

        return delays.ToArray();
    }


    public async Task<List<GeminiErrorDetail>> GenerateErrorGuidanceAsync(
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<AturanDetail> activeRules,
        CancellationToken cancellationToken = default,
        uint? antrianId = null,
        int? batchNumber = null,
        int? totalBatches = null,
        uint? dokumenId = null)
    {
        if (errors.Count == 0)
            return new List<GeminiErrorDetail>();

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enableLlm)
        {
            _logger.LogInformation("Gemini LLM disabled via config; using fallback guidance.");
            return BuildFallbackGuidance(errors, activeRules);
        }

        // Check cache first
        var cacheKey = GenerateCacheKey(errors, dokumenId);
        if (_guidanceCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.LogInformation("Cache hit for error guidance (key: {Key})", cacheKey[..8]);
            await LogLlmRequestAsync(
                antrianId,
                null,
                BuildLlmLogMessage("cache_hit", errorCount: errors.Count, batchNumber: batchNumber, totalBatches: totalBatches),
                0,
                cancellationToken,
                batchNumber: batchNumber,
                totalBatches: totalBatches,
                errorCount: errors.Count);
            return cached.Details;
        }

        _logger.LogInformation("Cache miss for error guidance, calling Gemini API");
        await LogLlmRequestAsync(
            antrianId,
            null,
            BuildLlmLogMessage("cache_miss", errorCount: errors.Count, batchNumber: batchNumber, totalBatches: totalBatches),
            0,
            cancellationToken,
            batchNumber: batchNumber,
            totalBatches: totalBatches,
            errorCount: errors.Count);

        var (enhancedErrors, ruleDefinitions) = BuildEnhancedErrors(errors, activeRules);
        Dictionary<int, OpenXmlContextPayload>? openXmlContexts = null;
        List<PageImageInfo>? pageImages = null;
        List<LlmImagePayload>? imagePayloads = null;

        if (dokumenId.HasValue)
        {
            openXmlContexts = await BuildOpenXmlContextsAsync(dokumenId.Value, errors, cancellationToken);
            var imageResult = await LoadPageImagesAsync(dokumenId.Value, errors, cancellationToken);
            pageImages = imageResult.Infos;
            imagePayloads = imageResult.Payloads;
        }

        var prompt = BuildErrorGuidancePrompt(enhancedErrors, ruleDefinitions, openXmlContexts, pageImages, strictJson: true);
        _logger.LogDebug("Error guidance prompt length: {Length} chars, error count: {Count}", prompt.Length, errors.Count);
        
        var generationConfig = new Dictionary<string, object>
        {
            ["temperature"] = _generationTemperature,
            ["topP"] = _generationTopP,
            ["topK"] = _generationTopK,
            ["maxOutputTokens"] = _generationMaxOutputTokens
        };

        if (SupportsResponseSchema(_structuredModel))
        {
            generationConfig["responseMimeType"] = "application/json";
            generationConfig["responseSchema"] = ErrorGuidanceSchema;
        }

        if (_enableThinking && _generationThinkingBudget > 0 && SupportsThinking(_structuredModel))
        {
            generationConfig["thinkingConfig"] = new Dictionary<string, object>
            {
                ["thinkingBudget"] = _generationThinkingBudget
            };
        }

        string response = string.Empty;
        List<GeminiErrorDetail> results = new();
        bool parsed = false;

        for (int attempt = 1; attempt <= _maxParseAttempts && !parsed; attempt++)
        {
            response = await GenerateContentWithRetryAsync(
                prompt,
                _errorGuidanceSystemInstruction,
                generationConfig,
                imagePayloads,
                cancellationToken,
                _structuredModel,
                antrianId,
                errors.Count,
                batchNumber,
                totalBatches);

            parsed = TryParseErrorGuidance(response, out results);
            if (!parsed)
            {
                _logger.LogWarning(
                    "Failed to parse guidance JSON (attempt {Attempt}/{Max}). Response preview: {Preview}",
                    attempt,
                    _maxParseAttempts,
                    BuildResponsePreview(response, 500));
                await LogLlmRequestAsync(
                    antrianId,
                    null,
                    BuildLlmLogMessage("parse_fail", errorCount: errors.Count, attempt: attempt, maxAttempts: _maxParseAttempts, batchNumber: batchNumber, totalBatches: totalBatches),
                    0,
                    cancellationToken,
                    batchNumber: batchNumber,
                    totalBatches: totalBatches,
                    errorCount: errors.Count);

                if (attempt < _maxParseAttempts)
                    await Task.Delay(_parseRetryDelay, cancellationToken);
            }
        }

        if (!parsed)
        {
            _logger.LogError("Failed to parse guidance JSON after {Max} attempts", _maxParseAttempts);
            results = new List<GeminiErrorDetail>();
        }

        NormalizeGuidanceIndices(errors.Count, results);
        ApplyGuardrails(enhancedErrors, results);
        LogGuidanceDiagnostics(results, response);
        await LogLlmRequestAsync(
            antrianId,
            null,
            BuildLlmLogMessage(parsed ? "parsed" : "parsed_fail", errorCount: errors.Count, parsedCount: results.Count, batchNumber: batchNumber, totalBatches: totalBatches),
            0,
            cancellationToken,
            batchNumber: batchNumber,
            totalBatches: totalBatches,
            errorCount: errors.Count);
        
        // Validate output indices match input
        ValidateErrorIndices(errors, results);

        // Store in cache
        if (results.Count == 0)
        {
            _logger.LogWarning("Skipping cache for empty Gemini guidance results (key: {Key})", cacheKey[..8]);
            return results;
        }

        _guidanceCache[cacheKey] = new CachedGuidance(results);
        
        return results;
    }

    private List<GeminiErrorDetail> BuildFallbackGuidance(
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<AturanDetail> activeRules)
    {
        var (enhancedErrors, _) = BuildEnhancedErrors(errors, activeRules);
        var results = new List<GeminiErrorDetail>(enhancedErrors.Count);

        foreach (var context in enhancedErrors)
        {
            var error = context.Error;
            results.Add(new GeminiErrorDetail
            {
                Index = context.Index,
                IsError = true,
                SkipReason = string.Empty,
                Title = BuildFallbackTitle(error),
                Explanation = BuildFallbackExplanation(error),
                Steps = BuildFallbackSteps(context),
                Location = BuildFallbackLocation(error)
            });
        }

        return results;
    }

    private static string BuildFallbackTitle(ValidationError error)
    {
        var title = !string.IsNullOrWhiteSpace(error.Message)
            ? error.Message
            : "Kesalahan format dokumen";
        return title.Length <= 100 ? title : title[..100];
    }

    private static string BuildFallbackExplanation(ValidationError error)
    {
        var message = string.IsNullOrWhiteSpace(error.Message)
            ? "Perlu perbaikan format."
            : error.Message;

        if (string.IsNullOrWhiteSpace(error.Expected) && string.IsNullOrWhiteSpace(error.Actual))
            return message;

        var expected = string.IsNullOrWhiteSpace(error.Expected) ? "-" : error.Expected;
        var actual = string.IsNullOrWhiteSpace(error.Actual) ? "-" : error.Actual;
        return $"{message} (expected: {expected}, actual: {actual})";
    }

    private static GeminiErrorLocation BuildFallbackLocation(ValidationError error)
    {
        var location = new GeminiErrorLocation
        {
            HalamanKe = 0,
            Section = "-"
        };

        if (error.Locations.Count > 0 && error.Locations[0] != null && error.Locations[0].HalamanKe > 0)
            location.HalamanKe = error.Locations[0].HalamanKe;

        if (error.SectionIndex.HasValue)
            location.Section = error.SectionIndex.Value.ToString(CultureInfo.InvariantCulture);

        return location;
    }

    private void LogGuidanceDiagnostics(List<GeminiErrorDetail> results, string response)
    {
        if (results.Count == 0)
        {
            _logger.LogWarning(
                "Gemini guidance parse returned 0 items. Response length: {Length}. Response preview: {Preview}",
                response.Length,
                BuildResponsePreview(response, 2000));
            return;
        }

        var missingExplanation = results.Count(r => string.IsNullOrWhiteSpace(r.Explanation));
        var missingSteps = results.Count(r => r.Steps == null || r.Steps.Count == 0);
        if (missingExplanation > 0 || missingSteps > 0)
        {
            _logger.LogWarning(
                "Gemini guidance missing fields. Items: {Count}, missing explanation: {MissingExplanation}, missing steps: {MissingSteps}",
                results.Count,
                missingExplanation,
                missingSteps);
        }
    }

    private static string GenerateCacheKey(IReadOnlyList<ValidationError> errors, uint? dokumenId)
    {
        // Create a signature based on error category, field, and message
        var signature = (dokumenId.HasValue ? dokumenId.Value.ToString() : "doc:none") + "|" +
            string.Join("|", errors.Select(e =>
                $"{e.Category}:{e.Field}:{e.Message}:{e.Expected}:{e.Actual}:{e.DokumenElemenId}:{e.SectionIndex}:{e.PageRange}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return Convert.ToHexString(bytes);
    }

    private static string BuildResponsePreview(string? response, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "(empty)";

        var compact = response.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (compact.Length <= maxLength)
            return compact;

        return compact[..maxLength] + "...";
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

    private void NormalizeGuidanceIndices(int errorCount, List<GeminiErrorDetail> results)
    {
        if (errorCount <= 0 || results.Count == 0)
            return;

        var validIndices = results
            .Select(r => r.Index)
            .Where(i => i >= 0 && i < errorCount)
            .ToList();

        if (validIndices.Count == 0 && results.Count <= errorCount)
        {
            for (var i = 0; i < results.Count; i++)
                results[i].Index = i;

            _logger.LogInformation("Assigned Gemini guidance indices by order (missing indices).");
            return;
        }

        var allOneBased = validIndices.Count == results.Count &&
                          validIndices.All(i => i >= 1 && i <= errorCount) &&
                          !validIndices.Contains(0);

        if (allOneBased)
        {
            foreach (var result in results)
                result.Index = result.Index - 1;

            _logger.LogInformation("Normalized Gemini guidance indices from 1-based to 0-based.");
            return;
        }

        var used = new HashSet<int>(validIndices);
        var next = 0;
        for (var i = 0; i < results.Count; i++)
        {
            if (results[i].Index >= 0 && results[i].Index < errorCount)
                continue;

            while (next < errorCount && used.Contains(next))
                next++;

            if (next >= errorCount)
                break;

            results[i].Index = next;
            used.Add(next);
        }
    }

    private async Task<string> GenerateContentWithRetryAsync(
        string prompt,
        string? systemInstruction,
        object generationConfig,
        IReadOnlyList<LlmImagePayload>? imagePayloads,
        CancellationToken cancellationToken = default,
        string? modelOverride = null,
        uint? antrianId = null,
        int? errorCount = null,
        int? batchNumber = null,
        int? totalBatches = null)
    {
        var currentModel = modelOverride ?? _analysisModel;
        Exception? lastException = null;

        for (int attempt = 0; attempt < _maxRetries; attempt++)
        {
            try
            {
                return await GenerateContentAsync(
                    prompt,
                    systemInstruction,
                    generationConfig,
                    imagePayloads,
                    cancellationToken,
                    currentModel,
                    antrianId,
                    errorCount,
                    batchNumber,
                    totalBatches,
                    attempt + 1,
                    _maxRetries);
            }
            catch (GeminiRateLimitException ex)
            {
                lastException = ex;
                var delay = ex.RetryAfter ?? _retryDelays[attempt];
                if (delay <= TimeSpan.Zero)
                    delay = _retryDelays[attempt];

                _logger.LogWarning(
                    "Gemini API rate limited (attempt {Attempt}/{Max}), retrying in {Delay}s",
                    attempt + 1,
                    _maxRetries,
                    delay.TotalSeconds);
                await LogLlmRequestAsync(
                    antrianId,
                    null,
                    BuildLlmLogMessage(
                        "retry_wait",
                        errorCount: errorCount,
                        retrySeconds: (int)Math.Ceiling(delay.TotalSeconds),
                        attempt: attempt + 1,
                        maxAttempts: _maxRetries,
                        model: currentModel,
                        batchNumber: batchNumber,
                        totalBatches: totalBatches),
                    (int)HttpStatusCode.TooManyRequests,
                    cancellationToken,
                    batchNumber: batchNumber,
                    totalBatches: totalBatches,
                    errorCount: errorCount);

                await Task.Delay(delay, cancellationToken);

                // On last retry, try fallback model
                if (_maxRetries > 1 && attempt == _maxRetries - 2)
                {
                    _logger.LogInformation("Switching to fallback model: {Model}", _fallbackModel);
                    await LogLlmRequestAsync(
                        antrianId,
                        null,
                        BuildLlmLogMessage(
                            "switch_model",
                            errorCount: errorCount,
                            model: _fallbackModel,
                            batchNumber: batchNumber,
                            totalBatches: totalBatches),
                        0,
                        cancellationToken,
                        batchNumber: batchNumber,
                        totalBatches: totalBatches,
                        errorCount: errorCount);
                    currentModel = _fallbackModel;
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("503") || ex.Message.Contains("ServiceUnavailable") || ex.Message.Contains("TooManyRequests"))
            {
                lastException = ex;
                _logger.LogWarning("Gemini API rate limited or unavailable (attempt {Attempt}/{Max}), retrying in {Delay}s", 
                    attempt + 1, _maxRetries, _retryDelays[attempt].TotalSeconds);
                await LogLlmRequestAsync(
                    antrianId,
                    null,
                    BuildLlmLogMessage(
                        "retry_wait",
                        errorCount: errorCount,
                        retrySeconds: (int)Math.Ceiling(_retryDelays[attempt].TotalSeconds),
                        attempt: attempt + 1,
                        maxAttempts: _maxRetries,
                        model: currentModel,
                        batchNumber: batchNumber,
                        totalBatches: totalBatches),
                    ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0,
                    cancellationToken,
                    batchNumber: batchNumber,
                    totalBatches: totalBatches,
                    errorCount: errorCount);
                
                await Task.Delay(_retryDelays[attempt], cancellationToken);
                
                // On last retry, try fallback model
                if (_maxRetries > 1 && attempt == _maxRetries - 2)
                {
                    _logger.LogInformation("Switching to fallback model: {Model}", _fallbackModel);
                    await LogLlmRequestAsync(
                        antrianId,
                        null,
                        BuildLlmLogMessage(
                            "switch_model",
                            errorCount: errorCount,
                            model: _fallbackModel,
                            batchNumber: batchNumber,
                            totalBatches: totalBatches),
                        0,
                        cancellationToken,
                        batchNumber: batchNumber,
                        totalBatches: totalBatches,
                        errorCount: errorCount);
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

    private void MarkKeyRateLimited(uint keyId, TimeSpan? retryAfter)
    {
        var delay = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
            ? retryAfter.Value
            : _defaultRateLimitDelay;
        RateLimitedKeys[keyId] = DateTimeOffset.UtcNow.Add(delay);
    }

    private static bool IsKeyRateLimited(uint keyId, DateTimeOffset now)
    {
        return RateLimitedKeys.TryGetValue(keyId, out var until) && until > now;
    }

    private static DateTimeOffset? GetRateLimitUntil(uint keyId)
    {
        return RateLimitedKeys.TryGetValue(keyId, out var until) ? until : null;
    }

    private static void PruneRateLimitedKeys(DateTimeOffset now)
    {
        foreach (var entry in RateLimitedKeys)
        {
            if (entry.Value <= now)
                RateLimitedKeys.TryRemove(entry.Key, out var removed);
        }
    }

    private static TimeSpan? TryGetRetryAfter(HttpResponseMessage response, string responseBody)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                if (int.TryParse(raw, out var seconds))
                    return TimeSpan.FromSeconds(seconds);

                if (DateTimeOffset.TryParse(raw, out var retryAt))
                {
                    var delay = retryAt - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                        return delay;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        var match = Regex.Match(responseBody, "\"retryDelay\"\\s*:\\s*\"?([0-9]+(\\.[0-9]+)?)s", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(responseBody, "\"retry_delay\"\\s*:\\s*\"?([0-9]+(\\.[0-9]+)?)s", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(responseBody, @"retry in ([0-9]+(\.[0-9]+)?)s", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var secondsFromBody))
            return null;

        return TimeSpan.FromSeconds(secondsFromBody);
    }

    /// <summary>
    /// Gets tokens used by a key in the last minute
    /// </summary>
    private static int GetKeyTokensUsedInLastMinute(uint keyId)
    {
        var cutoff = DateTimeOffset.UtcNow - TokenWindowDuration;
        if (!_keyTokenUsage.TryGetValue(keyId, out var usageList))
            return 0;

        lock (usageList)
        {
            // Remove expired entries
            usageList.RemoveAll(u => u.Time < cutoff);
            return usageList.Sum(u => u.Tokens);
        }
    }

    /// <summary>
    /// Records token usage for a key
    /// </summary>
    private static void RecordKeyTokenUsage(uint keyId, int tokens)
    {
        var usageList = _keyTokenUsage.GetOrAdd(keyId, _ => new List<(DateTimeOffset, int)>());
        lock (usageList)
        {
            usageList.Add((DateTimeOffset.UtcNow, tokens));
        }
    }

    /// <summary>
    /// Check if a key has exceeded token limit
    /// </summary>
    private bool IsKeyTokenLimited(uint keyId)
    {
        var used = GetKeyTokensUsedInLastMinute(keyId);
        var limited = used >= _tokensPerMinutePerKeyLimit;
        
        _logger.LogInformation(
            "[TOKEN-CHECK] Key {KeyId}: {Used}/{Limit} tokens in last 1min → {Status}",
            keyId, 
            used, 
            _tokensPerMinutePerKeyLimit,
            limited ? "EXCEEDED" : "OK");
        
        return limited;
    }

    /// <summary>
    /// Parse token usage from Gemini response
    /// </summary>
    private static int ParseTokenUsage(JsonElement result)
    {
        if (result.TryGetProperty("usageMetadata", out var metadata) &&
            metadata.TryGetProperty("totalTokenCount", out var totalTokens) &&
            totalTokens.TryGetInt32(out var tokens))
        {
            return tokens;
        }
        return 0;
    }

    private async Task<string> GenerateContentAsync(
        string prompt,
        string? systemInstruction,
        object generationConfig,
        IReadOnlyList<LlmImagePayload>? imagePayloads,
        CancellationToken cancellationToken = default,
        string? modelOverride = null,
        uint? antrianId = null,
        int? errorCount = null,
        int? batchNumber = null,
        int? totalBatches = null,
        int? attempt = null,
        int? maxAttempts = null)
    {
        var model = modelOverride ?? _analysisModel;
        var apiKey = await GetActiveApiKeyAsync(cancellationToken, batchNumber);
        
        _logger.LogInformation(
            "[SEND] Batch {Batch}/{Total}: model={Model}, key={KeyId}, errors={ErrorCount}",
            batchNumber ?? 0,
            totalBatches ?? 0,
            model, 
            apiKey.GeminiApiKeyId, 
            errorCount ?? 0);
        
        // Log send to database
        await LogLlmRequestAsync(
            antrianId,
            apiKey.GeminiApiKeyId,
            BuildLlmLogMessage(
                "send",
                errorCount: errorCount,
                attempt: attempt,
                maxAttempts: maxAttempts,
                model: model,
                batchNumber: batchNumber,
                totalBatches: totalBatches),
            0,
            cancellationToken,
            batchNumber: batchNumber,
            totalBatches: totalBatches,
            errorCount: errorCount);

        var endpoint = $"{_apiBaseUrl}/models/{model}:generateContent?key={apiKey.GeminiApiKeyValue}";

        var includeSystemInstruction = !string.IsNullOrWhiteSpace(systemInstruction) && SupportsSystemInstruction(model);
        var effectivePrompt = prompt;
        if (!includeSystemInstruction && !string.IsNullOrWhiteSpace(systemInstruction))
        {
            effectivePrompt = $"{systemInstruction}\n\n{prompt}";
            _logger.LogInformation("System instruction inlined into prompt for model: {Model}", model);
        }

        var parts = new List<object>
        {
            new { text = effectivePrompt }
        };

        if (imagePayloads != null && SupportsImages(model))
        {
            foreach (var image in imagePayloads)
            {
                if (string.IsNullOrWhiteSpace(image.Base64))
                    continue;

                parts.Add(new
                {
                    inlineData = new
                    {
                        mimeType = image.MimeType,
                        data = image.Base64
                    }
                });
            }
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = parts.ToArray()
                }
            },
            ["generationConfig"] = generationConfig
        };

        if (includeSystemInstruction)
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
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = TryGetRetryAfter(response, responseBody);
                    var retrySeconds = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
                        ? (int)Math.Ceiling(retryAfter.Value.TotalSeconds)
                        : (int?)null;
                    await LogLlmRequestAsync(
                        antrianId,
                        apiKey.GeminiApiKeyId,
                        BuildLlmLogMessage(
                            "429",
                            errorCount: errorCount,
                            responseLength: responseBody.Length,
                            retrySeconds: retrySeconds,
                            attempt: attempt,
                            maxAttempts: maxAttempts,
                            model: model,
                            batchNumber: batchNumber,
                            totalBatches: totalBatches),
                        (int)response.StatusCode,
                        cancellationToken,
                        batchNumber: batchNumber,
                        totalBatches: totalBatches,
                        errorCount: errorCount);
                    MarkKeyRateLimited(apiKey.GeminiApiKeyId, retryAfter);
                    var cooldown = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero ? retryAfter.Value : _defaultRateLimitDelay;
                    _logger.LogWarning(
                        "Gemini API key {KeyId} rate limited; cooling down for {Delay}s",
                        apiKey.GeminiApiKeyId,
                        cooldown.TotalSeconds);
                    throw new GeminiRateLimitException($"Gemini API error: {response.StatusCode}", retryAfter);
                }

                await LogLlmRequestAsync(
                    antrianId,
                    apiKey.GeminiApiKeyId,
                    BuildLlmLogMessage(
                        "error",
                        errorCount: errorCount,
                        responseLength: responseBody.Length,
                        attempt: attempt,
                        maxAttempts: maxAttempts,
                        model: model,
                        batchNumber: batchNumber,
                        totalBatches: totalBatches),
                    (int)response.StatusCode,
                    cancellationToken,
                    batchNumber: batchNumber,
                    totalBatches: totalBatches,
                    errorCount: errorCount);
                throw new HttpRequestException($"Gemini API error: {response.StatusCode}", null, response.StatusCode);
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            LogResponseMetadata(result);
            var responseParts = result
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts");

            var textBuilder = new StringBuilder();
            if (responseParts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in responseParts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object)
                        continue;

                    if (part.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        textBuilder.Append(textEl.GetString());
                }
            }

            var textContent = textBuilder.ToString();

            // Parse and record actual token usage
            var tokensUsed = ParseTokenUsage(result);
            var keyTokensUsed = 0;
            if (tokensUsed > 0)
            {
                RecordKeyTokenUsage(apiKey.GeminiApiKeyId, tokensUsed);
                keyTokensUsed = GetKeyTokensUsedInLastMinute(apiKey.GeminiApiKeyId);
                _logger.LogInformation(
                    "[RECV] Response: {Length} chars, {Tokens} tokens (key {KeyId}: {Used}/{Limit} TPM)",
                    textContent?.Length ?? 0,
                    tokensUsed,
                    apiKey.GeminiApiKeyId,
                    keyTokensUsed,
                    _tokensPerMinutePerKeyLimit);
            }
            else
            {
                _logger.LogInformation("[RECV] Response: {Length} chars (no token metadata)", textContent?.Length ?? 0);
            }

            // Log to database with token info
            await LogLlmRequestAsync(
                antrianId,
                apiKey.GeminiApiKeyId,
                BuildLlmLogMessage(
                    "recv",
                    errorCount: errorCount,
                    responseLength: responseBody.Length,
                    attempt: attempt,
                    maxAttempts: maxAttempts,
                    model: model,
                    batchNumber: batchNumber,
                    totalBatches: totalBatches),
                (int)response.StatusCode,
                cancellationToken,
                tokensUsed: tokensUsed > 0 ? tokensUsed : null,
                batchNumber: batchNumber,
                totalBatches: totalBatches,
                errorCount: errorCount,
                keyTokensUsed: keyTokensUsed > 0 ? keyTokensUsed : null);

            await IncrementUsageAsync(apiKey);
            return textContent ?? string.Empty;
        }
        catch (GeminiRateLimitException)
        {
            // Don't log as generic exception - let retry logic handle it
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Gemini API");
            await LogLlmRequestAsync(
                antrianId,
                apiKey.GeminiApiKeyId,
                BuildLlmLogMessage(
                    "exception",
                    errorCount: errorCount,
                    attempt: attempt,
                    maxAttempts: maxAttempts,
                    model: model,
                    batchNumber: batchNumber,
                    totalBatches: totalBatches),
                0,
                cancellationToken,
                batchNumber: batchNumber,
                totalBatches: totalBatches,
                errorCount: errorCount);
            throw;
        }
    }

    private async Task LogLlmRequestAsync(
        uint? antrianId, 
        uint? apiKeyId, 
        string message, 
        int? errorCode, 
        CancellationToken cancellationToken,
        int? tokensUsed = null,
        int? batchNumber = null,
        int? totalBatches = null,
        int? errorCount = null,
        int? keyTokensUsed = null)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message) ? "unknown" : message;
        if (safeMessage.Length > 50)
            safeMessage = safeMessage[..50];

        var log = new LlmApiLog
        {
            LogMessage = safeMessage,
            LogErrorCode = errorCode ?? 0,
            AntrianId = antrianId ?? 0u,
            ApiKeyId = apiKeyId ?? 0u,
            LogTokensUsed = tokensUsed ?? 0,
            LogBatchNumber = batchNumber ?? 0,
            LogTotalBatches = totalBatches ?? 0,
            LogErrorCount = errorCount ?? 0,
            LogKeyTokensUsed = keyTokensUsed ?? 0,
            LogCreatedAt = DateTime.Now
        };

        try
        {
            _db.LlmApiLogs.Add(log);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write LLM API log");
        }
    }

    private static string BuildLlmLogMessage(
        string status,
        int? errorCount = null,
        int? parsedCount = null,
        int? responseLength = null,
        int? retrySeconds = null,
        int? attempt = null,
        int? maxAttempts = null,
        string? model = null,
        int? batchNumber = null,
        int? totalBatches = null)
    {
        var builder = new StringBuilder(status);
        if (errorCount.HasValue)
            builder.Append(" e=").Append(errorCount.Value);
        if (parsedCount.HasValue)
            builder.Append(" p=").Append(parsedCount.Value);
        if (responseLength.HasValue)
            builder.Append(" len=").Append(responseLength.Value);
        if (retrySeconds.HasValue)
            builder.Append(" retry=").Append(retrySeconds.Value).Append('s');
        if (attempt.HasValue && maxAttempts.HasValue)
            builder.Append(" a=").Append(attempt.Value).Append('/').Append(maxAttempts.Value);
        if (batchNumber.HasValue && totalBatches.HasValue)
            builder.Append(" b=").Append(batchNumber.Value).Append('/').Append(totalBatches.Value);
        if (!string.IsNullOrWhiteSpace(model))
            builder.Append(" m=").Append(ShortenModelName(model, 16));

        var message = builder.ToString();
        return message.Length <= 50 ? message : message[..50];
    }

    private static string ShortenModelName(string model, int maxLength)
    {
        var name = model.Trim();
        var lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < name.Length - 1)
            name = name[(lastSlash + 1)..];

        if (name.Length <= maxLength)
            return name;

        return name[..maxLength];
    }

    private async Task<GeminiApiKey> GetActiveApiKeyAsync(CancellationToken cancellationToken = default, int? batchNumber = null)
    {
        var now = DateTimeOffset.UtcNow;
        PruneRateLimitedKeys(now);

        var candidates = await _db.GeminiApiKeys
            .Where(k => k.GeminiApiKeyStatus == 1)
            .OrderBy(k => k.GeminiApiKeyUsage ?? 0)
            .ThenBy(k => k.GeminiApiKeyId)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            throw new InvalidOperationException("Gemini API key not configured in database");

        // Filter out rate-limited and token-limited keys
        var available = candidates
            .Where(k => !IsKeyRateLimited(k.GeminiApiKeyId, now) && !IsKeyTokenLimited(k.GeminiApiKeyId))
            .ToList();

        if (available.Count > 0 && batchNumber.HasValue && batchNumber.Value > 0)
        {
            var ordered = available.OrderBy(k => k.GeminiApiKeyId).ToList();
            var index = (batchNumber.Value - 1) % ordered.Count;
            var selected = ordered[index];

            _logger.LogInformation(
                "[KEY-SELECT] Batch {Batch} -> key {KeyId} ({Used}/{Limit} tokens) from {Count} available",
                batchNumber.Value,
                selected.GeminiApiKeyId,
                GetKeyTokensUsedInLastMinute(selected.GeminiApiKeyId),
                _tokensPerMinutePerKeyLimit,
                ordered.Count);
            return selected;
        }

        if (available.Count > 0)
        {
            _logger.LogInformation(
                "[KEY-SELECT] Using key {KeyId} ({Used}/{Limit} tokens) - {AvailableCount} keys available",
                available[0].GeminiApiKeyId, 
                GetKeyTokensUsedInLastMinute(available[0].GeminiApiKeyId),
                _tokensPerMinutePerKeyLimit,
                available.Count);
            return available[0];
        }

        // All keys are limited - find the one that will be available soonest
        _logger.LogWarning("[KEY-SELECT] All {Count} keys are limited, checking alternatives...", candidates.Count);

        // First, try keys that are only token-limited (not 429 rate-limited)
        var tokenLimitedOnly = candidates
            .Where(k => !IsKeyRateLimited(k.GeminiApiKeyId, now) && IsKeyTokenLimited(k.GeminiApiKeyId))
            .ToList();

        if (tokenLimitedOnly.Count > 0)
        {
            // Wait for token window to reset (max 1 minute)
            _logger.LogWarning(
                "[WAIT] All keys are token-limited ({Count} keys). Waiting 60s for token window to reset...",
                tokenLimitedOnly.Count);
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            
            _logger.LogInformation("[WAIT] Wait complete. Using key {KeyId}", tokenLimitedOnly[0].GeminiApiKeyId);
            return tokenLimitedOnly[0];
        }

        // All keys are 429 rate-limited - wait for the first one to become available
        var fallback = candidates
            .Select(k => new { Key = k, Until = GetRateLimitUntil(k.GeminiApiKeyId) })
            .OrderBy(k => k.Until ?? DateTimeOffset.MinValue)
            .First();

        if (fallback.Until.HasValue && fallback.Until.Value > now)
        {
            var delay = fallback.Until.Value - now;
            _logger.LogWarning(
                "[WAIT] All keys 429 rate-limited. Waiting {Delay}s for key {KeyId} cooldown...",
                Math.Ceiling(delay.TotalSeconds),
                fallback.Key.GeminiApiKeyId);
            await Task.Delay(delay, cancellationToken);
            _logger.LogInformation("[WAIT] Cooldown complete. Using key {KeyId}", fallback.Key.GeminiApiKeyId);
        }

        return fallback.Key;
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

    private void LogResponseMetadata(JsonElement result)
    {
        var finishReasons = new List<string>();
        if (result.TryGetProperty("candidates", out var candidatesEl) && candidatesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidatesEl.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Object)
                    continue;

                if (candidate.TryGetProperty("finishReason", out var finishEl) && finishEl.ValueKind == JsonValueKind.String)
                    finishReasons.Add(finishEl.GetString() ?? string.Empty);
            }
        }

        var promptFeedback = "(none)";
        if (result.TryGetProperty("promptFeedback", out var promptFeedbackEl) && promptFeedbackEl.ValueKind != JsonValueKind.Null)
            promptFeedback = BuildResponsePreview(promptFeedbackEl.GetRawText(), 1000);

        var finishSummary = finishReasons.Count > 0 ? string.Join(", ", finishReasons) : "(none)";
        _logger.LogInformation("Gemini response metadata: finishReasons={FinishReasons}, promptFeedback={PromptFeedback}", finishSummary, promptFeedback);
    }

    private static bool SupportsSystemInstruction(string? model)
        => !IsGemmaModel(model);

    private static bool SupportsResponseSchema(string? model)
        => !IsGemmaModel(model);

    private static bool SupportsThinking(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        var name = model.Trim();
        var lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < name.Length - 1)
            name = name[(lastSlash + 1)..];

        return name.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsImages(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        return !IsGemmaModel(model);
    }

    private static bool IsGemmaModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        var name = model.Trim();
        var lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < name.Length - 1)
            name = name[(lastSlash + 1)..];

        return name.StartsWith("gemma-", StringComparison.OrdinalIgnoreCase);
    }
}

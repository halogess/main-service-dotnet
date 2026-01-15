using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        LogGuidanceDiagnostics(results, response);
        
        // Validate output indices match input
        ValidateErrorIndices(errors, results);
        
        // Store in cache
        _guidanceCache[cacheKey] = new CachedGuidance(results);
        
        return results;
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

    private static string GenerateCacheKey(IReadOnlyList<ValidationError> errors)
    {
        // Create a signature based on error category, field, and message
        var signature = string.Join("|", errors.Select(e => $"{e.Category}:{e.Field}:{e.Message}"));
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
}

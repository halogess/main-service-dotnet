using System.Text.Json;
using System.Text.Json.Serialization;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

/// <summary>
/// GeminiService partial - DTOs and nested model classes
/// </summary>
public partial class GeminiService
{
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

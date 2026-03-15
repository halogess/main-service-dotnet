using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = (args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("RULES_API_BASE_URL") ?? "http://localhost:5062").TrimEnd('/');
using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl + "/"),
    Timeout = TimeSpan.FromSeconds(60)
};

var before = await GetRulesAsync(httpClient);
var totalRules = before.data?.Count ?? 0;
var totalDetailsBefore = before.data?.Sum(rule => rule.details?.Count ?? 0) ?? 0;
var missingBefore = CountMissingHardConstraint(before.data);

Console.WriteLine($"API       : {baseUrl}");
Console.WriteLine($"Aturan    : {totalRules}");
Console.WriteLine($"Detail    : {totalDetailsBefore}");
Console.WriteLine($"Missing before backfill: {missingBefore}");

if (totalRules == 0)
{
    Console.WriteLine("Tidak ada aturan untuk dibackfill.");
    return;
}

foreach (var rule in before.data ?? [])
{
    var details = rule.details?
        .Where(detail => detail.aturan_detail_id > 0 && !string.IsNullOrWhiteSpace(detail.aturan_detail_json_value))
        .Select(detail => new RulesDetailPatchRequest
        {
            aturan_detail_id = detail.aturan_detail_id,
            aturan_detail_json_value = detail.aturan_detail_json_value
        })
        .ToList();

    if (details == null || details.Count == 0)
        continue;

    var response = await httpClient.PatchAsJsonAsync(
        $"api/rules/{rule.aturan_id}",
        new RulesPatchRequest { details = details });

    response.EnsureSuccessStatusCode();
    Console.WriteLine($"Backfilled aturan {rule.aturan_id} ({details.Count} detail).");
}

var after = await GetRulesAsync(httpClient);
var totalDetailsAfter = after.data?.Sum(rule => rule.details?.Count ?? 0) ?? 0;
var missingAfter = CountMissingHardConstraint(after.data);

Console.WriteLine($"Detail after backfill   : {totalDetailsAfter}");
Console.WriteLine($"Missing after backfill  : {missingAfter}");

if (missingAfter > 0)
    throw new InvalidOperationException("Backfill selesai tetapi masih ada detail tanpa is_hard_constraint.");

static async Task<RulesResponse> GetRulesAsync(HttpClient httpClient)
{
    var response = await httpClient.GetAsync("api/rules");
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadFromJsonAsync<RulesResponse>(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    return payload ?? new RulesResponse();
}

static int CountMissingHardConstraint(IEnumerable<RuleDto>? rules)
{
    return rules?
        .SelectMany(rule => rule.details ?? [])
        .Count(detail => !string.IsNullOrWhiteSpace(detail.aturan_detail_json_value) &&
                         !detail.aturan_detail_json_value.Contains("\"is_hard_constraint\"", StringComparison.Ordinal))
        ?? 0;
}

internal sealed class RulesResponse
{
    public List<RuleDto>? data { get; set; }
}

internal sealed class RuleDto
{
    public uint aturan_id { get; set; }
    public List<RuleDetailDto>? details { get; set; }
}

internal sealed class RuleDetailDto
{
    public uint aturan_detail_id { get; set; }
    public string? aturan_detail_json_value { get; set; }
}

internal sealed class RulesPatchRequest
{
    public List<RulesDetailPatchRequest> details { get; set; } = new();
}

internal sealed class RulesDetailPatchRequest
{
    public uint aturan_detail_id { get; set; }
    public string? aturan_detail_json_value { get; set; }
}

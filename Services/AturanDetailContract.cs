using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

internal static class AturanDetailContract
{
    private sealed record DetailContractInfo(string Key, string Kategori, string JsonValue, int Order);

    private static readonly Lazy<IReadOnlyDictionary<string, DetailContractInfo>> ContractByKey =
        new(BuildContractByKey);

    public static IReadOnlyList<string> CanonicalKeys =>
        ContractByKey.Value.Values
            .OrderBy(info => info.Order)
            .Select(info => info.Key)
            .ToArray();

    public static bool IsCanonicalKey(string? key)
    {
        return ContractByKey.Value.ContainsKey(NormalizeKey(key));
    }

    public static string NormalizeCanonicalKey(string? key)
    {
        var normalizedKey = NormalizeKey(key);
        if (!ContractByKey.Value.ContainsKey(normalizedKey))
            throw new InvalidOperationException($"Key aturan `{key}` bukan key canonical FE.");

        return normalizedKey;
    }

    public static string GetKategori(string? key)
    {
        var normalizedKey = NormalizeCanonicalKey(key);
        return ContractByKey.Value[normalizedKey].Kategori;
    }

    public static string GetDefaultJsonValue(string? key)
    {
        var normalizedKey = NormalizeCanonicalKey(key);
        return ContractByKey.Value[normalizedKey].JsonValue;
    }

    public static AturanDetail CreateDefaultDetail(uint aturanId, string key, bool appendTemplateNote = false)
    {
        var normalizedKey = NormalizeCanonicalKey(key);
        return AturanExportCatalog
            .CreateDefaultDetails(aturanId, appendTemplateNote)
            .First(detail => string.Equals(detail.AturanDetailKey, normalizedKey, StringComparison.OrdinalIgnoreCase));
    }

    public static List<AturanDetail> NormalizeDetailsForContract(IEnumerable<AturanDetail> details)
    {
        return details
            .Where(detail => AturanDetailVisibility.IsVisible(detail.AturanDetailKey))
            .Where(detail => IsCanonicalKey(detail.AturanDetailKey))
            .Select(ToContractDetail)
            .GroupBy(detail => NormalizeKey(detail.AturanDetailKey), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(detail => detail.AturanDetailId == 0 ? uint.MaxValue : detail.AturanDetailId)
                .First())
            .OrderBy(detail => ContractByKey.Value[NormalizeKey(detail.AturanDetailKey)].Order)
            .ThenBy(detail => detail.AturanDetailId == 0 ? uint.MaxValue : detail.AturanDetailId)
            .ToList();
    }

    public static IReadOnlyDictionary<string, AturanDetail> BuildDetailMap(IEnumerable<AturanDetail> details)
    {
        return NormalizeDetailsForContract(details)
            .ToDictionary(
                detail => NormalizeKey(detail.AturanDetailKey),
                detail => detail,
                StringComparer.OrdinalIgnoreCase);
    }

    private static AturanDetail ToContractDetail(AturanDetail detail)
    {
        var normalizedKey = NormalizeCanonicalKey(detail.AturanDetailKey);
        return new AturanDetail
        {
            AturanDetailId = detail.AturanDetailId,
            AturanId = detail.AturanId,
            AturanDetailKategori = ContractByKey.Value[normalizedKey].Kategori,
            AturanDetailKey = normalizedKey,
            AturanDetailJsonValue = AturanDetailCanonicalizer.CanonicalizeOrOriginal(normalizedKey, detail.AturanDetailJsonValue)
                ?? ContractByKey.Value[normalizedKey].JsonValue,
            AturanDetailCatatan = detail.AturanDetailCatatan
        };
    }

    private static IReadOnlyDictionary<string, DetailContractInfo> BuildContractByKey()
    {
        return AturanExportCatalog
            .CreateDefaultDetails(0)
            .Select((detail, index) => new DetailContractInfo(
                NormalizeKey(detail.AturanDetailKey),
                detail.AturanDetailKategori ?? string.Empty,
                detail.AturanDetailJsonValue ?? "{}",
                index))
            .ToDictionary(info => info.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}

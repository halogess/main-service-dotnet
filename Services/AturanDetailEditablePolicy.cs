using System.Text.Json.Nodes;

namespace ValidasiTugasAkhir.MainService.Services;

public static class AturanDetailEditablePolicy
{
    private const string ValueProperty = "value";
    private const string IsEditableProperty = "is_editable";
    private const string IsHardConstraintProperty = "is_hard_constraint";

    private static readonly IReadOnlySet<string> EmptyLockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> LockedPathsByDetailKey =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["page_settings"] = CreateLockedPathSet(),
            ["nomor_halaman"] = CreateLockedPathSet("numbering.number_format"),
            ["judul_bab"] = CreateLockedPathSet("numbering.number_format"),
            ["judul_subbab"] = CreateLockedPathSet(),
            ["paragraf"] = CreateLockedPathSet(),
            ["item_daftar"] = CreateLockedPathSet(),
            ["gambar"] = CreateLockedPathSet(
                "gambar.position.layout_option",
                "caption_gambar.numbering.number_format"),
            ["tabel"] = CreateLockedPathSet("caption_tabel.numbering.number_format"),
            ["kode"] = CreateLockedPathSet("judul_kode.numbering.number_format"),
            ["rumus"] = CreateLockedPathSet("numbering.number_format"),
            ["footnote"] = CreateLockedPathSet()
        };

    public static IReadOnlySet<string> GetLockedPaths(string? detailKey)
    {
        return LockedPathsByDetailKey.TryGetValue(NormalizeKey(detailKey), out var lockedPaths)
            ? lockedPaths
            : EmptyLockedPaths;
    }

    public static bool IsCanonicalRuleKey(string? detailKey)
    {
        return LockedPathsByDetailKey.ContainsKey(NormalizeKey(detailKey));
    }

    public static void Apply(string? detailKey, JsonNode? node)
    {
        if (!LockedPathsByDetailKey.TryGetValue(NormalizeKey(detailKey), out var lockedPaths))
            return;

        ApplyNode(node, lockedPaths, []);
    }

    private static void ApplyNode(JsonNode? node, IReadOnlySet<string> lockedPaths, IReadOnlyList<string> path)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
                ApplyNode(item, lockedPaths, path);
            return;
        }

        if (node is not JsonObject jsonObject)
            return;

        if (jsonObject.ContainsKey(IsEditableProperty))
            jsonObject[IsEditableProperty] = JsonValue.Create(!lockedPaths.Contains(string.Join('.', path)));

        foreach (var property in jsonObject.ToList())
        {
            if (property.Key.Equals(IsEditableProperty, StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals(IsHardConstraintProperty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ApplyNode(
                property.Value,
                lockedPaths,
                property.Key.Equals(ValueProperty, StringComparison.OrdinalIgnoreCase)
                    ? path
                    : AppendPath(path, property.Key));
        }
    }

    private static IReadOnlySet<string> CreateLockedPathSet(params string[] paths)
    {
        return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> AppendPath(IReadOnlyList<string> path, string segment)
    {
        if (path.Count == 0)
            return [segment];

        var result = new string[path.Count + 1];
        for (var index = 0; index < path.Count; index++)
            result[index] = path[index];
        result[^1] = segment;
        return result;
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}

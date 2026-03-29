using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

LoadDotEnvIfPresent();

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__KorektorBukuDbConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__KorektorBukuDbConnection is not set.");
    return 1;
}

var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
    .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 34)))
    .Options;

await using var db = new KorektorBukuDbContext(options);

const uint dokumenId = 852;
const uint partId = 6910;

var partInfo = await (from part in db.DokumenParts
    join section in db.DokumenSections on part.DsecId equals section.DsecId
    where part.DpartId == partId
    select new
    {
        part.DpartId,
        part.DpartType,
        section.DsecRefTipe,
        section.DsecRefId,
        section.DsecIndex
    }).FirstOrDefaultAsync();

Console.WriteLine("PART");
if (partInfo == null)
{
    Console.WriteLine("Part not found.");
    return 2;
}

Console.WriteLine(JsonSerializer.Serialize(partInfo, new JsonSerializerOptions { WriteIndented = true }));

var rows = await (from element in db.DokumenElemens
    join visual in db.DokumenElemenVisuals on element.DelemenId equals visual.DokumenElemenId into visuals
    from visual in visuals.DefaultIfEmpty()
    where element.DpartId == partId
    orderby element.DelemenSequence, visual.DevId
    select new
    {
        element.DelemenId,
        element.DelemenSequence,
        element.DelemenType,
        element.DelemenJsonTree,
        element.DelemenXml,
        VisualLabel = visual != null ? visual.DevLabel : null,
        VisualText = visual != null ? visual.DevText : null,
        visual.DevPage
    }).ToListAsync();

Console.WriteLine();
Console.WriteLine($"ELEMENT ROWS: {rows.Count}");

foreach (var row in rows)
{
    if (!LooksRelevant(row.DelemenJsonTree, row.DelemenXml, row.VisualLabel, row.VisualText))
        continue;

    Console.WriteLine();
    Console.WriteLine(new string('=', 80));
    Console.WriteLine($"elemen_id={row.DelemenId} seq={row.DelemenSequence} type={row.DelemenType} label={row.VisualLabel} page={row.DevPage}");
    if (!string.IsNullOrWhiteSpace(row.VisualText))
        Console.WriteLine($"visual_text={CollapseWhitespace(row.VisualText)}");

    DumpJson(row.DelemenJsonTree);
    DumpXml(row.DelemenXml);
}

return 0;

static bool LooksRelevant(string? json, string? xml, string? label, string? visualText)
{
    if (!string.IsNullOrWhiteSpace(label) &&
        (label.Contains("caption", StringComparison.OrdinalIgnoreCase) ||
         label.Contains("gambar", StringComparison.OrdinalIgnoreCase)))
    {
        return true;
    }

    foreach (var text in new[] { json, xml, visualText })
    {
        if (string.IsNullOrWhiteSpace(text))
            continue;

        if (text.Contains("Gambar", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("SEQ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("caption", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static void DumpJson(string? json)
{
    Console.WriteLine("-- JSON TREE");
    if (string.IsNullOrWhiteSpace(json))
    {
        Console.WriteLine("(empty)");
        return;
    }

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in content.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                    ? typeEl.GetString()
                    : null;
                var fieldType = item.TryGetProperty("field_type", out var fieldEl) && fieldEl.ValueKind == JsonValueKind.String
                    ? fieldEl.GetString()
                    : null;
                var value = item.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.String
                    ? valueEl.GetString()
                    : item.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
                        ? textEl.GetString()
                        : null;

                Console.WriteLine($"[{index}] type={type ?? "-"} field_type={fieldType ?? "-"} value={CollapseWhitespace(value)}");
                index++;
            }
        }
        else
        {
            Console.WriteLine(CollapseWhitespace(json));
        }
    }
    catch (JsonException)
    {
        Console.WriteLine(CollapseWhitespace(json));
    }
}

static void DumpXml(string? xml)
{
    Console.WriteLine("-- XML PREVIEW");
    Console.WriteLine(CollapseWhitespace(xml).PadRight(0)[..Math.Min(CollapseWhitespace(xml).Length, 1200)]);
}

static string CollapseWhitespace(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    var chars = value.Replace('\r', ' ').Replace('\n', ' ');
    return string.Join(" ", chars.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

static void LoadDotEnvIfPresent()
{
    var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
    if (!File.Exists(envPath))
        envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");

    if (!File.Exists(envPath))
        return;

    foreach (var rawLine in File.ReadLines(envPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            continue;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
            continue;

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        if (string.IsNullOrWhiteSpace(key))
            continue;

        if (Environment.GetEnvironmentVariable(key) == null)
            Environment.SetEnvironmentVariable(key, value);
    }
}

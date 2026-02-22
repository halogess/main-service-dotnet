using MySqlConnector;
using System.Text.Json;

const uint DokumenId = 602;
var cs = "Server=localhost;Port=3307;Database=db_korektor_buku;User=jessica;Password=pass123;TreatTinyAsBoolean=false";

await using var conn = new MySqlConnection(cs);
await conn.OpenAsync();
Console.WriteLine($"Connected. dokumen_id={DokumenId}");

var docInfo = await LoadDocInfo(conn, DokumenId);
Console.WriteLine($"Doc path: {docInfo}");

var visualCols = await LoadVisualColumns(conn);
var idColumn = ResolveVisualIdColumn(visualCols);
Console.WriteLine($"Visual id column: {idColumn ?? "(none)"}");

var bodyElems = await LoadBodyElements(conn, DokumenId);
Console.WriteLine($"Body elements: {bodyElems.Count}");

var visualById = await LoadVisualByElement(conn, idColumn, bodyElems.Select(e => e.Id));
Console.WriteLine($"Body elements with visual row: {visualById.Count}");

foreach (var e in bodyElems)
{
    if (visualById.TryGetValue(e.Id, out var v))
        e.VisualLabel = v.Label;
}

var tableCandidates = bodyElems
    .Where(e => IsTableType(e.Type) || IsTableLabel(e.VisualLabel))
    .ToList();

Console.WriteLine($"Table candidates (type=table OR label=tabel/table): {tableCandidates.Count}");

int tableWithVisual = 0, tableWithPage = 0, tableWithBbox = 0;
foreach (var t in tableCandidates)
{
    if (visualById.TryGetValue(t.Id, out var v))
    {
        tableWithVisual++;
        if (v.Page.HasValue && v.Page.Value > 0) tableWithPage++;
        if (v.HasBbox) tableWithBbox++;
    }
}

Console.WriteLine($"Table candidates with visual row: {tableWithVisual}");
Console.WriteLine($"Table candidates with dev_page: {tableWithPage}");
Console.WriteLine($"Table candidates with bbox: {tableWithBbox}");

Console.WriteLine("\nFirst 20 table candidates:");
foreach (var t in tableCandidates.Take(20))
{
    var hasVisual = visualById.TryGetValue(t.Id, out var v);
    Console.WriteLine($"- id={t.Id}, seq={t.Sequence}, type={t.Type}, label={t.VisualLabel ?? "(null)"}, visual={(hasVisual ? "yes" : "no")}, page={(hasVisual ? (v!.Page?.ToString() ?? "null") : "-")}, bbox={(hasVisual ? (v!.HasBbox ? "yes" : "no") : "-")}");
}

var kesalahanRows = await LoadKesalahanByDoc(conn, DokumenId);
Console.WriteLine($"\nKesalahan parents: {kesalahanRows.Count}");

var emptyLocRows = kesalahanRows
    .Where(k => AnalyzeLokasi(k.Lokasi, out _, out _) == "empty")
    .ToList();
Console.WriteLine($"Parents with empty lokasi: {emptyLocRows.Count}");

var byCategory = kesalahanRows
    .GroupBy(k => k.Category ?? string.Empty)
    .Select(g => new
    {
        Category = g.Key,
        Total = g.Count(),
        Empty = g.Count(x => AnalyzeLokasi(x.Lokasi, out _, out _) == "empty")
    })
    .OrderByDescending(x => x.Empty)
    .ThenByDescending(x => x.Total)
    .Take(10)
    .ToList();

Console.WriteLine("Top categories by empty lokasi:");
foreach (var c in byCategory)
    Console.WriteLine($"- {c.Category}: empty={c.Empty}, total={c.Total}");

var keywordStats = new[] { "tabel", "caption tabel", "kode", "gambar", "margin", "judul bab", "judul subbab" }
    .Select(k => new
    {
        Keyword = k,
        Count = kesalahanRows.Count(r => r.DetailTitles.Any(d => d.Contains(k, StringComparison.OrdinalIgnoreCase))),
        EmptyCount = kesalahanRows.Count(r =>
            AnalyzeLokasi(r.Lokasi, out _, out _) == "empty" &&
            r.DetailTitles.Any(d => d.Contains(k, StringComparison.OrdinalIgnoreCase)))
    })
    .ToList();

Console.WriteLine("Keyword counts in detail titles:");
foreach (var k in keywordStats)
    Console.WriteLine($"- {k.Keyword}: total={k.Count}, empty_lokasi={k.EmptyCount}");

var tableParents = kesalahanRows
    .Where(k => k.Category.Contains("isi", StringComparison.OrdinalIgnoreCase))
    .Where(k => k.DetailTitles.Any(d => d.Contains("tabel", StringComparison.OrdinalIgnoreCase) || d.Contains("caption tabel", StringComparison.OrdinalIgnoreCase)))
    .ToList();

Console.WriteLine($"Parents with table-related details: {tableParents.Count}");

int emptyLok = 0, nullBbox = 0, hasBboxLok = 0, mixed = 0, invalid = 0;
foreach (var p in tableParents)
{
    var state = AnalyzeLokasi(p.Lokasi, out var locCount, out var firstPage);
    switch (state)
    {
        case "empty": emptyLok++; break;
        case "null_bbox": nullBbox++; break;
        case "has_bbox": hasBboxLok++; break;
        case "mixed": mixed++; break;
        default: invalid++; break;
    }

    Console.WriteLine($"- kesalahan_id={p.Id}, state={state}, lokasi_count={locCount}, first_page={firstPage?.ToString() ?? "null"}, detail_count={p.DetailTitles.Count}");
    foreach (var d in p.DetailTitles.Take(3))
        Console.WriteLine($"  detail: {d}");
}

Console.WriteLine($"\nTable parent lokasi summary: empty={emptyLok}, null_bbox={nullBbox}, has_bbox={hasBboxLok}, mixed={mixed}, invalid={invalid}");

Console.WriteLine("\nSample empty-lokasi parents (top 10):");
foreach (var p in emptyLocRows.Take(10))
{
    var cat = string.IsNullOrWhiteSpace(p.Category) ? "(null)" : p.Category;
    Console.WriteLine($"- kesalahan_id={p.Id}, cat={cat}, detail_count={p.DetailTitles.Count}");
    foreach (var d in p.DetailTitles.Take(2))
        Console.WriteLine($"  detail: {d}");
}

static async Task<string?> LoadDocInfo(MySqlConnection conn, uint docId)
{
    await using var cmd = new MySqlCommand("SELECT dokumen_docx_path FROM dokumen WHERE dokumen_id=@id", conn);
    cmd.Parameters.AddWithValue("@id", docId);
    var obj = await cmd.ExecuteScalarAsync();
    return obj == null || obj == DBNull.Value ? null : obj.ToString();
}

static async Task<List<string>> LoadVisualColumns(MySqlConnection conn)
{
    var cols = new List<string>();
    await using var cmd = new MySqlCommand(@"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dokumen_elemen_visual'", conn);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        cols.Add(r.GetString(0));
    return cols;
}

static string? ResolveVisualIdColumn(List<string> columns)
{
    return columns.FirstOrDefault(c => c.Equals("delemen_id", StringComparison.OrdinalIgnoreCase))
        ?? columns.FirstOrDefault(c => c.EndsWith("delemen_id", StringComparison.OrdinalIgnoreCase))
        ?? columns.FirstOrDefault(c => c.Contains("elemen", StringComparison.OrdinalIgnoreCase) && c.Contains("id", StringComparison.OrdinalIgnoreCase));
}

static async Task<List<ElemRow>> LoadBodyElements(MySqlConnection conn, uint docId)
{
    var rows = new List<ElemRow>();
    const string sql = @"
SELECT e.delemen_id, e.delemen_sequence, e.delemen_type
FROM dokumen_elemen e
JOIN dokumen_part p ON p.dpart_id = e.dpart_id
JOIN dokumen_section s ON s.dsec_id = p.dsec_id
WHERE s.dokumen_id = @doc
  AND p.dpart_type = 'body'
ORDER BY s.dsec_index, e.delemen_sequence";

    await using var cmd = new MySqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@doc", docId);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        rows.Add(new ElemRow
        {
            Id = r.GetFieldValue<ulong>(0),
            Sequence = r.IsDBNull(1) ? 0 : r.GetInt32(1),
            Type = r.IsDBNull(2) ? null : r.GetString(2)
        });
    }

    return rows;
}

static async Task<Dictionary<ulong, VisualRow>> LoadVisualByElement(MySqlConnection conn, string? idColumn, IEnumerable<ulong> ids)
{
    var map = new Dictionary<ulong, VisualRow>();
    if (string.IsNullOrWhiteSpace(idColumn))
        return map;

    var idList = ids.Distinct().ToList();
    foreach (var chunk in idList.Chunk(500))
    {
        var joined = string.Join(",", chunk);
        var sql = $@"
SELECT `{idColumn}` AS vid,
       dev_label_struktural,
       dev_page,
       dev_bbox_x0,
       dev_bbox_y0,
       dev_bbox_x1,
       dev_bbox_y1
FROM dokumen_elemen_visual
WHERE `{idColumn}` IN ({joined})";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            if (r["vid"] == DBNull.Value)
                continue;

            var id = Convert.ToUInt64(r["vid"]);
            var page = r["dev_page"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["dev_page"]);
            var hasBbox = r["dev_bbox_x0"] != DBNull.Value &&
                          r["dev_bbox_y0"] != DBNull.Value &&
                          r["dev_bbox_x1"] != DBNull.Value &&
                          r["dev_bbox_y1"] != DBNull.Value;

            var label = r["dev_label_struktural"] == DBNull.Value ? null : r["dev_label_struktural"].ToString();

            if (!map.TryGetValue(id, out var existing))
            {
                map[id] = new VisualRow
                {
                    Label = label,
                    Page = page,
                    HasBbox = hasBbox
                };
            }
            else
            {
                // Merge multiple rows per element
                if (string.IsNullOrWhiteSpace(existing.Label) && !string.IsNullOrWhiteSpace(label))
                    existing.Label = label;
                if (!existing.Page.HasValue && page.HasValue)
                    existing.Page = page;
                existing.HasBbox = existing.HasBbox || hasBbox;
            }
        }
    }

    return map;
}

static async Task<List<KesalahanRow>> LoadKesalahanByDoc(MySqlConnection conn, uint docId)
{
    var rows = new List<KesalahanRow>();
    const string sql = @"
SELECT k.kesalahan_id, k.kesalahan_kategori, k.kesalahan_lokasi,
       kd.kesalahan_detail_judul
FROM kesalahan k
LEFT JOIN kesalahan_detail kd ON kd.kesalahan_id = k.kesalahan_id
WHERE k.kesalahan_ref_tipe='dokumen' AND k.kesalahan_ref_id=@doc
ORDER BY k.kesalahan_id DESC, kd.kesalahan_detail_id";

    var map = new Dictionary<uint, KesalahanRow>();
    await using var cmd = new MySqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@doc", docId);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var id = r.GetFieldValue<uint>(0);
        if (!map.TryGetValue(id, out var row))
        {
            row = new KesalahanRow
            {
                Id = id,
                Category = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                Lokasi = r.IsDBNull(2) ? null : r.GetString(2)
            };
            map[id] = row;
        }

        if (!r.IsDBNull(3))
            row.DetailTitles.Add(r.GetString(3));
    }

    rows.AddRange(map.Values.OrderByDescending(v => v.Id));
    return rows;
}

static bool IsTableType(string? t)
{
    if (string.IsNullOrWhiteSpace(t)) return false;
    return t.Equals("table", StringComparison.OrdinalIgnoreCase) || t.Equals("tabel", StringComparison.OrdinalIgnoreCase);
}

static bool IsTableLabel(string? l)
{
    if (string.IsNullOrWhiteSpace(l)) return false;
    var n = l.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    return n == "tabel" || n == "table";
}

static string AnalyzeLokasi(string? lokasi, out int locCount, out int? firstPage)
{
    locCount = 0;
    firstPage = null;
    if (string.IsNullOrWhiteSpace(lokasi))
        return "empty";

    try
    {
        using var doc = JsonDocument.Parse(lokasi);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return "invalid";

        var arr = doc.RootElement;
        locCount = arr.GetArrayLength();
        if (locCount == 0)
            return "empty";

        var hasObj = false;
        var hasNull = false;
        foreach (var loc in arr.EnumerateArray())
        {
            if (firstPage == null && loc.TryGetProperty("halaman_ke", out var hp) && hp.TryGetInt32(out var p))
                firstPage = p;

            if (!loc.TryGetProperty("bbox", out var b) || b.ValueKind == JsonValueKind.Null)
                hasNull = true;
            else if (b.ValueKind == JsonValueKind.Object)
                hasObj = true;
        }

        if (hasObj && hasNull) return "mixed";
        if (hasObj) return "has_bbox";
        return "null_bbox";
    }
    catch
    {
        return "invalid";
    }
}

sealed class ElemRow
{
    public ulong Id { get; set; }
    public int Sequence { get; set; }
    public string? Type { get; set; }
    public string? VisualLabel { get; set; }
}

sealed class VisualRow
{
    public string? Label { get; set; }
    public int? Page { get; set; }
    public bool HasBbox { get; set; }
}

sealed class KesalahanRow
{
    public uint Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Lokasi { get; set; }
    public List<string> DetailTitles { get; } = new();
}

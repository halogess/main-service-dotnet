using MySqlConnector;
using System.Text.Json;

const uint DokumenId = 601;
var cs = "Server=localhost;Port=3307;Database=db_korektor_buku;User=jessica;Password=pass123;TreatTinyAsBoolean=false";

await using var conn = new MySqlConnection(cs);
await conn.OpenAsync();
Console.WriteLine($"Connected. dokumen_id={DokumenId}");

var visualColumns = new List<string>();
await using (var cmd = new MySqlCommand(@"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dokumen_elemen_visual'", conn))
await using (var r = await cmd.ExecuteReaderAsync())
{
    while (await r.ReadAsync())
        visualColumns.Add(r.GetString(0));
}

var idColumn = visualColumns.FirstOrDefault(c => c.Equals("delemen_id", StringComparison.OrdinalIgnoreCase))
    ?? visualColumns.FirstOrDefault(c => c.EndsWith("delemen_id", StringComparison.OrdinalIgnoreCase))
    ?? visualColumns.FirstOrDefault(c => c.Contains("elemen", StringComparison.OrdinalIgnoreCase) && c.Contains("id", StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"Visual id column: {idColumn ?? "(none)"}");

if (string.IsNullOrWhiteSpace(idColumn))
{
    Console.WriteLine("Cannot continue without visual id column.");
    return;
}

// sections + body parts
var sections = new List<(uint dsecId, uint idx)>();
await using (var cmd = new MySqlCommand(@"
SELECT dsec_id, dsec_index
FROM dokumen_section
WHERE dokumen_id = @dok
ORDER BY dsec_index", conn))
{
    cmd.Parameters.AddWithValue("@dok", DokumenId);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        sections.Add((r.GetFieldValue<uint>(0), r.GetFieldValue<uint>(1)));
}

Console.WriteLine($"Sections: {sections.Count}");
foreach (var s in sections)
    Console.WriteLine($"- dsec_id={s.dsecId}, dsec_index={s.idx}");

var bodyParts = new List<(uint dpartId, uint dsecId)>();
await using (var cmd = new MySqlCommand(@"
SELECT dpart_id, dsec_id
FROM dokumen_part
WHERE dsec_id IN (SELECT dsec_id FROM dokumen_section WHERE dokumen_id = @dok)
  AND dpart_type = 'body'
ORDER BY dsec_id, dpart_id", conn))
{
    cmd.Parameters.AddWithValue("@dok", DokumenId);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        bodyParts.Add((r.GetFieldValue<uint>(0), r.GetFieldValue<uint>(1)));
}

Console.WriteLine($"Body parts: {bodyParts.Count}");
foreach (var bp in bodyParts)
    Console.WriteLine($"- dpart_id={bp.dpartId}, dsec_id={bp.dsecId}");

// count body elements + visual coverage
var allElementIds = new List<ulong>();
await using (var cmd = new MySqlCommand(@"
SELECT e.delemen_id
FROM dokumen_elemen e
JOIN dokumen_part p ON p.dpart_id = e.dpart_id
JOIN dokumen_section s ON s.dsec_id = p.dsec_id
WHERE s.dokumen_id = @dok
  AND p.dpart_type = 'body'", conn))
{
    cmd.Parameters.AddWithValue("@dok", DokumenId);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        allElementIds.Add(r.GetFieldValue<ulong>(0));
}

Console.WriteLine($"Body elements: {allElementIds.Count}");
if (allElementIds.Count > 0)
{
    var idList = string.Join(",", allElementIds);
    var sqlVisual = $"SELECT COUNT(*) total, SUM(CASE WHEN dev_page IS NOT NULL THEN 1 ELSE 0 END) with_page FROM dokumen_elemen_visual WHERE `{idColumn}` IN ({idList})";
    await using var cmd = new MySqlCommand(sqlVisual, conn);
    await using var r = await cmd.ExecuteReaderAsync();
    if (await r.ReadAsync())
    {
        var total = r.IsDBNull(0) ? 0 : Convert.ToInt32(r[0]);
        var withPage = r.IsDBNull(1) ? 0 : Convert.ToInt32(r[1]);
        Console.WriteLine($"Visual rows for body elements: total={total}, with_dev_page={withPage}");
    }
}

Console.WriteLine("\nSection anchor (first paragraph / first element) -> dev_page:");
foreach (var bp in bodyParts)
{
    ulong? anchor = null;

    await using (var cmd = new MySqlCommand(@"
SELECT delemen_id
FROM dokumen_elemen
WHERE dpart_id = @pid AND delemen_type = 'paragraph' AND delemen_json_tree IS NOT NULL
ORDER BY delemen_sequence, delemen_id
LIMIT 1", conn))
    {
        cmd.Parameters.AddWithValue("@pid", bp.dpartId);
        var val = await cmd.ExecuteScalarAsync();
        if (val != null && val != DBNull.Value)
            anchor = Convert.ToUInt64(val);
    }

    if (!anchor.HasValue)
    {
        await using var cmd = new MySqlCommand(@"
SELECT delemen_id
FROM dokumen_elemen
WHERE dpart_id = @pid AND delemen_json_tree IS NOT NULL
ORDER BY delemen_sequence, delemen_id
LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@pid", bp.dpartId);
        var val = await cmd.ExecuteScalarAsync();
        if (val != null && val != DBNull.Value)
            anchor = Convert.ToUInt64(val);
    }

    int? page = null;
    if (anchor.HasValue)
    {
        var sql = $"SELECT dev_page FROM dokumen_elemen_visual WHERE `{idColumn}` = @id AND dev_page IS NOT NULL ORDER BY dev_page LIMIT 1";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", anchor.Value);
        var val = await cmd.ExecuteScalarAsync();
        if (val != null && val != DBNull.Value)
            page = Convert.ToInt32(val);
    }

    Console.WriteLine($"- dsec_id={bp.dsecId}, dpart_id={bp.dpartId}, anchor={(anchor.HasValue ? anchor.Value.ToString() : "null")}, page={(page.HasValue ? page.Value.ToString() : "null")}");
}

Console.WriteLine("\nFirst element with visual page per body part:");
foreach (var bp in bodyParts)
{
    var sql = $@"
SELECT e.delemen_id, e.delemen_sequence, v.dev_page
FROM dokumen_elemen e
JOIN dokumen_elemen_visual v ON v.`{idColumn}` = e.delemen_id
WHERE e.dpart_id = @pid
  AND v.dev_page IS NOT NULL
ORDER BY e.delemen_sequence, e.delemen_id
LIMIT 1";

    await using var cmd = new MySqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@pid", bp.dpartId);
    await using var r = await cmd.ExecuteReaderAsync();
    if (await r.ReadAsync())
    {
        var elemId = r.GetFieldValue<ulong>(0);
        var seq = r.IsDBNull(1) ? 0 : r.GetInt32(1);
        var page = r.IsDBNull(2) ? 0 : r.GetInt32(2);
        Console.WriteLine($"- dsec_id={bp.dsecId}, dpart_id={bp.dpartId}, first_visual_elem={elemId}, seq={seq}, page={page}");
    }
    else
    {
        Console.WriteLine($"- dsec_id={bp.dsecId}, dpart_id={bp.dpartId}, first_visual_elem=null");
    }
}

// Existing page-settings parent errors
Console.WriteLine("\nStored page-settings parent errors:");
var pageRows = new List<(uint id, string? lokasi, int details)>();
await using (var cmd = new MySqlCommand(@"
SELECT k.kesalahan_id,
       k.kesalahan_lokasi,
       (SELECT COUNT(*) FROM kesalahan_detail kd WHERE kd.kesalahan_id = k.kesalahan_id) AS detail_count
FROM kesalahan k
WHERE k.kesalahan_ref_tipe = 'dokumen'
  AND k.kesalahan_ref_id = @dok
  AND LOWER(IFNULL(k.kesalahan_kategori,'')) = 'pengaturan halaman'
ORDER BY k.kesalahan_id DESC", conn))
{
    cmd.Parameters.AddWithValue("@dok", DokumenId);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        pageRows.Add((
            r.GetFieldValue<uint>(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? 0 : r.GetInt32(2)
        ));
    }
}

Console.WriteLine($"Count: {pageRows.Count}");
foreach (var row in pageRows.Take(5))
{
    var stat = AnalyzeLokasi(row.lokasi, out var firstPage, out var countLoc);
    Console.WriteLine($"- kesalahan_id={row.id}, details={row.details}, lokasi_count={countLoc}, first_page={firstPage?.ToString() ?? "null"}, state={stat}");
    if (!string.IsNullOrWhiteSpace(row.lokasi))
        Console.WriteLine($"  lokasi={Clip(row.lokasi, 200)}");
}

static string AnalyzeLokasi(string? lokasi, out int? firstPage, out int count)
{
    firstPage = null;
    count = 0;
    if (string.IsNullOrWhiteSpace(lokasi))
        return "empty";

    try
    {
        using var doc = JsonDocument.Parse(lokasi);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return "invalid";

        var arr = doc.RootElement;
        count = arr.GetArrayLength();
        if (count == 0)
            return "empty-array";

        var first = arr[0];
        if (first.TryGetProperty("halaman_ke", out var hp) && hp.TryGetInt32(out var p))
            firstPage = p;

        var hasObj = false;
        var hasNull = false;
        foreach (var loc in arr.EnumerateArray())
        {
            if (!loc.TryGetProperty("bbox", out var b) || b.ValueKind == JsonValueKind.Null)
                hasNull = true;
            else if (b.ValueKind == JsonValueKind.Object)
                hasObj = true;
        }

        if (hasObj && hasNull) return "mixed";
        if (hasObj) return "bbox";
        return "null-bbox";
    }
    catch
    {
        return "invalid";
    }
}

static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "...";

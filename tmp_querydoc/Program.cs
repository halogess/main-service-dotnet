using MySqlConnector;

var cs = "Server=localhost;Port=3307;Database=db_korektor_buku;User=jessica;Password=pass123;TreatTinyAsBoolean=false";
await using var conn = new MySqlConnection(cs);
await conn.OpenAsync();

var visualColumns = new List<string>();
await using (var cmd = new MySqlCommand(@"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'dokumen_elemen_visual'", conn))
await using (var r = await cmd.ExecuteReaderAsync())
{
    while (await r.ReadAsync())
        visualColumns.Add(r.GetString(0));
}

var visualIdCol = visualColumns.FirstOrDefault(c => c.Equals("delemen_id", StringComparison.OrdinalIgnoreCase))
    ?? visualColumns.FirstOrDefault(c => c.EndsWith("delemen_id", StringComparison.OrdinalIgnoreCase))
    ?? visualColumns.FirstOrDefault(c => c.Contains("elemen", StringComparison.OrdinalIgnoreCase) && c.Contains("id", StringComparison.OrdinalIgnoreCase));

if (string.IsNullOrWhiteSpace(visualIdCol))
{
    Console.WriteLine("Could not resolve visual element id column.");
    return;
}

Console.WriteLine($"visual id column: {visualIdCol}");

const string markerTitle = "Segmen Program 5.4";
const string markerLine1 = "isDrawing = true;";
const string markerLine2 = "GameObject lineObj = new GameObject(\"DrawnLine\")";

await using var targetCmd = new MySqlCommand($@"
SELECT DISTINCT s.dokumen_id
FROM dokumen_elemen e
JOIN dokumen_part p ON p.dpart_id = e.dpart_id
JOIN dokumen_section s ON s.dsec_id = p.dsec_id
WHERE p.dpart_type='body'
  AND (
    e.delemen_xml LIKE CONCAT('%', @title, '%')
    OR e.delemen_xml LIKE CONCAT('%', @line1, '%')
    OR e.delemen_xml LIKE CONCAT('%', @line2, '%')
  )
ORDER BY s.dokumen_id DESC", conn);
targetCmd.Parameters.AddWithValue("@title", markerTitle);
targetCmd.Parameters.AddWithValue("@line1", markerLine1);
targetCmd.Parameters.AddWithValue("@line2", markerLine2);

var docIds = new List<int>();
await using (var r = await targetCmd.ExecuteReaderAsync())
{
    while (await r.ReadAsync())
        docIds.Add(r.GetInt32(0));
}

Console.WriteLine("Candidate dokumen_id:");
foreach (var id in docIds)
{
    await using var pCmd = new MySqlCommand("SELECT dokumen_docx_path FROM dokumen WHERE dokumen_id=@id", conn);
    pCmd.Parameters.AddWithValue("@id", id);
    var path = (await pCmd.ExecuteScalarAsync())?.ToString() ?? "(null)";
    Console.WriteLine($"- {id}: {path}");
}

foreach (var docId in docIds.Take(3))
{
    Console.WriteLine($"\n=== Detail dokumen {docId} ===");

    await using var q = new MySqlCommand($@"
SELECT e.delemen_id, e.delemen_sequence, e.delemen_type,
       JSON_UNQUOTE(JSON_EXTRACT(e.delemen_json_tree, '$.plain_text')) AS plain_text,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(e.delemen_json_tree, '$.dfp_id')) AS UNSIGNED) AS dfp_id,
       v.dev_page, v.dev_label_struktural
FROM dokumen_elemen e
JOIN dokumen_part p ON p.dpart_id = e.dpart_id
JOIN dokumen_section s ON s.dsec_id = p.dsec_id
LEFT JOIN dokumen_elemen_visual v ON v.`{visualIdCol}` = e.delemen_id
WHERE s.dokumen_id=@docId
  AND p.dpart_type='body'
  AND (
    e.delemen_xml LIKE CONCAT('%', @title, '%')
    OR e.delemen_xml LIKE CONCAT('%', @line1, '%')
    OR e.delemen_xml LIKE CONCAT('%', @line2, '%')
  )
ORDER BY e.delemen_sequence", conn);
    q.Parameters.AddWithValue("@docId", docId);
    q.Parameters.AddWithValue("@title", markerTitle);
    q.Parameters.AddWithValue("@line1", markerLine1);
    q.Parameters.AddWithValue("@line2", markerLine2);

    var hitRows = new List<(ulong Id, int Seq, string? Type, string? Plain, uint? Dfp, int? Page, string? Label)>();
    await using (var r = await q.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            hitRows.Add((
                Convert.ToUInt64(r["delemen_id"]),
                r["delemen_sequence"] == DBNull.Value ? 0 : Convert.ToInt32(r["delemen_sequence"]),
                r["delemen_type"] == DBNull.Value ? null : r["delemen_type"].ToString(),
                r["plain_text"] == DBNull.Value ? null : r["plain_text"].ToString(),
                r["dfp_id"] == DBNull.Value ? null : Convert.ToUInt32(r["dfp_id"]),
                r["dev_page"] == DBNull.Value ? null : Convert.ToInt32(r["dev_page"]),
                r["dev_label_struktural"] == DBNull.Value ? null : r["dev_label_struktural"].ToString()
            ));
        }
    }

    foreach (var row in hitRows)
    {
        var text = (row.Plain ?? string.Empty).Trim();
        Console.WriteLine($"- id={row.Id} seq={row.Seq} page={row.Page} label={row.Label} type={row.Type} dfp={row.Dfp} text={text}");
    }

    var interestingIds = hitRows.Select(h => h.Id).Distinct().ToList();
    if (interestingIds.Count == 0)
        continue;

    var seqMin = hitRows.Min(h => h.Seq) - 5;
    var seqMax = hitRows.Max(h => h.Seq) + 8;

    await using var around = new MySqlCommand($@"
SELECT e.delemen_id, e.delemen_sequence,
       JSON_UNQUOTE(JSON_EXTRACT(e.delemen_json_tree, '$.plain_text')) AS plain_text,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(e.delemen_json_tree, '$.dfp_id')) AS UNSIGNED) AS dfp_id,
       v.dev_page, v.dev_label_struktural,
       v.dev_bbox_x0, v.dev_bbox_y0, v.dev_bbox_x1, v.dev_bbox_y1
FROM dokumen_elemen e
JOIN dokumen_part p ON p.dpart_id = e.dpart_id
JOIN dokumen_section s ON s.dsec_id = p.dsec_id
LEFT JOIN dokumen_elemen_visual v ON v.`{visualIdCol}` = e.delemen_id
WHERE s.dokumen_id=@docId
  AND p.dpart_type='body'
  AND e.delemen_sequence BETWEEN @seqMin AND @seqMax
ORDER BY e.delemen_sequence", conn);
    around.Parameters.AddWithValue("@docId", docId);
    around.Parameters.AddWithValue("@seqMin", seqMin);
    around.Parameters.AddWithValue("@seqMax", seqMax);

    var dfpIds = new HashSet<uint>();
    var aroundRows = new List<(ulong Id, int Seq, string? Text, uint? Dfp, int? Page, string? Label, bool HasBbox)>();
    await using (var r = await around.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            var dfp = r["dfp_id"] == DBNull.Value ? (uint?)null : Convert.ToUInt32(r["dfp_id"]);
            if (dfp.HasValue) dfpIds.Add(dfp.Value);
            aroundRows.Add((
                Convert.ToUInt64(r["delemen_id"]),
                Convert.ToInt32(r["delemen_sequence"]),
                r["plain_text"] == DBNull.Value ? null : r["plain_text"].ToString(),
                dfp,
                r["dev_page"] == DBNull.Value ? null : Convert.ToInt32(r["dev_page"]),
                r["dev_label_struktural"] == DBNull.Value ? null : r["dev_label_struktural"].ToString(),
                r["dev_bbox_x0"] != DBNull.Value &&
                r["dev_bbox_y0"] != DBNull.Value &&
                r["dev_bbox_x1"] != DBNull.Value &&
                r["dev_bbox_y1"] != DBNull.Value
            ));
        }
    }

    var pfMap = new Dictionary<uint, (uint Left, uint Start, uint Hanging, bool IsList, uint NumId)>();
    if (dfpIds.Count > 0)
    {
        var joined = string.Join(",", dfpIds);
        await using var pfCmd = new MySqlCommand($@"
SELECT dfp_id, COALESCE(dfp_ind_left_twips,0), COALESCE(dfp_ind_start_twips,0), COALESCE(dfp_ind_hanging_twips,0),
       COALESCE(dfp_is_list,0), COALESCE(dfp_list_numId,0)
FROM dokumen_format_paragraf
WHERE dfp_id IN ({joined})", conn);
        await using var r = await pfCmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = Convert.ToUInt32(r[0]);
            pfMap[id] = (
                Convert.ToUInt32(r[1]),
                Convert.ToUInt32(r[2]),
                Convert.ToUInt32(r[3]),
                Convert.ToBoolean(r[4]),
                Convert.ToUInt32(r[5]));
        }
    }

    Console.WriteLine("Around target:");
    foreach (var row in aroundRows)
    {
        var text = (row.Text ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (text.Length > 80) text = text[..80] + "...";
        string pfInfo = "pf=(none)";
        if (row.Dfp.HasValue && pfMap.TryGetValue(row.Dfp.Value, out var pf))
        {
            var left = pf.Left != 0 ? pf.Left : pf.Start;
            var hangCm = pf.Hanging / 1440.0m * 2.54m;
            pfInfo = $"pf={row.Dfp} leftTw={left} hangTw={pf.Hanging} hangCm={hangCm:F2} isList={pf.IsList} numId={pf.NumId}";
        }

        Console.WriteLine($"  seq={row.Seq} id={row.Id} page={row.Page} label={row.Label} bbox={(row.HasBbox ? "yes" : "no")} {pfInfo} text={text}");
    }
}

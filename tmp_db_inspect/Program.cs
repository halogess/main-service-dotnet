using System.Globalization;
using System.Text;
using System.Text.Json;
using MySqlConnector;

const int DokumenId = 588;
const int DpartId = 5426;

var connectionString = "Server=localhost;Port=3307;Database=db_korektor_buku;User=jessica;Password=pass123;TreatTinyAsBoolean=false";

await using var conn = new MySqlConnection(connectionString);
await conn.OpenAsync();

Console.WriteLine($"Connected. Doc={DokumenId}, Part={DpartId}");
Console.WriteLine();

await PrintPartInfoAsync(conn, DokumenId, DpartId);
var rule = await LoadListItemRuleAsync(conn);
Console.WriteLine($"Rule item_daftar: left_indent={Fmt(rule.LeftIndentCm)} cm, hanging={Fmt(rule.HangingCm)} cm");
Console.WriteLine();

var labels = await LoadLabelsAsync(conn, DpartId);
var elements = await LoadPartElementsAsync(conn, DpartId);
var dfpIds = elements
    .Select(e => e.DfpId)
    .Where(id => id.HasValue)
    .Select(id => id!.Value)
    .Distinct()
    .ToList();

var formats = await LoadParagraphFormatsAsync(conn, dfpIds);

Console.WriteLine($"Elements in part: {elements.Count}");
Console.WriteLine($"Elements with dfp_id: {elements.Count(e => e.DfpId.HasValue)}");
Console.WriteLine();

var mismatches = new List<ElementDebug>();

foreach (var e in elements)
{
    var label = labels.TryGetValue(e.DelemenId, out var l) ? NormalizeLabel(l) : string.Empty;
    var isListLabel = IsListLabel(label);

    if (!e.DfpId.HasValue || !formats.TryGetValue(e.DfpId.Value, out var pf))
        continue;

    var isListByType = IsListItemElement(e.DelemenType);
    var isListByFormat = pf.DfpIsList;
    var isListCandidate = isListLabel && (isListByType || isListByFormat);

    if (!isListCandidate)
        continue;

    var level = TryParseListItemLevel(e.DelemenType, pf.DfpListIlvl, label);
    var levelValue = level ?? 0;

    var hangingTwips = pf.DfpIndHangingTwips ?? 0;
    var hangingCm = TwipsToCm(hangingTwips);

    var leftTwips = pf.DfpIndLeftTwips.HasValue && pf.DfpIndLeftTwips.Value != 0
        ? pf.DfpIndLeftTwips.Value
        : pf.DfpIndStartTwips ?? 0;
    var leftCm = TwipsToCm(leftTwips);
    var alignedLeftCm = rule.HangingCm.HasValue ? leftCm - hangingCm : leftCm;

    var expectedLeftCm = rule.LeftIndentCm ?? 0m;
    if (rule.HangingCm.HasValue && levelValue > 0)
        expectedLeftCm += levelValue * rule.HangingCm.Value;

    var hangingMismatch = rule.HangingCm.HasValue && Math.Abs(hangingCm - rule.HangingCm.Value) > 0.05m;
    var leftMismatch = rule.LeftIndentCm.HasValue && Math.Abs(alignedLeftCm - expectedLeftCm) > 0.05m;

    if (hangingMismatch || leftMismatch)
    {
        mismatches.Add(new ElementDebug
        {
            DelemenId = e.DelemenId,
            Sequence = e.Sequence,
            Type = e.DelemenType,
            Label = label,
            TextPreview = e.TextPreview,
            DfpId = e.DfpId.Value,
            Level = levelValue,
            DfpIsList = pf.DfpIsList,
            DfpListNumId = pf.DfpListNumId,
            DfpListIlvl = pf.DfpListIlvl,
            LeftTwips = leftTwips,
            HangingTwips = hangingTwips,
            LeftCm = leftCm,
            HangingCm = hangingCm,
            AlignedLeftCm = alignedLeftCm,
            ExpectedLeftCm = expectedLeftCm,
            ExpectedHangingCm = rule.HangingCm,
            IsHangingMismatch = hangingMismatch,
            IsLeftMismatch = leftMismatch
        });
    }
}

Console.WriteLine($"List item mismatches in part {DpartId}: {mismatches.Count}");
Console.WriteLine();

foreach (var m in mismatches.Take(20))
{
    Console.WriteLine($"elemen={m.DelemenId} seq={m.Sequence} dfp={m.DfpId} level={m.Level} type={m.Type} label={m.Label}");
    Console.WriteLine($"  text   : {m.TextPreview}");
    Console.WriteLine($"  raw    : left={m.LeftTwips} tw ({m.LeftCm:F2} cm), hanging={m.HangingTwips} tw ({m.HangingCm:F2} cm)");
    Console.WriteLine($"  check  : expected hanging={Fmt(m.ExpectedHangingCm)} cm, actual={m.HangingCm:F2} cm, mismatch={m.IsHangingMismatch}");
    Console.WriteLine($"  check  : expected left={m.ExpectedLeftCm:F2} cm, actual(aligned)={m.AlignedLeftCm:F2} cm, mismatch={m.IsLeftMismatch}");
    Console.WriteLine();
}

var exactPattern = mismatches
    .Where(m => Math.Abs(m.HangingCm - 1.27m) <= 0.01m && Math.Abs(m.AlignedLeftCm + 0.64m) <= 0.01m)
    .ToList();

Console.WriteLine($"Pattern hanging=1.27 & aligned_left=-0.64: {exactPattern.Count}");
foreach (var m in exactPattern.Take(10))
{
    Console.WriteLine($"  elemen={m.DelemenId} seq={m.Sequence} dfp={m.DfpId} text={m.TextPreview}");
}

if (exactPattern.Count > 0)
{
    var sample = exactPattern[0];
    Console.WriteLine();
    Console.WriteLine($"Sample raw detail for elemen={sample.DelemenId}, dfp={sample.DfpId}:");
    await PrintRawDetailAsync(conn, sample.DelemenId, sample.DfpId);
}

static decimal TwipsToCm(uint twips) => twips / 1440.0m * 2.54m;

static string NormalizeLabel(string? label)
{
    if (string.IsNullOrWhiteSpace(label))
        return string.Empty;
    return label.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
}

static bool IsListLabel(string? label)
{
    var normalized = NormalizeLabel(label);
    return normalized.StartsWith("list_level_", StringComparison.OrdinalIgnoreCase);
}

static bool IsListItemElement(string? elementType)
{
    if (string.IsNullOrWhiteSpace(elementType))
        return false;
    return elementType.StartsWith("list-item-", StringComparison.OrdinalIgnoreCase);
}

static int? TryParseListItemLevel(string? elementType, uint? ilvl, string? label)
{
    var labelLevel = TryParseListLabelLevel(label);
    if (labelLevel.HasValue)
        return labelLevel.Value;

    if (!string.IsNullOrWhiteSpace(elementType))
    {
        var parts = elementType.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level >= 0)
            return level;
    }

    if (ilvl.HasValue)
        return (int)ilvl.Value;

    return null;
}

static int? TryParseListLabelLevel(string? label)
{
    var normalized = NormalizeLabel(label);
    if (!normalized.StartsWith("list_level_", StringComparison.OrdinalIgnoreCase))
        return null;

    var suffix = normalized["list_level_".Length..];
    if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level > 0)
        return level - 1;

    return null;
}

static string Fmt(decimal? value) => value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : "null";

static async Task PrintPartInfoAsync(MySqlConnection conn, int dokumenId, int dpartId)
{
    const string sql = @"
SELECT p.dpart_id, p.dpart_type, p.dsec_id, s.dokumen_id, s.dsec_index
       , d.dokumen_docx_path
FROM dokumen_part p
JOIN dokumen_section s ON s.dsec_id = p.dsec_id
JOIN dokumen d ON d.dokumen_id = s.dokumen_id
WHERE p.dpart_id = @partId";

    await using var cmd = new MySqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@partId", dpartId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        var partDoc = Convert.ToInt32(reader["dokumen_id"]);
        Console.WriteLine($"Part info: dpart={reader["dpart_id"]}, type={reader["dpart_type"]}, dsec={reader["dsec_id"]}, dsec_index={reader["dsec_index"]}, dokumen={partDoc}");
        Console.WriteLine($"Part belongs to requested doc: {partDoc == dokumenId}");
        Console.WriteLine($"Docx path: {reader["dokumen_docx_path"]}");
    }
    else
    {
        Console.WriteLine("Part info: not found");
    }
}

static async Task<ListItemRule> LoadListItemRuleAsync(MySqlConnection conn)
{
    const string sql = @"
SELECT ad.aturan_detail_json_value
FROM aturan a
JOIN aturan_detail ad ON ad.aturan_id = a.aturan_id
WHERE a.aturan_status = 1
  AND ad.aturan_detail_status = 1
  AND ad.aturan_detail_kategori = 'Isi Buku'
  AND ad.aturan_detail_key = 'item_daftar'
ORDER BY a.aturan_created_at DESC
LIMIT 1";

    await using var cmd = new MySqlCommand(sql, conn);
    var jsonObj = await cmd.ExecuteScalarAsync();

    var result = new ListItemRule();
    if (jsonObj == null || jsonObj == DBNull.Value)
        return result;

    var json = jsonObj.ToString();
    if (string.IsNullOrWhiteSpace(json))
        return result;

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    if (root.TryGetProperty("paragraph", out var paragraph) && paragraph.ValueKind == JsonValueKind.Object)
    {
        if (paragraph.TryGetProperty("indentation", out var indentation) && indentation.ValueKind == JsonValueKind.Object)
        {
            if (indentation.TryGetProperty("left_indent", out var leftIndent) && leftIndent.ValueKind == JsonValueKind.Object)
            {
                if (leftIndent.TryGetProperty("value", out var value) && value.TryGetDecimal(out var d))
                    result.LeftIndentCm = d;
            }

            if (indentation.TryGetProperty("hanging", out var hanging) && hanging.ValueKind == JsonValueKind.Object)
            {
                if (hanging.TryGetProperty("value", out var value) && value.TryGetDecimal(out var d))
                    result.HangingCm = d;
            }
        }
    }

    return result;
}

static async Task<Dictionary<ulong, string>> LoadLabelsAsync(MySqlConnection conn, int dpartId)
{
    var ids = new List<ulong>();
    const string idsSql = "SELECT delemen_id FROM dokumen_elemen WHERE dpart_id = @partId";
    await using (var idsCmd = new MySqlCommand(idsSql, conn))
    {
        idsCmd.Parameters.AddWithValue("@partId", dpartId);
        await using var reader = await idsCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(Convert.ToUInt64(reader[0]));
    }

    var labels = new Dictionary<ulong, string>();
    if (ids.Count == 0)
        return labels;

    var columns = new List<string>();
    const string colSql = @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dokumen_elemen_visual'";
    await using (var colCmd = new MySqlCommand(colSql, conn))
    await using (var colReader = await colCmd.ExecuteReaderAsync())
    {
        while (await colReader.ReadAsync())
            columns.Add(colReader.GetString(0));
    }

    var idColumn = columns.FirstOrDefault(c => c.Equals("delemen_id", StringComparison.OrdinalIgnoreCase))
        ?? columns.FirstOrDefault(c => c.EndsWith("delemen_id", StringComparison.OrdinalIgnoreCase))
        ?? columns.FirstOrDefault(c => c.Contains("elemen", StringComparison.OrdinalIgnoreCase) && c.Contains("id", StringComparison.OrdinalIgnoreCase));

    var labelColumn = columns.FirstOrDefault(c => c.Equals("dev_label_struktural", StringComparison.OrdinalIgnoreCase));

    if (string.IsNullOrWhiteSpace(idColumn) || string.IsNullOrWhiteSpace(labelColumn))
        return labels;

    foreach (var chunk in ids.Chunk(300))
    {
        var idList = string.Join(",", chunk);
        var sql = $"SELECT `{idColumn}` AS delemen_id, `{labelColumn}` AS label FROM dokumen_elemen_visual WHERE `{idColumn}` IN ({idList})";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader["delemen_id"] == DBNull.Value || reader["label"] == DBNull.Value)
                continue;

            var id = Convert.ToUInt64(reader["delemen_id"]);
            var label = reader["label"].ToString();
            if (!string.IsNullOrWhiteSpace(label))
                labels[id] = label!;
        }
    }

    return labels;
}

static async Task<List<ElementRow>> LoadPartElementsAsync(MySqlConnection conn, int dpartId)
{
    var result = new List<ElementRow>();
    const string sql = @"
SELECT delemen_id, delemen_sequence, delemen_type, delemen_json_tree
FROM dokumen_elemen
WHERE dpart_id = @partId
ORDER BY delemen_sequence";

    await using var cmd = new MySqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@partId", dpartId);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var id = Convert.ToUInt64(reader["delemen_id"]);
        var seq = reader["delemen_sequence"] == DBNull.Value ? 0U : Convert.ToUInt32(reader["delemen_sequence"]);
        var type = reader["delemen_type"] == DBNull.Value ? null : reader["delemen_type"].ToString();
        var json = reader["delemen_json_tree"] == DBNull.Value ? null : reader["delemen_json_tree"].ToString();

        uint? dfpId = null;
        var textPreview = string.Empty;

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("dfp_id", out var dfpEl) && dfpEl.TryGetUInt32(out var d))
                    dfpId = d;

                textPreview = BuildPreview(root);
            }
            catch
            {
                // ignore
            }
        }

        result.Add(new ElementRow
        {
            DelemenId = id,
            Sequence = seq,
            DelemenType = type,
            DfpId = dfpId,
            TextPreview = textPreview
        });
    }

    return result;
}

static string BuildPreview(JsonElement root)
{
    var sb = new StringBuilder();

    if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (!item.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                continue;

            var type = typeEl.GetString();
            if ((type == "text" || type == "field") && item.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.String)
            {
                sb.Append(valueEl.GetString());
            }
            else if (type == "math" && item.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                sb.Append(textEl.GetString());
            }
        }
    }
    else if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
    {
        sb.Append(text.GetString());
    }

    var s = (sb.ToString() ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
    if (s.Length > 90)
        s = s[..90] + "...";
    return s;
}

static async Task<Dictionary<uint, ParagraphFormatRow>> LoadParagraphFormatsAsync(MySqlConnection conn, List<uint> ids)
{
    var map = new Dictionary<uint, ParagraphFormatRow>();
    if (ids.Count == 0)
        return map;

    foreach (var chunk in ids.Chunk(500))
    {
        var idList = string.Join(",", chunk);
        var sql = $@"
SELECT dfp_id, dfp_is_list, dfp_list_numId, dfp_list_ilvl,
       dfp_ind_left_twips, dfp_ind_start_twips, dfp_ind_hanging_twips, dfp_ind_first_line_twips
FROM dokumen_format_paragraf
WHERE dfp_id IN ({idList})";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new ParagraphFormatRow
            {
                DfpId = Convert.ToUInt32(reader["dfp_id"]),
                DfpIsList = reader["dfp_is_list"] != DBNull.Value && Convert.ToBoolean(reader["dfp_is_list"]),
                DfpListNumId = reader["dfp_list_numId"] == DBNull.Value ? null : Convert.ToUInt32(reader["dfp_list_numId"]),
                DfpListIlvl = reader["dfp_list_ilvl"] == DBNull.Value ? null : Convert.ToUInt32(reader["dfp_list_ilvl"]),
                DfpIndLeftTwips = reader["dfp_ind_left_twips"] == DBNull.Value ? null : Convert.ToUInt32(reader["dfp_ind_left_twips"]),
                DfpIndStartTwips = reader["dfp_ind_start_twips"] == DBNull.Value ? null : Convert.ToUInt32(reader["dfp_ind_start_twips"]),
                DfpIndHangingTwips = reader["dfp_ind_hanging_twips"] == DBNull.Value ? null : Convert.ToUInt32(reader["dfp_ind_hanging_twips"]),
                DfpIndFirstLineTwips = reader["dfp_ind_first_line_twips"] == DBNull.Value ? null : Convert.ToUInt32(reader["dfp_ind_first_line_twips"])
            };

            map[row.DfpId] = row;
        }
    }

    return map;
}

static async Task PrintRawDetailAsync(MySqlConnection conn, ulong elemenId, uint dfpId)
{
    const string formatSql = @"
SELECT dfp_ind_left_twips, dfp_ind_start_twips, dfp_ind_hanging_twips,
       dfp_numpr_json, dfp_tabs_json, dfp_raw_ppr_xml
FROM dokumen_format_paragraf
WHERE dfp_id = @dfpId";

    await using (var cmd = new MySqlCommand(formatSql, conn))
    {
        cmd.Parameters.AddWithValue("@dfpId", dfpId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            Console.WriteLine($"  db format: left={reader["dfp_ind_left_twips"]}, start={reader["dfp_ind_start_twips"]}, hanging={reader["dfp_ind_hanging_twips"]}");
            Console.WriteLine($"  db numpr : {TrimForLog(reader["dfp_numpr_json"]?.ToString(), 300)}");
            Console.WriteLine($"  db tabs  : {TrimForLog(reader["dfp_tabs_json"]?.ToString(), 300)}");
            Console.WriteLine($"  raw pPr  : {TrimForLog(reader["dfp_raw_ppr_xml"]?.ToString(), 500)}");
        }
    }

    const string elementSql = "SELECT delemen_type, delemen_xml FROM dokumen_elemen WHERE delemen_id = @id";
    await using (var cmd = new MySqlCommand(elementSql, conn))
    {
        cmd.Parameters.AddWithValue("@id", elemenId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            Console.WriteLine($"  raw elem type: {reader["delemen_type"]}");
            Console.WriteLine($"  raw elem xml : {TrimForLog(reader["delemen_xml"]?.ToString(), 500)}");
        }
    }
}

static string TrimForLog(string? s, int max)
{
    if (string.IsNullOrWhiteSpace(s))
        return "(null)";
    var cleaned = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return cleaned.Length <= max ? cleaned : cleaned[..max] + "...";
}

internal sealed class ListItemRule
{
    public decimal? LeftIndentCm { get; set; }
    public decimal? HangingCm { get; set; }
}

internal sealed class ElementRow
{
    public ulong DelemenId { get; set; }
    public uint Sequence { get; set; }
    public string? DelemenType { get; set; }
    public uint? DfpId { get; set; }
    public string TextPreview { get; set; } = string.Empty;
}

internal sealed class ParagraphFormatRow
{
    public uint DfpId { get; set; }
    public bool DfpIsList { get; set; }
    public uint? DfpListNumId { get; set; }
    public uint? DfpListIlvl { get; set; }
    public uint? DfpIndLeftTwips { get; set; }
    public uint? DfpIndStartTwips { get; set; }
    public uint? DfpIndHangingTwips { get; set; }
    public uint? DfpIndFirstLineTwips { get; set; }
}

internal sealed class ElementDebug
{
    public ulong DelemenId { get; set; }
    public uint Sequence { get; set; }
    public string? Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public string TextPreview { get; set; } = string.Empty;
    public uint DfpId { get; set; }
    public int Level { get; set; }
    public bool DfpIsList { get; set; }
    public uint? DfpListNumId { get; set; }
    public uint? DfpListIlvl { get; set; }
    public uint LeftTwips { get; set; }
    public uint HangingTwips { get; set; }
    public decimal LeftCm { get; set; }
    public decimal HangingCm { get; set; }
    public decimal AlignedLeftCm { get; set; }
    public decimal ExpectedLeftCm { get; set; }
    public decimal? ExpectedHangingCm { get; set; }
    public bool IsHangingMismatch { get; set; }
    public bool IsLeftMismatch { get; set; }
}

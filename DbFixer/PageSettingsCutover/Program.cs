using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MySqlConnector;

const string PengaturanHalaman = "Pengaturan Halaman";
const string NomorHalaman = "Nomor Halaman";
const string PageSettingsKey = "page_settings";

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = false
};

var connectionString = (
    args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("KOREKTOR_BUKU_CONNECTION_STRING")
    ?? "Server=localhost;Port=3307;Database=korektor_buku;User=root;Password=root;").Trim();

using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();

Console.WriteLine($"Connected to DB: {connection.Database}");

var allDetails = await LoadDetailsAsync(connection);
var groupedByAturan = allDetails
    .GroupBy(detail => detail.AturanId)
    .OrderBy(group => group.Key)
    .ToList();

Console.WriteLine($"Aturan ditemukan: {groupedByAturan.Count}");

foreach (var aturanGroup in groupedByAturan)
{
    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        var details = aturanGroup.ToList();
        var pageSettingsDetail = details.FirstOrDefault(detail =>
            detail.Status == 1 &&
            Matches(detail.Kategori, PengaturanHalaman) &&
            Matches(detail.Key, PageSettingsKey));

        var legacyPageDetails = details
            .Where(detail =>
                detail.Status == 1 &&
                Matches(detail.Kategori, PengaturanHalaman) &&
                detail.Key is "paper" or "margin" or "header_footer" or "gutter" or "column")
            .ToList();

        var numberingDetails = details
            .Where(detail =>
                detail.Status == 1 &&
                Matches(detail.Kategori, NomorHalaman) &&
                detail.Key is "nomor_halaman_awal" or "nomor_halaman_isi" or "nomor_halaman_akhir" or "nomor_halaman_lampiran")
            .ToList();

        if (pageSettingsDetail == null && legacyPageDetails.Count == 0)
        {
            await transaction.CommitAsync();
            Console.WriteLine($"Aturan {aturanGroup.Key}: tidak ada row page settings yang perlu diproses.");
            continue;
        }

        var legacyPaper = ParseDetailObject(legacyPageDetails, "paper");
        var legacyMargin = ParseDetailObject(legacyPageDetails, "margin");
        var legacyHeaderFooter = ParseDetailObject(legacyPageDetails, "header_footer");
        var legacyGutter = ParseDetailObject(legacyPageDetails, "gutter");
        var legacyColumn = ParseDetailObject(legacyPageDetails, "column");
        var existingPageSettings = ParseJsonObject(pageSettingsDetail?.JsonValue);

        var pageSettingsJson = new JsonObject
        {
            ["paper"] = BuildPaperNode(legacyPaper, existingPageSettings),
            ["margin"] = BuildMarginNode(legacyMargin, existingPageSettings),
            ["header_footer"] = BuildHeaderFooterNode(legacyHeaderFooter, existingPageSettings),
            ["gutter"] = BuildGutterNode(legacyGutter, existingPageSettings),
            ["column"] = BuildColumnNode(legacyColumn, existingPageSettings)
        }.ToJsonString(jsonOptions);

        if (pageSettingsDetail == null)
        {
            await InsertPageSettingsAsync(connection, transaction, aturanGroup.Key, pageSettingsJson);
        }
        else
        {
            await UpdateJsonValueAsync(connection, transaction, pageSettingsDetail.Id, pageSettingsJson);
        }

        var differentOddEvenWrapper = ResolveDifferentOddEvenWrapper(legacyHeaderFooter, numberingDetails);
        foreach (var numberingDetail in numberingDetails)
        {
            var numberingJson = ParseJsonObject(numberingDetail.JsonValue) ?? new JsonObject();
            numberingJson["different_odd_even"] = differentOddEvenWrapper.DeepClone();
            await UpdateJsonValueAsync(connection, transaction, numberingDetail.Id, numberingJson.ToJsonString(jsonOptions));
        }

        if (legacyPageDetails.Count > 0)
        {
            await DeleteLegacyPageSettingsAsync(connection, transaction, legacyPageDetails.Select(detail => detail.Id).ToList());
        }

        await transaction.CommitAsync();
        Console.WriteLine($"Aturan {aturanGroup.Key}: page_settings upserted, numbering updated ({numberingDetails.Count}), legacy deleted ({legacyPageDetails.Count}).");
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Aturan {aturanGroup.Key}: gagal - {ex.Message}");
        throw;
    }
}

Console.WriteLine("Cutover selesai.");

static bool Matches(string? value, string expected)
{
    return string.Equals((value ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase);
}

static async Task<List<AturanDetailRow>> LoadDetailsAsync(MySqlConnection connection)
{
    const string sql = """
        SELECT aturan_detail_id,
               aturan_id,
               aturan_detail_kategori,
               aturan_detail_key,
               aturan_detail_json_value,
               aturan_detail_status,
               aturan_detail_catatan
        FROM aturan_detail
        ORDER BY aturan_id, aturan_detail_id
        """;

    using var command = new MySqlCommand(sql, connection);
    using var reader = await command.ExecuteReaderAsync();

    var rows = new List<AturanDetailRow>();
    while (await reader.ReadAsync())
    {
        rows.Add(new AturanDetailRow(
            Id: reader.GetUInt32("aturan_detail_id"),
            AturanId: reader.GetUInt32("aturan_id"),
            Kategori: reader.IsDBNull(reader.GetOrdinal("aturan_detail_kategori")) ? null : reader.GetString(reader.GetOrdinal("aturan_detail_kategori")),
            Key: reader.IsDBNull(reader.GetOrdinal("aturan_detail_key")) ? null : reader.GetString(reader.GetOrdinal("aturan_detail_key")),
            JsonValue: reader.IsDBNull(reader.GetOrdinal("aturan_detail_json_value")) ? null : reader.GetString(reader.GetOrdinal("aturan_detail_json_value")),
            Status: reader.GetSByte("aturan_detail_status"),
            Catatan: reader.IsDBNull(reader.GetOrdinal("aturan_detail_catatan")) ? null : reader.GetString(reader.GetOrdinal("aturan_detail_catatan"))));
    }

    return rows;
}

static JsonObject BuildPaperNode(JsonObject? legacyPaper, JsonObject? existingPageSettings)
{
    if (TryGetPropertyObject(legacyPaper, "section", out var sectionObject))
    {
        foreach (var sectionKey in new[] { "isi", "awal", "akhir", "lampiran" })
        {
            if (!TryGetWrapper(sectionObject, sectionKey, out var sectionWrapper))
                continue;

            var items = sectionWrapper.Value as JsonArray;
            var firstItem = items?.OfType<JsonObject>().FirstOrDefault();
            if (firstItem == null)
                continue;

            return new JsonObject
            {
                ["size"] = WrapValue(
                    CreateStringValue(firstItem["size"], "A4"),
                    sectionWrapper.IsEditable,
                    sectionWrapper.IsHardConstraint),
                ["orientation"] = WrapValue(
                    CreateStringValue(firstItem["orientation"], "PORTRAIT"),
                    sectionWrapper.IsEditable,
                    sectionWrapper.IsHardConstraint)
            };
        }
    }

    if (TryGetPropertyObject(existingPageSettings, "paper", out var existingPaper))
        return existingPaper.DeepClone().AsObject();

    return new JsonObject
    {
        ["size"] = WrapValue(JsonValue.Create("A4"), true, false),
        ["orientation"] = WrapValue(JsonValue.Create("PORTRAIT"), true, false)
    };
}

static JsonObject BuildMarginNode(JsonObject? legacyMargin, JsonObject? existingPageSettings)
{
    if (TryGetPropertyObject(legacyMargin, "paper", out var marginPaper))
    {
        foreach (var key in new[] { "a4_portrait", "a4_landscape", "a3_landscape" })
        {
            if (!TryGetWrapper(marginPaper, key, out var marginWrapper))
                continue;

            var marginObject = marginWrapper.Value as JsonObject;
            if (marginObject == null)
                continue;

            return CreateMarginObjectFromLegacy(marginObject, marginWrapper.IsEditable, marginWrapper.IsHardConstraint);
        }
    }

    if (TryGetPropertyObject(existingPageSettings, "margin", out var existingMargin))
        return existingMargin.DeepClone().AsObject();

    return CreateMarginObjectFromValues(4m, 3m, 4m, 3m, true, false);
}

static JsonObject CreateMarginObjectFromLegacy(JsonObject marginObject, bool isEditable, bool isHardConstraint)
{
    var top = ReadDecimal(marginObject["top"], 4m);
    var bottom = ReadDecimal(marginObject["bottom"], 3m);
    var left = ReadDecimal(marginObject["left"], 4m);
    var right = ReadDecimal(marginObject["right"], 3m);
    return CreateMarginObjectFromValues(top, bottom, left, right, isEditable, isHardConstraint);
}

static JsonObject CreateMarginObjectFromValues(
    decimal top,
    decimal bottom,
    decimal left,
    decimal right,
    bool isEditable,
    bool isHardConstraint)
{
    return new JsonObject
    {
        ["top"] = WrapValue(JsonValue.Create(top), isEditable, isHardConstraint),
        ["bottom"] = WrapValue(JsonValue.Create(bottom), isEditable, isHardConstraint),
        ["left"] = WrapValue(JsonValue.Create(left), isEditable, isHardConstraint),
        ["right"] = WrapValue(JsonValue.Create(right), isEditable, isHardConstraint)
    };
}

static JsonObject BuildHeaderFooterNode(JsonObject? legacyHeaderFooter, JsonObject? existingPageSettings)
{
    var source = legacyHeaderFooter;
    if (source == null && TryGetPropertyObject(existingPageSettings, "header_footer", out var existingHeaderFooter))
    {
        source = existingHeaderFooter;
    }

    var headerWrapper = GetWrapperOrDefault(source, "header_from_top", 2.5m);
    var footerWrapper = GetWrapperOrDefault(source, "footer_from_bottom", 1.5m);

    return new JsonObject
    {
        ["header_from_top"] = headerWrapper.Node.DeepClone(),
        ["footer_from_bottom"] = footerWrapper.Node.DeepClone()
    };
}

static JsonObject BuildGutterNode(JsonObject? legacyGutter, JsonObject? existingPageSettings)
{
    JsonNode sizeNode = WrapValue(JsonValue.Create(0m), true, false);
    JsonNode positionNode = WrapValue(JsonValue.Create("left"), true, false);

    if (TryGetWrapper(legacyGutter, "gutter", out var legacyGutterWrapper) ||
        TryGetWrapper(legacyGutter, "size", out legacyGutterWrapper))
    {
        var gutterSize = ReadDecimal(legacyGutterWrapper.Value, 0m);
        sizeNode = WrapValue(JsonValue.Create(gutterSize), legacyGutterWrapper.IsEditable, legacyGutterWrapper.IsHardConstraint);
    }
    else if (TryGetPropertyObject(existingPageSettings, "gutter", out var existingGutter) &&
             TryGetWrapper(existingGutter, "size", out var existingSizeWrapper))
    {
        sizeNode = existingSizeWrapper.Node.DeepClone();
    }

    if (TryGetWrapper(legacyGutter, "position", out var legacyPositionWrapper))
    {
        positionNode = WrapValue(
            JsonValue.Create(ReadString(legacyPositionWrapper.Value, "left")),
            legacyPositionWrapper.IsEditable,
            legacyPositionWrapper.IsHardConstraint);
    }
    else if (TryGetPropertyObject(existingPageSettings, "gutter", out var currentGutter) &&
             TryGetWrapper(currentGutter, "position", out var currentPositionWrapper))
    {
        positionNode = currentPositionWrapper.Node.DeepClone();
    }

    return new JsonObject
    {
        ["size"] = sizeNode,
        ["position"] = positionNode
    };
}

static JsonNode BuildColumnNode(JsonObject? legacyColumn, JsonObject? existingPageSettings)
{
    if (TryGetWrapper(legacyColumn, "count", out var countWrapper))
    {
        return WrapValue(
            JsonValue.Create(ReadInt(countWrapper.Value, 1)),
            countWrapper.IsEditable,
            countWrapper.IsHardConstraint);
    }

    if (IsWrapperObject(legacyColumn))
        return legacyColumn!.DeepClone();

    if (existingPageSettings?["column"] is JsonObject existingColumn && IsWrapperObject(existingColumn))
        return existingColumn.DeepClone();

    return WrapValue(JsonValue.Create(1), true, false);
}

static JsonNode ResolveDifferentOddEvenWrapper(JsonObject? legacyHeaderFooter, IReadOnlyList<AturanDetailRow> numberingDetails)
{
    if (TryGetWrapper(legacyHeaderFooter, "different_odd_even", out var legacyDifferentOddEven))
    {
        return WrapValue(
            JsonValue.Create(ReadBool(legacyDifferentOddEven.Value, false)),
            legacyDifferentOddEven.IsEditable,
            legacyDifferentOddEven.IsHardConstraint);
    }

    foreach (var numberingDetail in numberingDetails)
    {
        var numberingObject = ParseJsonObject(numberingDetail.JsonValue);
        if (TryGetWrapper(numberingObject, "different_odd_even", out var numberingWrapper))
            return numberingWrapper.Node.DeepClone();
    }

    return WrapValue(JsonValue.Create(false), true, false);
}

static WrapperNode GetWrapperOrDefault(JsonObject? parent, string propertyName, decimal defaultValue)
{
    if (TryGetWrapper(parent, propertyName, out var wrapper))
        return wrapper;

    return new WrapperNode(WrapValue(JsonValue.Create(defaultValue), true, false), JsonValue.Create(defaultValue), true, false);
}

static JsonObject? ParseDetailObject(IEnumerable<AturanDetailRow> details, string key)
{
    var detail = details.FirstOrDefault(row => Matches(row.Key, key));
    return ParseJsonObject(detail?.JsonValue);
}

static JsonObject? ParseJsonObject(string? json)
{
    if (string.IsNullOrWhiteSpace(json))
        return null;

    try
    {
        return JsonNode.Parse(json) as JsonObject;
    }
    catch (JsonException)
    {
        return null;
    }
}

static bool TryGetPropertyObject(JsonObject? parent, string propertyName, out JsonObject child)
{
    child = null!;
    if (parent == null)
        return false;

    if (parent[propertyName] is JsonObject childObject)
    {
        child = childObject;
        return true;
    }

    return false;
}

static bool TryGetWrapper(JsonObject? parent, string propertyName, out WrapperNode wrapper)
{
    wrapper = default;
    if (parent == null || parent[propertyName] is not JsonObject node || !IsWrapperObject(node))
        return false;

    wrapper = new WrapperNode(
        Node: node,
        Value: node["value"],
        IsEditable: ReadBool(node["is_editable"], true),
        IsHardConstraint: ReadBool(node["is_hard_constraint"], false));
    return true;
}

static bool IsWrapperObject(JsonObject? node)
{
    return node != null && node.ContainsKey("value");
}

static JsonObject WrapValue(JsonNode? value, bool isEditable, bool isHardConstraint)
{
    return new JsonObject
    {
        ["value"] = value?.DeepClone(),
        ["is_editable"] = JsonValue.Create(isEditable),
        ["is_hard_constraint"] = JsonValue.Create(isHardConstraint)
    };
}

static JsonNode CreateStringValue(JsonNode? valueNode, string fallback)
{
    return JsonValue.Create(ReadString(valueNode, fallback))!;
}

static string ReadString(JsonNode? node, string fallback)
{
    if (node is JsonValue jsonValue)
    {
        if (jsonValue.TryGetValue<string>(out var stringValue) && !string.IsNullOrWhiteSpace(stringValue))
            return stringValue.Trim();
    }

    return fallback;
}

static decimal ReadDecimal(JsonNode? node, decimal fallback)
{
    if (node is JsonValue jsonValue)
    {
        if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            return decimalValue;

        if (jsonValue.TryGetValue<double>(out var doubleValue))
            return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);

        if (jsonValue.TryGetValue<int>(out var intValue))
            return intValue;

        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            var normalized = (stringValue ?? string.Empty).Trim().Replace(',', '.');
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
    }

    return fallback;
}

static int ReadInt(JsonNode? node, int fallback)
{
    if (node is JsonValue jsonValue)
    {
        if (jsonValue.TryGetValue<int>(out var intValue))
            return intValue;

        if (jsonValue.TryGetValue<long>(out var longValue) && longValue <= int.MaxValue && longValue >= int.MinValue)
            return (int)longValue;

        if (jsonValue.TryGetValue<string>(out var stringValue) && int.TryParse(stringValue, out var parsed))
            return parsed;
    }

    return fallback;
}

static bool ReadBool(JsonNode? node, bool fallback)
{
    if (node is JsonValue jsonValue)
    {
        if (jsonValue.TryGetValue<bool>(out var boolValue))
            return boolValue;

        if (jsonValue.TryGetValue<int>(out var intValue))
            return intValue != 0;

        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            if (bool.TryParse(stringValue, out var parsedBool))
                return parsedBool;

            if (int.TryParse(stringValue, out var parsedInt))
                return parsedInt != 0;
        }
    }

    return fallback;
}

static async Task InsertPageSettingsAsync(
    MySqlConnection connection,
    MySqlTransaction transaction,
    uint aturanId,
    string jsonValue)
{
    const string sql = """
        INSERT INTO aturan_detail (
            aturan_id,
            aturan_detail_kategori,
            aturan_detail_key,
            aturan_detail_json_value,
            aturan_detail_status,
            aturan_detail_catatan
        ) VALUES (
            @aturanId,
            @kategori,
            @key,
            @jsonValue,
            1,
            NULL
        )
        """;

    using var command = new MySqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("@aturanId", aturanId);
    command.Parameters.AddWithValue("@kategori", PengaturanHalaman);
    command.Parameters.AddWithValue("@key", PageSettingsKey);
    command.Parameters.AddWithValue("@jsonValue", jsonValue);
    await command.ExecuteNonQueryAsync();
}

static async Task UpdateJsonValueAsync(
    MySqlConnection connection,
    MySqlTransaction transaction,
    uint detailId,
    string jsonValue)
{
    const string sql = """
        UPDATE aturan_detail
        SET aturan_detail_json_value = @jsonValue,
            aturan_detail_status = 1
        WHERE aturan_detail_id = @detailId
        """;

    using var command = new MySqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("@detailId", detailId);
    command.Parameters.AddWithValue("@jsonValue", jsonValue);
    await command.ExecuteNonQueryAsync();
}

static async Task DeleteLegacyPageSettingsAsync(
    MySqlConnection connection,
    MySqlTransaction transaction,
    IReadOnlyList<uint> detailIds)
{
    if (detailIds.Count == 0)
        return;

    var placeholders = detailIds
        .Select((_, index) => $"@id{index}")
        .ToArray();

    var sql = $"DELETE FROM aturan_detail WHERE aturan_detail_id IN ({string.Join(", ", placeholders)})";
    using var command = new MySqlCommand(sql, connection, transaction);
    for (var index = 0; index < detailIds.Count; index++)
    {
        command.Parameters.AddWithValue(placeholders[index], detailIds[index]);
    }

    await command.ExecuteNonQueryAsync();
}

internal sealed record AturanDetailRow(
    uint Id,
    uint AturanId,
    string? Kategori,
    string? Key,
    string? JsonValue,
    sbyte Status,
    string? Catatan);

internal readonly record struct WrapperNode(
    JsonObject Node,
    JsonNode? Value,
    bool IsEditable,
    bool IsHardConstraint);

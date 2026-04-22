using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MySqlConnector;

const string NomorHalamanKategori = "Nomor Halaman";
const string GlobalKey = "nomor_halaman";
const string SourceKey = "nomor_halaman_isi";
string[] LegacyKeys =
[
    "nomor_halaman_awal",
    "nomor_halaman_isi",
    "nomor_halaman_akhir",
    "nomor_halaman_lampiran"
];

var jsonOptions = new JsonSerializerOptions { WriteIndented = false };

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
        var globalDetail = details.FirstOrDefault(detail =>
            Matches(detail.Kategori, NomorHalamanKategori) &&
            Matches(detail.Key, GlobalKey));

        var legacyDetails = details
            .Where(detail =>
                Matches(detail.Kategori, NomorHalamanKategori) &&
                detail.Key != null &&
                LegacyKeys.Contains(detail.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (globalDetail == null && legacyDetails.Count == 0)
        {
            await transaction.CommitAsync();
            Console.WriteLine($"Aturan {aturanGroup.Key}: tidak ada row nomor_halaman yang perlu diproses.");
            continue;
        }

        string globalJson;
        if (globalDetail != null)
        {
            globalJson = NormalizeExistingGlobalJson(globalDetail.JsonValue, jsonOptions);
        }
        else
        {
            var sourceDetail = legacyDetails.FirstOrDefault(detail => Matches(detail.Key, SourceKey))
                ?? throw new InvalidOperationException($"Aturan {aturanGroup.Key} tidak memiliki source {SourceKey}.");
            globalJson = BuildGlobalNomorHalamanJson(sourceDetail.JsonValue, jsonOptions);
        }

        if (globalDetail == null)
        {
            await InsertGlobalNomorHalamanAsync(connection, transaction, aturanGroup.Key, globalJson);
        }
        else
        {
            await UpdateJsonValueAsync(connection, transaction, globalDetail.Id, globalJson);
        }

        if (legacyDetails.Count > 0)
        {
            await DeleteLegacyDetailsAsync(connection, transaction, legacyDetails.Select(detail => detail.Id).ToList());
        }

        await transaction.CommitAsync();
        Console.WriteLine($"Aturan {aturanGroup.Key}: nomor_halaman upserted, legacy deleted ({legacyDetails.Count}).");
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Aturan {aturanGroup.Key}: gagal - {ex.Message}");
        throw;
    }
}

Console.WriteLine("Cutover selesai.");

static string NormalizeExistingGlobalJson(string? json, JsonSerializerOptions jsonOptions)
{
    var root = ParseJsonObject(json) ?? throw new InvalidOperationException("JSON nomor_halaman global tidak valid.");
    var numberFormatWrapper = GetRequiredWrapper(root, "numbering", "number_format");
    numberFormatWrapper["value"] = JsonValue.Create(MapWordNumberFormat(ReadString(numberFormatWrapper["value"])));
    return root.ToJsonString(jsonOptions);
}

static string BuildGlobalNomorHalamanJson(string? legacyJson, JsonSerializerOptions jsonOptions)
{
    var legacy = ParseJsonObject(legacyJson) ?? throw new InvalidOperationException("JSON legacy nomor_halaman_isi tidak valid.");

    var continueValue = ReadBool(GetOptionalNode(legacy, "continue"), false);
    Ensure(!continueValue, "Rule legacy continue=true tidak bisa dipetakan ke nomor_halaman global.");

    var differentFirstPage = GetBoolWrapperOrDefault(legacy, true, "different_first_page");
    var differentOddEven = GetBoolWrapperOrDefault(legacy, false, "different_odd_even");

    var firstPage = GetRequiredObject(legacy, "first_page");
    var defaultPage = GetRequiredObject(legacy, "default_page");

    Ensure(!ReadBool(GetOptionalNode(firstPage, "allow_other_content"), false), "first_page.allow_other_content=true tidak didukung.");
    Ensure(!ReadBool(GetOptionalNode(defaultPage, "allow_other_content"), false), "default_page.allow_other_content=true tidak didukung.");
    Ensure(!ReadBool(GetOptionalNode(firstPage, "is_empty"), false), "first_page.is_empty=true tidak didukung.");

    EnsureIndentationZero(firstPage, "first_page");
    EnsureIndentationZero(defaultPage, "default_page");

    var firstFormat = MapWordNumberFormat(ReadNumberFormatType(firstPage));
    var defaultFormat = MapWordNumberFormat(ReadNumberFormatType(defaultPage));
    Ensure(string.Equals(firstFormat, defaultFormat, StringComparison.OrdinalIgnoreCase), "Format first_page dan default_page berbeda.");

    Ensure(string.IsNullOrWhiteSpace(ReadNumberFormatPrefix(firstPage)), "Prefix first_page tidak kosong.");
    Ensure(string.IsNullOrWhiteSpace(ReadNumberFormatPrefix(defaultPage)), "Prefix default_page tidak kosong.");

    EnsureTextStyleEqual(firstPage, defaultPage);

    var defaultTextStyle = GetRequiredObject(defaultPage, "text_style");
    var defaultPosition = GetRequiredObject(defaultPage, "position");
    var firstPosition = GetRequiredObject(firstPage, "position");

    var fontName = GetStringWrapperOrDefault(defaultTextStyle, "Times New Roman", "font_name");
    var fontSize = GetDecimalWrapperOrDefault(defaultTextStyle, 12m, "font_size");
    var lineSpacing = GetDecimalWrapperOrDefault(defaultTextStyle, 1m, "line_spacing");
    var spacingBefore = GetDecimalWrapperOrDefault(defaultTextStyle, 0m, "spacing_before");
    var spacingAfter = GetDecimalWrapperOrDefault(defaultTextStyle, 0m, "spacing_after");

    var defaultLocation = GetStringWrapperOrDefault(defaultPosition, "footer", "location");
    var defaultAlignment = GetStringWrapperOrDefault(defaultPosition, "center", "alignment");
    var firstLocation = GetStringWrapperOrDefault(firstPosition, "header", "location");
    var firstAlignment = GetStringWrapperOrDefault(firstPosition, "right", "alignment");

    var evenLocation = defaultLocation.DeepClone();
    var evenAlignment = defaultAlignment.DeepClone();

    var root = new JsonObject
    {
        ["numbering"] = new JsonObject
        {
            ["number_format"] = WrapValue(JsonValue.Create(defaultFormat), false, false)
        },
        ["font"] = new JsonObject
        {
            ["font_name"] = fontName.DeepClone(),
            ["font_size"] = fontSize.DeepClone(),
            ["font_style"] = new JsonObject
            {
                ["bold"] = WrapValue(JsonValue.Create(false), true, false),
                ["italic"] = WrapValue(JsonValue.Create(false), true, false),
                ["underline"] = WrapValue(JsonValue.Create(false), true, false)
            }
        },
        ["paragraph"] = new JsonObject
        {
            ["indentation"] = new JsonObject
            {
                ["left_indent"] = WrapValue(JsonValue.Create(0m), true, false),
                ["right_indent"] = WrapValue(JsonValue.Create(0m), true, false),
                ["first_line_indent"] = WrapValue(JsonValue.Create(0m), true, false)
            },
            ["spacing"] = new JsonObject
            {
                ["line_spacing"] = lineSpacing.DeepClone(),
                ["before"] = spacingBefore.DeepClone(),
                ["after"] = spacingAfter.DeepClone()
            }
        },
        ["struktur_konten"] = new JsonObject
        {
            ["cegah_baris_tambahan"] = WrapValue(JsonValue.Create(true), true, false)
        },
        ["variation"] = new JsonObject
        {
            ["default"] = new JsonObject
            {
                ["position"] = new JsonObject
                {
                    ["location"] = defaultLocation.DeepClone(),
                    ["alignment"] = defaultAlignment.DeepClone()
                }
            },
            ["different_first_page"] = new JsonObject
            {
                ["enabled"] = differentFirstPage.DeepClone(),
                ["first"] = new JsonObject
                {
                    ["position"] = new JsonObject
                    {
                        ["location"] = firstLocation.DeepClone(),
                        ["alignment"] = firstAlignment.DeepClone()
                    }
                }
            },
            ["different_odd_even"] = new JsonObject
            {
                ["enabled"] = differentOddEven.DeepClone(),
                ["even"] = new JsonObject
                {
                    ["position"] = new JsonObject
                    {
                        ["location"] = evenLocation,
                        ["alignment"] = evenAlignment
                    }
                }
            }
        }
    };

    return root.ToJsonString(jsonOptions);
}

static void EnsureTextStyleEqual(JsonObject firstPage, JsonObject defaultPage)
{
    var firstStyle = GetRequiredObject(firstPage, "text_style");
    var defaultStyle = GetRequiredObject(defaultPage, "text_style");

    foreach (var key in new[] { "font_name", "font_size", "line_spacing", "spacing_before", "spacing_after" })
    {
        var firstValue = ReadComparableWrapperValue(GetOptionalNode(firstStyle, key));
        var defaultValue = ReadComparableWrapperValue(GetOptionalNode(defaultStyle, key));
        Ensure(string.Equals(firstValue, defaultValue, StringComparison.OrdinalIgnoreCase), $"text_style.{key} first_page dan default_page berbeda.");
    }
}

static void EnsureIndentationZero(JsonObject pageObject, string label)
{
    var position = GetRequiredObject(pageObject, "position");
    var indentationNode = GetOptionalNode(position, "indentation");
    if (indentationNode == null)
        return;

    var normalized = ReadComparableWrapperValue(indentationNode);
    if (string.IsNullOrWhiteSpace(normalized))
        return;

    Ensure(
        normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals("0.0", StringComparison.OrdinalIgnoreCase),
        $"{label}.position.indentation harus none/0.");
}

static string ReadNumberFormatType(JsonObject pageObject)
{
    var numberFormat = GetRequiredObject(pageObject, "number_format");
    var typeNode = GetOptionalNode(numberFormat, "type");
    var type = ReadString(typeNode);
    Ensure(!string.IsNullOrWhiteSpace(type), "number_format.type wajib ada.");
    return type!;
}

static string? ReadNumberFormatPrefix(JsonObject pageObject)
{
    var numberFormat = GetRequiredObject(pageObject, "number_format");
    return ReadString(GetOptionalNode(numberFormat, "prefix"));
}

static string MapWordNumberFormat(string? value)
{
    var normalized = (value ?? string.Empty).Trim();
    return normalized.ToLowerInvariant() switch
    {
        "arabic" or "arab" or "decimal" or "desimal" => "decimal",
        "lowerroman" or "roman_lower" or "lower_roman" => "lowerRoman",
        "upperroman" or "roman_upper" or "upper_roman" => "upperRoman",
        "lowerletter" or "lower_letter" or "lower_alpha" or "letter_lower" => "lowerLetter",
        "upperletter" or "upper_letter" or "upper_alpha" or "letter_upper" => "upperLetter",
        _ => throw new InvalidOperationException($"Format nomor halaman '{value}' tidak didukung.")
    };
}

static JsonObject GetRequiredObject(JsonObject parent, string propertyName)
{
    if (parent[propertyName] is JsonObject child)
        return child;

    throw new InvalidOperationException($"Property '{propertyName}' wajib berupa object.");
}

static JsonObject GetRequiredWrapper(JsonObject parent, params string[] path)
{
    JsonNode? current = parent;
    foreach (var segment in path)
    {
        current = current is JsonObject currentObject ? currentObject[segment] : null;
    }

    if (current is JsonObject wrapper && wrapper.ContainsKey("value"))
        return wrapper;

    throw new InvalidOperationException($"Wrapper '{string.Join('.', path)}' tidak ditemukan.");
}

static JsonNode? GetOptionalNode(JsonObject parent, string propertyName)
{
    return parent[propertyName];
}

static JsonObject GetStringWrapperOrDefault(JsonObject parent, string fallback, string propertyName)
{
    if (parent[propertyName] is JsonObject wrapper && wrapper.ContainsKey("value"))
        return wrapper;

    return WrapValue(JsonValue.Create(fallback), true, false);
}

static JsonObject GetDecimalWrapperOrDefault(JsonObject parent, decimal fallback, string propertyName)
{
    if (parent[propertyName] is JsonObject wrapper && wrapper.ContainsKey("value"))
        return wrapper;

    return WrapValue(JsonValue.Create(fallback), true, false);
}

static JsonObject GetBoolWrapperOrDefault(JsonObject parent, bool fallback, string propertyName)
{
    if (parent[propertyName] is JsonObject wrapper && wrapper.ContainsKey("value"))
        return wrapper;

    return WrapValue(JsonValue.Create(fallback), true, false);
}

static string? ReadComparableWrapperValue(JsonNode? node)
{
    if (node is JsonObject wrapper && wrapper.ContainsKey("value"))
        return ReadComparableWrapperValue(wrapper["value"]);

    if (node is JsonValue value)
    {
        if (value.TryGetValue<string>(out var stringValue))
            return (stringValue ?? string.Empty).Trim();
        if (value.TryGetValue<bool>(out var boolValue))
            return boolValue.ToString().ToLowerInvariant();
        if (value.TryGetValue<decimal>(out var decimalValue))
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<double>(out var doubleValue))
            return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<int>(out var intValue))
            return intValue.ToString(CultureInfo.InvariantCulture);
    }

    return null;
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

static bool ReadBool(JsonNode? node, bool fallback)
{
    if (node is JsonObject wrapper && wrapper.ContainsKey("value"))
        return ReadBool(wrapper["value"], fallback);

    if (node is JsonValue value)
    {
        if (value.TryGetValue<bool>(out var boolValue))
            return boolValue;
        if (value.TryGetValue<int>(out var intValue))
            return intValue != 0;
        if (value.TryGetValue<string>(out var stringValue))
        {
            if (bool.TryParse(stringValue, out var parsedBool))
                return parsedBool;
            if (int.TryParse(stringValue, out var parsedInt))
                return parsedInt != 0;
        }
    }

    return fallback;
}

static string? ReadString(JsonNode? node)
{
    if (node is JsonObject wrapper && wrapper.ContainsKey("value"))
        return ReadString(wrapper["value"]);

    if (node is JsonValue value)
    {
        if (value.TryGetValue<string>(out var stringValue))
            return string.IsNullOrWhiteSpace(stringValue) ? null : stringValue.Trim();
        if (value.TryGetValue<int>(out var intValue))
            return intValue.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<decimal>(out var decimalValue))
            return decimalValue.ToString(CultureInfo.InvariantCulture);
    }

    return null;
}

static void Ensure(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

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
            Catatan: reader.IsDBNull(reader.GetOrdinal("aturan_detail_catatan")) ? null : reader.GetString(reader.GetOrdinal("aturan_detail_catatan"))));
    }

    return rows;
}

static async Task InsertGlobalNomorHalamanAsync(
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
            aturan_detail_catatan
        ) VALUES (
            @aturanId,
            @kategori,
            @key,
            @jsonValue,
            NULL
        )
        """;

    using var command = new MySqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("@aturanId", aturanId);
    command.Parameters.AddWithValue("@kategori", NomorHalamanKategori);
    command.Parameters.AddWithValue("@key", GlobalKey);
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
        SET aturan_detail_json_value = @jsonValue
        WHERE aturan_detail_id = @detailId
        """;

    using var command = new MySqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("@detailId", detailId);
    command.Parameters.AddWithValue("@jsonValue", jsonValue);
    await command.ExecuteNonQueryAsync();
}

static async Task DeleteLegacyDetailsAsync(
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
    string? Catatan);

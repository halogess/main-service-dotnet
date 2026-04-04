using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using ValidasiTugasAkhir.MainService.Services;

LoadDotEnvIfPresent();

var dokumenId = args.Length > 0 && int.TryParse(args[0], out var parsedDokumenId)
    ? parsedDokumenId
    : 2066;
var runLiveTableValidation = args.Any(arg => string.Equals(arg, "--validate-table", StringComparison.OrdinalIgnoreCase));
var scanRules = args.Any(arg => string.Equals(arg, "--scan-rules", StringComparison.OrdinalIgnoreCase));

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__KorektorBukuDbConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__KorektorBukuDbConnection is not set.");
    return 1;
}

connectionString = connectionString.Replace("host.docker.internal", "localhost", StringComparison.OrdinalIgnoreCase);
if (!connectionString.Contains("SslMode=", StringComparison.OrdinalIgnoreCase))
{
    connectionString += ";SslMode=None";
}

await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();

var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
{
    ["dokumen"] = await QuerySingleAsync(
        connection,
        """
        SELECT
            d.dokumen_id,
            d.mhs_nrp,
            d.dokumen_filename,
            d.dokumen_status,
            d.dokumen_jumlah_kesalahan,
            d.dokumen_created_at,
            d.dokumen_updated_at
        FROM dokumen d
        WHERE d.dokumen_id = @dokumenId
        """,
        ("@dokumenId", dokumenId)),
    ["queues"] = await QueryAsync(
        connection,
        """
        SELECT
            a.antrian_id,
            a.antrian_tipe,
            a.antrian_extraction_status,
            a.antrian_labeling_status,
            a.antrian_validation_status,
            a.antrian_error_message,
            a.antrian_created_at,
            a.antrian_updated_at
        FROM antrian a
        WHERE a.dokumen_id = @dokumenId
        ORDER BY a.antrian_id DESC
        LIMIT 10
        """,
        ("@dokumenId", dokumenId)),
    ["table_errors"] = await QueryAsync(
        connection,
        """
        SELECT
            k.kesalahan_id,
            k.kesalahan_kategori,
            k.kesalahan_lokasi,
            kd.kesalahan_detail_id,
            kd.kesalahan_detail_judul,
            kd.kesalahan_detail_penjelasan
        FROM kesalahan k
        JOIN kesalahan_detail kd ON kd.kesalahan_id = k.kesalahan_id
        WHERE k.kesalahan_ref_tipe = 'dokumen'
          AND k.kesalahan_ref_id = @dokumenId
          AND k.kesalahan_kategori = 'tabel'
        ORDER BY k.kesalahan_id, kd.kesalahan_detail_id
        """,
        ("@dokumenId", dokumenId)),
    ["continuation_caption_errors"] = await QueryAsync(
        connection,
        """
        SELECT
            k.kesalahan_id,
            k.kesalahan_lokasi,
            kd.kesalahan_detail_id,
            kd.kesalahan_detail_judul,
            kd.kesalahan_detail_penjelasan
        FROM kesalahan k
        JOIN kesalahan_detail kd ON kd.kesalahan_id = k.kesalahan_id
        WHERE k.kesalahan_ref_tipe = 'dokumen'
          AND k.kesalahan_ref_id = @dokumenId
          AND kd.kesalahan_detail_judul LIKE '%caption lanjutan%'
        ORDER BY k.kesalahan_id, kd.kesalahan_detail_id
        """,
        ("@dokumenId", dokumenId)),
    ["active_table_rules"] = await QueryAsync(
        connection,
        """
        SELECT
            a.aturan_id,
            a.aturan_versi,
            a.aturan_status,
            ad.aturan_detail_key,
            ad.aturan_detail_json_value
        FROM aturan a
        JOIN aturan_detail ad ON ad.aturan_id = a.aturan_id
        WHERE a.aturan_status = 'aktif'
          AND ad.aturan_detail_status = 1
          AND ad.aturan_detail_kategori = 'Isi Buku'
          AND ad.aturan_detail_key IN ('tabel', 'caption_tabel')
        ORDER BY a.aturan_created_at DESC, ad.aturan_detail_key
        LIMIT 4
        """),
    ["active_core_rules"] = await QueryAsync(
        connection,
        """
        SELECT
            a.aturan_id,
            a.aturan_versi,
            a.aturan_status,
            ad.aturan_detail_key,
            ad.aturan_detail_json_value
        FROM aturan a
        JOIN aturan_detail ad ON ad.aturan_id = a.aturan_id
        WHERE a.aturan_status = 'aktif'
          AND ad.aturan_detail_status = 1
          AND ad.aturan_detail_key IN ('page_settings', 'nomor_halaman', 'judul_bab', 'judul_subbab', 'paragraf', 'gambar', 'tabel', 'kode', 'rumus', 'footnote')
        ORDER BY ad.aturan_detail_key
        """)
};

if (runLiveTableValidation)
{
    result["live_table_validation"] = await RunLiveTableValidationAsync(connectionString, dokumenId);
}

if (scanRules)
{
    result["rule_scan"] = await BuildRuleScanAsync(connection);
    result["rule_key_summary"] = await QueryAsync(
        connection,
        """
        SELECT
            aturan_id,
            aturan_detail_kategori,
            aturan_detail_key,
            COUNT(*) AS row_count
        FROM aturan_detail
        WHERE aturan_detail_status = 1
        GROUP BY aturan_id, aturan_detail_kategori, aturan_detail_key
        ORDER BY aturan_id, aturan_detail_kategori, aturan_detail_key
        """);
}

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
{
    WriteIndented = true
}));

return 0;

static async Task<List<Dictionary<string, object?>>> QueryAsync(
    MySqlConnection connection,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    await using var command = new MySqlCommand(sql, connection);
    foreach (var (name, value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<Dictionary<string, object?>>();

    while (await reader.ReadAsync())
    {
        rows.Add(ReadRow(reader));
    }

    return rows;
}

static async Task<Dictionary<string, object?>?> QuerySingleAsync(
    MySqlConnection connection,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    var rows = await QueryAsync(connection, sql, parameters);
    return rows.Count > 0 ? rows[0] : null;
}

static async Task<object> RunLiveTableValidationAsync(string connectionString, int dokumenId)
{
    var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
        .UseMySql(
            connectionString,
            new MySqlServerVersion(new Version(8, 0, 34)),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure())
        .Options;

    await using var db = new KorektorBukuDbContext(options);
    var service = new ValidationService(db, NullLogger<ValidationService>.Instance);
    var method = typeof(ValidationService).GetMethod(
        "ValidateTableAsync",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    if (method == null)
    {
        return new Dictionary<string, object?>
        {
            ["error"] = "ValidateTableAsync not found."
        };
    }

    var task = (Task?)method.Invoke(service, new object?[] { dokumenId, CancellationToken.None });
    if (task == null)
    {
        return new Dictionary<string, object?>
        {
            ["error"] = "ValidateTableAsync invocation returned null."
        };
    }

    await task.ConfigureAwait(false);
    var resultProperty = task.GetType().GetProperty("Result");
    var validationResult = resultProperty?.GetValue(task) as ValidationResult;
    if (validationResult == null)
    {
        return new Dictionary<string, object?>
        {
            ["error"] = "Validation result was null."
        };
    }

    return new Dictionary<string, object?>
    {
        ["error_count"] = validationResult.Errors.Count,
        ["all_errors"] = validationResult.Errors
            .Select(error => new Dictionary<string, object?>
            {
                ["field"] = error.Field,
                ["message"] = error.Message,
                ["expected"] = error.Expected,
                ["actual"] = error.Actual,
                ["evidence"] = error.Evidence,
                ["dokumen_elemen_id"] = error.DokumenElemenId,
                ["locations"] = error.Locations.Select(location => new Dictionary<string, object?>
                {
                    ["halaman_ke"] = location.HalamanKe,
                    ["bbox"] = location.Bbox == null
                        ? null
                        : new Dictionary<string, object?>
                        {
                            ["x0"] = location.Bbox.X0,
                            ["y0"] = location.Bbox.Y0,
                            ["x1"] = location.Bbox.X1,
                            ["y1"] = location.Bbox.Y1
                        }
                }).ToList()
            })
            .ToList(),
        ["continuation_caption_errors"] = validationResult.Errors
            .Where(error =>
                error.Message.Contains("caption lanjutan", StringComparison.OrdinalIgnoreCase) ||
                (error.Evidence?.Contains("caption lanjutan", StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(error => new Dictionary<string, object?>
            {
                ["field"] = error.Field,
                ["message"] = error.Message,
                ["expected"] = error.Expected,
                ["actual"] = error.Actual,
                ["dokumen_elemen_id"] = error.DokumenElemenId,
                ["locations"] = error.Locations.Select(location => new Dictionary<string, object?>
                {
                    ["halaman_ke"] = location.HalamanKe,
                    ["bbox"] = location.Bbox == null
                        ? null
                        : new Dictionary<string, object?>
                        {
                            ["x0"] = location.Bbox.X0,
                            ["y0"] = location.Bbox.Y0,
                            ["x1"] = location.Bbox.X1,
                            ["y1"] = location.Bbox.Y1
                        }
                }).ToList()
            })
            .ToList()
    };
}

static async Task<object> BuildRuleScanAsync(MySqlConnection connection)
{
    var rows = await QueryAsync(
        connection,
        """
        SELECT
            a.aturan_id,
            a.aturan_versi,
            a.aturan_status,
            ad.aturan_detail_id,
            ad.aturan_detail_key,
            ad.aturan_detail_json_value
        FROM aturan a
        JOIN aturan_detail ad ON ad.aturan_id = a.aturan_id
        WHERE ad.aturan_detail_status = 1
          AND ad.aturan_detail_kategori = 'Isi Buku'
        ORDER BY a.aturan_id, ad.aturan_detail_key, ad.aturan_detail_id
        """);

    var items = rows.Select(row =>
    {
        var aturanId = Convert.ToUInt32(row["aturan_id"]!);
        var versi = row["aturan_versi"]?.ToString();
        var status = row["aturan_status"]?.ToString();
        var detailId = Convert.ToUInt32(row["aturan_detail_id"]!);
        var key = row["aturan_detail_key"]?.ToString() ?? string.Empty;
        var rawJson = row["aturan_detail_json_value"]?.ToString();

        var summary = SummarizeRuleJson(key, rawJson);
        return new Dictionary<string, object?>
        {
            ["aturan_id"] = aturanId,
            ["aturan_versi"] = versi,
            ["aturan_status"] = status,
            ["aturan_detail_id"] = detailId,
            ["aturan_detail_key"] = key,
            ["root_shape"] = summary.RootShape,
            ["has_embedded_caption_tabel"] = summary.HasEmbeddedCaptionTabel,
            ["has_embedded_judul_kode"] = summary.HasEmbeddedJudulKode,
            ["has_continuation_flag"] = summary.HasContinuationFlag,
            ["continuation_flag_value"] = summary.ContinuationFlagValue,
            ["parse_error"] = summary.ParseError
        };
    }).ToList();

    return new Dictionary<string, object?>
    {
        ["total_rows"] = items.Count,
        ["rows"] = items
    };
}

static RuleJsonSummary SummarizeRuleJson(string key, string? rawJson)
{
    if (string.IsNullOrWhiteSpace(rawJson))
    {
        return new RuleJsonSummary
        {
            RootShape = "empty"
        };
    }

    try
    {
        using var doc = JsonDocument.Parse(rawJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new RuleJsonSummary
            {
                RootShape = doc.RootElement.ValueKind.ToString()
            };
        }

        var root = doc.RootElement;
        var hasKeyWrapper = root.TryGetProperty(key, out var ownNode);
        var ownElement = hasKeyWrapper ? ownNode : root;
        var rootShape = hasKeyWrapper ? "wrapped" : "flat";

        JsonElement? captionNode = null;
        JsonElement? titleNode = null;

        if (root.TryGetProperty("caption_tabel", out var embeddedCaption))
            captionNode = embeddedCaption;
        else if (hasKeyWrapper && ownNode.ValueKind == JsonValueKind.Object && ownNode.TryGetProperty("caption_tabel", out var nestedCaption))
            captionNode = nestedCaption;

        if (root.TryGetProperty("judul_kode", out var embeddedTitle))
            titleNode = embeddedTitle;
        else if (hasKeyWrapper && ownNode.ValueKind == JsonValueKind.Object && ownNode.TryGetProperty("judul_kode", out var nestedTitle))
            titleNode = nestedTitle;

        JsonElement? flagNode = null;
        if (ownElement.ValueKind == JsonValueKind.Object &&
            ownElement.TryGetProperty("wajib_caption_lanjutan_jika_lintas_halaman", out var ownFlag))
        {
            flagNode = ownFlag;
        }
        else if (captionNode.HasValue &&
                 captionNode.Value.ValueKind == JsonValueKind.Object &&
                 captionNode.Value.TryGetProperty("wajib_caption_lanjutan_jika_lintas_halaman", out var captionFlag))
        {
            flagNode = captionFlag;
        }
        else if (titleNode.HasValue &&
                 titleNode.Value.ValueKind == JsonValueKind.Object &&
                 titleNode.Value.TryGetProperty("wajib_caption_lanjutan_jika_lintas_halaman", out var titleFlag))
        {
            flagNode = titleFlag;
        }

        bool? flagValue = null;
        if (flagNode.HasValue &&
            flagNode.Value.ValueKind == JsonValueKind.Object &&
            flagNode.Value.TryGetProperty("value", out var valueNode))
        {
            flagValue = valueNode.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(valueNode.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        return new RuleJsonSummary
        {
            RootShape = rootShape,
            HasEmbeddedCaptionTabel = captionNode.HasValue,
            HasEmbeddedJudulKode = titleNode.HasValue,
            HasContinuationFlag = flagNode.HasValue,
            ContinuationFlagValue = flagValue
        };
    }
    catch (JsonException ex)
    {
        return new RuleJsonSummary
        {
            RootShape = "invalid_json",
            ParseError = ex.Message
        };
    }
}

static Dictionary<string, object?> ReadRow(IDataRecord reader)
{
    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < reader.FieldCount; index++)
    {
        var value = reader.IsDBNull(index) ? null : reader.GetValue(index);
        if (value is DateTime dateTime)
        {
            value = dateTime.ToString("O");
        }

        row[reader.GetName(index)] = value;
    }

    return row;
}

static void LoadDotEnvIfPresent()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", ".env")
    };

    var envPath = candidates.FirstOrDefault(File.Exists);
    if (envPath == null)
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

sealed class RuleJsonSummary
{
    public string RootShape { get; init; } = "unknown";
    public bool HasEmbeddedCaptionTabel { get; init; }
    public bool HasEmbeddedJudulKode { get; init; }
    public bool HasContinuationFlag { get; init; }
    public bool? ContinuationFlagValue { get; init; }
    public string? ParseError { get; init; }
}

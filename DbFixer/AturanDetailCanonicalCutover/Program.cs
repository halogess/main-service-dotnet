using MySqlConnector;
using ValidasiTugasAkhir.MainService.Services;

LoadDotEnvIfPresent();

var options = ParseOptions(args);
var connectionString = options.ConnectionString
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__KorektorBukuDbConnection")
    ?? Environment.GetEnvironmentVariable("KOREKTOR_BUKU_CONNECTION_STRING")
    ?? "Server=localhost;Port=3307;Database=korektor_buku;User=root;Password=root;TreatTinyAsBoolean=false";

connectionString = connectionString.Replace("host.docker.internal", "localhost", StringComparison.OrdinalIgnoreCase);
if (!connectionString.Contains("SslMode=", StringComparison.OrdinalIgnoreCase))
{
    connectionString += ";SslMode=None";
}

using var connection = new MySqlConnection(connectionString.Trim());
await connection.OpenAsync();

Console.WriteLine($"Connected to DB      : {connection.Database}");
Console.WriteLine($"Mode                 : {(options.DryRun ? "dry-run" : "apply")}");
Console.WriteLine($"Target aturan        : {(options.AturanIds.Count == 0 ? "all" : string.Join(", ", options.AturanIds.OrderBy(id => id)))}");

var allDetails = await LoadDetailsAsync(connection, options.AturanIds);
var groupedByAturan = allDetails
    .GroupBy(detail => detail.AturanId)
    .OrderBy(group => group.Key)
    .ToList();

Console.WriteLine($"Aturan ditemukan     : {groupedByAturan.Count}");
Console.WriteLine($"Detail ditemukan     : {allDetails.Count}");

var totalChanged = 0;
var totalScanned = 0;

foreach (var aturanGroup in groupedByAturan)
{
    await using var transaction = options.DryRun ? null : await connection.BeginTransactionAsync();
    try
    {
        var changedInAturan = 0;
        foreach (var detail in aturanGroup)
        {
            totalScanned++;

            if (string.IsNullOrWhiteSpace(detail.JsonValue) || string.IsNullOrWhiteSpace(detail.Key))
                continue;

            if (!AturanDetailCanonicalizer.TryCanonicalize(
                detail.Key,
                detail.JsonValue,
                out var canonicalJson,
                out var changed,
                out var errorMessage))
            {
                throw new InvalidOperationException($"Detail {detail.Id} ({detail.Key}) gagal dikanonisasi: {errorMessage}");
            }

            if (!changed || string.Equals(detail.JsonValue, canonicalJson, StringComparison.Ordinal))
                continue;

            changedInAturan++;
            totalChanged++;

            if (!options.DryRun)
                await UpdateJsonValueAsync(connection, transaction!, detail.Id, canonicalJson!);
        }

        if (!options.DryRun)
            await transaction!.CommitAsync();

        Console.WriteLine($"Aturan {aturanGroup.Key}: changed {changedInAturan}/{aturanGroup.Count()} detail.");
    }
    catch (Exception ex)
    {
        if (!options.DryRun && transaction != null)
            await transaction.RollbackAsync();

        Console.WriteLine($"Aturan {aturanGroup.Key}: gagal - {ex.Message}");
        throw;
    }
}

Console.WriteLine($"Total detail scanned : {totalScanned}");
Console.WriteLine($"Total detail changed : {totalChanged}");
Console.WriteLine(options.DryRun
    ? "Dry-run selesai. Tidak ada perubahan DB yang disimpan."
    : "Canonical cutover selesai.");

static async Task<List<AturanDetailRow>> LoadDetailsAsync(MySqlConnection connection, IReadOnlyCollection<uint> aturanIds)
{
    var sql = """
        SELECT aturan_detail_id,
               aturan_id,
               aturan_detail_key,
               aturan_detail_json_value,
               aturan_detail_status
        FROM aturan_detail
        """
        + (aturanIds.Count == 0
            ? string.Empty
            : $" WHERE aturan_id IN ({string.Join(", ", aturanIds.Select((_, index) => $"@aturanId{index}"))})")
        + """
        
        ORDER BY aturan_id, aturan_detail_id
        """;

    using var command = new MySqlCommand(sql, connection);
    var parameterIndex = 0;
    foreach (var aturanId in aturanIds)
    {
        command.Parameters.AddWithValue($"@aturanId{parameterIndex}", aturanId);
        parameterIndex++;
    }

    using var reader = await command.ExecuteReaderAsync();

    var rows = new List<AturanDetailRow>();
    while (await reader.ReadAsync())
    {
        rows.Add(new AturanDetailRow(
            Id: reader.GetUInt32("aturan_detail_id"),
            AturanId: reader.GetUInt32("aturan_id"),
            Key: reader.IsDBNull(reader.GetOrdinal("aturan_detail_key")) ? null : reader.GetString(reader.GetOrdinal("aturan_detail_key")),
            JsonValue: reader.IsDBNull(reader.GetOrdinal("aturan_detail_json_value")) ? null : reader.GetString(reader.GetOrdinal("aturan_detail_json_value")),
            Status: reader.GetSByte("aturan_detail_status")));
    }

    return rows;
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

static Options ParseOptions(string[] args)
{
    string? connectionString = null;
    var aturanIds = new HashSet<uint>();
    var dryRun = false;

    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        switch (arg)
        {
            case "--dry-run":
                dryRun = true;
                break;
            case "--connection":
                if (index + 1 >= args.Length)
                    throw new ArgumentException("`--connection` membutuhkan nilai.");
                connectionString = args[++index];
                break;
            case "--aturan-id":
                if (index + 1 >= args.Length)
                    throw new ArgumentException("`--aturan-id` membutuhkan nilai.");
                if (!uint.TryParse(args[++index], out var aturanId))
                    throw new ArgumentException("Nilai `--aturan-id` harus berupa bilangan bulat positif.");
                aturanIds.Add(aturanId);
                break;
            default:
                if (arg.StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Argumen tidak dikenal: {arg}");

                connectionString ??= arg;
                break;
        }
    }

    return new Options(connectionString, dryRun, aturanIds);
}

static void LoadDotEnvIfPresent()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env")
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

internal sealed record Options(
    string? ConnectionString,
    bool DryRun,
    IReadOnlyCollection<uint> AturanIds);

internal sealed record AturanDetailRow(
    uint Id,
    uint AturanId,
    string? Key,
    string? JsonValue,
    sbyte Status);

using MySqlConnector;
using ValidasiTugasAkhir.MainService.Services;

string[] TargetKeys =
[
    "judul_subbab",
    "paragraf"
];

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
        var activeDetails = aturanGroup
            .Where(detail =>
                detail.Status == 1 &&
                detail.Key != null &&
                TargetKeys.Contains(detail.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (activeDetails.Count == 0)
        {
            await transaction.CommitAsync();
            Console.WriteLine($"Aturan {aturanGroup.Key}: tidak ada row paragraph shape yang perlu diproses.");
            continue;
        }

        var updatedCount = 0;
        foreach (var detail in activeDetails)
        {
            if (string.IsNullOrWhiteSpace(detail.JsonValue))
                throw new InvalidOperationException($"Detail {detail.Id} memiliki json kosong.");

            if (!AturanDetailParagraphCutover.TryTransform(detail.Key, detail.JsonValue, out var transformedJson, out var changed, out var errorMessage))
                throw new InvalidOperationException($"Detail {detail.Id} gagal ditransformasi: {errorMessage}");

            if (!changed || string.Equals(detail.JsonValue, transformedJson, StringComparison.Ordinal))
                continue;

            await UpdateJsonValueAsync(connection, transaction, detail.Id, transformedJson!);
            updatedCount++;
        }

        await transaction.CommitAsync();
        Console.WriteLine($"Aturan {aturanGroup.Key}: paragraph shape updated ({updatedCount}/{activeDetails.Count}).");
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Aturan {aturanGroup.Key}: gagal - {ex.Message}");
        throw;
    }
}

Console.WriteLine("Cutover selesai.");

static async Task<List<AturanDetailRow>> LoadDetailsAsync(MySqlConnection connection)
{
    const string sql = """
        SELECT aturan_detail_id,
               aturan_id,
               aturan_detail_key,
               aturan_detail_json_value,
               aturan_detail_status
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
        SET aturan_detail_json_value = @jsonValue,
            aturan_detail_status = 1
        WHERE aturan_detail_id = @detailId
        """;

    using var command = new MySqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("@detailId", detailId);
    command.Parameters.AddWithValue("@jsonValue", jsonValue);
    await command.ExecuteNonQueryAsync();
}

internal sealed record AturanDetailRow(
    uint Id,
    uint AturanId,
    string? Key,
    string? JsonValue,
    sbyte Status);

using MySqlConnector;

var connectionString = (
    args.FirstOrDefault(arg => !string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
    ?? Environment.GetEnvironmentVariable("KOREKTOR_BUKU_CONNECTION_STRING")
    ?? "Server=localhost;Port=3307;Database=korektor_buku;User=root;Password=root;").Trim();

var dryRun = args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));

var targets = new[]
{
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_left_twips", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_right_twips", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_first_line_twips", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_hanging_twips", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_start_twips", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_end_twips", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_left_chars", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_format_paragraf", "dfp_ind_right_chars", CanRecoverWrappedValues: true),
    new ColumnFixTarget("dokumen_section", "dsec_margin_top_twips", CanRecoverWrappedValues: false),
    new ColumnFixTarget("dokumen_section", "dsec_margin_bottom_twips", CanRecoverWrappedValues: false)
};

using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();

Console.WriteLine($"Connected to DB: {connection.Database}");
Console.WriteLine(dryRun ? "Mode: DRY RUN" : "Mode: APPLY");

var columns = await LoadColumnMetadataAsync(connection, targets);

foreach (var target in targets)
{
    if (!columns.TryGetValue((target.TableName, target.ColumnName), out var column))
    {
        Console.WriteLine($"SKIP  {target.TableName}.{target.ColumnName}: column not found.");
        continue;
    }

    if (!column.ColumnType.Contains("unsigned", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"SKIP  {target.TableName}.{target.ColumnName}: already signed ({column.ColumnType}).");
        continue;
    }

    var finalDefinition = BuildColumnDefinition("BIGINT", column);
    var steps = new List<string>();

    if (target.CanRecoverWrappedValues)
    {
        steps.Add($"ALTER TABLE `{target.TableName}` MODIFY COLUMN `{target.ColumnName}` {finalDefinition};");
        steps.Add(
            $"UPDATE `{target.TableName}` " +
            $"SET `{target.ColumnName}` = `{target.ColumnName}` - 4294967296 " +
            $"WHERE `{target.ColumnName}` > 2147483647;");
    }
    else
    {
        steps.Add($"ALTER TABLE `{target.TableName}` MODIFY COLUMN `{target.ColumnName}` {finalDefinition};");
    }

    Console.WriteLine($"PLAN  {target.TableName}.{target.ColumnName}: {column.ColumnType} -> {finalDefinition}");
    if (!target.CanRecoverWrappedValues)
    {
        Console.WriteLine($"NOTE  {target.TableName}.{target.ColumnName}: existing values that were previously normalized with abs() cannot be auto-restored.");
    }

    foreach (var sql in steps)
    {
        Console.WriteLine(sql);
        if (!dryRun)
        {
            using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

Console.WriteLine("Done.");

static async Task<Dictionary<(string TableName, string ColumnName), ColumnMetadata>> LoadColumnMetadataAsync(
    MySqlConnection connection,
    IEnumerable<ColumnFixTarget> targets)
{
    const string sql = """
        SELECT table_name,
               column_name,
               column_type,
               is_nullable,
               column_default
        FROM information_schema.columns
        WHERE table_schema = @schema
          AND (
                (table_name = @table0 AND column_name = @column0) OR
                (table_name = @table1 AND column_name = @column1) OR
                (table_name = @table2 AND column_name = @column2) OR
                (table_name = @table3 AND column_name = @column3) OR
                (table_name = @table4 AND column_name = @column4) OR
                (table_name = @table5 AND column_name = @column5) OR
                (table_name = @table6 AND column_name = @column6) OR
                (table_name = @table7 AND column_name = @column7) OR
                (table_name = @table8 AND column_name = @column8) OR
                (table_name = @table9 AND column_name = @column9)
              )
        """;

    var targetList = targets.ToList();

    using var command = new MySqlCommand(sql, connection);
    command.Parameters.AddWithValue("@schema", connection.Database);
    for (var i = 0; i < targetList.Count; i++)
    {
        command.Parameters.AddWithValue($"@table{i}", targetList[i].TableName);
        command.Parameters.AddWithValue($"@column{i}", targetList[i].ColumnName);
    }

    using var reader = await command.ExecuteReaderAsync();
    var result = new Dictionary<(string TableName, string ColumnName), ColumnMetadata>(targetList.Count);

    while (await reader.ReadAsync())
    {
        var tableName = reader.GetString("table_name");
        var columnName = reader.GetString("column_name");
        result[(tableName, columnName)] = new ColumnMetadata(
            TableName: tableName,
            ColumnName: columnName,
            ColumnType: reader.GetString("column_type"),
            IsNullable: string.Equals(reader.GetString("is_nullable"), "YES", StringComparison.OrdinalIgnoreCase),
            ColumnDefault: reader.IsDBNull(reader.GetOrdinal("column_default"))
                ? null
                : reader.GetValue(reader.GetOrdinal("column_default")));
    }

    return result;
}

static string BuildColumnDefinition(string sqlType, ColumnMetadata column)
{
    var nullableClause = column.IsNullable ? "NULL" : "NOT NULL";
    var defaultClause = BuildDefaultClause(column.ColumnDefault);
    return string.IsNullOrEmpty(defaultClause)
        ? $"{sqlType} {nullableClause}"
        : $"{sqlType} {nullableClause} {defaultClause}";
}

static string BuildDefaultClause(object? defaultValue)
{
    if (defaultValue == null)
        return string.Empty;

    return defaultValue switch
    {
        string s when string.Equals(s, "NULL", StringComparison.OrdinalIgnoreCase)
            => string.Empty,
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal or double or float
            => $"DEFAULT {Convert.ToString(defaultValue, System.Globalization.CultureInfo.InvariantCulture)}",
        string s when string.Equals(s, "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase)
            => "DEFAULT CURRENT_TIMESTAMP",
        string s
            => $"DEFAULT '{MySqlHelper.EscapeString(s)}'",
        _ => $"DEFAULT '{MySqlHelper.EscapeString(defaultValue.ToString() ?? string.Empty)}'"
    };
}

internal sealed record ColumnFixTarget(
    string TableName,
    string ColumnName,
    bool CanRecoverWrappedValues);

internal sealed record ColumnMetadata(
    string TableName,
    string ColumnName,
    string ColumnType,
    bool IsNullable,
    object? ColumnDefault);

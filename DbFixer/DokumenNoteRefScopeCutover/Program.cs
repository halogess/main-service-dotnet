                                                                                                                                                                                    using MySqlConnector;

var connectionString = (
    args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("KOREKTOR_BUKU_CONNECTION_STRING")
    ?? "Server=localhost;Port=3307;Database=korektor_buku;User=root;Password=root;").Trim();

await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();

Console.WriteLine($"Connected to DB: {connection.Database}");

await EnsureColumnExistsAsync(
    connection,
    "dokumen_note",
    "dnote_ref_tipe",
    "ALTER TABLE `dokumen_note` ADD COLUMN `dnote_ref_tipe` VARCHAR(16) NULL AFTER `dnote_id`");

await EnsureColumnExistsAsync(
    connection,
    "dokumen_note",
    "dnote_ref_id",
    "ALTER TABLE `dokumen_note` ADD COLUMN `dnote_ref_id` INT UNSIGNED NULL AFTER `dnote_ref_tipe`");

await EnsureColumnExistsAsync(
    connection,
    "dokumen_note",
    "dnote_number",
    "ALTER TABLE `dokumen_note` ADD COLUMN `dnote_number` INT UNSIGNED NULL AFTER `dnote_type`");

if (await ColumnExistsAsync(connection, "dokumen_note", "dokumen_id"))
{
    await ExecuteAsync(
        connection,
        "UPDATE `dokumen_note` " +
        "SET `dnote_ref_tipe` = COALESCE(NULLIF(TRIM(`dnote_ref_tipe`), ''), 'dokumen'), " +
        "`dnote_ref_id` = COALESCE(`dnote_ref_id`, `dokumen_id`) " +
        "WHERE `dokumen_id` IS NOT NULL");
}

await ExecuteAsync(
    connection,
    "UPDATE `dokumen_note` " +
    "SET `dnote_ref_tipe` = 'dokumen' " +
    "WHERE `dnote_ref_tipe` IS NULL OR TRIM(`dnote_ref_tipe`) = ''");

await ExecuteAsync(
    connection,
    "ALTER TABLE `dokumen_note` " +
    "MODIFY COLUMN `dnote_ref_tipe` ENUM('dokumen','bab','aturan') NOT NULL");

await ExecuteAsync(
    connection,
    "ALTER TABLE `dokumen_note` " +
    "MODIFY COLUMN `dnote_ref_id` INT UNSIGNED NOT NULL");

await EnsureIndexExistsAsync(
    connection,
    "dokumen_note",
    "idx_dnote_ref_kind",
    "CREATE INDEX `idx_dnote_ref_kind` ON `dokumen_note` (`dnote_ref_tipe`, `dnote_ref_id`, `dnote_kind`)");

if (await ColumnExistsAsync(connection, "dokumen_note", "dokumen_id"))
{
    await ExecuteAsync(connection, "ALTER TABLE `dokumen_note` DROP COLUMN `dokumen_id`");
}

Console.WriteLine("DokumenNote ref-scope cutover selesai.");

static async Task EnsureColumnExistsAsync(
    MySqlConnection connection,
    string tableName,
    string columnName,
    string alterSql)
{
    if (await ColumnExistsAsync(connection, tableName, columnName))
    {
        Console.WriteLine($"Column {tableName}.{columnName} already exists.");
        return;
    }

    await ExecuteAsync(connection, alterSql);
    Console.WriteLine($"Added column {tableName}.{columnName}.");
}

static async Task EnsureIndexExistsAsync(
    MySqlConnection connection,
    string tableName,
    string indexName,
    string createSql)
{
    if (await IndexExistsAsync(connection, tableName, indexName))
    {
        Console.WriteLine($"Index {tableName}.{indexName} already exists.");
        return;
    }

    await ExecuteAsync(connection, createSql);
    Console.WriteLine($"Added index {tableName}.{indexName}.");
}

static async Task<bool> ColumnExistsAsync(MySqlConnection connection, string tableName, string columnName)
{
    const string sql = """
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = @tableName
          AND COLUMN_NAME = @columnName
        LIMIT 1
        """;

    await using var cmd = new MySqlCommand(sql, connection);
    cmd.Parameters.AddWithValue("@tableName", tableName);
    cmd.Parameters.AddWithValue("@columnName", columnName);
    var result = await cmd.ExecuteScalarAsync();
    return result != null;
}

static async Task<bool> IndexExistsAsync(MySqlConnection connection, string tableName, string indexName)
{
    const string sql = """
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = @tableName
          AND INDEX_NAME = @indexName
        LIMIT 1
        """;

    await using var cmd = new MySqlCommand(sql, connection);
    cmd.Parameters.AddWithValue("@tableName", tableName);
    cmd.Parameters.AddWithValue("@indexName", indexName);
    var result = await cmd.ExecuteScalarAsync();
    return result != null;
}

static async Task ExecuteAsync(MySqlConnection connection, string sql)
{
    await using var cmd = new MySqlCommand(sql, connection);
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine(sql);
}

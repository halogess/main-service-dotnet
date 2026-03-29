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
    "aturan",
    "aturan_template_pdf_path",
    "ALTER TABLE `aturan` ADD COLUMN `aturan_template_pdf_path` VARCHAR(255) NULL AFTER `aturan_template_file_path`");

await EnsureColumnExistsAsync(
    connection,
    "antrian",
    "aturan_id",
    "ALTER TABLE `antrian` ADD COLUMN `aturan_id` INT NULL AFTER `dokumen_id`");

await NormalizeAturanStatusColumnAsync(connection);

await ExecuteAsync(connection,
    "UPDATE `aturan` SET `aturan_status` = 'tidak_aktif' WHERE `aturan_status` IS NULL OR TRIM(`aturan_status`) = ''");

await ExecuteAsync(connection,
    "UPDATE `aturan` SET `aturan_status` = CASE LOWER(TRIM(`aturan_status`)) " +
    "WHEN '1' THEN 'aktif' " +
    "WHEN '0' THEN 'tidak_aktif' " +
    "WHEN 'null' THEN 'tidak_aktif' " +
    "WHEN 'aktif' THEN 'aktif' " +
    "WHEN 'diproses' THEN 'diproses' " +
    "WHEN 'menunggu_review' THEN 'menunggu_review' " +
    "WHEN 'gagal' THEN 'gagal' " +
    "ELSE 'tidak_aktif' END");

await ExecuteAsync(connection,
    "ALTER TABLE `aturan` MODIFY COLUMN `aturan_status` " +
    "ENUM('diproses','menunggu_review','tidak_aktif','aktif','gagal') NOT NULL DEFAULT 'tidak_aktif'");

await ExecuteAsync(connection,
    "ALTER TABLE `antrian` MODIFY COLUMN `antrian_tipe` ENUM('dokumen','buku','aturan') NOT NULL");

await ExecuteAsync(connection,
    "ALTER TABLE `dokumen_elemen_visual` MODIFY COLUMN `dev_ref_tipe` ENUM('dokumen','buku','bab','aturan') NULL");

await ExecuteAsync(connection,
    "ALTER TABLE `dokumen_section` MODIFY COLUMN `dsec_ref_tipe` ENUM('dokumen','buku','bab','aturan') NULL");

Console.WriteLine("Aturan lifecycle cutover selesai.");

static async Task NormalizeAturanStatusColumnAsync(MySqlConnection connection)
{
    var dataType = await GetColumnDataTypeAsync(connection, "aturan", "aturan_status");
    if (string.Equals(dataType, "tinyint", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dataType, "smallint", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dataType, "mediumint", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dataType, "int", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dataType, "bigint", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Converting aturan_status numeric column to text lifecycle...");

        await EnsureColumnExistsAsync(
            connection,
            "aturan",
            "aturan_status_text",
            "ALTER TABLE `aturan` ADD COLUMN `aturan_status_text` VARCHAR(32) NULL AFTER `aturan_status`");

        await ExecuteAsync(connection,
            "UPDATE `aturan` SET `aturan_status_text` = CASE " +
            "WHEN `aturan_status` = 1 THEN 'aktif' " +
            "ELSE 'tidak_aktif' END");

        await ExecuteAsync(connection, "ALTER TABLE `aturan` DROP COLUMN `aturan_status`");
        await ExecuteAsync(connection, "ALTER TABLE `aturan` CHANGE COLUMN `aturan_status_text` `aturan_status` VARCHAR(32) NULL");
    }
}

static async Task<string?> GetColumnDataTypeAsync(MySqlConnection connection, string tableName, string columnName)
{
    const string sql = """
        SELECT DATA_TYPE
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
    return result?.ToString();
}

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

static async Task ExecuteAsync(MySqlConnection connection, string sql)
{
    await using var cmd = new MySqlCommand(sql, connection);
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine(sql);
}

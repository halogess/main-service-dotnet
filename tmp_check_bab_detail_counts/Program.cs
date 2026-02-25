using MySqlConnector;

var envPath = Path.Combine("e:\\main-service-dotnet", ".env");
string? connStr = null;
foreach (var raw in File.ReadAllLines(envPath))
{
    var line = raw.Trim();
    if (line.StartsWith("ConnectionStrings__KorektorBukuDbConnection="))
    {
        connStr = line["ConnectionStrings__KorektorBukuDbConnection=".Length..].Trim();
        break;
    }
}

var csb = new MySqlConnectionStringBuilder(connStr!) { Server = "localhost" };
await using var conn = new MySqlConnection(csb.ConnectionString);
await conn.OpenAsync();

var babIds = new uint[] { 6203, 6204, 6205 };

foreach (var babId in babIds)
{
    Console.WriteLine($"\nBAB {babId}");

    await using (var cmd = new MySqlCommand(@"
SELECT COUNT(*)
FROM kesalahan
WHERE kesalahan_ref_tipe='bab' AND kesalahan_ref_id=@bab", conn))
    {
        cmd.Parameters.AddWithValue("@bab", babId);
        var parentCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Console.WriteLine($"- parent kesalahan rows: {parentCount}");
    }

    await using (var cmd = new MySqlCommand(@"
SELECT COUNT(*)
FROM kesalahan_detail kd
JOIN kesalahan k ON k.kesalahan_id = kd.kesalahan_id
WHERE k.kesalahan_ref_tipe='bab' AND k.kesalahan_ref_id=@bab", conn))
    {
        cmd.Parameters.AddWithValue("@bab", babId);
        var detailCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Console.WriteLine($"- detail rows: {detailCount}");
    }

    await using (var cmd = new MySqlCommand(@"
SELECT k.kesalahan_id, k.kesalahan_kategori, k.kesalahan_lokasi, COUNT(kd.kesalahan_detail_id) detail_count
FROM kesalahan k
LEFT JOIN kesalahan_detail kd ON kd.kesalahan_id = k.kesalahan_id
WHERE k.kesalahan_ref_tipe='bab' AND k.kesalahan_ref_id=@bab
GROUP BY k.kesalahan_id, k.kesalahan_kategori, k.kesalahan_lokasi
ORDER BY detail_count DESC, k.kesalahan_id", conn))
    {
        cmd.Parameters.AddWithValue("@bab", babId);
        await using var rd = await cmd.ExecuteReaderAsync();
        Console.WriteLine("- per parent:");
        while (await rd.ReadAsync())
        {
            var id = Convert.ToUInt32(rd["kesalahan_id"]);
            var kat = rd["kesalahan_kategori"] == DBNull.Value ? "" : rd["kesalahan_kategori"].ToString();
            var det = Convert.ToInt32(rd["detail_count"]);
            var lokasi = rd["kesalahan_lokasi"] == DBNull.Value ? null : rd["kesalahan_lokasi"].ToString();
            var hasPage = lokasi != null && lokasi.Contains("halaman_ke");
            Console.WriteLine($"  kesalahan_id={id}, kategori={kat}, detail_count={det}, lokasi_has_halaman_ke={hasPage}");
        }
    }

    await using (var cmd = new MySqlCommand("SELECT bab_jumlah_kesalahan FROM bab WHERE bab_id=@bab", conn))
    {
        cmd.Parameters.AddWithValue("@bab", babId);
        var bjk = await cmd.ExecuteScalarAsync();
        Console.WriteLine($"- bab.bab_jumlah_kesalahan: {bjk}");
    }
}
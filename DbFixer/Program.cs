using System;
using MySqlConnector;

class Program
{
    static void Main()
    {
        var connectionString = "Server=localhost;Port=3307;Database=korektor_buku;User=root;Password=root;";

        using var connection = new MySqlConnection(connectionString);
        try
        {
            connection.Open();
            Console.WriteLine("Connected to database.");

            // Check if column exists
            using var checkCmd = new MySqlCommand("SHOW COLUMNS FROM dokumen_format_table_row LIKE 'dfr_cant_split'", connection);
            var exists = checkCmd.ExecuteScalar() != null;

            if (!exists)
            {
                Console.WriteLine("Column dfr_cant_split missing. Adding it...");
                using var alterCmd = new MySqlCommand("ALTER TABLE dokumen_format_table_row ADD COLUMN dfr_cant_split TINYINT(1) DEFAULT 0", connection);
                alterCmd.ExecuteNonQuery();
                Console.WriteLine("Column added successfully.");
            }
            else
            {
                Console.WriteLine("Column dfr_cant_split already exists.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

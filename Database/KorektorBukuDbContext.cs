using _.Models;
using Microsoft.EntityFrameworkCore;

public class KorektorBukuDbContext : DbContext
{
    public KorektorBukuDbContext(DbContextOptions<KorektorBukuDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        // Anda bisa menambahkan konfigurasi untuk entitas lain di sini
        // modelBuilder.Entity<Jurusan>(...);
    }
}
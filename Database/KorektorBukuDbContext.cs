using _.Models;
using Microsoft.EntityFrameworkCore;

public class KorektorBukuDbContext : DbContext
{
    public KorektorBukuDbContext(DbContextOptions<KorektorBukuDbContext> options) : base(options) { }

    public DbSet<Dokumen> Dokumens { get; set; }
    public DbSet<Buku> Bukus { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dokumen>(entity =>
        {
            entity.Property(e => e.DokumenCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.DokumenUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<Buku>(entity =>
        {
            entity.Property(e => e.BukuCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.BukuUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();
        });
    }
}
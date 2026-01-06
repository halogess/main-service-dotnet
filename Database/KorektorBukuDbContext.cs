using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

public class KorektorBukuDbContext : DbContext
{
    public KorektorBukuDbContext(DbContextOptions<KorektorBukuDbContext> options) : base(options) { }

    public DbSet<Buku> Bukus { get; set; }
    public DbSet<Bab> Babs { get; set; }
    public DbSet<Dokumen> Dokumens { get; set; }
    public DbSet<DokumenElemen> DokumenElemens { get; set; }
    public DbSet<DokumenMedia> DokumenMedias { get; set; }
    public DbSet<DokumenSection> DokumenSections { get; set; }

    public DbSet<AdobeCredential> AdobeCredentials { get; set; }
    public DbSet<AdobeApiLog> AdobeApiLogs { get; set; }
    public DbSet<Antrian> Antrians { get; set; }
    public DbSet<Aturan> Aturans { get; set; }
    public DbSet<AturanDetail> AturanDetails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Buku>(entity =>
        {
            entity.Property(e => e.BukuCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.BukuUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<Bab>(entity =>
        {
            entity.HasKey(e => e.BabId);
        });

        modelBuilder.Entity<Dokumen>(entity =>
        {
            entity.Property(e => e.DokumenCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.DokumenUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<DokumenElemen>(entity =>
        {
            entity.HasKey(e => e.DokumenElemenId);
            entity.Property(e => e.DokumenElemenJsonTree).HasColumnType("json");
        });

        modelBuilder.Entity<DokumenMedia>(entity =>
        {
            entity.HasKey(e => e.DokumenMediaId);
        });

        modelBuilder.Entity<DokumenSection>(entity =>
        {
            entity.HasKey(e => e.DsecId);
        });


        modelBuilder.Entity<AdobeCredential>(entity =>
        {
            entity.HasKey(e => e.AdobeCredentialsId);
            entity.Property(e => e.AdobeCredentialsCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.AdobeCredentialsUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<AdobeApiLog>(entity =>
        {
            entity.HasKey(e => e.AdobeApiLogsId);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<Antrian>(entity =>
        {
            entity.HasKey(e => e.AntrianId);
            entity.Property(e => e.AntrianCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.AntrianUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<Aturan>(entity =>
        {
            entity.HasKey(e => e.AturanId);
            entity.HasIndex(e => e.AturanVersi).IsUnique();
            entity.Property(e => e.AturanCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.AturanUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<AturanDetail>(entity =>
        {
            entity.HasKey(e => e.AturanDetailId);
            entity.HasOne(e => e.Aturan)
                .WithMany()
                .HasForeignKey(e => e.AturanId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
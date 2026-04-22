using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

public class KorektorBukuDbContext : DbContext
{
    public KorektorBukuDbContext(DbContextOptions<KorektorBukuDbContext> options) : base(options) { }

    public DbSet<Buku> Bukus { get; set; }
    public DbSet<Bab> Babs { get; set; }
    public DbSet<Dokumen> Dokumens { get; set; }
    public DbSet<DokumenElemen> DokumenElemens { get; set; }
    public DbSet<DokumenElemenVisual> DokumenElemenVisuals { get; set; }
    public DbSet<DokumenSection> DokumenSections { get; set; }
    public DbSet<DokumenPart> DokumenParts { get; set; }
    public DbSet<DokumenNote> DokumenNotes { get; set; }
    public DbSet<DokumenFormatParagraf> DokumenFormatParagrafs { get; set; }
    public DbSet<DokumenFormatTable> DokumenFormatTables { get; set; }
    public DbSet<DokumenFormatText> DokumenFormatTexts { get; set; }
    public DbSet<DokumenFormatDrawing> DokumenFormatDrawings { get; set; }

    public DbSet<AdobeCredential> AdobeCredentials { get; set; }
    public DbSet<AdobeApiLog> AdobeApiLogs { get; set; }
    public DbSet<LlmApiLog> LlmApiLogs { get; set; }
    public DbSet<Antrian> Antrians { get; set; }
    public DbSet<Aturan> Aturans { get; set; }
    public DbSet<AturanDetail> AturanDetails { get; set; }
    public DbSet<Kesalahan> Kesalahans { get; set; }
    public DbSet<KesalahanDetail> KesalahanDetails { get; set; }
    public DbSet<GeminiApiKey> GeminiApiKeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isSqlite = Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        modelBuilder.Entity<Buku>(entity =>
        {
            entity.Property(e => e.BukuCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.BukuUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.BukuReportPath)
                .HasMaxLength(255);

            entity.Property(e => e.BukuDocxZipPath)
                .HasMaxLength(100);

            entity.Property(e => e.BukuPdfZipPath)
                .HasMaxLength(100);

            entity.Property(e => e.BukuAturanVersiValidasi)
                .HasMaxLength(255);
        });

        modelBuilder.Entity<Bab>(entity =>
        {
            entity.HasKey(e => e.BabId);
            entity.Property(e => e.BabImagesPath).HasMaxLength(255);
        });

        modelBuilder.Entity<Dokumen>(entity =>
        {
            entity.Property(e => e.DokumenCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.DokumenUpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.DokumenReportPath)
                .HasMaxLength(255);
        });

        modelBuilder.Entity<DokumenElemen>(entity =>
        {
            entity.HasKey(e => e.DelemenId);
            entity.Property(e => e.DelemenJsonTree).HasColumnType("longtext");
            entity.HasOne(e => e.Part)
                .WithMany(p => p.Elements)
                .HasForeignKey(e => e.DpartId);
        });

        modelBuilder.Entity<DokumenElemenVisual>(entity =>
        {
            entity.HasKey(e => e.DevId);
            if (!isSqlite)
            {
                entity.Property(e => e.DevRefTipe)
                    .HasColumnType("enum('dokumen','bab','buku','aturan')");
            }
            entity.Property(e => e.DevText).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenSection>(entity =>
        {
            entity.HasKey(e => e.DsecId);
            if (!isSqlite)
            {
                entity.Property(e => e.DsecRefTipe)
                    .HasColumnType("enum('bab','dokumen','buku','aturan')");
            }
        });

        modelBuilder.Entity<DokumenPart>(entity =>
        {
            entity.HasKey(e => e.DpartId);
            entity.HasOne(e => e.Section)
                .WithMany(s => s.Parts)
                .HasForeignKey(e => e.DsecId);
        });

        modelBuilder.Entity<DokumenNote>(entity =>
        {
            entity.HasKey(e => e.DnoteId);
            if (!isSqlite)
            {
                entity.Property(e => e.DnoteRefTipe)
                    .HasColumnType("enum('dokumen','bab','aturan')");
            }
            entity.Property(e => e.DnoteJsonTree).HasColumnType("longtext");
            entity.HasOne(e => e.Elemen)
                .WithMany(e => e.Notes)
                .HasForeignKey(e => e.DelemenId);
        });

        modelBuilder.Entity<DokumenFormatParagraf>(entity =>
        {
            entity.HasKey(e => e.DfpId);
            entity.Property(e => e.DfpNumprJson).HasColumnType("longtext");
            entity.Property(e => e.DfpTabsJson).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenFormatTable>(entity =>
        {
            entity.HasKey(e => e.DftId);
        });

        modelBuilder.Entity<DokumenFormatText>(entity =>
        {
            entity.HasKey(e => e.DftxId);
        });

        modelBuilder.Entity<DokumenFormatDrawing>(entity =>
        {
            entity.HasKey(e => e.DfdrId);
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

        modelBuilder.Entity<LlmApiLog>(entity =>
        {
            entity.HasKey(e => e.LogId);
        });

        modelBuilder.Entity<Antrian>(entity =>
        {
            entity.HasKey(e => e.AntrianId);
            if (!isSqlite)
            {
                entity.Property(e => e.AntrianTipe)
                    .HasColumnType("enum('dokumen','buku','aturan')");
            }
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
            if (!isSqlite)
            {
                entity.Property(e => e.AturanStatus)
                    .HasColumnType("enum('diproses','menunggu_review','tidak_aktif','aktif','gagal')");
            }
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

        modelBuilder.Entity<Kesalahan>(entity =>
        {
            entity.HasKey(e => e.KesalahanId);
            entity.Property(e => e.KesalahanLokasi).HasColumnType("varchar(255)");
            entity.Property(e => e.KesalahanRefTipe)
                .HasConversion(
                    v => v.ToString(),
                    v => string.Equals(v, "buku", StringComparison.OrdinalIgnoreCase)
                        ? KesalahanRefTipe.bab
                        : Enum.Parse<KesalahanRefTipe>(v, true));
            if (!isSqlite)
            {
                entity.Property(e => e.KesalahanRefTipe)
                    .HasColumnType("enum('bab','dokumen')");
            }
            entity.HasMany(e => e.Details)
                .WithOne(d => d.Kesalahan)
                .HasForeignKey(d => d.KesalahanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KesalahanDetail>(entity =>
        {
            entity.HasKey(e => e.KesalahanDetailId);
            entity.Property(e => e.KesalahanDetailSteps).HasColumnType("longtext");
        });

        modelBuilder.Entity<GeminiApiKey>(entity =>
        {
            entity.HasKey(e => e.GeminiApiKeyId);
            entity.Property(e => e.GeminiApiKeyCreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.GeminiApiKeyUpdatedAt)
                .ValueGeneratedOnAddOrUpdate();
        });
    }
}

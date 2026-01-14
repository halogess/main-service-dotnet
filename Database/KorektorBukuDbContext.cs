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
    public DbSet<DokumenPart> DokumenParts { get; set; }
    public DbSet<DokumenNote> DokumenNotes { get; set; }
    public DbSet<DokumenFormatParagraf> DokumenFormatParagrafs { get; set; }
    public DbSet<DokumenFormatTable> DokumenFormatTables { get; set; }
    public DbSet<DokumenFormatTableRow> DokumenFormatTableRows { get; set; }
    public DbSet<DokumenFormatTableCell> DokumenFormatTableCells { get; set; }
    public DbSet<DokumenFormatText> DokumenFormatTexts { get; set; }
    public DbSet<DokumenFormatDrawing> DokumenFormatDrawings { get; set; }
    public DbSet<DokumenFormatField> DokumenFormatFields { get; set; }

    public DbSet<AdobeCredential> AdobeCredentials { get; set; }
    public DbSet<AdobeApiLog> AdobeApiLogs { get; set; }
    public DbSet<Antrian> Antrians { get; set; }
    public DbSet<Aturan> Aturans { get; set; }
    public DbSet<AturanDetail> AturanDetails { get; set; }
    public DbSet<Kesalahan> Kesalahans { get; set; }
    public DbSet<GeminiApiKey> GeminiApiKeys { get; set; }

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
            entity.HasKey(e => e.DelemenId);
            entity.Property(e => e.DelemenJsonTree).HasColumnType("longtext");
            entity.HasOne(e => e.Part)
                .WithMany(p => p.Elements)
                .HasForeignKey(e => e.DpartId);
        });

        modelBuilder.Entity<DokumenMedia>(entity =>
        {
            entity.HasKey(e => e.DokumenMediaId);
        });

        modelBuilder.Entity<DokumenSection>(entity =>
        {
            entity.HasKey(e => e.DsecId);
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
            entity.Property(e => e.DnoteJsonTree).HasColumnType("longtext");
            entity.HasOne(e => e.Elemen)
                .WithMany(e => e.Notes)
                .HasForeignKey(e => e.DelemenId);
        });

        modelBuilder.Entity<DokumenFormatParagraf>(entity =>
        {
            entity.HasKey(e => e.DfpId);
            entity.Property(e => e.DfpNumprJson).HasColumnType("longtext");
            entity.Property(e => e.DfpPbdrJson).HasColumnType("longtext");
            entity.Property(e => e.DfpShdJson).HasColumnType("longtext");
            entity.Property(e => e.DfpTabsJson).HasColumnType("longtext");
            entity.Property(e => e.DfpCnfStyleJson).HasColumnType("longtext");
            entity.Property(e => e.DfpParaMarkRprJson).HasColumnType("longtext");
            entity.Property(e => e.DfpPprChangeJson).HasColumnType("longtext");
            entity.Property(e => e.DfpRawPprXml).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenFormatTable>(entity =>
        {
            entity.HasKey(e => e.DftId);
            entity.Property(e => e.DftTblBordersJson).HasColumnType("longtext");
            entity.Property(e => e.DftTblpprJson).HasColumnType("longtext");
            entity.Property(e => e.DftRawTblprXml).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenFormatTableRow>(entity =>
        {
            entity.HasKey(e => e.DftrId);
            entity.Property(e => e.DftrRawTrprXml).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenFormatTableCell>(entity =>
        {
            entity.HasKey(e => e.DftcId);
            entity.Property(e => e.DftcRawTcprXml).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenFormatText>(entity =>
        {
            entity.HasKey(e => e.DftxId);
            entity.Property(e => e.DftxRawRprXml).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenFormatDrawing>(entity =>
        {
            entity.HasKey(e => e.DfdrId);
            entity.Property(e => e.DfdrAnchorJson).HasColumnType("longtext");
            entity.Property(e => e.DfdrWrapJson).HasColumnType("longtext");
            entity.Property(e => e.DfdrRawDrawingXml).HasColumnType("longtext");
        });

        modelBuilder.Entity<DokumenFormatField>(entity =>
        {
            entity.HasKey(e => e.DffdId);
            entity.Property(e => e.DffdInstrText).HasColumnType("text");
            entity.Property(e => e.DffdResultText).HasColumnType("text");
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

        modelBuilder.Entity<Kesalahan>(entity =>
        {
            entity.HasKey(e => e.KesalahanId);
            entity.Property(e => e.KesalahanPenjelasan).HasColumnType("text");
            entity.Property(e => e.KesalahanBboxVisual).HasColumnType("json");
            entity.Property(e => e.KesalahanSteps)
                .HasColumnType("json")
                .HasDefaultValueSql("(JSON_ARRAY())");
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

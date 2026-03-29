using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

public class SttsDbContext : DbContext
{
    public SttsDbContext(DbContextOptions<SttsDbContext> options) : base(options) { }
    
    public DbSet<Mahasiswa> Mahasiswas { get; set; }
    public DbSet<Jurusan> Jurusans { get; set; }
    public DbSet<Proposal> Proposals { get; set; }
    public DbSet<Dosen> Dosens { get; set; }
    public DbSet<Karyawan> Karyawans { get; set; }
    

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Konfigurasi untuk entitas Mahasiswa
        modelBuilder.Entity<Mahasiswa>(entity =>
        {
            entity.ToTable("mahasiswa");
            entity.HasKey(e => e.MhsNrp);

             entity.Property(e => e.MhsNrp)
                      .HasColumnName("mhs_nrp")
                      .HasMaxLength(11);

                entity.Property(e => e.MhsNama)
                      .HasColumnName("mhs_nama")
                      .HasMaxLength(200);

                entity.Property(e => e.MhsEmail)
                      .HasColumnName("mhs_email_stts")
                      .HasMaxLength(255);

                entity.Property(e => e.MhsHp)
                      .HasColumnName("mhs_hp")
                      .HasMaxLength(255);

                entity.Property(e => e.MhsStatus)
                      .HasColumnName("mhs_status")
                      .HasColumnType("int")
                      .IsRequired(false);

                entity.Property(e => e.JurKode)
                      .HasColumnName("jur_kode")
                      .HasMaxLength(2);

                entity.Property(e => e.MhsIpk)
                      .HasColumnName("mhs_ipk")
                      .HasPrecision(3, 2);

                entity.Property(e => e.MhsLulusTahun)
                      .HasColumnName("mhs_lulus_tahun")
                      .HasMaxLength(4);

                entity.Property(e => e.MhsAngkatan)
                      .HasColumnName("mhs_angkatan");
            
        });

        modelBuilder.Entity<Jurusan>(entity =>
        {
            entity.ToTable("aka_jurusan");
            entity.HasKey(e => e.JurKode);
            entity.Property(e => e.JurKode).HasColumnName("jur_kode").HasMaxLength(2);
            entity.Property(e => e.JurNama).HasColumnName("jur_nama").HasMaxLength(50);
            entity.Property(e => e.JurSingkat).HasColumnName("jur_singkat").HasMaxLength(20);
            entity.Property(e => e.JurGelar).HasColumnName("jur_gelar").HasMaxLength(50);
            entity.Property(e => e.JurFakultas).HasColumnName("jur_fakultas").HasMaxLength(50);
            entity.Property(e => e.JurStatus).HasColumnName("jur_status");
        });

        modelBuilder.Entity<Proposal>(entity =>
        {
            entity.ToTable("aka_ta_proposal");
            entity.HasKey(e => e.ProposalKode);
        });

        modelBuilder.Entity<Dosen>(entity =>
        {
            entity.ToTable("tk_dosen");
            entity.HasKey(e => e.DosenKode);
            entity.Property(e => e.DosenKode).HasColumnName("dosen_kode").HasMaxLength(10);
            entity.Property(e => e.DosenNamaSk).HasColumnName("dosen_nama_sk").HasMaxLength(255);
            entity.Property(e => e.DosenStatus).HasColumnName("dosen_status");
            entity.Property(e => e.KaryawanNip).HasColumnName("karyawan_nip").HasMaxLength(15);
        });

        modelBuilder.Entity<Karyawan>(entity =>
        {
            entity.ToTable("tk_karyawan");
            entity.HasKey(e => e.KaryawanNip);
            entity.Property(e => e.KaryawanNip).HasColumnName("karyawan_nip").HasMaxLength(15);
            entity.Property(e => e.KaryawanStatus).HasColumnName("karyawan_status");
        });
    }
}

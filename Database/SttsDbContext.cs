using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

public class SttsDbContext : DbContext
{
    public SttsDbContext(DbContextOptions<SttsDbContext> options) : base(options) { }
    
    public DbSet<Mahasiswa> Mahasiswas { get; set; }
    // public DbSet<Jurusan> Jurusan { get; set; }
    

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
                      .HasColumnName("mhs_email")
                      .HasMaxLength(255);

                entity.Property(e => e.MhsHp)
                      .HasColumnName("mhs_hp")
                      .HasMaxLength(255);

                entity.Property(e => e.MhsStatus)
                      .HasColumnName("mhs_status");

                entity.Property(e => e.JurKode)
                      .HasColumnName("jur_kode")
                      .HasMaxLength(2);

                entity.Property(e => e.MhsIpk)
                      .HasColumnName("mhs_ipk")
                      .HasPrecision(3, 2);
            
        });

        // Anda bisa menambahkan konfigurasi untuk entitas lain di sini
        // modelBuilder.Entity<Jurusan>(...);
    }
}
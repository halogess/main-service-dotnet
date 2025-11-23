using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestingController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;

    public TestingController(KorektorBukuDbContext db, SttsDbContext sttsDb)
    {
        _db = db;
        _sttsDb = sttsDb;
    }

    [HttpGet("bulk-insert-buku-aktif")]
    public async Task<IActionResult> BulkInsertBukuAktif()
    {
        var inserted = 0;
        var skipped = 0;
        var errors = new List<string>();

        // Ambil 50 mahasiswa aktif yang punya proposal (perpanjangan 0, terbaru) dan jurusan aktif
        var mahasiswaWithProposal = await (
            from m in _sttsDb.Mahasiswas
            where m.MhsStatus != null && m.MhsStatus == 1
            join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode
            where j.JurStatus == 1
            join p in _sttsDb.Proposals on m.MhsNrp equals p.MhsNrp
            where p.ProposalPerpanjangan == 0
            group p by new { m.MhsNrp, m.MhsNama, m.JurKode } into g
            select new {
                Nrp = g.Key.MhsNrp,
                Nama = g.Key.MhsNama,
                JurKode = g.Key.JurKode,
                Judul = g.OrderByDescending(p => p.ProposalTglDoc).First().ProposalJudulBaru
            }
        ).OrderByDescending(x => x.Nrp)
        .Take(50)
        .ToListAsync();

        var random = new Random();
        var statuses = new[] { "dalam_antrian", "diproses", "lolos", "tidak_lolos", "dibatalkan" };

        // Insert buku untuk setiap mahasiswa x setiap status
        foreach (var mhs in mahasiswaWithProposal)
        {
            foreach (var status in statuses)
        {
            try
            {
                var judul = mhs.Judul ?? "Judul Belum Tersedia";
                var jumlahBab = random.Next(3, 8);
                var randomDays = random.Next(1, 365);

                // Insert buku
                var buku = new Buku
                {
                    MhsNrp = mhs.Nrp,
                    BukuJudul = judul,
                    BukuStatus = status,
                    BukuJumlahBab = jumlahBab,
                    BukuSkor = status == "lolos" ? random.Next(75, 95) : (status == "tidak_lolos" ? random.Next(50, 70) : null),
                    BukuJumlahKesalahan = status == "lolos" ? random.Next(1, 10) : (status == "tidak_lolos" ? random.Next(15, 35) : null),
                    BukuCreatedAt = DateTime.Now.AddDays(-randomDays),
                    BukuUpdatedAt = DateTime.Now
                };

                _db.Bukus.Add(buku);
                await _db.SaveChangesAsync();

                // Buat direktori dan file dummy
                var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
                var docxDir = Path.Combine(storagePath, "buku", mhs.Nrp, buku.BukuId.ToString(), "docx");
                var pdfDir = Path.Combine(storagePath, "buku", mhs.Nrp, buku.BukuId.ToString(), "pdf");

                Directory.CreateDirectory(docxDir);
                if (status == "lolos" || status == "tidak_lolos")
                    Directory.CreateDirectory(pdfDir);

                // Insert bab-bab
                for (byte i = 1; i <= jumlahBab; i++)
                {
                    var bab = new Bab
                    {
                        BukuId = (uint)buku.BukuId,
                        BabOrder = i,
                        BabFilename = $"Bab_{i}.docx",
                        BabDocxPath = $"buku/{mhs.Nrp}/{buku.BukuId}/docx/Bab_{i}.docx",
                        BabPdfPath = status == "lolos" || status == "tidak_lolos" ? $"buku/{mhs.Nrp}/{buku.BukuId}/pdf/Bab_{i}.pdf" : null
                    };
                    _db.Babs.Add(bab);

                    // Buat file dummy
                    var docxFile = Path.Combine(docxDir, $"Bab_{i}.docx");
                    await System.IO.File.WriteAllTextAsync(docxFile, $"Dummy content for {mhs.Nrp} - Bab {i}");

                    if (status == "lolos" || status == "tidak_lolos")
                    {
                        var pdfFile = Path.Combine(pdfDir, $"Bab_{i}.pdf");
                        await System.IO.File.WriteAllTextAsync(pdfFile, $"Dummy PDF for {mhs.Nrp} - Bab {i}");
                    }
                }
                await _db.SaveChangesAsync();

                inserted++;
            }
            catch (Exception ex)
            {
                errors.Add($"{mhs.Nrp} - {status}: {ex.Message}");
                skipped++;
            }
            }
        }

        return Ok(new
        {
            message = "Bulk insert mahasiswa aktif selesai",
            total_mahasiswa = mahasiswaWithProposal.Count,
            inserted = inserted,
            skipped = skipped,
            errors = errors
        });
    }

    [HttpGet("bulk-insert-buku-nonaktif-mhs")]
    public async Task<IActionResult> BulkInsertBukuNonaktifMhs()
    {
        var inserted = 0;
        var skipped = 0;
        var errors = new List<string>();

        var random = new Random();
        var statusBukuOptions = new[] { "lolos", "tidak_lolos", "dalam_antrian", "diproses" };
        
        // Ambil proposal dulu (lebih sedikit)
        var proposals = await _sttsDb.Proposals
            .Where(p => p.ProposalJudulBaru != null && p.ProposalPerpanjangan == 0)
            .ToListAsync();
        
        Console.WriteLine($"[TESTING] Total proposals: {proposals.Count}");
        
        // Ambil NRP yang punya proposal
        var nrpsWithProposal = proposals.Select(p => p.MhsNrp).Distinct().ToList();
        
        // Filter mahasiswa non-aktif yang punya proposal
        var mahasiswaNonAktif = await _sttsDb.Mahasiswas
            .Where(m => nrpsWithProposal.Contains(m.MhsNrp) && (!m.MhsStatus.HasValue || m.MhsStatus.Value != 1))
            .ToListAsync();
        
        Console.WriteLine($"[TESTING] Mahasiswa non-aktif with proposal: {mahasiswaNonAktif.Count}");
        
        // Join dan ambil proposal terbaru per mahasiswa (client-side)
        var allMahasiswaWithProposal = (
            from m in mahasiswaNonAktif
            join p in proposals on m.MhsNrp equals p.MhsNrp
            group p by new { m.MhsNrp, m.MhsStatus } into g
            let latestProposal = g.OrderByDescending(p => p.ProposalTglDoc).First()
            select new {
                MhsNrp = g.Key.MhsNrp,
                MhsStatus = g.Key.MhsStatus,
                ProposalJudul = latestProposal.ProposalJudulBaru
            }
        ).OrderByDescending(x => x.MhsNrp).ToList();
        
        // Ambil 10 per status
        var mahasiswaWithProposal = allMahasiswaWithProposal
            .GroupBy(m => m.MhsStatus)
            .SelectMany(g => g.Take(10))
            .ToList();
        
        Console.WriteLine($"[TESTING] Total mahasiswa non-aktif with proposal: {mahasiswaWithProposal.Count}");
        
        var statusDistribution = mahasiswaWithProposal.GroupBy(m => m.MhsStatus).Select(g => new { Status = g.Key, Count = g.Count() }).ToList();
        Console.WriteLine($"[TESTING] Status distribution: {string.Join(", ", statusDistribution.Select(s => $"{s.Status?.ToString() ?? "null"}={s.Count}"))}");
        
        // Group by status
        var groupedByStatus = mahasiswaWithProposal.GroupBy(m => m.MhsStatus).ToList();
        
        foreach (var group in groupedByStatus)
        {
            var mhsStatus = group.Key;
            var insertedForStatus = 0;
            var targetPerStatus = 10;
            
            Console.WriteLine($"[TESTING] Status {mhsStatus?.ToString() ?? "null"}: Found {group.Count()} mahasiswa");
            
            foreach (var mhs in group)
            {
                if (insertedForStatus >= targetPerStatus)
                    break;
                
                try
                {
                    // Cek apakah sudah punya buku
                    var hasBuku = await _db.Bukus.AnyAsync(b => b.MhsNrp == mhs.MhsNrp);
                    if (hasBuku)
                    {
                        Console.WriteLine($"[TESTING] Skip {mhs.MhsNrp} - already has buku");
                        skipped++;
                        continue;
                    }
                    
                    var judul = mhs.ProposalJudul ?? "Judul Belum Tersedia";
                    
                    // Insert 1-3 buku per mahasiswa
                    var jumlahBuku = random.Next(1, 4);
                    for (int i = 0; i < jumlahBuku; i++)
                    {
                        var statusBuku = statusBukuOptions[random.Next(statusBukuOptions.Length)];
                        var jumlahBab = random.Next(3, 8);
                        
                        var buku = new Buku
                        {
                            MhsNrp = mhs.MhsNrp!,
                            BukuJudul = judul + (i > 0 ? $" (Revisi {i})" : ""),
                            BukuStatus = statusBuku,
                            BukuJumlahBab = jumlahBab,
                            BukuSkor = statusBuku == "lolos" ? random.Next(75, 95) : (statusBuku == "tidak_lolos" ? random.Next(50, 70) : null),
                            BukuJumlahKesalahan = statusBuku == "lolos" ? random.Next(1, 10) : (statusBuku == "tidak_lolos" ? random.Next(15, 35) : null),
                            BukuCreatedAt = DateTime.Now.AddDays(-random.Next(1, 365)),
                            BukuUpdatedAt = DateTime.Now
                        };
                        
                        _db.Bukus.Add(buku);
                        await _db.SaveChangesAsync();
                        
                        insertedForStatus++;
                        Console.WriteLine($"[TESTING] Inserted buku for NRP: {mhs.MhsNrp}, Status: {mhs.MhsStatus?.ToString() ?? "null"}, Judul: {judul}, Buku ID: {buku.BukuId}");
                        
                        // Buat direktori dan file dummy
                        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
                        var docxDir = Path.Combine(storagePath, "buku", mhs.MhsNrp!, buku.BukuId.ToString(), "docx");
                        var pdfDir = Path.Combine(storagePath, "buku", mhs.MhsNrp!, buku.BukuId.ToString(), "pdf");
                        
                        Directory.CreateDirectory(docxDir);
                        if (statusBuku == "lolos" || statusBuku == "tidak_lolos")
                            Directory.CreateDirectory(pdfDir);
                        
                        // Insert bab-bab
                        for (byte j = 1; j <= jumlahBab; j++)
                        {
                            var bab = new Bab
                            {
                                BukuId = (uint)buku.BukuId,
                                BabOrder = j,
                                BabFilename = $"Bab_{j}.docx",
                                BabDocxPath = $"buku/{mhs.MhsNrp}/{buku.BukuId}/docx/Bab_{j}.docx",
                                BabPdfPath = statusBuku == "lolos" || statusBuku == "tidak_lolos" ? $"buku/{mhs.MhsNrp}/{buku.BukuId}/pdf/Bab_{j}.pdf" : null
                            };
                            _db.Babs.Add(bab);
                            
                            // Buat file dummy
                            var docxFile = Path.Combine(docxDir, $"Bab_{j}.docx");
                            await System.IO.File.WriteAllTextAsync(docxFile, $"Dummy content for {mhs.MhsNrp} - Bab {j}");
                            
                            if (statusBuku == "lolos" || statusBuku == "tidak_lolos")
                            {
                                var pdfFile = Path.Combine(pdfDir, $"Bab_{j}.pdf");
                                await System.IO.File.WriteAllTextAsync(pdfFile, $"Dummy PDF for {mhs.MhsNrp} - Bab {j}");
                            }
                        }
                        await _db.SaveChangesAsync();
                        inserted++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{mhs.MhsNrp}: {ex.Message}");
                    skipped++;
                }
            }
        }

        Console.WriteLine($"[TESTING] Total inserted: {inserted}, skipped: {skipped}");
        
        return Ok(new
        {
            message = "Bulk insert mahasiswa non-aktif selesai",
            inserted = inserted,
            skipped = skipped,
            errors = errors.Take(10).ToList()
        });
    }

    [HttpDelete("hapus-buku-mahasiswa/{nrp}")]
    public async Task<IActionResult> HapusBukuMahasiswa(string nrp)
    {
        var bukus = await _db.Bukus.Where(b => b.MhsNrp == nrp).ToListAsync();
        var babs = await _db.Babs.Where(b => bukus.Select(bk => bk.BukuId).Contains((int)b.BukuId)).ToListAsync();
        
        _db.Babs.RemoveRange(babs);
        _db.Bukus.RemoveRange(bukus);
        await _db.SaveChangesAsync();
        
        // Hapus direktori
        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var mahasiswaDir = Path.Combine(storagePath, "buku", nrp);
        
        if (Directory.Exists(mahasiswaDir))
        {
            Directory.Delete(mahasiswaDir, true);
        }
        
        return Ok(new { 
            message = $"Buku mahasiswa {nrp} berhasil dihapus", 
            deleted_buku = bukus.Count, 
            deleted_bab = babs.Count,
            deleted_directory = mahasiswaDir
        });
    }

    [HttpGet("clear-all-buku")]
    public async Task<IActionResult> ClearAllBuku()
    {
        var allBuku = await _db.Bukus.ToListAsync();
        var allBab = await _db.Babs.ToListAsync();
        
        _db.Babs.RemoveRange(allBab);
        _db.Bukus.RemoveRange(allBuku);
        await _db.SaveChangesAsync();
        
        return Ok(new { message = "All buku cleared", deleted_buku = allBuku.Count, deleted_bab = allBab.Count });
    }

    [HttpPost("bulk-insert-buku")]
    public async Task<IActionResult> BulkInsertBuku([FromBody] BulkInsertRequest request)
    {

        var inserted = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var nrp in request.nrp_list)
        {
            try
            {
                // Cek apakah mahasiswa ada
                var mahasiswa = await _sttsDb.Mahasiswas.FirstOrDefaultAsync(m => m.MhsNrp == nrp);
                if (mahasiswa == null)
                {
                    errors.Add($"{nrp}: Mahasiswa tidak ditemukan");
                    skipped++;
                    continue;
                }

                // Cek apakah sudah punya buku
                var existingBuku = await _db.Bukus.AnyAsync(b => b.MhsNrp == nrp);
                if (existingBuku)
                {
                    errors.Add($"{nrp}: Sudah memiliki buku");
                    skipped++;
                    continue;
                }

                // Ambil judul dari proposal
                var judul = await _sttsDb.Proposals
                    .Where(p => p.MhsNrp == nrp && p.ProposalPerpanjangan == 0)
                    .OrderByDescending(p => p.ProposalTglDoc)
                    .Select(p => p.ProposalJudulBaru)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(judul))
                {
                    judul = "Judul Belum Tersedia";
                }

                // Insert buku dengan status sesuai request
                var buku = new Buku
                {
                    MhsNrp = nrp,
                    BukuJudul = judul,
                    BukuStatus = request.status ?? "lolos",
                    BukuJumlahBab = request.jumlah_bab ?? 5,
                    BukuSkor = request.status == "lolos" ? 85 : (request.status == "tidak_lolos" ? 65 : null),
                    BukuJumlahKesalahan = request.status == "lolos" ? 5 : (request.status == "tidak_lolos" ? 25 : null),
                    BukuCreatedAt = DateTime.Now,
                    BukuUpdatedAt = DateTime.Now
                };

                _db.Bukus.Add(buku);
                await _db.SaveChangesAsync();
                inserted++;
            }
            catch (Exception ex)
            {
                errors.Add($"{nrp}: {ex.Message}");
                skipped++;
            }
        }

        return Ok(new
        {
            message = "Bulk insert selesai",
            inserted = inserted,
            skipped = skipped,
            errors = errors
        });
    }
}

public class BulkInsertRequest
{
    public List<string> nrp_list { get; set; } = new();
    public string? status { get; set; } = "lolos";
    public int? jumlah_bab { get; set; } = 5;
}

public class BulkInsertLulusRequest
{
    public string? status { get; set; } = "lolos";
    public int? jumlah_bab { get; set; } = 5;
}

using Microsoft.AspNetCore.Mvc;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestingController : ControllerBase
{
    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;
    private readonly IDocxExtractionService _docxExtraction;

    public TestingController(
        KorektorBukuDbContext db,
        SttsDbContext sttsDb,
        IDocxExtractionService docxExtraction)
    {
        _db = db;
        _sttsDb = sttsDb;
        _docxExtraction = docxExtraction;
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

    [HttpGet("reset-visual/{tipe}/{id}")]
    public async Task<IActionResult> ResetVisual(string tipe, int id)
    {
        if (tipe != "buku" && tipe != "dokumen")
            return BadRequest(new { message = "Tipe harus 'buku' atau 'dokumen'" });

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var deletedFolders = new List<string>();

        try
        {
            if (tipe == "buku")
            {
                var buku = await _db.Bukus.FirstOrDefaultAsync(b => b.BukuId == id);
                if (buku == null)
                    return NotFound(new { message = "Buku tidak ditemukan" });

                var imageDir = Path.Combine(storagePath, "buku", buku.MhsNrp, id.ToString(), "images");
                var imageResultDir = Path.Combine(storagePath, "buku", buku.MhsNrp, id.ToString(), "image-result");
                var imageResultPdfDir = Path.Combine(storagePath, "buku", buku.MhsNrp, id.ToString(), "image-result-pdf");
                var imageResultOcrDir = Path.Combine(storagePath, "buku", buku.MhsNrp, id.ToString(), "image-result-ocr");

                if (Directory.Exists(imageDir))
                {
                    Directory.Delete(imageDir, true);
                    deletedFolders.Add(imageDir);
                }
                if (Directory.Exists(imageResultDir))
                {
                    Directory.Delete(imageResultDir, true);
                    deletedFolders.Add(imageResultDir);
                }
                if (Directory.Exists(imageResultPdfDir))
                {
                    Directory.Delete(imageResultPdfDir, true);
                    deletedFolders.Add(imageResultPdfDir);
                }
                if (Directory.Exists(imageResultOcrDir))
                {
                    Directory.Delete(imageResultOcrDir, true);
                    deletedFolders.Add(imageResultOcrDir);
                }

                var updated = await _db.Antrians
                    .Where(a => a.BukuId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.AntrianValidationStatus, "in_queue"));

                return Ok(new { message = "Reset visual berhasil", deleted_folders = deletedFolders, updated_antrian = updated });
            }
            else
            {
                var dokumen = await _db.Dokumens.FirstOrDefaultAsync(d => d.DokumenId == id);
                if (dokumen == null)
                    return NotFound(new { message = "Dokumen tidak ditemukan" });

                var imageDir = Path.Combine(storagePath, "dokumen", dokumen.MhsNrp, id.ToString(), "images");
                var imageResultDir = Path.Combine(storagePath, "dokumen", dokumen.MhsNrp, id.ToString(), "image-result");
                var imageResultPdfDir = Path.Combine(storagePath, "dokumen", dokumen.MhsNrp, id.ToString(), "image-result-pdf");
                var imageResultOcrDir = Path.Combine(storagePath, "dokumen", dokumen.MhsNrp, id.ToString(), "image-result-ocr");

                if (Directory.Exists(imageDir))
                {
                    Directory.Delete(imageDir, true);
                    deletedFolders.Add(imageDir);
                }
                if (Directory.Exists(imageResultDir))
                {
                    Directory.Delete(imageResultDir, true);
                    deletedFolders.Add(imageResultDir);
                }
                if (Directory.Exists(imageResultPdfDir))
                {
                    Directory.Delete(imageResultPdfDir, true);
                    deletedFolders.Add(imageResultPdfDir);
                }
                if (Directory.Exists(imageResultOcrDir))
                {
                    Directory.Delete(imageResultOcrDir, true);
                    deletedFolders.Add(imageResultOcrDir);
                }

                var updated = await _db.Antrians
                    .Where(a => a.DokumenId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.AntrianValidationStatus, "in_queue"));

                return Ok(new { message = "Reset visual berhasil", deleted_folders = deletedFolders, updated_antrian = updated });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Gagal reset visual", error = ex.Message, stack = ex.StackTrace });
        }
    }

    [HttpPost("extract-docx/{dokumenId}")]
    public async Task<IActionResult> ExtractDocx(int dokumenId)
    {
        try
        {
            var dokumen = await _db.Dokumens.FindAsync(dokumenId);
            if (dokumen == null)
                return NotFound(new { message = "Dokumen tidak ditemukan" });

            if (string.IsNullOrEmpty(dokumen.DokumenDocxPath))
                return BadRequest(new { message = "Path DOCX tidak ditemukan" });

            var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
            var docxPath = Path.Combine(storagePath, dokumen.DokumenDocxPath);

            if (!System.IO.File.Exists(docxPath))
                return NotFound(new { message = "File DOCX tidak ditemukan" });

            await _docxExtraction.ExtractDocxToDatabase(docxPath, dokumenId);

            return Ok(new { message = "Ekstraksi DOCX berhasil", dokumen_id = dokumenId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Gagal ekstraksi DOCX", error = ex.Message });
        }
    }

    [HttpPost("re-extract-docx/{dokumenId}")]
    public async Task<IActionResult> ReExtractDocx(int dokumenId)
    {
        try
        {
            var dokumen = await _db.Dokumens.FindAsync(dokumenId);
            if (dokumen == null)
                return NotFound(new { message = "Dokumen tidak ditemukan" });

            if (string.IsNullOrEmpty(dokumen.DokumenDocxPath))
                return BadRequest(new { message = "Path DOCX tidak ditemukan" });

            var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
            var docxPath = Path.Combine(storagePath, dokumen.DokumenDocxPath);

            if (!System.IO.File.Exists(docxPath))
                return NotFound(new { message = "File DOCX tidak ditemukan" });

            // Delete existing elements and media
            // Get all part IDs for this dokumen through sections
            var partIds = await _db.DokumenParts
                .Where(p => p.Section != null && p.Section.DsecRefTipe == "dokumen" && p.Section.DsecRefId == dokumenId)
                .Select(p => p.DpartId)
                .ToListAsync();
            
            var existingElements = _db.DokumenElemens.Where(e => e.DpartId.HasValue && partIds.Contains(e.DpartId.Value));
            _db.DokumenElemens.RemoveRange(existingElements);
            
            var existingMedia = _db.DokumenMedias.Where(m => m.DokumenId == dokumenId);
            _db.DokumenMedias.RemoveRange(existingMedia);
            
            await _db.SaveChangesAsync();

            // Re-extract
            await _docxExtraction.ExtractDocxToDatabase(docxPath, dokumenId);

            // Get updated part IDs after re-extraction
            var newPartIds = await _db.DokumenParts
                .Where(p => p.Section != null && p.Section.DsecRefTipe == "dokumen" && p.Section.DsecRefId == dokumenId)
                .Select(p => p.DpartId)
                .ToListAsync();
            var newCount = await _db.DokumenElemens.CountAsync(e => e.DpartId.HasValue && newPartIds.Contains(e.DpartId.Value));
            return Ok(new { 
                message = "Re-ekstraksi DOCX berhasil", 
                dokumen_id = dokumenId,
                element_count = newCount
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Gagal re-ekstraksi DOCX", error = ex.Message, stack = ex.StackTrace });
        }
    }
}


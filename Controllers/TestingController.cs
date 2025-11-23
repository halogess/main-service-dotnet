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

    [HttpGet("bulk-insert-buku-lulus")]
    public async Task<IActionResult> BulkInsertBukuLulus()
    {
        var inserted = 0;
        var skipped = 0;
        var errors = new List<string>();

        // Ambil mahasiswa yang bukan aktif (status 0, 2, 3, 9 atau ada tahun lulus) dan jurusan aktif (maksimal 100)
        var mahasiswaLulus = await (
            from m in _sttsDb.Mahasiswas
            where (m.MhsStatus == 0 || m.MhsStatus == 2 || m.MhsStatus == 3 || m.MhsStatus == 9 || (m.MhsLulusTahun != null && m.MhsLulusTahun != ""))
            join j in _sttsDb.Jurusans on m.JurKode equals j.JurKode
            where j.JurStatus == 1
            orderby m.MhsNrp descending
            select m
        ).Take(100).ToListAsync();

        foreach (var mhs in mahasiswaLulus)
        {
            try
            {
                // Cek apakah sudah punya buku
                var existingBuku = await _db.Bukus.AnyAsync(b => b.MhsNrp == mhs.MhsNrp);
                if (existingBuku)
                {
                    skipped++;
                    continue;
                }

                // Ambil judul dari proposal
                var judul = await _sttsDb.Proposals
                    .Where(p => p.MhsNrp == mhs.MhsNrp && p.ProposalPerpanjangan == 0)
                    .OrderByDescending(p => p.ProposalTglDoc)
                    .Select(p => p.ProposalJudulBaru)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(judul))
                {
                    judul = "Judul Belum Tersedia";
                }

                // Random status
                var statuses = new[] { "lolos", "lolos", "lolos", "tidak_lolos", "dalam_antrian", "diproses" };
                var random = new Random();
                var status = statuses[random.Next(statuses.Length)];

                // Insert buku dengan status random
                var buku = new Buku
                {
                    MhsNrp = mhs.MhsNrp,
                    BukuJudul = judul,
                    BukuStatus = status,
                    BukuJumlahBab = random.Next(3, 8),
                    BukuSkor = status == "lolos" ? random.Next(75, 95) : (status == "tidak_lolos" ? random.Next(50, 70) : null),
                    BukuJumlahKesalahan = status == "lolos" ? random.Next(1, 10) : (status == "tidak_lolos" ? random.Next(15, 35) : null),
                    BukuCreatedAt = DateTime.Now.AddDays(-random.Next(1, 180)),
                    BukuUpdatedAt = DateTime.Now
                };

                _db.Bukus.Add(buku);
                await _db.SaveChangesAsync();
                inserted++;
            }
            catch (Exception ex)
            {
                errors.Add($"{mhs.MhsNrp}: {ex.Message}");
                skipped++;
            }
        }

        return Ok(new
        {
            message = "Bulk insert mahasiswa lulus selesai",
            total_mahasiswa = mahasiswaLulus.Count,
            inserted = inserted,
            skipped = skipped,
            errors = errors.Take(10).ToList()
        });
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

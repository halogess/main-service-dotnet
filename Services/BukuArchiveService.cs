using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IBukuArchiveService
{
    string GetDocxArchiveRelativePath(string nrp, int bukuId);
    string GetPdfArchiveRelativePath(string nrp, int bukuId);
    bool TryResolveStorageFilePath(string filePath, out string fullPath);
    Task<string?> RefreshDocxArchiveAsync(int bukuId, CancellationToken cancellationToken = default);
    Task<string?> RefreshPdfArchiveAsync(int bukuId, CancellationToken cancellationToken = default);
}

public class BukuArchiveService : IBukuArchiveService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<BukuArchiveService> _logger;
    private readonly string _storageBasePath;

    public BukuArchiveService(KorektorBukuDbContext db, ILogger<BukuArchiveService> logger)
    {
        _db = db;
        _logger = logger;
        _storageBasePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
    }

    public string GetDocxArchiveRelativePath(string nrp, int bukuId)
        => BuildArchiveRelativePath(nrp, bukuId, "docx", "buku-docx.zip");

    public string GetPdfArchiveRelativePath(string nrp, int bukuId)
        => BuildArchiveRelativePath(nrp, bukuId, "pdf", "buku-pdf.zip");

    public bool TryResolveStorageFilePath(string filePath, out string fullPath)
    {
        fullPath = string.Empty;

        var fullStoragePath = Path.GetFullPath(_storageBasePath);
        var candidatePath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(_storageBasePath, filePath);

        var resolved = Path.GetFullPath(candidatePath);
        if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = resolved;
        return true;
    }

    public Task<string?> RefreshDocxArchiveAsync(int bukuId, CancellationToken cancellationToken = default)
        => RefreshArchiveAsync(
            bukuId,
            "docx",
            bab => bab.BabDocxPath,
            ".docx",
            buku => buku.BukuDocxZipPath,
            (buku, path) => buku.BukuDocxZipPath = path,
            cancellationToken);

    public Task<string?> RefreshPdfArchiveAsync(int bukuId, CancellationToken cancellationToken = default)
        => RefreshArchiveAsync(
            bukuId,
            "pdf",
            bab => bab.BabPdfPath,
            ".pdf",
            buku => buku.BukuPdfZipPath,
            (buku, path) => buku.BukuPdfZipPath = path,
            cancellationToken);

    private async Task<string?> RefreshArchiveAsync(
        int bukuId,
        string archiveKind,
        Func<Bab, string?> sourcePathSelector,
        string expectedExtension,
        Func<Buku, string?> archivePathSelector,
        Action<Buku, string?> archivePathSetter,
        CancellationToken cancellationToken)
    {
        var buku = await _db.Bukus.FirstOrDefaultAsync(b => b.BukuId == bukuId, cancellationToken)
            ?? throw new KeyNotFoundException($"Buku {bukuId} tidak ditemukan");

        var canonicalRelativePath = archiveKind == "docx"
            ? GetDocxArchiveRelativePath(buku.MhsNrp, buku.BukuId)
            : GetPdfArchiveRelativePath(buku.MhsNrp, buku.BukuId);

        if (!TryResolveStorageFilePath(canonicalRelativePath, out var archiveFullPath))
            throw new InvalidOperationException("Path file ZIP tidak valid");

        var files = await CollectArchiveSourcesAsync(bukuId, sourcePathSelector, expectedExtension, cancellationToken);
        if (files.Count == 0)
        {
            TryDeleteFile(archiveFullPath);
            if (!string.IsNullOrWhiteSpace(archivePathSelector(buku)))
            {
                archivePathSetter(buku, null);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        await WriteArchiveFileAsync(files, archiveFullPath, cancellationToken);

        var trackedRelativePath = NormalizeRelativePath(archivePathSelector(buku));
        if (!string.Equals(trackedRelativePath, canonicalRelativePath, StringComparison.Ordinal))
        {
            archivePathSetter(buku, canonicalRelativePath);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return canonicalRelativePath;
    }

    private async Task<List<BukuArchiveSource>> CollectArchiveSourcesAsync(
        int bukuId,
        Func<Bab, string?> sourcePathSelector,
        string expectedExtension,
        CancellationToken cancellationToken)
    {
        var babs = await _db.Babs
            .AsNoTracking()
            .Where(b => b.BukuId == (uint)bukuId)
            .OrderBy(b => b.BabOrder ?? byte.MaxValue)
            .ThenBy(b => b.BabId)
            .ToListAsync(cancellationToken);

        var archiveFiles = new List<BukuArchiveSource>();
        var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bab in babs)
        {
            var relativePath = sourcePathSelector(bab);
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            if (!TryResolveStorageFilePath(relativePath, out var fullPath))
            {
                throw new InvalidOperationException(
                    $"Path file bab {bab.BabOrder ?? 0} tidak valid");
            }

            if (!File.Exists(fullPath))
                continue;

            var storedFileName = Path.GetFileName(relativePath.Trim());
            var entryName = EnsureUniqueArchiveEntryName(
                BuildArchiveEntryName(bab.BabOrder, storedFileName, expectedExtension),
                usedEntryNames);

            archiveFiles.Add(new BukuArchiveSource(entryName, fullPath));
        }

        return archiveFiles;
    }

    private async Task WriteArchiveFileAsync(
        IReadOnlyCollection<BukuArchiveSource> files,
        string archiveFullPath,
        CancellationToken cancellationToken)
    {
        var archiveDirectory = Path.GetDirectoryName(archiveFullPath)
            ?? throw new InvalidOperationException("Direktori file ZIP tidak valid");

        Directory.CreateDirectory(archiveDirectory);

        var tempArchivePath = Path.Combine(
            archiveDirectory,
            $".{Path.GetFileName(archiveFullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var archiveStream = new FileStream(
                tempArchivePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                useAsync: true))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = archive.CreateEntry(file.EntryName, CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    await using var fileStream = new FileStream(
                        file.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        useAsync: true);

                    await fileStream.CopyToAsync(entryStream, cancellationToken);
                }
            }

            try
            {
                File.Move(tempArchivePath, archiveFullPath, overwrite: true);
            }
            catch (IOException ex) when (File.Exists(archiveFullPath) && new FileInfo(archiveFullPath).Length > 0)
            {
                _logger.LogWarning(
                    ex,
                    "Gagal mengganti ZIP buku yang sudah ada: {ArchiveFullPath}",
                    archiveFullPath);
            }
        }
        finally
        {
            if (File.Exists(tempArchivePath))
                File.Delete(tempArchivePath);
        }
    }

    private static string BuildArchiveRelativePath(string nrp, int bukuId, string archiveKind, string fileName)
        => NormalizeRelativePath(Path.Combine("buku", nrp, bukuId.ToString(), archiveKind, fileName));

    private static string NormalizeRelativePath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');

    private static void TryDeleteFile(string fullPath)
    {
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    private static string BuildArchiveEntryName(byte? babOrder, string? storedFileName, string expectedExtension)
    {
        string fileName;
        if (!string.IsNullOrWhiteSpace(storedFileName))
        {
            var trimmed = Path.GetFileName(storedFileName.Trim());
            if (string.Equals(expectedExtension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = Path.GetFileNameWithoutExtension(trimmed);
                fileName = string.IsNullOrWhiteSpace(baseName)
                    ? $"bab{babOrder ?? 0}.pdf"
                    : baseName + ".pdf";
            }
            else
            {
                fileName = trimmed;
            }
        }
        else
        {
            fileName = $"bab{babOrder ?? 0}{expectedExtension}";
        }

        return babOrder.HasValue
            ? $"{babOrder.Value:D2}_{fileName}"
            : fileName;
    }

    private static string EnsureUniqueArchiveEntryName(string entryName, ISet<string> usedNames)
    {
        if (usedNames.Add(entryName))
            return entryName;

        var extension = Path.GetExtension(entryName);
        var baseName = Path.GetFileNameWithoutExtension(entryName);

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName}_{suffix}{extension}";
            if (usedNames.Add(candidate))
                return candidate;
        }
    }

    private sealed record BukuArchiveSource(string EntryName, string FullPath);
}

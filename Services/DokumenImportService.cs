using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IDokumenImportService
{
    Task<DokumenImportPreviewResult> PreviewAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<DokumenImportExecutionResult> ImportAsync(string sourcePath, IEnumerable<string>? selectedRelativePaths = null, CancellationToken cancellationToken = default);
}

public sealed class DokumenImportPreviewResult
{
    public string SourcePath { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int DocxFiles { get; init; }
    public int UnsupportedFiles { get; init; }
    public int ImportableFiles { get; init; }
    public int DuplicateExistingFiles { get; init; }
    public int DuplicateSourceFiles { get; init; }
    public int MissingNrpFiles { get; init; }
    public int MissingMahasiswaFiles { get; init; }
    public List<DokumenImportPreviewItem> Items { get; init; } = new();
}

public sealed class DokumenImportPreviewItem
{
    public string RelativePath { get; init; } = string.Empty;
    public string SourceGroup { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? Nrp { get; init; }
    public string? MahasiswaName { get; init; }
    public bool CanImport { get; init; }
    public string Status { get; init; } = "skipped";
    public string Reason { get; init; } = string.Empty;
}

public sealed class DokumenImportExecutionResult
{
    public string SourcePath { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int ImportableFiles { get; init; }
    public int ImportedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int FailedFiles { get; init; }
    public List<DokumenImportExecutionItem> Items { get; init; } = new();
}

public sealed class DokumenImportExecutionItem
{
    public string RelativePath { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public string? Nrp { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? DokumenId { get; init; }
}

public sealed class DokumenImportService : IDokumenImportService
{
    private static readonly Regex NrpPrefixRegex = new(@"^(?<nrp>\d{9,})\b", RegexOptions.Compiled);
    private static readonly Regex BabKeywordRegex = new(@"\bbab\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly KorektorBukuDbContext _db;
    private readonly SttsDbContext _sttsDb;
    private readonly IDokumenService _dokumenService;
    private readonly ILogger<DokumenImportService> _logger;

    public DokumenImportService(
        KorektorBukuDbContext db,
        SttsDbContext sttsDb,
        IDokumenService dokumenService,
        ILogger<DokumenImportService> logger)
    {
        _db = db;
        _sttsDb = sttsDb;
        _dokumenService = dokumenService;
        _logger = logger;
    }

    public async Task<DokumenImportPreviewResult> PreviewAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var preview = await BuildPreviewAsync(sourcePath, cancellationToken);
        return preview.Result;
    }

    public async Task<DokumenImportExecutionResult> ImportAsync(string sourcePath, IEnumerable<string>? selectedRelativePaths = null, CancellationToken cancellationToken = default)
    {
        var preview = await BuildPreviewAsync(sourcePath, cancellationToken);
        var selectedPathSet = selectedRelativePaths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resultItems = new List<DokumenImportExecutionItem>();
        var importedCount = 0;
        var failedCount = 0;
        var selectedImportableCount = 0;

        foreach (var file in preview.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isSelected = selectedPathSet == null || selectedPathSet.Contains(NormalizeRelativePath(file.RelativePath));

            if (!file.CanImport)
            {
                resultItems.Add(new DokumenImportExecutionItem
                {
                    RelativePath = file.RelativePath,
                    Filename = file.Filename,
                    Nrp = file.Nrp,
                    Status = "skipped",
                    Message = file.Reason,
                });
                continue;
            }

            if (!isSelected)
            {
                resultItems.Add(new DokumenImportExecutionItem
                {
                    RelativePath = file.RelativePath,
                    Filename = file.Filename,
                    Nrp = file.Nrp,
                    Status = "skipped",
                    Message = "File tidak dipilih untuk diimpor",
                });
                continue;
            }

            selectedImportableCount++;

            try
            {
                await using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var formFile = new FormFile(stream, 0, stream.Length, "file", file.Filename)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                };

                var dokumen = await _dokumenService.UploadDokumen(file.Nrp!, formFile);
                importedCount++;

                resultItems.Add(new DokumenImportExecutionItem
                {
                    RelativePath = file.RelativePath,
                    Filename = file.Filename,
                    Nrp = file.Nrp,
                    Status = "imported",
                    Message = "Dokumen berhasil diimpor",
                    DokumenId = dokumen.DokumenId,
                });
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogWarning(ex, "Gagal mengimpor dokumen lokal: NRP={Nrp}, File={File}", file.Nrp, file.FullPath);

                resultItems.Add(new DokumenImportExecutionItem
                {
                    RelativePath = file.RelativePath,
                    Filename = file.Filename,
                    Nrp = file.Nrp,
                    Status = "failed",
                    Message = ex.Message,
                });
            }
        }

        return new DokumenImportExecutionResult
        {
            SourcePath = preview.Result.SourcePath,
            TotalFiles = preview.Result.TotalFiles,
            ImportableFiles = selectedImportableCount,
            ImportedFiles = importedCount,
            FailedFiles = failedCount,
            SkippedFiles = resultItems.Count(item => item.Status == "skipped"),
            Items = resultItems,
        };
    }

    private async Task<PreparedPreviewResult> BuildPreviewAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeSourcePath(sourcePath);
        var discoveredFiles = DiscoverFiles(normalizedPath);
        var nrps = discoveredFiles
            .Select(file => file.Nrp)
            .Where(nrp => !string.IsNullOrWhiteSpace(nrp))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        var mahasiswaMap = nrps.Count == 0
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : (await _sttsDb.Mahasiswas
                .Where(m => nrps.Contains(m.MhsNrp))
                .Select(m => new { m.MhsNrp, m.MhsNama })
                .ToListAsync(cancellationToken))
                .ToDictionary(item => item.MhsNrp, item => item.MhsNama, StringComparer.OrdinalIgnoreCase);

        var existingKeys = nrps.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await _db.Dokumens
                .Where(d => nrps.Contains(d.MhsNrp))
                .Select(d => new { d.MhsNrp, d.DokumenFilename })
                .ToListAsync(cancellationToken))
                .Select(item => BuildIdentityKey(item.MhsNrp, item.DokumenFilename))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenSourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preparedFiles = new List<PreparedImportFile>();

        var docxCount = 0;
        var unsupportedCount = 0;
        var importableCount = 0;
        var duplicateExistingCount = 0;
        var duplicateSourceCount = 0;
        var missingNrpCount = 0;
        var missingMahasiswaCount = 0;

        foreach (var file in discoveredFiles
                     .OrderBy(item => item.Nrp ?? "~", StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var isDocx = string.Equals(file.Extension, ".docx", StringComparison.OrdinalIgnoreCase);
            if (isDocx)
                docxCount++;

            var mahasiswaName = file.Nrp != null && mahasiswaMap.TryGetValue(file.Nrp, out var name)
                ? name
                : null;

            var prepared = new PreparedImportFile
            {
                FullPath = file.FullPath,
                RelativePath = file.RelativePath,
                SourceGroup = file.SourceGroup,
                Filename = file.Filename,
                Extension = file.Extension,
                SizeBytes = file.SizeBytes,
                Nrp = file.Nrp,
                MahasiswaName = mahasiswaName,
            };

            if (file.Filename.StartsWith("~$", StringComparison.OrdinalIgnoreCase))
            {
                prepared.Status = "skipped";
                prepared.Reason = "File sementara Microsoft Word dilewati";
            }
            else if (!isDocx)
            {
                unsupportedCount++;
                prepared.Status = "skipped";
                prepared.Reason = "Hanya file .docx yang dapat diimpor";
            }
            else if (string.IsNullOrWhiteSpace(file.Nrp))
            {
                missingNrpCount++;
                prepared.Status = "skipped";
                prepared.Reason = "NRP tidak bisa diambil dari nama folder atau nama file";
            }
            else if (string.IsNullOrWhiteSpace(mahasiswaName))
            {
                missingMahasiswaCount++;
                prepared.Status = "skipped";
                prepared.Reason = "Mahasiswa dengan NRP ini tidak ditemukan";
            }
            else
            {
                var identityKey = BuildIdentityKey(file.Nrp, file.Filename);

                if (!seenSourceKeys.Add(identityKey))
                {
                    duplicateSourceCount++;
                    prepared.Status = "skipped";
                    prepared.Reason = "Ada file lain dengan nama yang sama untuk NRP ini di folder sumber";
                }
                else if (existingKeys.Contains(identityKey))
                {
                    duplicateExistingCount++;
                    prepared.Status = "skipped";
                    prepared.Reason = "Dokumen dengan nama file ini sudah ada di sistem";
                }
                else
                {
                    importableCount++;
                    prepared.CanImport = true;
                    prepared.Status = "ready";
                    prepared.Reason = "Siap diimpor";
                }
            }

            preparedFiles.Add(prepared);
        }

        return new PreparedPreviewResult
        {
            Files = preparedFiles,
            Result = new DokumenImportPreviewResult
            {
                SourcePath = normalizedPath,
                TotalFiles = preparedFiles.Count,
                DocxFiles = docxCount,
                UnsupportedFiles = unsupportedCount,
                ImportableFiles = importableCount,
                DuplicateExistingFiles = duplicateExistingCount,
                DuplicateSourceFiles = duplicateSourceCount,
                MissingNrpFiles = missingNrpCount,
                MissingMahasiswaFiles = missingMahasiswaCount,
                Items = preparedFiles.Select(file => new DokumenImportPreviewItem
                {
                    RelativePath = file.RelativePath,
                    SourceGroup = file.SourceGroup,
                    Filename = file.Filename,
                    Extension = file.Extension,
                    SizeBytes = file.SizeBytes,
                    Nrp = file.Nrp,
                    MahasiswaName = file.MahasiswaName,
                    CanImport = file.CanImport,
                    Status = file.Status,
                    Reason = file.Reason,
                }).ToList()
            }
        };
    }

    private static string NormalizeSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("Path sumber tidak boleh kosong");

        var requestedPath = sourcePath.Trim().Trim('"');
        var normalized = ResolveAccessibleSourcePath(requestedPath);
        if (!Directory.Exists(normalized))
            throw new DirectoryNotFoundException($"Folder tidak ditemukan: {normalized}");

        return normalized;
    }

    private static string ResolveAccessibleSourcePath(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            return requestedPath;

        var candidatePath = Path.GetFullPath(requestedPath);
        if (Directory.Exists(candidatePath))
            return candidatePath;

        var hostImportPath = Environment.GetEnvironmentVariable("DOKUMEN_IMPORT_HOST_PATH")?.Trim();
        var containerImportPath = Environment.GetEnvironmentVariable("DOKUMEN_IMPORT_CONTAINER_PATH")?.Trim();

        if (!string.IsNullOrWhiteSpace(hostImportPath) &&
            !string.IsNullOrWhiteSpace(containerImportPath) &&
            TryMapHostPathToContainerPath(requestedPath, hostImportPath, containerImportPath, out var mappedPath))
        {
            var normalizedMappedPath = Path.GetFullPath(mappedPath);
            if (Directory.Exists(normalizedMappedPath))
                return normalizedMappedPath;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && LooksLikeWindowsPath(requestedPath))
        {
            if (!string.IsNullOrWhiteSpace(containerImportPath))
            {
                throw new DirectoryNotFoundException(
                    $"Folder Windows '{requestedPath}' tidak bisa diakses langsung dari container Linux. " +
                    $"Gunakan path container '{containerImportPath}' atau pastikan mount host-path sudah dikonfigurasi.");
            }

            throw new DirectoryNotFoundException(
                $"Folder Windows '{requestedPath}' tidak bisa diakses langsung dari container Linux. " +
                "Mount folder host ke container terlebih dahulu, lalu gunakan path container yang sesuai.");
        }

        return candidatePath;
    }

    private static bool TryMapHostPathToContainerPath(string requestedPath, string hostPath, string containerPath, out string mappedPath)
    {
        mappedPath = string.Empty;

        var normalizedRequested = NormalizeForHostComparison(requestedPath);
        var normalizedHost = NormalizeForHostComparison(hostPath);
        if (string.IsNullOrWhiteSpace(normalizedRequested) || string.IsNullOrWhiteSpace(normalizedHost))
            return false;

        if (!normalizedRequested.StartsWith(normalizedHost, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = normalizedRequested[normalizedHost.Length..].TrimStart('/', '\\');
        mappedPath = string.IsNullOrWhiteSpace(suffix)
            ? containerPath
            : Path.Combine(containerPath, suffix.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

        return true;
    }

    private static bool LooksLikeWindowsPath(string path)
        => Regex.IsMatch(path, @"^[A-Za-z]:[\\/]");

    private static string NormalizeForHostComparison(string path)
        => path.Trim().Trim('"').Replace('\\', '/').TrimEnd('/');

    private static List<DiscoveredFile> DiscoverFiles(string sourcePath)
    {
        return Directory
            .EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Where(fullPath => ContainsBabKeyword(Path.GetFileNameWithoutExtension(fullPath)))
            .Select(fullPath =>
            {
                var fileInfo = new FileInfo(fullPath);
                var relativePath = Path.GetRelativePath(sourcePath, fullPath).Replace('\\', '/');
                var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var sourceGroup = segments.Length > 1 ? segments[0] : "(root)";
                var parsedNrp = ExtractNrp(sourceGroup);
                if (string.IsNullOrWhiteSpace(parsedNrp))
                    parsedNrp = ExtractNrp(fileInfo.Name);

                return new DiscoveredFile
                {
                    FullPath = fullPath,
                    RelativePath = relativePath,
                    SourceGroup = sourceGroup,
                    Filename = fileInfo.Name,
                    Extension = fileInfo.Extension,
                    SizeBytes = fileInfo.Length,
                    Nrp = parsedNrp,
                };
            })
            .ToList();
    }

    private static bool ContainsBabKeyword(string? filename)
        => !string.IsNullOrWhiteSpace(filename) && BabKeywordRegex.IsMatch(filename);

    private static string? ExtractNrp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = NrpPrefixRegex.Match(value.Trim());
        return match.Success ? match.Groups["nrp"].Value : null;
    }

    private static string BuildIdentityKey(string nrp, string filename)
        => $"{nrp.Trim()}|{filename.Trim()}";

    private static string NormalizeRelativePath(string path)
        => path.Trim().Replace('\\', '/');

    private sealed class DiscoveredFile
    {
        public string FullPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string SourceGroup { get; init; } = string.Empty;
        public string Filename { get; init; } = string.Empty;
        public string Extension { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public string? Nrp { get; init; }
    }

    private sealed class PreparedImportFile
    {
        public string FullPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string SourceGroup { get; init; } = string.Empty;
        public string Filename { get; init; } = string.Empty;
        public string Extension { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public string? Nrp { get; init; }
        public string? MahasiswaName { get; init; }
        public bool CanImport { get; set; }
        public string Status { get; set; } = "skipped";
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class PreparedPreviewResult
    {
        public DokumenImportPreviewResult Result { get; init; } = new();
        public List<PreparedImportFile> Files { get; init; } = new();
    }
}

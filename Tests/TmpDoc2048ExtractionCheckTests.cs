using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class TmpDoc2048ExtractionCheckTests
{
    [Fact]
    public async Task GenerateLiveSummaryForDokumen2048Extraction()
    {
        var connectionString = LoadConnectionString();
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
            .UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 0, 34)),
                mysql => mysql.EnableRetryOnFailure())
            .Options;

        await using var db = new KorektorBukuDbContext(options);

        var dokumen = await db.Dokumens
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DokumenId == 2048);
        Assert.NotNull(dokumen);

        var queues = await db.Antrians
            .AsNoTracking()
            .Where(a => a.DokumenId == 2048)
            .OrderByDescending(a => a.AntrianId)
            .ToListAsync();

        var queueIds = queues.Select(q => q.AntrianId).ToList();
        var adobeLogs = queueIds.Count == 0
            ? new List<ValidasiTugasAkhir.MainService.Models.AdobeApiLog>()
            : await db.AdobeApiLogs
                .AsNoTracking()
                .Where(l => l.AntrianId.HasValue && queueIds.Contains(l.AntrianId.Value))
                .OrderByDescending(l => l.AdobeApiLogsId)
                .Take(80)
                .ToListAsync();

        var storagePath = ResolveStoragePath();
        var fullDocxPath = string.IsNullOrWhiteSpace(dokumen!.DokumenDocxPath)
            ? null
            : Path.GetFullPath(Path.Combine(storagePath, dokumen.DokumenDocxPath));
        var fullPdfPath = string.IsNullOrWhiteSpace(dokumen.DokumenPdfPath)
            ? null
            : Path.GetFullPath(Path.Combine(storagePath, dokumen.DokumenPdfPath));

        object? docxProbe = null;
        if (!string.IsNullOrWhiteSpace(fullDocxPath) && File.Exists(fullDocxPath))
        {
            try
            {
                using var wordDoc = WordprocessingDocument.Open(fullDocxPath, false);
                docxProbe = new
                {
                    exists = true,
                    size_bytes = new FileInfo(fullDocxPath).Length,
                    has_main_document = wordDoc.MainDocumentPart?.Document != null,
                    has_styles = wordDoc.MainDocumentPart?.StyleDefinitionsPart != null,
                    has_numbering = wordDoc.MainDocumentPart?.NumberingDefinitionsPart != null,
                    body_child_count = wordDoc.MainDocumentPart?.Document?.Body is null
                        ? (int?)null
                        : wordDoc.MainDocumentPart.Document.Body.ChildElements.Count
                };
            }
            catch (Exception ex)
            {
                docxProbe = new
                {
                    exists = true,
                    open_error = ex.GetType().Name,
                    message = ex.Message
                };
            }
        }
        else
        {
            docxProbe = new
            {
                exists = false
            };
        }

        var summary = new
        {
            dokumen = new
            {
                dokumen.DokumenId,
                dokumen.MhsNrp,
                dokumen.DokumenFilename,
                dokumen.DokumenStatus,
                dokumen.DokumenDocxPath,
                dokumen.DokumenPdfPath,
                dokumen.DokumenImagesPath,
                dokumen.DokumenReportPath,
                dokumen.DokumenCreatedAt,
                dokumen.DokumenUpdatedAt,
                docx_full_path = fullDocxPath,
                docx_exists = !string.IsNullOrWhiteSpace(fullDocxPath) && File.Exists(fullDocxPath),
                pdf_full_path = fullPdfPath,
                pdf_exists = !string.IsNullOrWhiteSpace(fullPdfPath) && File.Exists(fullPdfPath)
            },
            docx_probe = docxProbe,
            queues = queues.Select(q => new
            {
                q.AntrianId,
                q.AntrianTipe,
                q.AntrianExtractionStatus,
                q.AntrianLabelingStatus,
                q.AntrianValidationStatus,
                q.AntrianErrorMessage,
                q.AntrianCreatedAt,
                q.AntrianUpdatedAt
            }),
            adobe_logs = adobeLogs.Select(l => new
            {
                l.AdobeApiLogsId,
                l.AntrianId,
                l.Activity,
                l.Endpoint,
                l.Method,
                l.StatusCode,
                l.ResponseTimeMs,
                l.ErrorMessage,
                l.CreatedAt
            })
        };

        var outputPath = Path.Combine(Path.GetTempPath(), "doc2048-extraction-check.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        Console.WriteLine(outputPath);
    }

    private static string ResolveStoragePath()
    {
        var envPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env");
        var fullPath = Path.GetFullPath(envPath);
        var values = File.ReadLines(fullPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line =>
            {
                var idx = line.IndexOf('=');
                return idx > 0
                    ? new KeyValuePair<string, string>(line[..idx].Trim(), line[(idx + 1)..].Trim())
                    : new KeyValuePair<string, string>(string.Empty, string.Empty);
            })
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        if (values.TryGetValue("VOLUME_BASE_PATH", out var storagePath) && !string.IsNullOrWhiteSpace(storagePath))
            return storagePath;

        return @"E:\docker-volumes\validasi-ta";
    }

    private static string LoadConnectionString()
    {
        var envPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env");
        var fullPath = Path.GetFullPath(envPath);
        foreach (var rawLine in File.ReadLines(fullPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!string.Equals(key, "ConnectionStrings__KorektorBukuDbConnection", StringComparison.Ordinal))
                continue;

            return value.Replace("Server=host.docker.internal", "Server=localhost", StringComparison.OrdinalIgnoreCase);
        }

        return string.Empty;
    }
}

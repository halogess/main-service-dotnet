using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IValidationReportService
{
    Task<ValidationReportResult> GenerateDokumenReportAsync(
        int dokumenId,
        string? requesterNrp,
        string? role,
        bool refresh,
        CancellationToken cancellationToken);
}

public sealed record ValidationReportResult(byte[] Content, string FileName);

public sealed class ValidationReportService : IValidationReportService
{
    private readonly KorektorBukuDbContext _db;
    private readonly ILogger<ValidationReportService> _logger;

    public ValidationReportService(KorektorBukuDbContext db, ILogger<ValidationReportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ValidationReportResult> GenerateDokumenReportAsync(
        int dokumenId,
        string? requesterNrp,
        string? role,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var dokumen = await _db.Dokumens.FindAsync(new object[] { dokumenId }, cancellationToken);
        if (dokumen == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan");

        if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(dokumen.MhsNrp, requesterNrp, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();

        if (!string.Equals(dokumen.DokumenStatus, "lolos", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(dokumen.DokumenStatus, "tidak_lolos", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dokumen belum divalidasi");

        var (reportFullPath, reportRelativePath, reportFileName) = ResolveReportPath(dokumen, dokumenId);

        if (!refresh && File.Exists(reportFullPath))
        {
            if (string.IsNullOrWhiteSpace(dokumen.DokumenReportPath) ||
                !string.Equals(dokumen.DokumenReportPath, reportRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                dokumen.DokumenReportPath = reportRelativePath;
                dokumen.DokumenUpdatedAt = DateTime.Now;
                await _db.SaveChangesAsync(cancellationToken);
            }

            var existing = await File.ReadAllBytesAsync(reportFullPath, cancellationToken);
            return new ValidationReportResult(existing, reportFileName);
        }

        var kesalahanList = await _db.Kesalahans
            .Include(k => k.Details)
            .Where(k => k.KesalahanRefTipe == KesalahanRefTipe.dokumen && k.KesalahanRefId == (uint)dokumenId)
            .OrderBy(k => k.KesalahanId)
            .ToListAsync(cancellationToken);

        var rows = BuildRows(kesalahanList);
        var summary = BuildSummary(rows);

        var data = new ValidationReportData
        {
            GeneratedAt = DateTime.Now,
            DokumenId = dokumen.DokumenId,
            Nrp = dokumen.MhsNrp,
            Filename = dokumen.DokumenFilename,
            Tipe = dokumen.DokumenTipe,
            Status = dokumen.DokumenStatus,
            Skor = dokumen.DokumenSkor,
            TotalKesalahan = dokumen.DokumenJumlahKesalahan,
            CreatedAt = dokumen.DokumenCreatedAt,
            UpdatedAt = dokumen.DokumenUpdatedAt,
            Summary = summary,
            Rows = rows
        };

        QuestPDF.Settings.License = LicenseType.Community;
        var pdfBytes = new ValidationReportDocument(data).GeneratePdf();

        Directory.CreateDirectory(Path.GetDirectoryName(reportFullPath)!);
        await File.WriteAllBytesAsync(reportFullPath, pdfBytes, cancellationToken);

        dokumen.DokumenReportPath = reportRelativePath;
        dokumen.DokumenUpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);

        return new ValidationReportResult(pdfBytes, reportFileName);
    }

    private static (string FullPath, string RelativePath, string FileName) ResolveReportPath(Dokumen dokumen, int dokumenId)
    {
        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        var fullStoragePath = Path.GetFullPath(storagePath);

        var relativeDir = Path.Combine("dokumen", dokumen.MhsNrp, dokumenId.ToString(), "report");
        var reportDir = Path.GetFullPath(Path.Combine(storagePath, relativeDir));
        if (!reportDir.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path report tidak valid");

        var fileName = $"report_validasi_{dokumenId}.pdf";
        var fullPath = Path.Combine(reportDir, fileName);
        var relativePath = Path.Combine(relativeDir, fileName);
        return (fullPath, relativePath, fileName);
    }

    private static List<ValidationReportRow> BuildRows(List<Kesalahan> kesalahanList)
    {
        var rows = new List<ValidationReportRow>();
        foreach (var kesalahan in kesalahanList)
        {
            var pages = ParsePages(kesalahan.KesalahanLokasi);
            var pagesText = FormatPageRanges(pages);
            var firstPage = pages.FirstOrDefault();

            foreach (var detail in kesalahan.Details.OrderBy(d => d.KesalahanDetailId))
            {
                rows.Add(new ValidationReportRow
                {
                    Category = kesalahan.KesalahanKategori,
                    Title = detail.KesalahanDetailJudul,
                    Explanation = BuildExplanation(detail.KesalahanDetailPenjelasan, detail.KesalahanDetailSteps),
                    Pages = pagesText,
                    FirstPage = firstPage,
                    IsRequired = detail.KesalahanIsRequired
                });
            }
        }

        return rows
            .OrderBy(r => r.FirstPage == 0 ? int.MaxValue : r.FirstPage)
            .ThenBy(r => r.Category)
            .ThenBy(r => r.Title)
            .Select((row, index) => row with { Index = index + 1 })
            .ToList();
    }

    private static ValidationReportSummary BuildSummary(IReadOnlyList<ValidationReportRow> rows)
    {
        var total = rows.Count;
        var required = rows.Count(r => r.IsRequired);
        var optional = total - required;
        var pages = rows
            .SelectMany(r => ParsePagesFromFormatted(r.Pages))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        return new ValidationReportSummary
        {
            TotalDetails = total,
            RequiredDetails = required,
            OptionalDetails = optional,
            PagesText = FormatPageRanges(pages)
        };
    }

    private static List<int> ParsePages(string? lokasiJson)
    {
        var pages = new List<int>();
        if (string.IsNullOrWhiteSpace(lokasiJson))
            return pages;

        try
        {
            using var doc = JsonDocument.Parse(lokasiJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return pages;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (!item.TryGetProperty("halaman_ke", out var halamanEl))
                    continue;

                if (halamanEl.ValueKind == JsonValueKind.Number && halamanEl.TryGetInt32(out var page) && page > 0)
                    pages.Add(page);
            }
        }
        catch (JsonException)
        {
            // Ignore parsing errors
        }

        return pages.Distinct().OrderBy(p => p).ToList();
    }

    private static List<int> ParsePagesFromFormatted(string? pagesText)
    {
        var pages = new List<int>();
        if (string.IsNullOrWhiteSpace(pagesText) || pagesText == "-")
            return pages;

        var parts = pagesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var range = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (range.Length == 1 && int.TryParse(range[0], out var single))
            {
                pages.Add(single);
            }
            else if (range.Length == 2 &&
                     int.TryParse(range[0], out var start) &&
                     int.TryParse(range[1], out var end) &&
                     start <= end)
            {
                for (var p = start; p <= end; p++)
                    pages.Add(p);
            }
        }

        return pages;
    }

    private static string FormatPageRanges(IEnumerable<int> pages)
    {
        var ordered = pages
            .Where(p => p > 0)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (ordered.Count == 0)
            return "-";

        var ranges = new List<string>();
        var start = ordered[0];
        var prev = ordered[0];

        for (var i = 1; i < ordered.Count; i++)
        {
            var current = ordered[i];
            if (current == prev + 1)
            {
                prev = current;
                continue;
            }

            ranges.Add(start == prev ? start.ToString(CultureInfo.InvariantCulture) : $"{start}-{prev}");
            start = prev = current;
        }

        ranges.Add(start == prev ? start.ToString(CultureInfo.InvariantCulture) : $"{start}-{prev}");
        return string.Join(", ", ranges);
    }

    private static string BuildExplanation(string? explanation, string? stepsJson)
    {
        var text = string.IsNullOrWhiteSpace(explanation) ? "-" : explanation.Trim();
        var steps = ParseSteps(stepsJson);
        if (steps.Count == 0)
            return text;

        return text + "\nLangkah: " + string.Join("; ", steps);
    }

    private static List<string> ParseSteps(string? stepsJson)
    {
        if (string.IsNullOrWhiteSpace(stepsJson))
            return new List<string>();

        try
        {
            var steps = JsonSerializer.Deserialize<List<string>>(stepsJson);
            return steps == null
                ? new List<string>()
                : steps.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}

public sealed record ValidationReportRow
{
    public int Index { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string Pages { get; init; } = "-";
    public int FirstPage { get; init; }
    public bool IsRequired { get; init; }
}

public sealed record ValidationReportSummary
{
    public int TotalDetails { get; init; }
    public int RequiredDetails { get; init; }
    public int OptionalDetails { get; init; }
    public string PagesText { get; init; } = "-";
}

public sealed record ValidationReportData
{
    public DateTime GeneratedAt { get; init; }
    public int DokumenId { get; init; }
    public string? Nrp { get; init; }
    public string? Filename { get; init; }
    public string? Tipe { get; init; }
    public string? Status { get; init; }
    public int? Skor { get; init; }
    public int? TotalKesalahan { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public ValidationReportSummary Summary { get; init; } = new();
    public IReadOnlyList<ValidationReportRow> Rows { get; init; } = new List<ValidationReportRow>();
}

internal sealed class ValidationReportDocument : IDocument
{
    private static readonly CultureInfo IdCulture = new("id-ID");
    private readonly ValidationReportData _data;

    public ValidationReportDocument(ValidationReportData data)
    {
        _data = data;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(11));

            page.Content().Column(column =>
            {
                column.Item().AlignCenter().Text("Laporan Validasi Dokumen").FontSize(18).SemiBold();
                column.Item().AlignRight().Text($"Tanggal laporan: {FormatDateTime(_data.GeneratedAt)}").FontSize(9);

                column.Item().PaddingVertical(8).LineHorizontal(1);

                column.Item().Text("Informasi Validasi").SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(110);
                        columns.RelativeColumn();
                        columns.ConstantColumn(110);
                        columns.RelativeColumn();
                    });

                    AddInfoRow(table, "Dokumen ID", _data.DokumenId.ToString(CultureInfo.InvariantCulture), "NRP", _data.Nrp);
                    AddInfoRow(table, "Nama file", _data.Filename, "Tipe", _data.Tipe);
                    AddInfoRow(table, "Status", _data.Status, "Skor", _data.Skor?.ToString(CultureInfo.InvariantCulture));
                    AddInfoRow(table, "Jumlah kesalahan", _data.TotalKesalahan?.ToString(CultureInfo.InvariantCulture), "Halaman terdampak", _data.Summary.PagesText);
                    AddInfoRow(table, "Dibuat", FormatDate(_data.CreatedAt), "Terakhir update", FormatDate(_data.UpdatedAt));
                });

                column.Item().PaddingVertical(8).LineHorizontal(1);

                column.Item().Text("Ringkasan").SemiBold();
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Total detail: {_data.Summary.TotalDetails}");
                    row.RelativeItem().Text($"Wajib: {_data.Summary.RequiredDetails}");
                    row.RelativeItem().Text($"Saran: {_data.Summary.OptionalDetails}");
                });

                column.Item().PaddingVertical(6);

                if (_data.Rows.Count == 0)
                {
                    column.Item().Text("Tidak ada kesalahan yang ditemukan.");
                    return;
                }

                column.Item().Text("Tabel Kesalahan Detail").SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(26);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(4);
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(45);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCellStyle).Text("No");
                        header.Cell().Element(HeaderCellStyle).Text("Kategori");
                        header.Cell().Element(HeaderCellStyle).Text("Judul");
                        header.Cell().Element(HeaderCellStyle).Text("Penjelasan");
                        header.Cell().Element(HeaderCellStyle).Text("Halaman");
                        header.Cell().Element(HeaderCellStyle).Text("Wajib");
                    });

                    foreach (var row in _data.Rows)
                    {
                        table.Cell().Element(BodyCellStyle).Text(row.Index.ToString(CultureInfo.InvariantCulture));
                        table.Cell().Element(BodyCellStyle).Text(row.Category);
                        table.Cell().Element(BodyCellStyle).Text(row.Title);
                        table.Cell().Element(BodyCellStyle).Text(row.Explanation);
                        table.Cell().Element(BodyCellStyle).Text(row.Pages);
                        table.Cell().Element(BodyCellStyle).Text(row.IsRequired ? "Ya" : "Saran");
                    }
                });
            });

            page.Footer().AlignRight().DefaultTextStyle(x => x.FontSize(9)).Text(text =>
            {
                text.Span("Halaman ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private static void AddInfoRow(TableDescriptor table, string label1, string? value1, string label2, string? value2)
    {
        table.Cell().Element(InfoLabelStyle).Text(label1);
        table.Cell().Element(InfoValueStyle).Text(string.IsNullOrWhiteSpace(value1) ? "-" : value1);
        table.Cell().Element(InfoLabelStyle).Text(label2);
        table.Cell().Element(InfoValueStyle).Text(string.IsNullOrWhiteSpace(value2) ? "-" : value2);
    }

    private static IContainer HeaderCellStyle(IContainer container)
    {
        return container
            .Background(Colors.Grey.Lighten3)
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(4)
            .PaddingHorizontal(3)
            .DefaultTextStyle(x => x.FontSize(9).SemiBold());
    }

    private static IContainer BodyCellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3)
            .PaddingHorizontal(3)
            .DefaultTextStyle(x => x.FontSize(9));
    }

    private static IContainer InfoLabelStyle(IContainer container)
    {
        return container
            .PaddingVertical(2)
            .PaddingRight(6)
            .DefaultTextStyle(x => x.FontSize(9).SemiBold());
    }

    private static IContainer InfoValueStyle(IContainer container)
    {
        return container
            .PaddingVertical(2)
            .DefaultTextStyle(x => x.FontSize(9));
    }

    private static string FormatDate(DateTime? date)
    {
        if (!date.HasValue)
            return "-";

        return date.Value.ToString("dd MMMM yyyy HH:mm", IdCulture);
    }

    private static string FormatDateTime(DateTime date)
    {
        return date.ToString("dd MMMM yyyy HH:mm", IdCulture);
    }
}

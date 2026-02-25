using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var isValidatedStatus =
            string.Equals(dokumen.DokumenStatus, "lolos", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dokumen.DokumenStatus, "tidak_lolos", StringComparison.OrdinalIgnoreCase);
        var hasValidationResult = dokumen.DokumenJumlahKesalahan.HasValue;

        if (!isValidatedStatus && !hasValidationResult)
            throw new InvalidOperationException("Dokumen belum divalidasi");

        var reportStatus = isValidatedStatus
            ? dokumen.DokumenStatus
            : (dokumen.DokumenJumlahKesalahan!.Value == 0 ? "lolos" : "tidak_lolos");

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
            Status = reportStatus,
            Skor = dokumen.DokumenSkor,
            TotalKesalahan = dokumen.DokumenJumlahKesalahan,
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
            var (firstPage, firstY) = ParseLocationSortKey(kesalahan.KesalahanLokasi);
            var evidence = ExtractEvidence(kesalahan.KesalahanLokasi);
            var isPageSettings = IsPageSettingsCategory(kesalahan.KesalahanKategori);
            var orderedDetails = kesalahan.Details
                .OrderBy(d => d.KesalahanDetailId)
                .ToList();
            var problematicText = FormatProblematicText(
                !string.IsNullOrWhiteSpace(evidence)
                    ? evidence
                    : orderedDetails.FirstOrDefault()?.KesalahanDetailJudul);
            var elementKey = $"kesalahan:{kesalahan.KesalahanId}";

            if (isPageSettings)
            {
                var (sectionNumber, sectionType) = ExtractPageSettingsSectionContext(orderedDetails);
                elementKey = BuildPageSettingsElementKey(firstPage, sectionNumber, sectionType);
                problematicText = string.Empty;
            }

            foreach (var detail in orderedDetails)
            {
                rows.Add(new ValidationReportRow
                {
                    ElementKey = elementKey,
                    Category = kesalahan.KesalahanKategori,
                    ProblematicText = problematicText,
                    Title = detail.KesalahanDetailJudul,
                    Explanation = FormatNullableText(detail.KesalahanDetailPenjelasan),
                    Pages = pagesText,
                    Page = firstPage,
                    FirstY = firstY,
                    SortPriority = isPageSettings ? 0 : 1,
                    IsRequired = detail.KesalahanIsRequired
                });
            }
        }

        return rows
            .OrderBy(r => NormalizeSortPage(r.Page))
            .ThenBy(r => r.SortPriority)
            .ThenBy(r => r.ElementKey)
            .ThenBy(r => r.FirstY)
            .ThenBy(r => r.Title)
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

    private static (int FirstPage, double FirstY) ParseLocationSortKey(string? lokasiJson)
    {
        if (string.IsNullOrWhiteSpace(lokasiJson))
            return (0, double.MaxValue);

        var hasValue = false;
        var bestPage = 0;
        var bestY = double.MaxValue;

        try
        {
            using var doc = JsonDocument.Parse(lokasiJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (0, double.MaxValue);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var halamanKe = 0;
                if (item.TryGetProperty("halaman_ke", out var halamanEl) &&
                    halamanEl.ValueKind == JsonValueKind.Number &&
                    halamanEl.TryGetInt32(out var parsedPage) &&
                    parsedPage > 0)
                {
                    halamanKe = parsedPage;
                }

                var yTerkecil = double.MaxValue;
                if (item.TryGetProperty("bbox", out var bboxEl) &&
                    bboxEl.ValueKind == JsonValueKind.Object &&
                    bboxEl.TryGetProperty("y0", out var y0El) &&
                    y0El.ValueKind == JsonValueKind.Number &&
                    y0El.TryGetDouble(out var parsedY))
                {
                    yTerkecil = parsedY;
                }

                if (!hasValue ||
                    NormalizeSortPage(halamanKe) < NormalizeSortPage(bestPage) ||
                    (NormalizeSortPage(halamanKe) == NormalizeSortPage(bestPage) && yTerkecil < bestY))
                {
                    hasValue = true;
                    bestPage = halamanKe;
                    bestY = yTerkecil;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parsing errors
        }

        return hasValue ? (bestPage, bestY) : (0, double.MaxValue);
    }

    private static int NormalizeSortPage(int page)
        => page <= 0 ? int.MaxValue : page;

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

    private static string? ExtractEvidence(string? lokasiJson)
    {
        if (string.IsNullOrWhiteSpace(lokasiJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(lokasiJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (!item.TryGetProperty("evidence", out var evidenceEl) ||
                    evidenceEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var evidence = evidenceEl.GetString();
                if (string.IsNullOrWhiteSpace(evidence))
                    continue;

                return NormalizeInlineWhitespace(evidence);
            }
        }
        catch (JsonException)
        {
            // Ignore parsing errors
        }

        return null;
    }

    private static string FormatProblematicText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "-";

        var normalized = NormalizeInlineWhitespace(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return "-";

        const int maxLength = 50;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static (string? SectionNumber, string? SectionType) ExtractPageSettingsSectionContext(
        IReadOnlyList<KesalahanDetail> details)
    {
        if (details == null || details.Count == 0)
            return (null, null);

        foreach (var detail in details)
        {
            var combined = string.Join(" ", new[]
            {
                detail.KesalahanDetailJudul,
                detail.KesalahanDetailPenjelasan
            }.Where(v => !string.IsNullOrWhiteSpace(v)));

            if (string.IsNullOrWhiteSpace(combined))
                continue;

            var sectionMatch = Regex.Match(combined, @"section\s+(\d+)", RegexOptions.IgnoreCase);
            var typeMatch = Regex.Match(combined, @"\(\s*bagian\s+([^)]+)\)", RegexOptions.IgnoreCase);

            var sectionNumber = sectionMatch.Success ? sectionMatch.Groups[1].Value.Trim() : null;
            var sectionType = typeMatch.Success ? typeMatch.Groups[1].Value.Trim() : null;

            if (!string.IsNullOrWhiteSpace(sectionNumber) || !string.IsNullOrWhiteSpace(sectionType))
                return (sectionNumber, sectionType);
        }

        return (null, null);
    }

    private static string BuildPageSettingsElementKey(int page, string? sectionNumber, string? sectionType)
    {
        var pagePart = page > 0 ? page.ToString(CultureInfo.InvariantCulture) : "unknown";
        var sectionPart = string.IsNullOrWhiteSpace(sectionNumber) ? "unknown" : sectionNumber.Trim();
        var typePart = string.IsNullOrWhiteSpace(sectionType) ? "unknown" : sectionType.Trim().ToLowerInvariant();
        return $"page-settings:{pagePart}:{sectionPart}:{typePart}";
    }

    private static string NormalizeInlineWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    private static string FormatNullableText(string? text)
        => string.IsNullOrWhiteSpace(text) ? "-" : NormalizeInlineWhitespace(text);

    private static bool IsPageSettingsCategory(string? category)
        => string.Equals(category?.Trim(), "pengaturan halaman", StringComparison.OrdinalIgnoreCase);
}

public sealed record ValidationReportRow
{
    public string ElementKey { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string ProblematicText { get; init; } = "-";
    public string Title { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string Pages { get; init; } = "-";
    public int Page { get; init; }
    public double FirstY { get; init; } = double.MaxValue;
    public int SortPriority { get; init; } = 1;
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
            page.Margin(1.27f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Content().Column(column =>
            {
                column.Item().AlignCenter().Text("Laporan Validasi Dokumen").FontSize(16).SemiBold();

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
                });

                column.Item().PaddingVertical(8).LineHorizontal(1);

                column.Item().Text("Ringkasan").SemiBold();
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Total kesalahan: {_data.Summary.TotalDetails}");
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

                var rowsByPage = _data.Rows
                    .GroupBy(r => r.Page)
                    .OrderBy(g => g.Key <= 0 ? int.MaxValue : g.Key);

                foreach (var pageGroup in rowsByPage)
                {
                    var pageText = pageGroup.Key > 0
                        ? $"Halaman {pageGroup.Key}"
                        : "Halaman tidak diketahui";

                    var orderedRows = pageGroup
                        .OrderBy(r => r.SortPriority)
                        .ThenBy(r => r.ElementKey)
                        .ThenBy(r => r.FirstY)
                        .ThenBy(r => r.Title)
                        .ToList();

                    column.Item().PaddingTop(6).Text(pageText).SemiBold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(45);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCellStyle).Text("Kategori");
                            header.Cell().Element(HeaderCellStyle).Text("Text bermasalah");
                            header.Cell().Element(HeaderCellStyle).Text("Judul");
                            header.Cell().Element(HeaderCellStyle).Text("Penjelasan");
                            header.Cell().Element(HeaderCellStyle).Text("Wajib");
                        });

                        for (var i = 0; i < orderedRows.Count; i++)
                        {
                            var row = orderedRows[i];
                            var isFirstRowForElement = i == 0 ||
                                !string.Equals(orderedRows[i - 1].ElementKey, row.ElementKey, StringComparison.Ordinal);

                            if (isFirstRowForElement)
                            {
                                var rowSpan = 1;
                                while (i + rowSpan < orderedRows.Count &&
                                       string.Equals(orderedRows[i + rowSpan].ElementKey, row.ElementKey, StringComparison.Ordinal))
                                {
                                    rowSpan++;
                                }

                                table.Cell().RowSpan((uint)rowSpan).Element(BodyCellStyle).Text(row.Category);
                                table.Cell().RowSpan((uint)rowSpan).Element(BodyCellStyle).Text(row.ProblematicText);
                            }

                            table.Cell().Element(BodyCellStyle).Text(row.Title);
                            table.Cell().Element(BodyCellStyle).Text(row.Explanation);
                            table.Cell().Element(BodyCellStyle).Text(row.IsRequired ? "Ya" : "Saran");
                        }
                    });
                }
            });

            page.Footer().DefaultTextStyle(x => x.FontSize(7)).Row(row =>
            {
                row.RelativeItem().AlignLeft().Text($"Dicetak pada: {FormatPrintedDateTime(_data.GeneratedAt)}");
                row.ConstantItem(90).AlignRight().Text(text =>
                {
                    text.Span("Halaman ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
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
            .DefaultTextStyle(x => x.FontSize(7).SemiBold());
    }

    private static IContainer BodyCellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3)
            .PaddingHorizontal(3)
            .DefaultTextStyle(x => x.FontSize(7));
    }

    private static IContainer InfoLabelStyle(IContainer container)
    {
        return container
            .PaddingVertical(2)
            .PaddingRight(6)
            .DefaultTextStyle(x => x.FontSize(7).SemiBold());
    }

    private static IContainer InfoValueStyle(IContainer container)
    {
        return container
            .PaddingVertical(2)
            .DefaultTextStyle(x => x.FontSize(7));
    }

    private static string FormatPrintedDateTime(DateTime date)
        => date.ToString("dd MMMM yyyy HH.mm", IdCulture);
}

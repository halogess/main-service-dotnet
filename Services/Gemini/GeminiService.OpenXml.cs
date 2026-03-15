using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Models;

namespace ValidasiTugasAkhir.MainService.Services;

public partial class GeminiService
{
    private sealed class OpenXmlContextPayload
    {
        [JsonPropertyName("dokumen_elemen")]
        public DokumenElemenPayload? DokumenElemen { get; set; }

        [JsonPropertyName("dokumen_format_paragraf")]
        public DokumenFormatParagraf? DokumenFormatParagraf { get; set; }

        [JsonPropertyName("dokumen_format_text")]
        public List<DokumenFormatText>? DokumenFormatText { get; set; }
    }

    private sealed class DokumenElemenPayload
    {
        [JsonPropertyName("delemen_id")]
        public ulong DelemenId { get; set; }

        [JsonPropertyName("dpart_id")]
        public uint? DpartId { get; set; }

        [JsonPropertyName("delemen_sequence")]
        public uint? DelemenSequence { get; set; }

        [JsonPropertyName("delemen_type")]
        public string? DelemenType { get; set; }

        [JsonPropertyName("delemen_json_tree")]
        public string? DelemenJsonTree { get; set; }

        [JsonPropertyName("delemen_xml")]
        public string? DelemenXml { get; set; }
    }

    private sealed class PageImageInfo
    {
        [JsonPropertyName("image_index")]
        public int ImageIndex { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }

    private sealed class LlmImagePayload
    {
        public int Page { get; set; }
        public string MimeType { get; set; } = "image/jpeg";
        public string Base64 { get; set; } = string.Empty;
        public string? FileName { get; set; }
    }

    private sealed class ElementFormatIds
    {
        public uint? ParagraphFormatId { get; set; }
        public List<uint> TextFormatIds { get; } = new();
    }

    private async Task<Dictionary<int, OpenXmlContextPayload>> BuildOpenXmlContextsAsync(
        uint dokumenId,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, OpenXmlContextPayload>();

        var indexed = errors
            .Select((error, index) => new { Error = error, Index = index })
            .Where(item => item.Error.DokumenElemenId.HasValue)
            .ToList();

        if (indexed.Count == 0)
            return result;

        var elementIds = indexed
            .Select(item => item.Error.DokumenElemenId!.Value)
            .Distinct()
            .ToList();

        var elements = await _db.DokumenElemens
            .Where(e => elementIds.Contains(e.DelemenId))
            .Select(e => new DokumenElemenPayload
            {
                DelemenId = e.DelemenId,
                DpartId = e.DpartId,
                DelemenSequence = e.DelemenSequence,
                DelemenType = e.DelemenType,
                DelemenJsonTree = e.DelemenJsonTree,
                // Raw XML is intentionally excluded to keep LLM payload compact.
                DelemenXml = null
            })
            .ToListAsync(cancellationToken);

        var elementById = elements.ToDictionary(e => e.DelemenId);

        var formatIdsByElement = new Dictionary<ulong, ElementFormatIds>();
        var paragraphFormatIds = new HashSet<uint>();
        var textFormatIds = new HashSet<uint>();

        foreach (var element in elements)
        {
            var formats = ExtractFormatIds(element.DelemenJsonTree);
            formatIdsByElement[element.DelemenId] = formats;
            if (formats.ParagraphFormatId.HasValue)
                paragraphFormatIds.Add(formats.ParagraphFormatId.Value);
            foreach (var textId in formats.TextFormatIds)
                textFormatIds.Add(textId);
        }

        var paragraphFormats = paragraphFormatIds.Count > 0
            ? await _db.DokumenFormatParagrafs
                .Where(p => paragraphFormatIds.Contains(p.DfpId))
                .ToDictionaryAsync(p => p.DfpId, cancellationToken)
            : new Dictionary<uint, DokumenFormatParagraf>();

        var textFormats = textFormatIds.Count > 0
            ? await _db.DokumenFormatTexts
                .Where(t => textFormatIds.Contains(t.DftxId))
                .ToDictionaryAsync(t => t.DftxId, cancellationToken)
            : new Dictionary<uint, DokumenFormatText>();

        foreach (var item in indexed)
        {
            var elementId = item.Error.DokumenElemenId!.Value;
            if (!elementById.TryGetValue(elementId, out var element))
                continue;

            var formats = formatIdsByElement[elementId];
            var includeText = IsFontError(item.Error);
            var includeParagraph = !includeText;

            var payload = new OpenXmlContextPayload
            {
                DokumenElemen = element
            };

            if (includeParagraph && formats.ParagraphFormatId.HasValue &&
                paragraphFormats.TryGetValue(formats.ParagraphFormatId.Value, out var paragraphFormat))
            {
                payload.DokumenFormatParagraf = paragraphFormat;
            }

            if (includeText && formats.TextFormatIds.Count > 0)
            {
                var formatsForElement = formats.TextFormatIds
                    .Select(id => textFormats.TryGetValue(id, out var tf) ? tf : null)
                    .Where(tf => tf != null)
                    .Cast<DokumenFormatText>()
                    .ToList();

                if (formatsForElement.Count > 0)
                    payload.DokumenFormatText = formatsForElement;
            }

            result[item.Index] = payload;
        }

        return result;
    }

    private async Task<(List<PageImageInfo> Infos, List<LlmImagePayload> Payloads)> LoadPageImagesAsync(
        uint dokumenId,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var infos = new List<PageImageInfo>();
        var payloads = new List<LlmImagePayload>();

        var pages = errors
            .SelectMany(error => error.Locations)
            .Select(loc => loc.HalamanKe)
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToList();

        if (pages.Count == 0)
            return (infos, payloads);

        var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
        string? imagesDir = null;

        // Default flow: dokumen/{nrp}/{dokumen_id}/images/<page>.jpg
        var dokumen = await _db.Dokumens
            .Where(d => d.DokumenId == (int)dokumenId)
            .Select(d => new { d.DokumenId, d.MhsNrp })
            .FirstOrDefaultAsync(cancellationToken);

        if (dokumen != null && !string.IsNullOrWhiteSpace(dokumen.MhsNrp))
        {
            imagesDir = Path.GetFullPath(Path.Combine(
                storagePath,
                "dokumen",
                dokumen.MhsNrp,
                dokumen.DokumenId.ToString(),
                "images"));
        }
        else
        {
            // Buku flow: buku/{nrp}/{buku_id}/images/{bab_order}/<page>.jpg
            var bab = await _db.Babs
                .Where(b => b.BabId == dokumenId)
                .Select(b => new { b.BukuId, b.BabOrder })
                .FirstOrDefaultAsync(cancellationToken);

            if (bab != null && bab.BabOrder.HasValue)
            {
                var buku = await _db.Bukus
                    .Where(b => b.BukuId == (int)bab.BukuId)
                    .Select(b => new { b.BukuId, b.MhsNrp })
                    .FirstOrDefaultAsync(cancellationToken);

                if (buku != null && !string.IsNullOrWhiteSpace(buku.MhsNrp))
                {
                    imagesDir = Path.GetFullPath(Path.Combine(
                        storagePath,
                        "buku",
                        buku.MhsNrp,
                        buku.BukuId.ToString(),
                        "images",
                        bab.BabOrder.Value.ToString()));
                }
            }
        }

        if (string.IsNullOrWhiteSpace(imagesDir))
            return (infos, payloads);

        if (!Directory.Exists(imagesDir))
            return (infos, payloads);

        var allowedExts = new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp"
        };

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            string? path = null;
            foreach (var ext in allowedExts)
            {
                var candidate = Path.Combine(imagesDir, $"{page}{ext}");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }

            if (path == null)
                continue;

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (bytes.Length == 0)
                continue;

            var extName = Path.GetExtension(path).ToLowerInvariant();
            var mimeType = extName switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tif" => "image/tiff",
                ".tiff" => "image/tiff",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

            payloads.Add(new LlmImagePayload
            {
                Page = page,
                MimeType = mimeType,
                Base64 = Convert.ToBase64String(bytes),
                FileName = Path.GetFileName(path)
            });

            infos.Add(new PageImageInfo
            {
                ImageIndex = payloads.Count - 1,
                Page = page,
                MimeType = mimeType,
                FileName = Path.GetFileName(path)
            });
        }

        return (infos, payloads);
    }

    private static ElementFormatIds ExtractFormatIds(string? json)
    {
        var info = new ElementFormatIds();
        if (string.IsNullOrWhiteSpace(json))
            return info;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("dfp_id", out var dfpEl) &&
                dfpEl.ValueKind == JsonValueKind.Number &&
                dfpEl.TryGetUInt32(out var dfpId))
            {
                info.ParagraphFormatId = dfpId;
            }

            if (root.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in contentEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    if (item.TryGetProperty("result_dftx_id", out var resultEl) &&
                        resultEl.ValueKind == JsonValueKind.Number &&
                        resultEl.TryGetUInt32(out var resultId))
                    {
                        info.TextFormatIds.Add(resultId);
                    }
                    else if (item.TryGetProperty("dftx_id", out var dftxEl) &&
                             dftxEl.ValueKind == JsonValueKind.Number &&
                             dftxEl.TryGetUInt32(out var dftxId))
                    {
                        info.TextFormatIds.Add(dftxId);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore invalid JSON payloads.
        }

        return info;
    }

    private static bool IsFontError(ValidationError error)
    {
        var message = error.Message ?? string.Empty;
        var field = error.Field ?? string.Empty;
        var category = error.Category ?? string.Empty;

        return message.Contains("font", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("bold", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("italic", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("underline", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ukuran font", StringComparison.OrdinalIgnoreCase) ||
               field.Contains("font", StringComparison.OrdinalIgnoreCase) ||
               field.Contains("bold", StringComparison.OrdinalIgnoreCase) ||
               field.Contains("italic", StringComparison.OrdinalIgnoreCase) ||
               field.Contains("underline", StringComparison.OrdinalIgnoreCase) ||
               category.Contains("font", StringComparison.OrdinalIgnoreCase);
    }

}

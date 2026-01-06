using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles extraction and saving of media files from DOCX documents
/// </summary>
public class MediaExtractor
{
    private readonly ILogger _logger;
    private readonly string _storagePath;

    public MediaExtractor(ILogger logger, string storagePath)
    {
        _logger = logger;
        _storagePath = storagePath;
    }

    /// <summary>
    /// Extracts all media (images) from the document and saves them to storage
    /// </summary>
    public async Task ExtractAllMedia(WordprocessingDocument doc, int dokumenId)
    {
        var main = doc.MainDocumentPart!;
        
        foreach (var imgPart in main.ImageParts)
        {
            string rId = main.GetIdOfPart(imgPart);
            
            using var stream = imgPart.GetStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            
            string ext = Path.GetExtension(imgPart.Uri.OriginalString).TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "png";
            
            string filename = $"{dokumenId}_{rId}.{ext}";
            string fullPath = Path.Combine(_storagePath, filename);
            
            await File.WriteAllBytesAsync(fullPath, ms.ToArray());
            _logger.LogInformation("Saved media: {Filename}", filename);
        }
    }
}

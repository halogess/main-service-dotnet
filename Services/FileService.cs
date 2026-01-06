using Microsoft.AspNetCore.Http;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Security;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IFileService
{
    void ValidateExtension(string filename);
    Task ValidateDocumentSource(IFormFile file);
    Task<string> SaveFile(IFormFile file, string nrp, int entityId, string entityType);
    string GetPdfPath(string docxPath);
    void DeleteFile(string filename);
}

public class FileService : IFileService
{
    private readonly string[] _allowedExtensions = { ".docx" };
    private readonly string _storageBasePath;

    public FileService()
    {
        _storageBasePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
    }

    public void ValidateExtension(string filename)
    {
        var extension = Path.GetExtension(filename).ToLower();
        
        if (!_allowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"Ekstensi file tidak diizinkan. Hanya {string.Join(", ", _allowedExtensions)} yang diperbolehkan.");
        }
    }

    public async Task ValidateDocumentSource(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLower();
        
        if (extension == ".pdf" || extension != ".docx")
            return;

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        try
        {
            using var wordDoc = WordprocessingDocument.Open(memoryStream, false);
            
            var extendedProps = wordDoc.ExtendedFilePropertiesPart
                ?? throw new InvalidOperationException("File tidak memiliki extended properties. Kemungkinan bukan file Word asli");

            var application = extendedProps.Properties?.Application?.Text;
            if (string.IsNullOrEmpty(application) || !application.Contains("Microsoft"))
                throw new InvalidOperationException("File tidak dibuat dengan Microsoft Word. Pastikan file asli dibuat di Word, bukan hasil konversi dari PDF");

            if (wordDoc.MainDocumentPart?.Document == null)
                throw new InvalidOperationException("File tidak memiliki main document part");

            var stylesPart = wordDoc.MainDocumentPart?.StyleDefinitionsPart;
            if (stylesPart == null)
                throw new InvalidOperationException("File tidak memiliki style definition. Pastikan file dibuat di Microsoft Word");
            
            if (stylesPart?.Styles == null || !stylesPart.Styles.OuterXml.Contains("styleId=\"Normal\""))
                throw new InvalidOperationException("File tidak memiliki style standar Microsoft Word");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("File tidak valid atau rusak");
        }
    }

    public async Task<string> SaveFile(IFormFile file, string nrp, int entityId, string entityType)
    {
        if (string.IsNullOrEmpty(nrp))
            throw new InvalidOperationException("NRP tidak boleh kosong");
        if (string.IsNullOrEmpty(entityType))
            throw new InvalidOperationException("Entity type tidak boleh kosong");

        var entityPath = Path.Combine(_storageBasePath, entityType, nrp, entityId.ToString(), "docx");
        Directory.CreateDirectory(entityPath);

        var safeFilename = Path.GetFileName(file.FileName);
        var filePath = Path.Combine(entityPath, safeFilename);

        var fullEntityPath = Path.GetFullPath(entityPath);
        var fullFilePath = Path.GetFullPath(filePath);
        
        if (!fullFilePath.StartsWith(fullEntityPath))
            throw new SecurityException("Path traversal detected!");

        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return Path.Combine(entityType, nrp, entityId.ToString(), "docx", safeFilename);
    }

    public string GetPdfPath(string docxPath)
    {
        return docxPath.Replace("/docx/", "/pdf/").Replace("\\docx\\", "\\pdf\\").Replace(".docx", ".pdf");
    }

    public void DeleteFile(string filename)
    {
        if (File.Exists(filename))
            File.Delete(filename);
    }
}

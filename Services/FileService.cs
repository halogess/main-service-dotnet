using Microsoft.AspNetCore.Http;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Wordprocessing;

namespace _.Services;

public interface IFileService
{
    void ValidateExtension(string filename);
    Task ValidateDocumentSource(IFormFile file);
    Task<string> SaveFile(IFormFile file, string nrp, int dokumenId);
    void DeleteFile(string filename);
}

public class FileService : IFileService
{
    private readonly string[] _allowedExtensions = { ".pdf", ".doc", ".docx" };
    private readonly string _uploadPath = "uploads";

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
        
        if (extension == ".pdf")
        {
            return;
        }

        if (extension != ".docx")
        {
            throw new InvalidOperationException("Hanya file .docx yang dapat divalidasi sumbernya");
        }

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        try
        {
            using var wordDoc = WordprocessingDocument.Open(memoryStream, false);
            
                var extendedProps = wordDoc.ExtendedFilePropertiesPart?.Properties;
            var coreProps = wordDoc.PackageProperties;
            
            // Whitelist: Hanya terima file dari Microsoft Word
            var appName = extendedProps?.Application?.Text;
            
            if (string.IsNullOrEmpty(appName))
            {
                throw new InvalidOperationException("File tidak memiliki informasi aplikasi pembuat. Pastikan file dibuat di Microsoft Word");
            }
            
            var lowerAppName = appName.ToLower();
            if (!lowerAppName.Contains("microsoft") && !lowerAppName.Contains("word"))
            {
                throw new InvalidOperationException($"File harus dibuat dengan Microsoft Word. Aplikasi pembuat: {appName}");
            }
            
            // Validasi tambahan: Cek revision count
            var revision = coreProps.Revision;
            if (!string.IsNullOrEmpty(revision) && int.TryParse(revision, out int revNum))
            {
                if (revNum < 2)
                {
                    throw new InvalidOperationException("File harus dibuat dan diedit di Microsoft Word, bukan hasil konversi dari aplikasi lain");
                }
            }
            
            // Validasi struktur: Cek apakah punya style standar MS Word
            var mainPart = wordDoc.MainDocumentPart;
            var stylesPart = mainPart?.StyleDefinitionsPart;
            
            if (stylesPart?.Styles == null)
            {
                throw new InvalidOperationException("File tidak memiliki style definition. Pastikan file dibuat di Microsoft Word");
            }
            
            var stylesXml = stylesPart.Styles.OuterXml;
            if (!stylesXml.Contains("styleId=\"Normal\""))
            {
                throw new InvalidOperationException("File tidak memiliki style standar Microsoft Word");
            }
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

    public async Task<string> SaveFile(IFormFile file, string nrp, int dokumenId)
    {
        if (string.IsNullOrEmpty(nrp))
        {
            throw new InvalidOperationException("NRP tidak boleh kosong");
        }

        var uploadDir = Path.Combine(_uploadPath, nrp);
        Directory.CreateDirectory(uploadDir);

        var filename = $"{dokumenId}_{file.FileName}";
        var filePath = Path.Combine(uploadDir, filename);

        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return filename;
    }

    public void DeleteFile(string filename)
    {
        if (File.Exists(filename))
        {
            File.Delete(filename);
        }
    }
}

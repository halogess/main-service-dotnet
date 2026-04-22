using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class DocxExtractionNoteRefScopeTests
{
    [Theory]
    [InlineData("dokumen", 101u)]
    [InlineData("bab", 202u)]
    [InlineData("aturan", 303u)]
    public async Task ExtractDocxToDatabase_ShouldPersistRefScopedFootnoteMetadata(
        string refTipe,
        uint refId)
    {
        var docxPath = CreateDocxWithSingleFootnote();

        try
        {
            var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            using var db = new KorektorBukuDbContext(options);
            var loggerMock = new Mock<ILogger<DocxExtractionService>>();
            var service = new DocxExtractionService(db, loggerMock.Object);

            await service.ExtractDocxToDatabase(docxPath, refTipe, refId);

            var note = await db.DokumenNotes.SingleAsync();

            Assert.Equal(refTipe, note.DnoteRefTipe);
            Assert.Equal(refId, note.DnoteRefId);
            Assert.Equal((uint)1, note.DnoteNumber);
            Assert.Equal("footnote", note.DnoteKind);
            Assert.Contains("\"dfp_id\"", note.DnoteJsonTree ?? string.Empty);
            Assert.Contains("\"dftx_id\"", note.DnoteJsonTree ?? string.Empty);
        }
        finally
        {
            if (File.Exists(docxPath))
                File.Delete(docxPath);
        }
    }

    private static string CreateDocxWithSingleFootnote()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");

        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();

        var footnotesPart = mainPart.AddNewPart<FootnotesPart>();
        footnotesPart.Footnotes = new Footnotes(
            new Footnote(
                new Paragraph(
                    new Run(new Text("Catatan kaki uji"))
                ))
            {
                Id = 1
            });
        footnotesPart.Footnotes.Save();

        var body = new Body(
            new Paragraph(
                new Run(new Text("Isi utama ")),
                new Run(new FootnoteReference { Id = 1 })),
            new SectionProperties());

        mainPart.Document.Append(body);
        mainPart.Document.Save();

        return path;
    }
}

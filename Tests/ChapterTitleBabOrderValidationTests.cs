using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ChapterTitleBabOrderValidationTests
{
    [Fact]
    public async Task ValidateChapterTitleAsync_ShouldReportMismatch_WhenBabNumberDoesNotMatchBabOrder()
    {
        using var fixture = new SqliteChapterTitleFixture();
        fixture.AddBab(babId: 21, babOrder: 2);
        fixture.AddActiveChapterRule("BAB I");
        fixture.AddBodyStructure(babId: 21);
        fixture.AddChapterTitleElements(numberLine: "BAB I", titleLine: "TINJAUAN PUSTAKA");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeChapterTitleValidationForBabAsync(service, babId: 21);

        var error = Assert.Single(result.Errors, item => item.Message == "Nomor bab tidak sesuai dengan urutan bab");
        Assert.Equal("judul_bab", error.Field);
        Assert.Equal("BAB II", error.Expected);
        Assert.Equal("BAB I", error.Actual);
    }

    [Fact]
    public async Task ValidateChapterTitleAsync_ShouldPass_WhenBabNumberMatchesDecimalBabOrder()
    {
        using var fixture = new SqliteChapterTitleFixture();
        fixture.AddBab(babId: 22, babOrder: 2);
        fixture.AddActiveChapterRule("BAB 1");
        fixture.AddBodyStructure(babId: 22);
        fixture.AddChapterTitleElements(numberLine: "BAB 2", titleLine: "TINJAUAN PUSTAKA");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeChapterTitleValidationForBabAsync(service, babId: 22);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Nomor bab tidak sesuai dengan urutan bab");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateChapterTitleAsync_ShouldAcceptTrailingManualLineBreak_AsEmptyLineAfterTitle()
    {
        using var fixture = new SqliteChapterTitleFixture();
        fixture.AddBab(babId: 23, babOrder: 1);
        fixture.AddActiveChapterRule("BAB I");
        fixture.AddBodyStructure(babId: 23);
        fixture.AddChapterTitleElementWithTrailingManualLineBreak("BAB I\nPENDAHULUAN\n");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeChapterTitleValidationForBabAsync(service, babId: 23);

        Assert.DoesNotContain(
            result.Errors,
            item => item.Message == "Jumlah baris kosong setelah judul bab tidak sesuai");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateChapterTitleAsync_ShouldValidateBlankParagraphFormatAgainstParagraphRule()
    {
        using var fixture = new SqliteChapterTitleFixture();
        fixture.AddBab(babId: 24, babOrder: 1);
        fixture.AddActiveChapterRule("BAB I");
        fixture.AddParagraphRule();
        fixture.AddBodyStructure(babId: 24);
        fixture.AddChapterTitleElementsWithFormattedBlankParagraph(
            blankParagraphFormatId: 301,
            blankTextFormatId: 401);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeChapterTitleValidationForBabAsync(service, babId: 24);

        Assert.Contains(result.Errors, item => item.Message == "Font baris kosong setelah judul bab tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Ukuran font baris kosong setelah judul bab tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Line spacing baris kosong setelah judul bab tidak sesuai dengan aturan paragraf");
    }

    [Fact]
    public async Task ValidateChapterTitleAsync_ShouldReportFirstLineIndent_WhenTitleStartsWithLeadingSpaces()
    {
        using var fixture = new SqliteChapterTitleFixture();
        fixture.AddBab(babId: 25, babOrder: 1);
        fixture.AddActiveChapterRule("BAB I");
        fixture.AddBodyStructure(babId: 25);
        fixture.AddChapterTitleElements(numberLine: "BAB I", titleLine: "  PENDAHULUAN");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeChapterTitleValidationForBabAsync(service, babId: 25);

        var error = Assert.Single(result.Errors, item => item.Message == "First line indent judul bab tidak sesuai karena diawali spasi/tab");
        Assert.Equal("judul_bab", error.Field);
        Assert.Equal("0 cm", error.Expected);
        Assert.Contains("0.00 cm + 2 spasi awal", error.Actual, StringComparison.Ordinal);
    }

    private static ValidationService CreateValidationService(KorektorBukuDbContext db)
        => new(db, NullLogger<ValidationService>.Instance);

    private static async Task<ValidationResult> InvokeChapterTitleValidationForBabAsync(ValidationService service, uint babId)
    {
        var validationServiceType = typeof(ValidationService);
        var contextType = validationServiceType.GetNestedType("ValidationTargetContext", BindingFlags.NonPublic);
        Assert.NotNull(contextType);

        var context = Activator.CreateInstance(contextType!, nonPublic: true);
        Assert.NotNull(context);

        SetAutoPropertyBackingField(context!, "SectionRefType", "bab");
        SetAutoPropertyBackingField(context!, "SectionRefId", babId);
        SetAutoPropertyBackingField(context!, "BabId", babId);

        var contextField = validationServiceType.GetField("_activeValidationTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(contextField);

        var previous = contextField!.GetValue(service);
        contextField.SetValue(service, context);

        try
        {
            var method = validationServiceType.GetMethod("ValidateChapterTitleAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return await InvokeAsync<ValidationResult>(method!, service, new object[] { (int)babId, null!, CancellationToken.None });
        }
        finally
        {
            contextField.SetValue(service, previous);
        }
    }

    private static async Task<T> InvokeAsync<T>(MethodInfo method, object target, object[] args)
    {
        var task = method.Invoke(target, args) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task!.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        var value = resultProperty!.GetValue(task);
        return Assert.IsType<T>(value);
    }

    private static void SetAutoPropertyBackingField(object target, string propertyName, object? value)
    {
        var field = target.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private sealed class SqliteChapterTitleFixture : IDisposable
    {
        public SqliteChapterTitleFixture()
        {
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            var options = new DbContextOptionsBuilder<KorektorBukuDbContext>()
                .UseSqlite(Connection)
                .Options;

            Db = new KorektorBukuDbContext(options);
            Db.Database.EnsureCreated();
        }

        public SqliteConnection Connection { get; }

        public KorektorBukuDbContext Db { get; }

        public void AddBab(uint babId, byte babOrder)
        {
            Db.Babs.Add(new Bab
            {
                BabId = babId,
                BukuId = 1,
                BabOrder = babOrder,
                BabFilename = $"bab-{babOrder}.docx"
            });
        }

        public void AddActiveChapterRule(string numberFormat)
        {
            Db.Aturans.Add(new Aturan
            {
                AturanId = 1,
                AturanVersi = "test",
                AturanStatus = AturanStatusValues.Aktif,
                AturanCreatedAt = DateTime.UtcNow
            });

            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 1,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "judul_bab",
                AturanDetailJsonValue =
                    $$"""
                    {
                      "numbering": {
                        "number_format": { "value": "{{numberFormat}}", "is_editable": false },
                        "case": { "value": "UPPERCASE", "is_editable": true },
                        "enter_after_number": { "value": true, "is_editable": true }
                      },
                      "paragraph": {
                        "indentation": {
                          "first_line_indent": { "value": 0, "is_editable": true }
                        }
                      },
                      "struktur_konten": {
                        "jumlah_baris_kosong_setelah": { "value": 1, "is_editable": true }
                      }
                    }
                    """,
                AturanDetailStatus = 1
            });
        }

        public void AddParagraphRule()
        {
            Db.AturanDetails.Add(new AturanDetail
            {
                AturanDetailId = 2,
                AturanId = 1,
                AturanDetailKategori = "Isi Buku",
                AturanDetailKey = "paragraf",
                AturanDetailJsonValue =
                    """
                    {
                      "font": {
                        "font_name": { "value": "Times New Roman", "is_editable": true },
                        "font_size": { "value": 12, "is_editable": true }
                      },
                      "paragraph": {
                        "spacing": {
                          "line_spacing": { "value": 1.5, "is_editable": true },
                          "before": { "value": 0, "is_editable": true },
                          "after": { "value": 0, "is_editable": true }
                        }
                      }
                    }
                    """,
                AturanDetailStatus = 1
            });
        }

        public void AddBodyStructure(uint babId)
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = 1,
                DsecRefTipe = "bab",
                DsecRefId = babId,
                DsecIndex = 1
            });

            Db.DokumenParts.Add(new DokumenPart
            {
                DpartId = 1,
                DsecId = 1,
                DpartType = "body"
            });
        }

        public void AddChapterTitleElements(string numberLine, string titleLine)
        {
            AddParagraphFormat(101);
            AddParagraphFormat(102);
            AddParagraphFormat(103);

            AddElement(elementId: 1001, sequence: 1, paragraphFormatId: 101, text: numberLine);
            AddElement(elementId: 1002, sequence: 2, paragraphFormatId: 102, text: titleLine);
            AddElement(elementId: 1003, sequence: 3, paragraphFormatId: 103, text: string.Empty);

            AddVisual(visualId: 1, elementId: 1001, label: "judul_bab");
            AddVisual(visualId: 2, elementId: 1002, label: "judul_bab");
        }

        public void AddChapterTitleElementWithTrailingManualLineBreak(string titleText)
        {
            AddParagraphFormat(201);
            AddParagraphFormat(202);

            AddElement(elementId: 2001, sequence: 1, paragraphFormatId: 201, text: titleText);
            AddElement(elementId: 2002, sequence: 2, paragraphFormatId: 202, text: "Paragraf isi bab pertama.");

            AddVisual(visualId: 11, elementId: 2001, label: "judul_bab");
        }

        public void AddChapterTitleElementsWithFormattedBlankParagraph(uint blankParagraphFormatId, uint blankTextFormatId)
        {
            AddParagraphFormat(101);
            AddParagraphFormat(102);
            AddParagraphFormat(blankParagraphFormatId, spacingLineTwips: 240);
            AddParagraphFormat(104);
            AddTextFormat(blankTextFormatId, "Arial", 20);

            AddElement(elementId: 3001, sequence: 1, paragraphFormatId: 101, text: "BAB I");
            AddElement(elementId: 3002, sequence: 2, paragraphFormatId: 102, text: "PENDAHULUAN");
            AddElementWithTextFormat(elementId: 3003, sequence: 3, paragraphFormatId: blankParagraphFormatId, textFormatId: blankTextFormatId, text: string.Empty);
            AddElement(elementId: 3004, sequence: 4, paragraphFormatId: 104, text: "Paragraf isi bab pertama.");

            AddVisual(visualId: 21, elementId: 3001, label: "judul_bab");
            AddVisual(visualId: 22, elementId: 3002, label: "judul_bab");
        }

        private void AddParagraphFormat(uint paragraphFormatId, uint? spacingLineTwips = null)
        {
            Db.DokumenFormatParagrafs.Add(new DokumenFormatParagraf
            {
                DfpId = paragraphFormatId,
                DfpIndFirstLineTwips = 0,
                DfpSpacingLineTwips = spacingLineTwips,
                DfpSpacingLineRule = spacingLineTwips.HasValue ? "auto" : null,
                DfpSpacingBeforeTwips = 0,
                DfpSpacingAfterTwips = 0
            });
        }

        private void AddTextFormat(uint textFormatId, string fontName, ushort sizeHalfPt)
        {
            Db.DokumenFormatTexts.Add(new DokumenFormatText
            {
                DftxId = textFormatId,
                DftxFontAscii = fontName,
                DftxSizeHalfpt = sizeHalfPt,
                DftxBold = false,
                DftxItalic = false,
                DftxUnderline = "none"
            });
        }

        private void AddElement(ulong elementId, uint sequence, uint paragraphFormatId, string text)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = $$"""{"dfp_id":{{paragraphFormatId}},"text":{{JsonSerializer.Serialize(text)}}}""",
                DelemenXml = string.Empty
            });
        }

        private void AddElementWithTextFormat(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId, string text)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree =
                    $$"""{"dfp_id":{{paragraphFormatId}},"content":[{"type":"text","dftx_id":{{textFormatId}},"value":{{JsonSerializer.Serialize(text)}}}]}""",
                DelemenXml = string.Empty
            });
        }

        private void AddVisual(ulong visualId, ulong elementId, string label)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = visualId,
                DokumenElemenId = elementId,
                DevLabel = label,
                DevLabelStruktural = label,
                DevPage = 1,
                DevRefTipe = "bab",
                DevRefId = 1,
                DevBboxX0 = 10,
                DevBboxY0 = 10,
                DevBboxX1 = 50,
                DevBboxY1 = 20
            });
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

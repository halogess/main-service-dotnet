using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class SubchapterBlankParagraphValidationTests
{
    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldRequireBlankParagraphBeforeSubchapter_WhenContinuingOnSamePage()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules();

        fixture.AddParagraphElement(1001, 1, 102, 202, "Paragraf sebelumnya", page: 1, y0: 100f, y1: 120f);
        fixture.AddSubchapterElement(1002, 2, 101, "1.1 Latar Belakang", page: 1, y0: 140f, y1: 160f);
        fixture.AddParagraphElement(1003, 3, 102, 202, "Isi subbab dimulai di sini.", page: 1, y0: 180f, y1: 220f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        var error = Assert.Single(result.Errors, item => item.Message == "Jumlah baris kosong sebelum judul subbab tidak sesuai");
        Assert.Equal("judul_subbab", error.Field);
        Assert.Equal("Tepat 1 baris kosong", error.Expected);
        Assert.Equal("0 baris kosong", error.Actual);
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldSkipBlankParagraphRequirement_WhenSubchapterStartsAtTopOfNewPage()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules();

        fixture.AddParagraphElement(1001, 1, 102, 202, "Paragraf terakhir halaman sebelumnya", page: 1, y0: 680f, y1: 710f);
        fixture.AddSubchapterElement(1002, 2, 101, "1.1 Latar Belakang", page: 2, y0: 100f, y1: 120f);
        fixture.AddParagraphElement(1003, 3, 102, 202, "Isi subbab di halaman baru.", page: 2, y0: 140f, y1: 180f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sebelum judul subbab tidak sesuai");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldValidateBlankParagraphFormatAgainstParagraphRule()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules();

        fixture.AddParagraphElement(1001, 1, 102, 202, "Paragraf sebelum subbab", page: 1, y0: 100f, y1: 120f);
        fixture.AddBlankParagraphElement(1002, 2, 103, 203, page: 1, y0: 130f, y1: 145f);
        fixture.AddSubchapterElement(1003, 3, 101, "1.1 Latar Belakang", page: 1, y0: 155f, y1: 175f);
        fixture.AddParagraphElement(1004, 4, 102, 202, "Isi subbab setelah judul.", page: 1, y0: 190f, y1: 225f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.Contains(result.Errors, item => item.Message == "Font baris kosong sebelum judul subbab tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Ukuran font baris kosong sebelum judul subbab tidak sesuai dengan aturan paragraf");
        Assert.Contains(result.Errors, item => item.Message == "Line spacing baris kosong sebelum judul subbab tidak sesuai dengan aturan paragraf");
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldNormalizeLeftIndentForNumberedSubchapter()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules();

        fixture.AddSubchapterElement(1201, 1, 104, "1.1 Latar Belakang", page: 1, y0: 100f, y1: 120f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Left indent judul subbab tidak sesuai");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldReportLeftIndent_WhenNormalizedNumberingIndentDoesNotMatch()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules();

        fixture.AddSubchapterElement(1301, 1, 105, "1.1 Latar Belakang", page: 1, y0: 100f, y1: 120f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        var error = Assert.Single(result.Errors, item => item.Message == "Left indent judul subbab tidak sesuai");
        Assert.Equal("0 cm", error.Expected);
        Assert.Equal("1.27 cm", error.Actual);
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldCountInvisibleBlankParagraphBeforeSubchapter()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules();

        fixture.AddParagraphElement(1401, 1, 102, 202, "Paragraf sebelum subbab", page: 1, y0: 100f, y1: 120f);
        fixture.AddInvisibleBlankParagraphElement(1402, 2, 102, 202);
        fixture.AddSubchapterElement(1403, 3, 104, "1.1 Latar Belakang", page: 1, y0: 150f, y1: 170f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah baris kosong sebelum judul subbab tidak sesuai");
        Assert.DoesNotContain(result.Errors, item => item.Message == "Left indent judul subbab tidak sesuai");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldIgnoreQuotedLowercasePhraseForTitleCase()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules();

        fixture.AddParagraphElement(1101, 1, 102, 202, "Paragraf terakhir halaman sebelumnya", page: 1, y0: 680f, y1: 710f);
        fixture.AddSubchapterElement(1102, 2, 101, "1.1 Konsep \"machine learning\" Dasar", page: 2, y0: 100f, y1: 120f);
        fixture.AddParagraphElement(1103, 3, 102, 202, "Isi subbab di halaman baru.", page: 2, y0: 140f, y1: 180f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Judul subbab harus Title Case");
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldUseNumberedDocxSubchapterWithoutVisualAsSequenceContext()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules(blankLinesBeforeSubchapter: 0);

        fixture.AddSubchapterElement(1501, 1, 104, "1.1 Latar Belakang", page: 1, y0: 100f, y1: 120f);
        fixture.AddSubchapterElementWithoutVisual(1502, 2, 104, "1.2 Rumusan Masalah");
        fixture.AddSubchapterElement(1503, 3, 104, "1.3 Tujuan Penelitian", page: 1, y0: 180f, y1: 200f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Subbab 1.2 tidak ditemukan sebelum 1.3");
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldNotUseVisuallyClassifiedListAsSequenceSubchapterFallback()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules(blankLinesBeforeSubchapter: 0);

        fixture.AddSubchapterElement(1551, 1, 104, "1.1 Latar Belakang", page: 1, y0: 100f, y1: 120f);
        fixture.AddListElement(1552, 2, 104, "1.2 Item List Biasa", page: 1, y0: 140f, y1: 160f);
        fixture.AddSubchapterElement(1553, 3, 104, "1.3 Tujuan Penelitian", page: 1, y0: 180f, y1: 200f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.Contains(result.Errors, item => item.Message == "Subbab 1.2 tidak ditemukan sebelum 1.3");
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldReportDuplicateForDifferentVisualSubchapterElements()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules(blankLinesBeforeSubchapter: 0);

        fixture.AddSubchapterElement(1601, 1, 104, "1.1 Latar Belakang", page: 1, y0: 100f, y1: 120f);
        fixture.AddSubchapterElement(1602, 2, 104, "1.1 Rumusan Masalah", page: 1, y0: 160f, y1: 180f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.Contains(result.Errors, item => item.Message == "Nomor subbab 1.1 duplikat");
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldNotReportDuplicateForMultipleVisualRowsOfSameElement()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules(blankLinesBeforeSubchapter: 0);

        fixture.AddSubchapterElement(1701, 1, 104, "1.1 Latar Belakang", page: 1, y0: 100f, y1: 120f);
        fixture.AddSecondVisualRow(1701, 2701, "1.1 Latar Belakang", page: 2, y0: 100f, y1: 120f, label: "judul_subbab");
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.DoesNotContain(result.Errors, item => item.Message == "Nomor subbab 1.1 duplikat");
    }

    [Fact]
    public async Task ValidateSubchapterTitleAsync_ShouldSuppressSameLevelNoiseForBranchWithSequenceError()
    {
        using var fixture = new SqliteSubchapterFixture();
        fixture.AddDokumen();
        fixture.AddBodyStructure();
        fixture.AddFormats();
        fixture.AddActiveRules(
            minimumSameLevelSubchapters: 2,
            blankLinesBeforeSubchapter: 0);

        fixture.AddSubchapterElement(1801, 1, 104, "1.1 Latar Belakang", page: 1, y0: 100f, y1: 120f);
        fixture.AddSubchapterElement(1802, 2, 104, "1.3 Tujuan Penelitian", page: 1, y0: 160f, y1: 180f);
        await fixture.Db.SaveChangesAsync();

        var service = CreateValidationService(fixture.Db);
        var result = await InvokeSubchapterTitleValidationAsync(service, 10);

        Assert.Contains(result.Errors, item => item.Message == "Subbab 1.2 tidak ditemukan sebelum 1.3");
        Assert.DoesNotContain(result.Errors, item => item.Message == "Jumlah subbab pada level yang sama tidak sesuai, minimal harus ada 2");
    }

    private static ValidationService CreateValidationService(KorektorBukuDbContext db)
        => new(db, NullLogger<ValidationService>.Instance);

    private static async Task<ValidationResult> InvokeSubchapterTitleValidationAsync(ValidationService service, int dokumenId)
    {
        var method = typeof(ValidationService).GetMethod("ValidateSubchapterTitleAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(service, new object?[] { dokumenId, null, CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task!;

        var resultProperty = task!.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return Assert.IsType<ValidationResult>(resultProperty!.GetValue(task));
    }

    private sealed class SqliteSubchapterFixture : IDisposable
    {
        public SqliteSubchapterFixture()
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

        public void AddDokumen()
        {
            Db.Dokumens.Add(new Dokumen
            {
                DokumenId = 10,
                MhsNrp = "1234567890",
                DokumenFilename = "uji-subbab.docx",
                DokumenStatus = "selesai"
            });
        }

        public void AddBodyStructure()
        {
            Db.DokumenSections.Add(new DokumenSection
            {
                DsecId = 1,
                DsecRefTipe = "dokumen",
                DsecRefId = 10,
                DsecIndex = 1,
                DsecPageWidthTwips = 11907,
                DsecPageHeightTwips = 16839,
                DsecOrientation = "portrait",
                DsecMarginTopTwips = 2268,
                DsecMarginBottomTwips = 1701,
                DsecMarginLeftTwips = 2268,
                DsecMarginRightTwips = 1701,
                DsecHeaderMarginTwips = 1417,
                DsecFooterMarginTwips = 850,
                DsecColumnCount = 1
            });

            Db.DokumenParts.Add(new DokumenPart
            {
                DpartId = 1,
                DsecId = 1,
                DpartType = "body"
            });
        }

        public void AddActiveRules(
            int minimumSameLevelSubchapters = 1,
            int blankLinesBeforeSubchapter = 1)
        {
            Db.Aturans.Add(new Aturan
            {
                AturanId = 1,
                AturanVersi = "test",
                AturanStatus = AturanStatusValues.Aktif,
                AturanCreatedAt = DateTime.UtcNow
            });

            Db.AturanDetails.AddRange(
                new AturanDetail
                {
                    AturanDetailId = 1,
                    AturanId = 1,
                    AturanDetailKategori = "Isi Buku",
                    AturanDetailKey = "judul_subbab",
                    AturanDetailJsonValue =
                        $$"""
                        {
                          "font": {
                            "font_name": { "value": "Times New Roman", "is_editable": true },
                            "font_size": { "value": 14, "is_editable": true },
                            "font_style": {
                              "bold": { "value": true, "is_editable": true },
                              "italic": { "value": false, "is_editable": true },
                              "underline": { "value": false, "is_editable": true }
                            }
                          },
                          "paragraph": {
                            "alignment": { "value": "left", "is_editable": true },
                            "indentation": {
                              "left_indent": { "value": 0, "is_editable": true },
                              "right_indent": { "value": 0, "is_editable": true }
                            },
                            "hanging_min_cm": { "value": 1.27, "is_editable": true },
                            "hanging_max_cm": { "value": 2.5, "is_editable": true },
                            "spacing": {
                              "line_spacing": { "value": 1.5, "is_editable": true },
                              "before": { "value": 0, "is_editable": true },
                              "after": { "value": 0, "is_editable": true }
                            }
                          },
                          "numbering": {
                            "number_format": { "value": "1.1, 1.1.1, 1.1.1.1", "is_editable": false },
                            "case": { "value": "Title Case", "is_editable": true }
                          },
                          "struktur_konten": {
                            "minimal_paragraf_setelah": { "value": 0, "is_editable": true },
                            "cegah_posisi_paling_bawah": { "value": false, "is_editable": true },
                            "minimal_subbab_level_sama": { "value": {{minimumSameLevelSubchapters}}, "is_editable": true },
                            "jumlah_baris_kosong_sebelum": { "value": {{blankLinesBeforeSubchapter}}, "is_editable": true },
                            "abaikan_jika_di_awal_halaman": { "value": true, "is_editable": true }
                          }
                        }
                        """
                },
                new AturanDetail
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
                            "alignment": { "value": "justify", "is_editable": true },
                            "indentation": {
                              "left_indent": { "value": 0, "is_editable": true },
                              "right_indent": { "value": 0, "is_editable": true },
                              "first_line_indent": { "value": 1.27, "is_editable": true }
                            },
                            "spacing": {
                              "line_spacing": { "value": 1.5, "is_editable": true },
                              "before": { "value": 0, "is_editable": true },
                              "after": { "value": 0, "is_editable": true }
                            }
                          }
                        }
                        """
                });
        }

        public void AddFormats()
        {
            Db.DokumenFormatParagrafs.AddRange(
                new DokumenFormatParagraf
                {
                    DfpId = 101,
                    DfpJc = "left",
                    DfpIndHangingTwips = 720,
                    DfpSpacingLineTwips = 360,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0,
                    DfpIsList = true,
                    DfpListNumId = 1,
                    DfpListIlvl = 0
                },
                new DokumenFormatParagraf
                {
                    DfpId = 102,
                    DfpJc = "both",
                    DfpIndFirstLineTwips = 720,
                    DfpSpacingLineTwips = 360,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0
                },
                new DokumenFormatParagraf
                {
                    DfpId = 103,
                    DfpJc = "both",
                    DfpIndFirstLineTwips = 720,
                    DfpSpacingLineTwips = 240,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0
                },
                new DokumenFormatParagraf
                {
                    DfpId = 104,
                    DfpJc = "left",
                    DfpIndLeftTwips = 720,
                    DfpIndStartTwips = 720,
                    DfpIndHangingTwips = 720,
                    DfpSpacingLineTwips = 360,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0,
                    DfpIsList = true,
                    DfpListNumId = 1,
                    DfpListIlvl = 0
                },
                new DokumenFormatParagraf
                {
                    DfpId = 105,
                    DfpJc = "left",
                    DfpIndLeftTwips = 1440,
                    DfpIndStartTwips = 1440,
                    DfpIndHangingTwips = 720,
                    DfpSpacingLineTwips = 360,
                    DfpSpacingLineRule = "auto",
                    DfpSpacingBeforeTwips = 0,
                    DfpSpacingAfterTwips = 0,
                    DfpIsList = true,
                    DfpListNumId = 1,
                    DfpListIlvl = 0
                });

            Db.DokumenFormatTexts.AddRange(
                new DokumenFormatText
                {
                    DftxId = 201,
                    DftxFontAscii = "Times New Roman",
                    DftxSizeHalfpt = 28,
                    DftxBold = true,
                    DftxItalic = false,
                    DftxUnderline = "none"
                },
                new DokumenFormatText
                {
                    DftxId = 202,
                    DftxFontAscii = "Times New Roman",
                    DftxSizeHalfpt = 24,
                    DftxBold = false,
                    DftxItalic = false,
                    DftxUnderline = "none"
                },
                new DokumenFormatText
                {
                    DftxId = 203,
                    DftxFontAscii = "Arial",
                    DftxSizeHalfpt = 20,
                    DftxBold = false,
                    DftxItalic = false,
                    DftxUnderline = "none"
                });
        }

        public void AddParagraphElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId, string text, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, paragraphFormatId, textFormatId, text, page, y0, y1, "paragraf");
        }

        public void AddBlankParagraphElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, paragraphFormatId, textFormatId, string.Empty, page, y0, y1, "paragraf");
        }

        public void AddInvisibleBlankParagraphElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateParagraphJson(paragraphFormatId, textFormatId, string.Empty),
                DelemenXml = string.Empty
            });
        }

        public void AddSubchapterElement(ulong elementId, uint sequence, uint paragraphFormatId, string text, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, paragraphFormatId, 201, text, page, y0, y1, "judul_subbab");
        }

        public void AddListElement(ulong elementId, uint sequence, uint paragraphFormatId, string text, uint page, float y0, float y1)
        {
            AddElement(elementId, sequence, paragraphFormatId, 201, text, page, y0, y1, "list_level_1");
        }

        public void AddSubchapterElementWithoutVisual(ulong elementId, uint sequence, uint paragraphFormatId, string text)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateParagraphJson(paragraphFormatId, 201, text),
                DelemenXml = string.Empty
            });
        }

        public void AddSecondVisualRow(ulong elementId, ulong visualId, string text, uint page, float y0, float y1, string label)
        {
            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = visualId,
                DokumenElemenId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DevBboxX0 = 40,
                DevBboxY0 = y0,
                DevBboxX1 = 520,
                DevBboxY1 = y1,
                DevLabel = label,
                DevLabelStruktural = label,
                DevText = text
            });
        }

        private void AddElement(ulong elementId, uint sequence, uint paragraphFormatId, uint textFormatId, string text, uint page, float y0, float y1, string label)
        {
            Db.DokumenElemens.Add(new DokumenElemen
            {
                DelemenId = elementId,
                DpartId = 1,
                DelemenSequence = sequence,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateParagraphJson(paragraphFormatId, textFormatId, text),
                DelemenXml = string.Empty
            });

            Db.DokumenElemenVisuals.Add(new DokumenElemenVisual
            {
                DevId = elementId,
                DokumenElemenId = elementId,
                DevRefTipe = "dokumen",
                DevRefId = 10,
                DevPage = page,
                DevBboxX0 = 40,
                DevBboxY0 = y0,
                DevBboxX1 = 520,
                DevBboxY1 = y1,
                DevLabel = label,
                DevLabelStruktural = label,
                DevText = text
            });
        }

        private static string CreateParagraphJson(uint paragraphFormatId, uint textFormatId, string text)
        {
            var escapedText = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $$"""{"dfp_id":{{paragraphFormatId}},"content":[{"type":"text","dftx_id":{{textFormatId}},"value":"{{escapedText}}"}]}""";
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class ExtractionArtifactCleanupServiceTests
{
    [Fact]
    public async Task ResetAsync_ShouldDeleteArtifactsOnlyForRequestedDokumen()
    {
        using var db = ControllerTestHelpers.CreateDbContext();

        db.DokumenSections.AddRange(
            new DokumenSection { DsecId = 1, DsecRefTipe = "dokumen", DsecRefId = 42, DsecIndex = 0 },
            new DokumenSection { DsecId = 2, DsecRefTipe = "dokumen", DsecRefId = 43, DsecIndex = 0 });

        db.DokumenParts.AddRange(
            new DokumenPart { DpartId = 10, DsecId = 1, DpartType = "body" },
            new DokumenPart { DpartId = 20, DsecId = 2, DpartType = "body" });

        db.DokumenElemens.AddRange(
            new DokumenElemen
            {
                DelemenId = 100,
                DpartId = 10,
                DelemenSequence = 1,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateElementJson(11, 21, 22, 41, 71)
            },
            new DokumenElemen
            {
                DelemenId = 200,
                DpartId = 20,
                DelemenSequence = 1,
                DelemenType = "paragraph",
                DelemenJsonTree = CreateElementJson(12, 24, 25, 42, 72)
            });

        db.DokumenNotes.AddRange(
            new DokumenNote
            {
                DnoteId = 300,
                DnoteRefTipe = "dokumen",
                DnoteRefId = 42,
                DnoteNumber = 1,
                DnoteKind = "footnote",
                DnoteJsonTree = """{"content":[{"type":"paragraph","content":[{"type":"text","dftx_id":23,"value":"catatan"}]}]}"""
            },
            new DokumenNote
            {
                DnoteId = 301,
                DnoteRefTipe = "dokumen",
                DnoteRefId = 43,
                DnoteNumber = 1,
                DnoteKind = "footnote",
                DnoteJsonTree = """{"content":[{"type":"paragraph","content":[{"type":"text","dftx_id":26,"value":"lain"}]}]}"""
            });

        db.DokumenElemenVisuals.AddRange(
            new DokumenElemenVisual { DevId = 400, DevRefTipe = "dokumen", DevRefId = 42, DokumenElemenId = 100 },
            new DokumenElemenVisual { DevId = 401, DevRefTipe = "dokumen", DevRefId = 43, DokumenElemenId = 200 });

        db.DokumenFormatParagrafs.AddRange(
            new DokumenFormatParagraf { DfpId = 11 },
            new DokumenFormatParagraf { DfpId = 12 });

        db.DokumenFormatTexts.AddRange(
            new DokumenFormatText { DftxId = 21 },
            new DokumenFormatText { DftxId = 22 },
            new DokumenFormatText { DftxId = 23 },
            new DokumenFormatText { DftxId = 24 },
            new DokumenFormatText { DftxId = 25 },
            new DokumenFormatText { DftxId = 26 });

        db.DokumenFormatTables.AddRange(
            new DokumenFormatTable { DftId = 41 },
            new DokumenFormatTable { DftId = 42 });

        db.DokumenFormatDrawings.AddRange(
            new DokumenFormatDrawing { DfdrId = 71 },
            new DokumenFormatDrawing { DfdrId = 72 });

        await db.SaveChangesAsync();

        var service = new ExtractionArtifactCleanupService(
            db,
            NullLogger<ExtractionArtifactCleanupService>.Instance);

        await service.ResetAsync("dokumen", 42);

        Assert.DoesNotContain(db.DokumenSections, item => item.DsecRefId == 42);
        Assert.DoesNotContain(db.DokumenParts, item => item.DsecId == 1);
        Assert.DoesNotContain(db.DokumenElemens, item => item.DelemenId == 100);
        Assert.DoesNotContain(db.DokumenNotes, item => item.DnoteRefTipe == "dokumen" && item.DnoteRefId == 42);
        Assert.DoesNotContain(db.DokumenElemenVisuals, item => item.DevRefId == 42);

        Assert.DoesNotContain(db.DokumenFormatParagrafs, item => item.DfpId == 11);
        Assert.DoesNotContain(db.DokumenFormatTexts, item => item.DftxId == 21 || item.DftxId == 22 || item.DftxId == 23);
        Assert.DoesNotContain(db.DokumenFormatTables, item => item.DftId == 41);
        Assert.DoesNotContain(db.DokumenFormatDrawings, item => item.DfdrId == 71);

        Assert.Contains(db.DokumenSections, item => item.DsecRefId == 43);
        Assert.Contains(db.DokumenParts, item => item.DsecId == 2);
        Assert.Contains(db.DokumenElemens, item => item.DelemenId == 200);
        Assert.Contains(db.DokumenNotes, item => item.DnoteRefTipe == "dokumen" && item.DnoteRefId == 43);
        Assert.Contains(db.DokumenElemenVisuals, item => item.DevRefId == 43);
        Assert.Contains(db.DokumenFormatParagrafs, item => item.DfpId == 12);
        Assert.Contains(db.DokumenFormatTexts, item => item.DftxId == 24 || item.DftxId == 25 || item.DftxId == 26);
        Assert.Contains(db.DokumenFormatTables, item => item.DftId == 42);
        Assert.Contains(db.DokumenFormatDrawings, item => item.DfdrId == 72);
    }

    [Fact]
    public async Task ResetAsync_ShouldDeleteNotesOnlyForRequestedBabReference()
    {
        using var db = ControllerTestHelpers.CreateDbContext();

        db.DokumenNotes.AddRange(
            new DokumenNote
            {
                DnoteId = 1,
                DnoteRefTipe = "bab",
                DnoteRefId = 10,
                DnoteKind = "footnote",
                DnoteNumber = 1,
                DnoteJsonTree = """{"content":[{"type":"paragraph","dfp_id":10,"content":[{"type":"text","dftx_id":20,"value":"Bab 10"}]}]}"""
            },
            new DokumenNote
            {
                DnoteId = 2,
                DnoteRefTipe = "bab",
                DnoteRefId = 11,
                DnoteKind = "footnote",
                DnoteNumber = 1,
                DnoteJsonTree = """{"content":[{"type":"paragraph","dfp_id":11,"content":[{"type":"text","dftx_id":21,"value":"Bab 11"}]}]}"""
            },
            new DokumenNote
            {
                DnoteId = 3,
                DnoteRefTipe = "aturan",
                DnoteRefId = 10,
                DnoteKind = "footnote",
                DnoteNumber = 1,
                DnoteJsonTree = """{"content":[{"type":"paragraph","dfp_id":12,"content":[{"type":"text","dftx_id":22,"value":"Aturan 10"}]}]}"""
            });

        await db.SaveChangesAsync();

        var service = new ExtractionArtifactCleanupService(
            db,
            NullLogger<ExtractionArtifactCleanupService>.Instance);

        await service.ResetAsync("bab", 10);

        Assert.DoesNotContain(db.DokumenNotes, item => item.DnoteRefTipe == "bab" && item.DnoteRefId == 10);
        Assert.Contains(db.DokumenNotes, item => item.DnoteRefTipe == "bab" && item.DnoteRefId == 11);
        Assert.Contains(db.DokumenNotes, item => item.DnoteRefTipe == "aturan" && item.DnoteRefId == 10);
    }

    private static string CreateElementJson(
        uint dfpId,
        uint dftxId,
        uint resultDftxId,
        uint dftId,
        ulong dfdrId)
    {
        var root = new JObject
        {
            ["dfp_id"] = dfpId,
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["dftx_id"] = dftxId,
                    ["value"] = "isi"
                },
                new JObject
                {
                    ["type"] = "field",
                    ["field_type"] = "PAGE",
                    ["result_dftx_id"] = resultDftxId,
                    ["value"] = "1"
                },
                new JObject
                {
                    ["type"] = "drawing",
                    ["dfdr_id"] = dfdrId
                },
                new JObject
                {
                    ["type"] = "table",
                    ["content"] = new JObject
                    {
                        ["dft_id"] = dftId,
                        ["rows"] = new JArray
                        {
                            new JObject
                            {
                                ["cells"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["content"] = new JArray()
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        return root.ToString();
    }
}

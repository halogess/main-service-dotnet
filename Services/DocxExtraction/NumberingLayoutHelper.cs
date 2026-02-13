using System.Drawing;
using System.Drawing.Text;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

internal static class NumberingLayoutHelper
{
    private const int DefaultDpi = 96;
    private const int TwipsPerInch = 1440;

    public static uint? TryComputeEffectiveHangingTwips(
        Level? level,
        string? numberingLabelWithSuffix,
        int defaultTabStopTwips)
    {
        if (level == null || string.IsNullOrWhiteSpace(numberingLabelWithSuffix))
            return null;

        var suffix = level.LevelSuffix?.Val?.Value ?? LevelSuffixValues.Tab;
        if (suffix != LevelSuffixValues.Tab)
            return null;

        var label = numberingLabelWithSuffix.TrimEnd('\t');
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var tabStop = GetNumberTabStop(level) ?? defaultTabStopTwips;
        if (tabStop <= 0)
            return null;

        var (fontName, fontPt, bold) = GetNumberingFont(level);
        var widthTwips = MeasureLabelWidthTwips(label, fontName, fontPt, bold);
        if (!widthTwips.HasValue)
            return null;

        var effective = widthTwips.Value > tabStop
            ? NextTabStop(widthTwips.Value, defaultTabStopTwips)
            : tabStop;

        return (uint)effective;
    }

    private static int? GetNumberTabStop(Level level)
    {
        var pPr = level.PreviousParagraphProperties;
        var tabs = pPr?.Tabs;
        if (tabs == null)
            return null;

        foreach (var tab in tabs.Elements<TabStop>())
        {
            if (tab.Val?.Value == TabStopValues.Number && tab.Position?.Value != null)
                return tab.Position.Value;
        }

        foreach (var tab in tabs.Elements<TabStop>())
        {
            if (tab.Position?.Value != null)
                return tab.Position.Value;
        }

        return null;
    }

    private static (string FontName, float FontPt, bool Bold) GetNumberingFont(Level level)
    {
        var rPr = level.NumberingSymbolRunProperties;
        var fontName = "Times New Roman";
        var fontPt = 12f;
        var bold = false;

        if (rPr == null)
            return (fontName, fontPt, bold);

        var rFonts = rPr.RunFonts;
        if (rFonts != null)
        {
            fontName = rFonts.Ascii?.Value
                       ?? rFonts.HighAnsi?.Value
                       ?? rFonts.ComplexScript?.Value
                       ?? rFonts.EastAsia?.Value
                       ?? fontName;
        }

        var sz = rPr.FontSize;
        if (sz?.Val?.Value != null && int.TryParse(sz.Val.Value, out var halfPt))
            fontPt = halfPt / 2f;

        bold = rPr.Bold != null || rPr.BoldComplexScript != null;

        return (fontName, fontPt, bold);
    }

    private static int? MeasureLabelWidthTwips(string text, string fontName, float fontPt, bool bold)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var pxSize = Math.Max(1, (int)Math.Round(fontPt * DefaultDpi / 72f));
            using var bmp = new System.Drawing.Bitmap(1, 1);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = new System.Drawing.Font(fontName, pxSize, bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            using var format = new System.Drawing.StringFormat(System.Drawing.StringFormat.GenericTypographic);
            var size = g.MeasureString(text, font, int.MaxValue, format);
            var widthPx = size.Width;
            var widthTwips = (int)Math.Round(widthPx * TwipsPerInch / (float)DefaultDpi);
            return widthTwips;
        }
        catch
        {
            return null;
        }
    }

    private static int NextTabStop(int widthTwips, int defaultTabStopTwips)
    {
        if (defaultTabStopTwips <= 0)
            return widthTwips;

        var slots = (int)Math.Ceiling(widthTwips / (double)defaultTabStopTwips);
        return slots * defaultTabStopTwips;
    }
}

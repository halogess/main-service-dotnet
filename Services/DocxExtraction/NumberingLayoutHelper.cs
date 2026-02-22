using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

internal static class NumberingLayoutHelper
{
    private const int DefaultDpi = 96;
    private const int TwipsPerInch = 1440;

    public static uint? TryComputeEffectiveHangingTwips(
        Level? level,
        string? numberingLabelWithSuffix,
        int defaultTabStopTwips,
        bool useHangingIndentTabStop = true)
    {
        if (level == null || string.IsNullOrWhiteSpace(numberingLabelWithSuffix))
            return null;

        var suffix = level.LevelSuffix?.Val?.Value ?? LevelSuffixValues.Tab;
        if (suffix != LevelSuffixValues.Tab)
            return null;

        var label = numberingLabelWithSuffix.TrimEnd('\t');
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var (fontName, fontPt, bold) = GetNumberingFont(level);
        var widthTwips = MeasureLabelWidthTwips(label, fontName, fontPt, bold);
        if (!widthTwips.HasValue)
            return null;

        var hangingTabStop = useHangingIndentTabStop
            ? GetHangingIndentTabStop(level)
            : null;
        var numberTabStop = GetNumberTabStop(level);

        var effective = ResolveEffectiveTabStop(
            widthTwips.Value,
            hangingTabStop,
            numberTabStop,
            defaultTabStopTwips);

        if (!effective.HasValue || effective.Value <= 0)
            return null;

        return (uint)effective.Value;
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

    private static int? GetHangingIndentTabStop(Level level)
    {
        var pPr = level.PreviousParagraphProperties;
        var ind = pPr?.GetFirstChild<Indentation>();
        if (ind?.Hanging?.Value == null)
            return null;

        return int.TryParse(ind.Hanging.Value, out var hanging) && hanging > 0
            ? hanging
            : null;
    }

    private static int? ResolveEffectiveTabStop(
        int labelWidthTwips,
        int? hangingTabStopTwips,
        int? numberTabStopTwips,
        int defaultTabStopTwips)
    {
        var initialStop = hangingTabStopTwips.HasValue && hangingTabStopTwips.Value > 0
            ? hangingTabStopTwips.Value
            : numberTabStopTwips.HasValue && numberTabStopTwips.Value > 0
                ? numberTabStopTwips.Value
                : defaultTabStopTwips;

        if (initialStop <= 0)
            return null;

        if (labelWidthTwips <= initialStop)
            return initialStop;

        return FindNextStop(
            initialStop,
            labelWidthTwips,
            numberTabStopTwips,
            defaultTabStopTwips);
    }

    private static int? FindNextStop(
        int currentStopTwips,
        int labelWidthTwips,
        int? numberTabStopTwips,
        int defaultTabStopTwips)
    {
        var stop = currentStopTwips;

        while (labelWidthTwips > stop)
        {
            int? nextStop = null;

            if (numberTabStopTwips.HasValue && numberTabStopTwips.Value > stop)
            {
                nextStop = numberTabStopTwips.Value;
            }
            else if (defaultTabStopTwips > 0)
            {
                var autoStop = NextTabStop(stop + 1, defaultTabStopTwips);
                if (autoStop > stop)
                    nextStop = autoStop;
            }

            if (!nextStop.HasValue || nextStop.Value <= stop)
                return null;

            stop = nextStop.Value;
        }

        return stop;
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
            var pxSize = Math.Max(1f, fontPt * DefaultDpi / 72f);
            var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;

            using var typeface = SKTypeface.FromFamilyName(fontName, style) ?? SKTypeface.Default;
            using var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pxSize,
                IsAntialias = true,
                LcdRenderText = true,
                SubpixelText = true,
                IsStroke = false
            };

            var widthPx = paint.MeasureText(text);
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

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles extraction of numbering/list labels from OpenXML numbering definitions
/// </summary>
public static class NumberingExtractor
{
    /// <summary>
    /// Get the formatted numbering text for a list item.
    /// Counters are keyed by numId to keep each numbering instance independent.
    /// </summary>
    public static string GetNumberingText(
        NumberingDefinitionsPart numberingPart, 
        int numId, 
        int ilvl, 
        Dictionary<int, Dictionary<int, int>> counters)
    {
        var numbering = numberingPart.Numbering;
        if (numbering == null) return "?";

        var numInstance = numbering.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId);
        if (numInstance == null) return "?";

        int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
        var abstractNum = numbering.Elements<AbstractNum>()
            .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
        if (abstractNum == null) return "?";

        var level = GetEffectiveLevel(numInstance, abstractNum, ilvl, out var startOverride);
        if (level == null) return "?";

        if (!counters.TryGetValue(numId, out var numCounters))
        {
            numCounters = new Dictionary<int, int>();
            counters[numId] = numCounters;
        }

        if (!numCounters.TryGetValue(ilvl, out var currentVal))
        {
            currentVal = startOverride ?? GetLevelStart(level);
            numCounters[ilvl] = currentVal;
        }
        else
        {
            currentVal++;
            numCounters[ilvl] = currentVal;
        }

        ResetLowerLevels(numCounters, numInstance, abstractNum, ilvl);

        var numFmt = level.NumberingFormat?.Val?.Value ?? NumberFormatValues.Decimal;
        var lvlText = level.LevelText?.Val?.Value ?? $"%{ilvl + 1}";

        if (numFmt == NumberFormatValues.None)
            return string.Empty;

        if (numFmt == NumberFormatValues.Bullet || level.LevelPictureBulletId != null)
        {
            var normalizedBullet = NormalizeBulletChar(lvlText);
            var bulletText = normalizedBullet.Length > 0 ? normalizedBullet : "â€¢";
            return bulletText + GetLevelSuffix(level);
        }

        var isLegal = level.IsLegalNumberingStyle != null &&
                      (level.IsLegalNumberingStyle.Val?.Value ?? true);

        var formatted = ReplaceLevelText(
            lvlText,
            placeholderIndex =>
            {
                int levelIndex = placeholderIndex - 1;
                if (levelIndex < 0)
                    return null;

                var subLevel = GetEffectiveLevel(numInstance, abstractNum, levelIndex, out var subStartOverride);
                var levelValue = numCounters.TryGetValue(levelIndex, out var existing)
                    ? existing
                    : (subStartOverride ?? GetLevelStart(subLevel));

                var subFmt = isLegal
                    ? NumberFormatValues.Decimal
                    : (subLevel?.NumberingFormat?.Val?.Value ?? NumberFormatValues.Decimal);

                return FormatNumber(levelValue, subFmt, subLevel?.LevelText?.Val?.Value);
            });

        return formatted + GetLevelSuffix(level);
    }

    /// <summary>
    /// Format a number according to the numbering format
    /// </summary>
    public static string FormatNumber(int value, NumberFormatValues format, string? levelText = null)
    {
        if (format == NumberFormatValues.DecimalZero)
            return value.ToString("D2");
        if (format == NumberFormatValues.LowerLetter)
            return GetLetter(value, true);
        if (format == NumberFormatValues.UpperLetter)
            return GetLetter(value, false);
        if (format == NumberFormatValues.LowerRoman)
            return ToRoman(value).ToLowerInvariant();
        if (format == NumberFormatValues.UpperRoman)
            return ToRoman(value);
        if (format == NumberFormatValues.Bullet)
            return NormalizeBulletChar(levelText ?? string.Empty);
        if (format == NumberFormatValues.None)
            return string.Empty;

        return value.ToString();
    }

    private static Level? GetEffectiveLevel(
        NumberingInstance numInstance,
        AbstractNum abstractNum,
        int ilvl,
        out int? startOverride)
    {
        startOverride = null;

        var levelOverride = numInstance.Elements<LevelOverride>()
            .FirstOrDefault(lo => lo.LevelIndex?.Value == ilvl);
        if (levelOverride != null)
        {
            startOverride = levelOverride.StartOverrideNumberingValue?.Val?.Value;
            if (levelOverride.Level != null)
                return levelOverride.Level;
        }

        return abstractNum.Elements<Level>()
            .FirstOrDefault(l => l.LevelIndex?.Value == ilvl);
    }

    private static int GetLevelStart(Level? level)
    {
        return level?.StartNumberingValue?.Val?.Value ?? 1;
    }

    private static void ResetLowerLevels(
        Dictionary<int, int> numCounters,
        NumberingInstance numInstance,
        AbstractNum abstractNum,
        int currentLevel)
    {
        var keys = numCounters.Keys.ToList();
        foreach (var levelIndex in keys)
        {
            if (levelIndex <= currentLevel)
                continue;

            var restart = GetLevelRestart(numInstance, abstractNum, levelIndex);
            if (restart.HasValue && restart.Value > currentLevel)
                continue;

            numCounters.Remove(levelIndex);
        }
    }

    private static int? GetLevelRestart(
        NumberingInstance numInstance,
        AbstractNum abstractNum,
        int levelIndex)
    {
        var level = GetEffectiveLevel(numInstance, abstractNum, levelIndex, out var unusedStartOverride);
        return level?.LevelRestart?.Val?.Value;
    }

    private static string ReplaceLevelText(string levelText, Func<int, string?> resolvePlaceholder)
    {
        if (string.IsNullOrEmpty(levelText))
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < levelText.Length; i++)
        {
            var ch = levelText[i];
            if (ch != '%' || i + 1 >= levelText.Length)
            {
                sb.Append(ch);
                continue;
            }

            var next = levelText[i + 1];
            if (next == '%')
            {
                sb.Append('%');
                i++;
                continue;
            }

            if (!char.IsDigit(next))
            {
                sb.Append(ch);
                continue;
            }

            int j = i + 1;
            int number = 0;
            while (j < levelText.Length && char.IsDigit(levelText[j]))
            {
                number = number * 10 + (levelText[j] - '0');
                j++;
            }

            var replacement = resolvePlaceholder(number);
            if (replacement != null)
                sb.Append(replacement);
            else
                sb.Append(levelText, i, j - i);

            i = j - 1;
        }

        return sb.ToString();
    }

    private static string GetLevelSuffix(Level level)
    {
        var suffix = level.LevelSuffix?.Val?.Value ?? LevelSuffixValues.Tab;
        if (suffix == LevelSuffixValues.Nothing)
            return "";
        if (suffix == LevelSuffixValues.Space)
            return " ";
        return "\t";
    }
    
    /// <summary>
    /// Normalize bullet characters from Symbol/Wingdings fonts to proper Unicode
    /// </summary>
    public static string NormalizeBulletChar(string bulletChar)
    {
        if (string.IsNullOrEmpty(bulletChar)) return "•";
        
        // Symbol font character mappings (Private Use Area -> Unicode)
        var symbolMappings = new Dictionary<char, char>
        {
            { '\uF0B7', '•' },  // Bullet
            { '\uF0A7', '•' },  // Bullet variant
            { '\uF076', '•' },  // Another bullet
            { '\uF0D8', '◆' },  // Diamond
            { '\uF0FC', '✓' },  // Check mark
            { '\uF0A8', '■' },  // Square bullet
            { '\uF0E0', '→' },  // Arrow
            { '\uF0E8', '⮕' },  // Arrow variant
        };
        
        // Wingdings font character mappings
        var wingdingsMappings = new Dictionary<char, char>
        {
            { '\u006C', '●' },  // Circle (Wingdings 'l')
            { '\u006E', '■' },  // Square (Wingdings 'n')
            { '\u0075', '◆' },  // Diamond (Wingdings 'u')
            { '\u00A8', '➢' },  // Arrow (Wingdings)
            { '\u00FC', '✓' },  // Check mark (Wingdings)
            { '\u0076', '✔' },  // Check (Wingdings 'v')
            { '\u00D8', '➔' },  // Arrow (Wingdings)
            { '\u0077', '✗' },  // Cross (Wingdings 'w')
        };
        
        if (bulletChar.Length == 1)
        {
            char c = bulletChar[0];
            
            if (symbolMappings.TryGetValue(c, out char symbol))
                return symbol.ToString();
                
            if (wingdingsMappings.TryGetValue(c, out char wingding))
                return wingding.ToString();
                
            // Common bullet characters that are fine as-is
            if (c == '•' || c == '●' || c == '○' || c == '■' || c == '□' || 
                c == '◆' || c == '◇' || c == '▪' || c == '▫' || c == '►' ||
                c == '➢' || c == '➤' || c == '✓' || c == '✔' || c == '✗' ||
                c == '-' || c == '–' || c == '—' || c == '*')
                return bulletChar;
                
            // If in Private Use Area but not mapped, use default bullet
            if (c >= '\uE000' && c <= '\uF8FF')
                return "•";
                
            // Problematic characters
            if (c == '?' || c < 32)
                return "•";
        }
        
        return bulletChar;
    }
    
    /// <summary>
    /// Convert number to letter (a, b, c, ... aa, ab, ...)
    /// </summary>
    public static string GetLetter(int val, bool lower)
    {
        if (val <= 0) return "?";
        val--; 
        string s = "";
        do {
            s = (char)('A' + (val % 26)) + s;
            val /= 26;
            val--;
        } while (val >= 0);
        return lower ? s.ToLowerInvariant() : s;
    }
    
    /// <summary>
    /// Convert number to Roman numerals
    /// </summary>
    public static string ToRoman(int number)
    {
        if (number < 1) return string.Empty;
        if (number >= 1000) return "M" + ToRoman(number - 1000);
        if (number >= 900) return "CM" + ToRoman(number - 900);
        if (number >= 500) return "D" + ToRoman(number - 500);
        if (number >= 400) return "CD" + ToRoman(number - 400);
        if (number >= 100) return "C" + ToRoman(number - 100);
        if (number >= 90) return "XC" + ToRoman(number - 90);
        if (number >= 50) return "L" + ToRoman(number - 50);
        if (number >= 40) return "XL" + ToRoman(number - 40);
        if (number >= 10) return "X" + ToRoman(number - 10);
        if (number >= 9) return "IX" + ToRoman(number - 9);
        if (number >= 5) return "V" + ToRoman(number - 5);
        if (number >= 4) return "IV" + ToRoman(number - 4);
        return "I" + ToRoman(number - 1);
    }
}

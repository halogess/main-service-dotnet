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
    /// Counters are keyed by abstractNumId because multiple numIds can share the same abstractNumId.
    /// </summary>
    public static string GetNumberingText(
        NumberingDefinitionsPart numberingPart, 
        int numId, 
        int ilvl, 
        Dictionary<int, Dictionary<int, int>> counters)
    {
        var numInstance = numberingPart.Numbering.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId);
        if (numInstance == null) return "?";
        
        int abstractNumId = numInstance.AbstractNumId?.Val?.Value ?? -1;
        
        var abstractNum = numberingPart.Numbering.Elements<AbstractNum>()
            .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
        if (abstractNum == null) return "?";
        
        var level = abstractNum.Elements<Level>()
            .FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == ilvl);
        if (level == null) return "?";
        
        // Check for lvlOverride with startOverride in num instance
        int? startOverride = null;
        var lvlOverride = numInstance.Elements<LevelOverride>()
            .FirstOrDefault(lo => lo.LevelIndex?.Value == ilvl);
        if (lvlOverride != null)
        {
            startOverride = lvlOverride.StartOverrideNumberingValue?.Val?.Value;
        }
        
        // Use abstractNumId as key so different numIds sharing same abstractNumId share counter
        if (!counters.ContainsKey(abstractNumId))
            counters[abstractNumId] = new Dictionary<int, int>();
        
        // Track applied startOverrides using negative numId as key
        int appliedKey = -numId - 1;
        bool startOverrideApplied = counters.ContainsKey(appliedKey);
        
        if (startOverride.HasValue && !startOverrideApplied)
        {
            counters[appliedKey] = new Dictionary<int, int>();
            counters[abstractNumId][ilvl] = startOverride.Value;
        }
        else if (!counters[abstractNumId].ContainsKey(ilvl))
        {
            int start = level.StartNumberingValue?.Val ?? 1;
            counters[abstractNumId][ilvl] = start;
        }
        else
        {
            counters[abstractNumId][ilvl]++;
        }
        
        // Reset lower levels when we move to a higher level
        foreach (var key in counters[abstractNumId].Keys.ToList())
        {
            if (key > ilvl) counters[abstractNumId].Remove(key);
        }
        
        int currentVal = counters[abstractNumId][ilvl];
        
        string lvlText = level.LevelText?.Val?.Value ?? "";
        string numFmt = level.NumberingFormat?.Val?.ToString() ?? "decimal";
        
        if (numFmt == "bullet")
        {
            string normalizedBullet = NormalizeBulletChar(lvlText);
            return normalizedBullet.Length > 0 ? normalizedBullet : "•";
        }
        
        string formatted = lvlText;
        for (int i = 0; i <= ilvl; i++)
        {
            int cVal = counters[abstractNumId].ContainsKey(i) 
                ? counters[abstractNumId][i] 
                : (int)(abstractNum.Elements<Level>()
                    .FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == i)?
                    .StartNumberingValue?.Val ?? 1);
             
            var subLevel = abstractNum.Elements<Level>()
                .FirstOrDefault(l => l.LevelIndex != null && l.LevelIndex.Value == i);
            string subFmt = subLevel?.NumberingFormat?.Val?.ToString() ?? "decimal";
             
            string subValStr = FormatNumber(cVal, subFmt, subLevel?.LevelText?.Val?.Value);
            formatted = formatted.Replace($"%{i + 1}", subValStr);
        }
        
        return formatted;
    }
    
    /// <summary>
    /// Format a number according to the numbering format
    /// </summary>
    public static string FormatNumber(int value, string format, string? levelText = null)
    {
        return format switch
        {
            "decimalZero" => value.ToString("D2"),
            "lowerLetter" => GetLetter(value, true),
            "upperLetter" => GetLetter(value, false),
            "lowerRoman" => ToRoman(value).ToLowerInvariant(),
            "upperRoman" => ToRoman(value),
            "bullet" => NormalizeBulletChar(levelText ?? ""),
            _ => value.ToString()
        };
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

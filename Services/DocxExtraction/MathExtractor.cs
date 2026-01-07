using DocumentFormat.OpenXml;
using System.Text;

namespace ValidasiTugasAkhir.MainService.Services.DocxExtraction;

/// <summary>
/// Handles extraction of mathematical content from OpenXML Math elements
/// </summary>
public static class MathExtractor
{
    /// <summary>
    /// Comprehensive extraction of math content including all special elements
    /// </summary>
    public static string ExtractMathContent(OpenXmlElement mathElement)
    {
        var sb = new StringBuilder();
        ExtractMathElementRecursive(mathElement, sb);
        return sb.ToString();
    }
    
    /// <summary>
    /// Fallback: extract math text using InnerText or Text descendants
    /// </summary>
    public static string ExtractMathText(OpenXmlElement mathElement)
    {
        var innerText = mathElement.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(innerText))
            return innerText;
        
        var texts = mathElement.Descendants<DocumentFormat.OpenXml.Math.Text>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        
        var result = string.Join("", texts);
        return string.IsNullOrWhiteSpace(result) ? "[formula]" : result;
    }
    
    private static void ExtractMathElementRecursive(OpenXmlElement element, StringBuilder sb)
    {
        foreach (var child in element.ChildElements)
        {
            // Math Text - the basic text content
            if (child is DocumentFormat.OpenXml.Math.Text t)
            {
                sb.Append(t.Text);
            }
            // Delimiter - handles (), [], {}, ||, etc.
            else if (child is DocumentFormat.OpenXml.Math.Delimiter d)
            {
                var dPr = d.DelimiterProperties;
                string beginChar = dPr?.BeginChar?.Val?.Value ?? "(";
                string endChar = dPr?.EndChar?.Val?.Value ?? ")";
                
                sb.Append(beginChar);
                foreach (var dElem in d.Elements<DocumentFormat.OpenXml.Math.Base>())
                {
                    ExtractMathElementRecursive(dElem, sb);
                }
                sb.Append(endChar);
            }
            // Nary - summation, product, integral (∑, ∏, ∫)
            else if (child is DocumentFormat.OpenXml.Math.Nary nary)
            {
                var naryPr = nary.NaryProperties;
                string chr = naryPr?.AccentChar?.Val?.Value ?? "∑";
                sb.Append(chr);
                
                var sub = nary.SubArgument;
                if (sub != null)
                {
                    sb.Append("_");
                    ExtractMathElementRecursive(sub, sb);
                }
                
                var sup = nary.SuperArgument;
                if (sup != null)
                {
                    sb.Append("^");
                    ExtractMathElementRecursive(sup, sb);
                }
                
                var baseElem = nary.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null)
                {
                    ExtractMathElementRecursive(baseElem, sb);
                }
            }
            // Radical (square root, nth root)
            else if (child is DocumentFormat.OpenXml.Math.Radical rad)
            {
                var radPr = rad.RadicalProperties;
                var hideDegreeVal = radPr?.HideDegree?.Val?.Value;
                bool hideDegree = hideDegreeVal == null || 
                    hideDegreeVal == DocumentFormat.OpenXml.Math.BooleanValues.True || 
                    hideDegreeVal == DocumentFormat.OpenXml.Math.BooleanValues.On;
                
                sb.Append("√");
                
                if (!hideDegree)
                {
                    var degree = rad.Degree;
                    if (degree != null)
                    {
                        sb.Append("[");
                        ExtractMathElementRecursive(degree, sb);
                        sb.Append("]");
                    }
                }
                
                sb.Append("(");
                var baseElem = rad.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null)
                {
                    ExtractMathElementRecursive(baseElem, sb);
                }
                sb.Append(")");
            }
            // Fraction
            else if (child is DocumentFormat.OpenXml.Math.Fraction frac)
            {
                sb.Append("(");
                var num = frac.Numerator;
                if (num != null) ExtractMathElementRecursive(num, sb);
                sb.Append("/");
                var den = frac.Denominator;
                if (den != null) ExtractMathElementRecursive(den, sb);
                sb.Append(")");
            }
            // Superscript
            else if (child is DocumentFormat.OpenXml.Math.Superscript sSup)
            {
                var baseElem = sSup.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("^");
                var supArg = sSup.SuperArgument;
                if (supArg != null) ExtractMathElementRecursive(supArg, sb);
            }
            // Subscript
            else if (child is DocumentFormat.OpenXml.Math.Subscript sSub)
            {
                var baseElem = sSub.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("_");
                var subArg = sSub.SubArgument;
                if (subArg != null) ExtractMathElementRecursive(subArg, sb);
            }
            // SubSuperscript (both sub and super)
            else if (child is DocumentFormat.OpenXml.Math.SubSuperscript sSubSup)
            {
                var baseElem = sSubSup.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("_");
                var subArg = sSubSup.SubArgument;
                if (subArg != null) ExtractMathElementRecursive(subArg, sb);
                sb.Append("^");
                var supArg = sSubSup.SuperArgument;
                if (supArg != null) ExtractMathElementRecursive(supArg, sb);
            }
            // Accent (overline, hat, arrow, etc.)
            else if (child is DocumentFormat.OpenXml.Math.Accent acc)
            {
                var accPr = acc.AccentProperties;
                string accChar = accPr?.AccentChar?.Val?.Value ?? "";
                
                var baseElem = acc.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                if (!string.IsNullOrEmpty(accChar)) sb.Append(accChar);
            }
            // Bar (overbar, underbar)
            else if (child is DocumentFormat.OpenXml.Math.Bar bar)
            {
                var baseElem = bar.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("̄"); // combining overline
            }
            // Function (sin, cos, lim, etc.)
            else if (child is DocumentFormat.OpenXml.Math.MathFunction func)
            {
                var funcName = func.FunctionName;
                if (funcName != null) ExtractMathElementRecursive(funcName, sb);
                
                var baseElem = func.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
            }
            // Limit Lower (e.g., lim with subscript)
            else if (child is DocumentFormat.OpenXml.Math.LimitLower limLow)
            {
                var baseElem = limLow.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("_");
                var limit = limLow.Limit;
                if (limit != null) ExtractMathElementRecursive(limit, sb);
            }
            // Limit Upper
            else if (child is DocumentFormat.OpenXml.Math.LimitUpper limUp)
            {
                var baseElem = limUp.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                sb.Append("^");
                var limit = limUp.Limit;
                if (limit != null) ExtractMathElementRecursive(limit, sb);
            }
            // Matrix
            else if (child is DocumentFormat.OpenXml.Math.Matrix matrix)
            {
                sb.Append("[");
                bool firstRow = true;
                foreach (var row in matrix.Elements<DocumentFormat.OpenXml.Math.MatrixRow>())
                {
                    if (!firstRow) sb.Append("; ");
                    firstRow = false;
                    
                    bool firstCol = true;
                    foreach (var col in row.Elements<DocumentFormat.OpenXml.Math.Base>())
                    {
                        if (!firstCol) sb.Append(", ");
                        firstCol = false;
                        ExtractMathElementRecursive(col, sb);
                    }
                }
                sb.Append("]");
            }
            // GroupChar (underbrace, overbrace, etc.)
            else if (child is DocumentFormat.OpenXml.Math.GroupChar grpChr)
            {
                var grpPr = grpChr.GroupCharProperties;
                string chr = grpPr?.AccentChar?.Val?.Value ?? "";
                
                var baseElem = grpChr.Elements<DocumentFormat.OpenXml.Math.Base>().FirstOrDefault();
                if (baseElem != null) ExtractMathElementRecursive(baseElem, sb);
                if (!string.IsNullOrEmpty(chr)) sb.Append(chr);
            }
            // Container elements - just recurse
            else if (child is DocumentFormat.OpenXml.Math.Box 
                  || child is DocumentFormat.OpenXml.Math.BorderBox
                  || child is DocumentFormat.OpenXml.Math.EquationArray
                  || child is DocumentFormat.OpenXml.Math.Phantom
                  || child is DocumentFormat.OpenXml.Math.Run
                  || child is DocumentFormat.OpenXml.Math.OfficeMath
                  || child is DocumentFormat.OpenXml.Math.Base
                  || child is DocumentFormat.OpenXml.Math.FunctionName
                  || child is DocumentFormat.OpenXml.Math.Numerator
                  || child is DocumentFormat.OpenXml.Math.Denominator
                  || child is DocumentFormat.OpenXml.Math.Degree
                  || child is DocumentFormat.OpenXml.Math.SubArgument
                  || child is DocumentFormat.OpenXml.Math.SuperArgument
                  || child is DocumentFormat.OpenXml.Math.Limit)
            {
                ExtractMathElementRecursive(child, sb);
            }
            // Skip properties and other non-content elements
            else if (child.LocalName.EndsWith("Pr") || child is DocumentFormat.OpenXml.Math.ControlProperties)
            {
                // Skip properties
            }
            else
            {
                // For unknown elements, try to recurse if they have children
                if (child.HasChildren)
                {
                    ExtractMathElementRecursive(child, sb);
                }
            }
        }
    }
}

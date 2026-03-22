// Copyright Warren Harding 2026
using System.Text.RegularExpressions;

namespace Sym.CSharpIO;

/// <summary>
/// Helper for converting LaTeX-like math input into C#-script expressions consumable by CSharpIO.
/// </summary>
public static class MathSyntax
{
    private static readonly Regex Frac = new(@"\\frac\{([^{}]+)\}\{([^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex SqrtN = new(@"\\sqrt\[([^{}]+)\]\{([^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex Sqrt = new(@"\\sqrt\{([^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex PowBlock = new(@"\^\{([^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex PowChar = new(@"\^([A-Za-z0-9]+)", RegexOptions.Compiled);
    private static readonly Regex FuncWord = new(@"\\(sin|cos|tan|log|exp|arcsin|arccos|arctan|csc|sec|cot|ceil|floor|gcd|lcm|greatestcommondivisor|leastcommonmultiple)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FuncImplicitToken = new(@"\b(sin|cos|tan|log|exp|arcsin|arccos|arctan|csc|sec|cot|ceil|floor|gcd|lcm|greatestcommondivisor|leastcommonmultiple)\s*([A-Za-z0-9.]+(\^\([^()]+\))?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Greek = new(@"\\(pi|theta|alpha|beta|gamma|delta|phi|sigma|omega)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AbsBar = new(@"\|([^|]+)\|", RegexOptions.Compiled);
    private static readonly Regex Percent = new(@"([0-9.]+|\([^()]+\))\%", RegexOptions.Compiled);

    /// <summary>
    /// Converts a subset of LaTeX math into a C#-style expression string.
    /// </summary>
    public static string FromLatex(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return string.Empty;
        var text = latex;
        text = text.Replace(@"\left", string.Empty).Replace(@"\right", string.Empty);
        text = text.Replace(@"\cdot", "*").Replace(@"\times", "*");
        text = Percent.Replace(text, "($1/100)");
        text = AbsBar.Replace(text, "Abs($1)");
        text = SqrtN.Replace(text, "($2)^(1/($1))");
        text = Frac.Replace(text, "($1)/($2)");
        text = Sqrt.Replace(text, "($1)^(1/2)");
        text = PowBlock.Replace(text, "^($1)");
        text = PowChar.Replace(text, "^($1)");
        text = FuncWord.Replace(text, m => m.Groups[1].Value);
        text = Greek.Replace(text, m => m.Groups[1].Value.ToLowerInvariant());
        text = text.Replace("{", "(").Replace("}", ")");
        text = FuncImplicitToken.Replace(text, "$1($2)");
        
        // Handle implicit multiplication: 2pi -> 2 * pi, (a)(b) -> (a) * (b), 2(x) -> 2 * (x)
        text = Regex.Replace(text, @"(\d+)([A-Za-z])", "$1 * $2");
        text = Regex.Replace(text, @"(\d+)\(", "$1 * (");
        text = Regex.Replace(text, @"\)([A-Za-z])", ") * $1");
        text = Regex.Replace(text, @"\)\(", ") * (");

        text = text.Replace(" ", string.Empty);
        return text;
    }
}

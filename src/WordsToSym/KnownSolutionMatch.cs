using System;
using System.Text.RegularExpressions;
using Sym.CSharpIO;

namespace WordsToSym;

public sealed record KnownSolutionMatchOptions(
    bool PreferBoxedAnswer = true,
    bool UseStrictFallback = true,
    bool ValidateCSharpIo = true,
    bool NormalizePowCalls = true,
    bool NormalizeCaretPowers = true);

public static class KnownSolutionMatch
{
    private static string NormalizeMatch(string? match, KnownSolutionMatchOptions options)
    {
        var text = (match ?? string.Empty).Trim();
        if (text.Length == 0) return text;

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        if (options.NormalizePowCalls)
        {
            text = MathPowRegex.Replace(text, "Pow($1, $2)");
            text = PowCallRegex.Replace(text, "Pow($1, $2)");
        }

        if (options.NormalizeCaretPowers)
        {
            text = CaretPowRegex.Replace(text, "Pow($1, $2)");
        }

        return text.Trim();
    }

    private static bool IsCSharpIoExpression(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return false;

        try
        {
            return CSharpIO.ParseExpressions(trimmed).Any();
        }
        catch
        {
            return false;
        }
    }

    private static string? TryExtractBoxedAnswer(string knownSolution)
    {
        if (string.IsNullOrWhiteSpace(knownSolution)) return null;

        var markers = new[] { "\\boxed{", "\\fbox{" };
        int markerIndex = -1;
        string? marker = null;

        foreach (var m in markers)
        {
            var idx = knownSolution.LastIndexOf(m, StringComparison.Ordinal);
            if (idx >= 0 && idx > markerIndex)
            {
                markerIndex = idx;
                marker = m;
            }
        }

        if (markerIndex < 0 || marker is null) return null;

        int braceStart = markerIndex + marker.Length - 1;
        int depth = 0;
        int contentStart = -1;

        for (int i = braceStart; i < knownSolution.Length; i++)
        {
            var ch = knownSolution[i];
            if (ch == '{')
            {
                depth++;
                if (depth == 1)
                {
                    contentStart = i + 1;
                }
                continue;
            }

            if (ch == '}')
            {
                if (depth == 0) break;
                depth--;
                if (depth == 0 && contentStart >= 0)
                {
                    var content = knownSolution.Substring(contentStart, i - contentStart).Trim();
                    return content.Length == 0 ? null : content;
                }
            }
        }

        return null;
    }

    private static readonly Regex PowCallRegex =
        new(@"\b([A-Za-z_][A-Za-z0-9_]*)\.Pow\s*\(\s*([^\)]+)\s*\)", RegexOptions.Compiled);

    private static readonly Regex MathPowRegex =
        new(@"\bMath\.Pow\s*\(\s*([^,]+)\s*,\s*([^\)]+)\s*\)", RegexOptions.Compiled);

    private static readonly Regex CaretPowRegex =
        new(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*\^\s*([0-9]+)\b", RegexOptions.Compiled);
}

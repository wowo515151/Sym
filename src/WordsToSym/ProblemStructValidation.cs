using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sym.CSharpIO;
using Sym.Core;
using SymSolvers.ProblemStructure;

namespace WordsToSym;

public sealed record ProblemStructValidationOptions(
    bool AllowEmptyConstraints = false,
    bool RequireMeaningfulConstraint = true,
    ProblemScriptCleanupOptions? CleanupOptions = null);

public sealed record ProblemStructValidationResult(bool IsValid, string? Message);

public static class ProblemStructValidator
{
    public static ProblemStructValidationResult Validate(ProblemStruct problem, ProblemStructValidationOptions? options = null)
    {
        if (problem is null)
        {
            return new ProblemStructValidationResult(false, "ProblemStruct was null.");
        }

        options ??= new ProblemStructValidationOptions();
        var cleanupOptions = options.CleanupOptions ?? new ProblemScriptCleanupOptions();

        var constraintLines = ProblemScriptCleaner.NormalizeConstraintLines(problem.Constraints, cleanupOptions).ToList();
        if (constraintLines.Count == 0)
        {
            return options.AllowEmptyConstraints
                ? new ProblemStructValidationResult(true, null)
                : new ProblemStructValidationResult(false, "No constraints were parsed.");
        }

        int parseable = 0;
        int meaningful = 0;

        foreach (var line in constraintLines)
        {
            if (TryParseConstraint(line, out _))
            {
                parseable++;
            }

            if (IsMeaningfulConstraint(line))
            {
                meaningful++;
            }
        }

        if (parseable == 0)
        {
            return new ProblemStructValidationResult(false, "No constraints could be parsed into expressions.");
        }

        if (options.RequireMeaningfulConstraint && meaningful == 0)
        {
            return new ProblemStructValidationResult(false, "Only standalone targets were found.");
        }

        return new ProblemStructValidationResult(true, null);
    }

    private static bool TryParseConstraint(string line, out IExpression expr)
    {
        expr = null!;
        var text = (line ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (text.Length == 0) return false;

        if (TryParse(text, out expr))
        {
            return true;
        }

        var rewritten = text.Replace("==", "=", StringComparison.Ordinal);
        return !string.Equals(rewritten, text, StringComparison.Ordinal) && TryParse(rewritten, out expr);
    }

    private static bool TryParse(string text, out IExpression expr)
    {
        expr = null!;
        try
        {
            var parsed = CSharpIO.ParseExpressionsStrict(text).FirstOrDefault();
            if (parsed is null)
            {
                return false;
            }
            expr = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMeaningfulConstraint(string line)
    {
        var trimmed = (line ?? string.Empty).Trim().TrimEnd(';');
        if (trimmed.Length == 0) return false;
        if (StandaloneIdentifierRegex.IsMatch(trimmed)) return false;

        if (trimmed.Contains("==", StringComparison.Ordinal) ||
            trimmed.Contains("!=", StringComparison.Ordinal) ||
            trimmed.Contains("<=", StringComparison.Ordinal) ||
            trimmed.Contains(">=", StringComparison.Ordinal) ||
            trimmed.Contains("<", StringComparison.Ordinal) ||
            trimmed.Contains(">", StringComparison.Ordinal) ||
            trimmed.Contains("=", StringComparison.Ordinal))
        {
            return true;
        }

        // Any other non-empty expression (e.g., function definition) counts as meaningful.
        return true;
    }

    private static readonly Regex StandaloneIdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
}

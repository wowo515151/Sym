// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SymSolvers.ProblemStructure;

namespace WordsToSym;

public static class RelatedConstraintInference
{
    public static IReadOnlyList<string> InferConstraints(ProblemStruct problem, IReadOnlyList<string> constraintLines)
    {
        if (problem is null) return Array.Empty<string>();
        constraintLines ??= Array.Empty<string>();

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in constraintLines)
        {
            var key = NormalizeConstraintKey(line);
            if (key.Length > 0) existing.Add(key);
        }

        var variables = CollectVariables(problem, constraintLines);
        if (variables.Count == 0) return Array.Empty<string>();

        var nonScalar = GetNonScalarVariables(constraintLines);
        var scalarVariables = variables.Where(v => !nonScalar.Contains(v)).ToList();

        var wordTokens = ExtractWordTokens(problem.WordProblem);
        var tags = problem.Tags ?? new List<string>();

        var inferred = new List<string>();

        if (LooksLikeCircleContext(tags, wordTokens, variables))
        {
            AddCircleConstraints(inferred, existing, scalarVariables, wordTokens);
        }

        if (LooksLikeRectangleContext(tags, wordTokens, variables))
        {
            AddRectangleConstraints(inferred, existing, scalarVariables, wordTokens, isSquare: false);
        }

        if (LooksLikeSquareContext(tags, wordTokens, variables))
        {
            AddRectangleConstraints(inferred, existing, scalarVariables, wordTokens, isSquare: true);
        }

        return inferred;
    }

    private static HashSet<string> GetNonScalarVariables(IEnumerable<string> constraintLines)
    {
        var nonScalar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in constraintLines)
        {
            if (line == null) continue;
            if (line.Contains("Vector", StringComparison.OrdinalIgnoreCase) || line.Contains("Matrix", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"\b([A-Za-z_][A-Za-z0-9_]*)\s*==");
                if (match.Success) nonScalar.Add(match.Groups[1].Value);
            }
        }
        return nonScalar;
    }

    private static void AddCircleConstraints(
        List<string> inferred,
        HashSet<string> existing,
        IReadOnlyList<string> variables,
        HashSet<string> wordTokens)
    {
        var radius = PickVariable(variables, new[] { "radius", "rad" }, allowSingleLetter: true, preferredSingle: "r", wordTokens);
        var diameter = PickVariable(variables, new[] { "diameter", "diam" }, allowSingleLetter: true, preferredSingle: "d", wordTokens);
        var circumference = PickVariable(variables, new[] { "circumference", "circum", "perimeter" }, allowSingleLetter: true, preferredSingle: "c", wordTokens);
        var area = PickVariable(variables, new[] { "area" }, allowSingleLetter: false, preferredSingle: string.Empty, wordTokens);

        if (!string.IsNullOrWhiteSpace(radius) && !string.IsNullOrWhiteSpace(diameter))
        {
            AddConstraint(inferred, existing, $"{diameter} == 2 * {radius}");
        }

        if (!string.IsNullOrWhiteSpace(circumference) && !string.IsNullOrWhiteSpace(radius))
        {
            AddConstraint(inferred, existing, $"{circumference} == 2 * pi * {radius}");
        }

        if (!string.IsNullOrWhiteSpace(circumference) && !string.IsNullOrWhiteSpace(diameter))
        {
            AddConstraint(inferred, existing, $"{circumference} == pi * {diameter}");
        }

        if (!string.IsNullOrWhiteSpace(area) && !string.IsNullOrWhiteSpace(radius))
        {
            AddConstraint(inferred, existing, $"{area} == pi * Pow({radius}, 2)");
        }

        if (!string.IsNullOrWhiteSpace(area) && !string.IsNullOrWhiteSpace(diameter))
        {
            AddConstraint(inferred, existing, $"{area} == pi * Pow({diameter}, 2) / 4");
        }

        if (!string.IsNullOrWhiteSpace(area) && !string.IsNullOrWhiteSpace(circumference))
        {
            AddConstraint(inferred, existing, $"{area} == Pow({circumference}, 2) / (4 * pi)");
        }

        AddNonNegative(inferred, existing, radius);
        AddNonNegative(inferred, existing, diameter);
        AddNonNegative(inferred, existing, circumference);
        AddNonNegative(inferred, existing, area);
    }

    private static void AddRectangleConstraints(
        List<string> inferred,
        HashSet<string> existing,
        IReadOnlyList<string> variables,
        HashSet<string> wordTokens,
        bool isSquare)
    {
        var length = PickVariable(variables, new[] { "length", "len" }, allowSingleLetter: !isSquare, preferredSingle: "l", wordTokens);
        var width = PickVariable(variables, new[] { "width", "wid" }, allowSingleLetter: !isSquare, preferredSingle: "w", wordTokens);
        var area = PickVariable(variables, new[] { "area" }, allowSingleLetter: false, preferredSingle: string.Empty, wordTokens);
        var perimeter = PickVariable(variables, new[] { "perimeter", "perim" }, allowSingleLetter: !isSquare, preferredSingle: "p", wordTokens);
        var side = PickVariable(variables, new[] { "side" }, allowSingleLetter: isSquare, preferredSingle: "s", wordTokens);

        if (isSquare && !string.IsNullOrWhiteSpace(side))
        {
            if (!string.IsNullOrWhiteSpace(area))
            {
                AddConstraint(inferred, existing, $"{area} == Pow({side}, 2)");
            }

            if (!string.IsNullOrWhiteSpace(perimeter))
            {
                AddConstraint(inferred, existing, $"{perimeter} == 4 * {side}");
            }

            AddNonNegative(inferred, existing, side);
            AddNonNegative(inferred, existing, perimeter);
            AddNonNegative(inferred, existing, area);
            return;
        }

        if (!string.IsNullOrWhiteSpace(length) && !string.IsNullOrWhiteSpace(width))
        {
            if (!string.IsNullOrWhiteSpace(area))
            {
                AddConstraint(inferred, existing, $"{area} == {length} * {width}");
            }

            if (!string.IsNullOrWhiteSpace(perimeter))
            {
                AddConstraint(inferred, existing, $"{perimeter} == 2 * ({length} + {width})");
            }

            AddNonNegative(inferred, existing, length);
            AddNonNegative(inferred, existing, width);
            AddNonNegative(inferred, existing, perimeter);
            AddNonNegative(inferred, existing, area);
        }
    }

    private static void AddNonNegative(List<string> inferred, HashSet<string> existing, string? variable)
    {
        if (string.IsNullOrWhiteSpace(variable)) return;
        AddConstraint(inferred, existing, $"{variable} >= 0");
    }

    private static void AddConstraint(List<string> inferred, HashSet<string> existing, string constraint)
    {
        var key = NormalizeConstraintKey(constraint);
        if (key.Length == 0 || existing.Contains(key)) return;
        inferred.Add(constraint);
        existing.Add(key);
    }

    private static bool LooksLikeCircleContext(IReadOnlyList<string> tags, HashSet<string> wordTokens, IReadOnlyList<string> variables)
    {
        if (tags.Any(t => t.Contains("circle", StringComparison.OrdinalIgnoreCase))) return true;
        if (wordTokens.Contains("circle") || wordTokens.Contains("radius") || wordTokens.Contains("diameter") ||
            wordTokens.Contains("circumference") || wordTokens.Contains("perimeter")) return true;
        return variables.Any(v => v.Contains("radius", StringComparison.OrdinalIgnoreCase) ||
                                  v.Contains("diameter", StringComparison.OrdinalIgnoreCase) ||
                                  v.Contains("circumference", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeRectangleContext(IReadOnlyList<string> tags, HashSet<string> wordTokens, IReadOnlyList<string> variables)
    {
        if (tags.Any(t => t.Contains("rectangle", StringComparison.OrdinalIgnoreCase))) return true;
        if (wordTokens.Contains("rectangle") || wordTokens.Contains("length") || wordTokens.Contains("width")) return true;
        return variables.Any(v => v.Contains("length", StringComparison.OrdinalIgnoreCase) ||
                                  v.Contains("width", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeSquareContext(IReadOnlyList<string> tags, HashSet<string> wordTokens, IReadOnlyList<string> variables)
    {
        if (tags.Any(t => t.Contains("square", StringComparison.OrdinalIgnoreCase))) return true;
        if (wordTokens.Contains("square") || wordTokens.Contains("side")) return true;
        return variables.Any(v => v.Contains("side", StringComparison.OrdinalIgnoreCase));
    }

    private static string? PickVariable(
        IReadOnlyList<string> variables,
        IReadOnlyList<string> keywords,
        bool allowSingleLetter,
        string preferredSingle,
        HashSet<string> wordTokens)
    {
        var keywordSet = new HashSet<string>(keywords.Select(k => k.ToLowerInvariant()));

        var exact = variables
            .Where(v => keywordSet.Contains(v.ToLowerInvariant()))
            .OrderBy(v => v.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(exact)) return exact;

        var tokenMatches = variables
            .Where(v => SplitIdentifierTokens(v).Any(t => keywordSet.Contains(t)))
            .OrderBy(v => v.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tokenMatches)) return tokenMatches;

        if (allowSingleLetter && !string.IsNullOrWhiteSpace(preferredSingle))
        {
            var single = variables.FirstOrDefault(v =>
                v.Length == 1 &&
                v.Equals(preferredSingle, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(single)) return single;
        }

        if (allowSingleLetter && wordTokens.Overlaps(keywordSet))
        {
            var single = variables.FirstOrDefault(v => v.Length == 1);
            if (!string.IsNullOrWhiteSpace(single)) return single;
        }

        return null;
    }

    private static List<string> CollectVariables(ProblemStruct problem, IReadOnlyList<string> constraintLines)
    {
        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in problem.Variables ?? new List<ProblemStruct.Variable>())
        {
            var name = v.VariableName?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                variables.Add(name);
            }
        }

        foreach (var line in constraintLines ?? Array.Empty<string>())
        {
            foreach (Match match in Regex.Matches(line ?? string.Empty, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
            {
                var name = match.Value.Trim();
                if (name.Length == 0 || ReservedIdentifiers.Contains(name)) continue;
                variables.Add(name);
            }
        }

        return variables.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HashSet<string> ExtractWordTokens(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text ?? string.Empty, @"[A-Za-z]+"))
        {
            var token = match.Value.ToLowerInvariant();
            if (token.Length > 0) tokens.Add(token);
        }
        return tokens;
    }

    private static IEnumerable<string> SplitIdentifierTokens(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;
        foreach (var part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (Match match in Regex.Matches(part, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+"))
            {
                var token = match.Value.ToLowerInvariant();
                if (token.Length > 0) yield return token;
            }
        }
    }

    private static string NormalizeConstraintKey(string? constraint)
    {
        var text = (constraint ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (text.Length == 0) return string.Empty;
        return Regex.Replace(text, @"\s+", string.Empty).ToLowerInvariant();
    }

    private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "pi", "e",
        "vector", "matrix", "interval", "count", "filter", "length",
        "and", "or", "not", "implies", "iff",
        "sum", "product", "pow", "sqrt", "abs", "sin", "cos", "tan",
        "log", "ln", "exp", "mod", "gcd", "lcm",
        "lt", "le", "gt", "ge", "ne"
    };
}

using System;
using System.Collections.Generic;
using System.Linq;
using SymSolvers.ProblemStructure;

namespace WordsToSym;

public sealed record ProblemScriptBuildOptions(
    bool IncludeWordProblemComment = true,
    bool IncludeTagsComment = true,
    bool FlattenWordProblem = true,
    bool MergeParsedProblemScript = true,
    ProblemScriptCleanupOptions? CleanupOptions = null);

public sealed record ProblemScriptBuildResult(
    string ProblemScript,
    IReadOnlyList<string> ConstraintLines,
    string? TargetVariable,
    IReadOnlyList<string> TargetVariables);

public static class ProblemScriptBuilder
{
    public static ProblemScriptBuildResult Build(ProblemStruct problem, ProblemScriptBuildOptions? options = null)
    {
        if (problem is null)
        {
            return new ProblemScriptBuildResult(string.Empty, Array.Empty<string>(), null, Array.Empty<string>());
        }

        options ??= new ProblemScriptBuildOptions();
        var cleanupOptions = options.CleanupOptions ?? new ProblemScriptCleanupOptions();

        var constraints = new List<string>();
        if (problem.Constraints.Count > 0)
        {
            constraints.AddRange(problem.Constraints);
        }

        if (options.MergeParsedProblemScript && !string.IsNullOrWhiteSpace(problem.ProblemScript))
        {
            var cleanedScript = ProblemScriptCleaner.NormalizeProblemScript(problem.ProblemScript, cleanupOptions);
            var parsed = ProblemStruct.ProblemScriptToProblemStruct(cleanedScript);
            constraints = MergeConstraints(constraints, parsed.Constraints);
        }

        var constraintLines = ProblemScriptCleaner.NormalizeConstraintLines(constraints, cleanupOptions).ToList();
        var inferred = RelatedConstraintInference.InferConstraints(problem, constraintLines);
        if (inferred.Count > 0)
        {
            constraintLines.AddRange(inferred);
        }

        var targetResolution = TargetInference.ResolveTarget(problem, constraintLines);
        var target = targetResolution.TargetName;
        if (!string.IsNullOrWhiteSpace(targetResolution.TargetConstraint))
        {
            constraintLines.Add(targetResolution.TargetConstraint);
        }

        foreach (var t in targetResolution.TargetNames)
        {
            if (!string.IsNullOrWhiteSpace(t) && !TargetInference.HasStandaloneTarget(constraintLines, t))
            {
                constraintLines.Add(t);
            }
        }

        var lines = new List<string>();

        if (options.IncludeWordProblemComment && !string.IsNullOrWhiteSpace(problem.WordProblem))
        {
            var wordProblem = options.FlattenWordProblem ? FlattenWordProblem(problem.WordProblem) : problem.WordProblem.Trim();
            if (!string.IsNullOrWhiteSpace(wordProblem))
            {
                lines.Add($"// WordProblem: {wordProblem}");
            }
        }

        foreach (var line in constraintLines)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (trimmed.Length == 0) continue;
            lines.Add(trimmed.EndsWith(";", StringComparison.Ordinal) ? trimmed : trimmed + ";");
        }

        if (options.IncludeTagsComment && problem.Tags.Count > 0)
        {
            lines.Add($"// <Tags>{string.Join(", ", problem.Tags)}</Tags>");
        }

        var combinedTarget = targetResolution.TargetNames.Count > 1 
            ? string.Join("; ", targetResolution.TargetNames) 
            : target;

        return new ProblemScriptBuildResult(string.Join(Environment.NewLine, lines), constraintLines, combinedTarget, targetResolution.TargetNames);
    }

    private static List<string> MergeConstraints(IEnumerable<string> primary, IEnumerable<string> secondary)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddIfNew(string? constraint)
        {
            var cleaned = (constraint ?? string.Empty).Trim();
            if (cleaned.Length == 0) return;
            if (seen.Add(cleaned))
            {
                merged.Add(cleaned);
            }
        }

        foreach (var c in primary) AddIfNew(c);
        foreach (var c in secondary) AddIfNew(c);

        return merged;
    }

    private static string FlattenWordProblem(string wordProblem)
    {
        if (string.IsNullOrWhiteSpace(wordProblem))
        {
            return string.Empty;
        }

        var parts = wordProblem.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }
}

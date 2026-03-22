using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SymSolvers.ProblemStructure;

namespace WordsToSym;

public static class TargetInference
{
    public sealed record TargetResolution(string? TargetName, string? TargetConstraint, IReadOnlyList<string> TargetNames);

    public static TargetResolution ResolveTarget(ProblemStruct problem, IReadOnlyList<string> constraintLines)
    {
        ArgumentNullException.ThrowIfNull(constraintLines);
        if (problem is null)
        {
            return new TargetResolution(null, null, Array.Empty<string>());
        }

        // Filter out asy commands from constraint lines for target inference
        var asyCommands = new[] { "label(", "dot(", "draw(", "fill(", "filldraw(", "drawanglemark(", "drawrightanglemark(", "anglemark(", "rightanglemark(", "shipout(", "clip(" };
        var filteredConstraintLines = constraintLines
            .Where(line => !asyCommands.Any(cmd => line.Trim().StartsWith(cmd, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var normalizedWordProblem = NormalizeWordProblemText(problem.WordProblem);
        var variables = GetCandidateVariables(problem, filteredConstraintLines);
        var lastSentence = ExtractLastSentence(normalizedWordProblem);
        
        var trailingStandalone = new List<string>();
        for (int i = filteredConstraintLines.Count - 1; i >= 0; i--)
        {
            var line = filteredConstraintLines[i].Trim().TrimEnd(';');
            if (line.Length == 0) continue;
            if (IsStandaloneIdentifier(line))
            {
                trailingStandalone.Insert(0, line);
            }
            else
            {
                break;
            }
        }

        // ... [rest of method uses trailingStandalone or filteredConstraintLines]
        
        // Update: use filtered lines in the rest of the method
        var standaloneIdentifiers = filteredConstraintLines.Where(IsStandaloneIdentifier).Select(l => l.Trim().TrimEnd(';')).ToList();
        var assignedVariables = GetAssignedVariables(filteredConstraintLines);

        if (trailingStandalone.Count > 1 && HasOptimizationTag(problem))
        {
            var assignedTargets = trailingStandalone
                .Where(t => assignedVariables.Contains(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (assignedTargets.Count == 1)
            {
                if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
                {
                    Console.WriteLine($"DEBUG: ResolveTarget picked optimization target: {assignedTargets[0]}");
                }
                return new TargetResolution(assignedTargets[0], null, new[] { assignedTargets[0] });
            }
        }

        var explicitAssignedTarget = FindExplicitTargetAssignment(filteredConstraintLines);
        if (!string.IsNullOrWhiteSpace(explicitAssignedTarget))
        {
            if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
            {
                Console.WriteLine($"DEBUG: ResolveTarget picked explicit assigned target: {explicitAssignedTarget}");
            }
            return new TargetResolution(explicitAssignedTarget, null, new[] { explicitAssignedTarget });
        }

        // 1. Keyword Targets (Strongest Compound)
        if (TryBuildKeywordTarget(normalizedWordProblem, variables, filteredConstraintLines, out var keywordTargetResolved, out var keywordConstraint))
        {
            if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
            {
                Console.WriteLine($"DEBUG: ResolveTarget picked keyword target: {keywordTargetResolved} -> {keywordConstraint}");
            }
            return new TargetResolution(keywordTargetResolved, keywordConstraint, new[] { keywordTargetResolved });
        }

        if (TryBuildSumOfSolutionsTarget(normalizedWordProblem, variables, filteredConstraintLines, out var sosTarget, out var sosConstraint))
        {
            if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
            {
                Console.WriteLine($"DEBUG: ResolveTarget picked sum/product of solutions target: {sosTarget} -> {sosConstraint}");
            }
            return new TargetResolution(sosTarget, sosConstraint, new[] { sosTarget });
        }

        if (TryBuildCountOfSolutionsTarget(normalizedWordProblem, variables, filteredConstraintLines, out var countTarget, out var countConstraint))
        {
            if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
            {
                Console.WriteLine($"DEBUG: ResolveTarget picked count of solutions target: {countTarget} -> {countConstraint}");
            }
            return new TargetResolution(countTarget, countConstraint, new[] { countTarget });
        }

        if (TryBuildUnitsDigitTarget(normalizedWordProblem, variables, filteredConstraintLines, out var unitsTarget, out var unitsConstraint))
        {
            if (System.Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
                Console.WriteLine($"DEBUG: ResolveTarget picked units digit target: {unitsTarget} -> {unitsConstraint}");
            return new TargetResolution(unitsTarget, unitsConstraint, new[] { unitsTarget });
        }

        if (trailingStandalone.Count > 0)
        {
            if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
            {
                Console.WriteLine($"DEBUG: ResolveTarget found trailing standalone identifiers: {string.Join(", ", trailingStandalone)}");
            }
            
            // If multiple, try to pick the best one using word problem scoring
            string bestTrailing = trailingStandalone.Last();
            if (trailingStandalone.Count > 1)
            {
                var scoredTrailing = ScoreVariablesFromWordProblem(normalizedWordProblem, trailingStandalone, filteredConstraintLines);
                if (scoredTrailing != null)
                {
                    bestTrailing = scoredTrailing;
                }
                
                // If multiple, check if word problem implies a sum via "and" or "total"
                var questionPart = ExtractLastSentence(normalizedWordProblem);
                if (Regex.IsMatch(questionPart, @"\b(and|total|sum|all\s+together)\b", RegexOptions.IgnoreCase))
                {
                    var existing = new HashSet<string>(variables, StringComparer.Ordinal);
                    var sumTarget = BuildUniqueName("total_ans", existing);
                    var sumConstraint = $"{sumTarget} == {string.Join(" + ", trailingStandalone)}";
                    return new TargetResolution(sumTarget, sumConstraint, new[] { sumTarget });
                }
            }

            // Optimization wrapper
            if (HasOptimizationTag(problem))
            {
                var mode = InferRootSelectionMode(normalizedWordProblem);
                if (mode != null)
                {
                    var optTarget = BuildUniqueName("opt_target", new HashSet<string>(variables, StringComparer.Ordinal));
                    var optConstraint = $"{optTarget} == {(mode == "min" ? "minimize" : "maximize")}({bestTrailing})";
                    return new TargetResolution(optTarget, optConstraint, new[] { optTarget });
                }
            }

            // Integer inference for counts
            if (Regex.IsMatch(normalizedWordProblem, @"\b(how\s+many|number\s+of)\b", RegexOptions.IgnoreCase))
            {
                if (bestTrailing.Contains("count") || bestTrailing.Contains("num") || bestTrailing.Contains("item") || bestTrailing.Contains("people") || bestTrailing.Contains("person") || bestTrailing.Contains("way"))
                {
                    var intConstraint = $"integer({bestTrailing})";
                    return new TargetResolution(bestTrailing, intConstraint, new[] { bestTrailing });
                }
            }

            // If only one (or the selected best), check for composite interpretation
            if (trailingStandalone.Count >= 1)
            {
                var target = bestTrailing;
                if (!assignedVariables.Contains(target))
                {
                    if (TryInterpretCompositeName(target, variables, out var compositeConstraint))
                    {
                        return new TargetResolution(target, compositeConstraint, trailingStandalone);
                    }
                }
            }
            
            return new TargetResolution(bestTrailing, null, trailingStandalone);
        }

        // If the last line is a standalone expression, always create a new target.
        if (filteredConstraintLines.Count > 0)
        {
            var lastLine = filteredConstraintLines.Last().Trim().TrimEnd(';');
            if (lastLine.Length > 0 && !IsStandaloneIdentifier(lastLine) && !lastLine.Contains('=') && !lastLine.Contains('<') && !lastLine.Contains('>') && !lastLine.Contains("Integer(", StringComparison.OrdinalIgnoreCase) && !lastLine.Contains("real(", StringComparison.OrdinalIgnoreCase) && !lastLine.Contains("ne("))
            {
                if (IsExpression(lastLine))
                {
                    var uniqueName = BuildUniqueName("target_expr", new HashSet<string>(variables, StringComparer.Ordinal));
                    if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
                    {
                        Console.WriteLine($"DEBUG: ResolveTarget picked last line expression as target: {lastLine}");
                    }
                    return new TargetResolution(uniqueName, $"{uniqueName} == {lastLine}", new[] { uniqueName });
                }
            }
        }

        // If no standalone identifiers but there is a standalone expression at the end, it might be the target.
        var standaloneExpressions = filteredConstraintLines
            .Select(l => l.Trim().TrimEnd(';'))
            .Where(l => l.Length > 0 && !IsStandaloneIdentifier(l) && !l.Contains('=', StringComparison.Ordinal) && !l.Contains('<', StringComparison.Ordinal) && !l.Contains('>', StringComparison.Ordinal) && !l.Contains("Integer(", StringComparison.Ordinal) && !l.Contains("real(", StringComparison.Ordinal) && !l.Contains("ne(", StringComparison.Ordinal))
            .ToList();

        if (standaloneExpressions.Count > 0)
        {
            // Pick the last one as it's typically the goal in ProblemScript.
            var lastExpr = standaloneExpressions.Last();
            if (lastExpr.Length > 0 && !IgnoredIdentifiers.Contains(lastExpr))
            {
                // Verify it's not just a declaration like 'int x' or 'decimal y'
                if (!lastExpr.StartsWith("int ", StringComparison.OrdinalIgnoreCase) && 
                    !lastExpr.StartsWith("decimal ", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsExpression(lastExpr))
                    {
                        var uniqueName = BuildUniqueName("target_expr", new HashSet<string>(variables, StringComparer.Ordinal));
                        return new TargetResolution(uniqueName, $"{uniqueName} == {lastExpr}", new[] { uniqueName });
                    }
                    return new TargetResolution(lastExpr, null, new[] { lastExpr });
                }
            }
        }

        // 2. Explicit Targets ("Find X")
        var explicitTarget = TryExtractExplicitTarget(normalizedWordProblem, variables);
        if (!string.IsNullOrWhiteSpace(explicitTarget))
        {
            if (IsExpression(explicitTarget))
            {
                var uniqueName = BuildUniqueName("target_expr", new HashSet<string>(variables, StringComparer.Ordinal));
                return new TargetResolution(uniqueName, $"{uniqueName} == {explicitTarget}", new[] { uniqueName });
            }
            return new TargetResolution(explicitTarget, null, new[] { explicitTarget });
        }

        // 3. Implicit Sum Targets (Weakest Compound)
        if (TryBuildImplicitSumTarget(normalizedWordProblem, variables, filteredConstraintLines, out var implicitSumTarget, out var implicitSumConstraint))
        {
             return new TargetResolution(implicitSumTarget, implicitSumConstraint, new[] { implicitSumTarget });
        }

        var operationTarget = TryMatchOperationTarget(normalizedWordProblem, variables);
        if (!string.IsNullOrWhiteSpace(operationTarget))
        {
            return new TargetResolution(operationTarget, null, new[] { operationTarget });
        }

        var keywordTarget = TryMatchKeywordTarget(normalizedWordProblem, variables);
        if (!string.IsNullOrWhiteSpace(keywordTarget))
        {
            return new TargetResolution(keywordTarget, null, new[] { keywordTarget });
        }

        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "from", "to", "step", "in", "by", "each", "total" };

        var lastAssigned = GetLastAssignedIdentifier(filteredConstraintLines);

        var unassigned = variables
            .Where(v => !assignedVariables.Contains(v) && !keywords.Contains(v))
            .ToList();

        // 3. Prefer scored variables from word problem (highest confidence)
        var scoredAll = ScoreVariablesFromWordProblem(normalizedWordProblem, variables, filteredConstraintLines);
        if (!string.IsNullOrWhiteSpace(scoredAll))
        {
            if (IsExpression(scoredAll))
            {
                var uniqueName = BuildUniqueName("target_expr", new HashSet<string>(variables, StringComparer.Ordinal));
                return new TargetResolution(uniqueName, $"{uniqueName} == {scoredAll}", new[] { uniqueName });
            }
            return new TargetResolution(scoredAll, null, new[] { scoredAll });
        }

        if (standaloneIdentifiers.Count > 0)
        {
            var mentionedStandalone = standaloneIdentifiers
                .Where(id => WordProblemMentionsVariable(normalizedWordProblem, id))
                .ToList();

            var candidates = mentionedStandalone.Count > 0 ? mentionedStandalone : standaloneIdentifiers;
            var preferred = candidates.FirstOrDefault(id =>
                id.Contains("result", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("answer", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("ans", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("sum", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("product", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("ratio", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("target", StringComparison.OrdinalIgnoreCase))
                ?? candidates.Last();

            if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
            {
                Console.WriteLine($"DEBUG: ResolveTarget picked standalone identifier as target: {preferred}");
            }

            if (!assignedVariables.Contains(preferred))
            {
                if (TryInterpretCompositeName(preferred, variables, out var compositeConstraint))
                {
                    return new TargetResolution(preferred, compositeConstraint, new[] { preferred });
                }
            }
            return new TargetResolution(preferred, null, new[] { preferred });
        }

        if (unassigned.Count == 1)
        {
            var candidate = unassigned[0];
            if (!string.IsNullOrWhiteSpace(lastAssigned))
            {
                var candidateMentioned = WordProblemMentionsVariable(normalizedWordProblem, candidate);
                var lastMentioned = WordProblemMentionsVariable(normalizedWordProblem, lastAssigned);
                if (!candidateMentioned && lastMentioned)
                {
                    return new TargetResolution(lastAssigned, null, new[] { lastAssigned });
                }
            }

            return new TargetResolution(candidate, null, new[] { candidate });
        }

        var resultCandidate = variables.FirstOrDefault(v =>
            v.Equals("result", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("ans", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("answer", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(resultCandidate))
        {
            return new TargetResolution(resultCandidate, null, new[] { resultCandidate });
        }

        if (!string.IsNullOrWhiteSpace(lastAssigned))
        {
            return new TargetResolution(lastAssigned, null, new[] { lastAssigned });
        }

        // Final fallback: if there are ANY variables, and one is mentioned in the last sentence, pick it.
        var lastSentenceVariables = variables.Where(v => WordProblemMentionsVariable(lastSentence, v)).ToList();
        if (lastSentenceVariables.Count == 1)
        {
            return new TargetResolution(lastSentenceVariables[0], null, new[] { lastSentenceVariables[0] });
        }

        return new TargetResolution(null, null, Array.Empty<string>());
    }

    private static bool HasOptimizationTag(ProblemStruct problem)
    {
        if (problem is null) return false;
        foreach (var tag in problem.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            if (tag.Contains("optimization", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("minimum", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("min", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        // Also check word problem text
        var text = NormalizeWordProblemText(problem.WordProblem);
        if (Regex.IsMatch(text, @"\b(minimum|maximum|least|greatest|smallest|largest|maximize|minimize|min\s+|max\s+)\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool TryBuildSumOfSolutionsTarget(
        string wordProblem,
        IReadOnlyList<string> variables,
        IReadOnlyList<string> constraintLines,
        out string targetName,
        out string targetConstraint)
    {
        targetName = string.Empty;
        targetConstraint = string.Empty;

        if (string.IsNullOrWhiteSpace(wordProblem)) return false;

        var lastSentence = ExtractLastSentence(wordProblem);
        bool isSum = Regex.IsMatch(lastSentence, @"\bsum\s+of\s+(?:all\s+)?(?:the\s+)?(?:solutions|roots|values)\b", RegexOptions.IgnoreCase);
        bool isProduct = Regex.IsMatch(lastSentence, @"\bproduct\s+of\s+(?:all\s+)?(?:the\s+)?(?:solutions|roots|values)\b", RegexOptions.IgnoreCase);

        if (isSum || isProduct)
        {
            var existing = new HashSet<string>(variables, StringComparer.Ordinal);
            foreach (var line in constraintLines)
            {
                if (TrySplitEquality(line, out var lhs) && IsStandaloneIdentifier(lhs))
                {
                    existing.Add(lhs);
                }
            }

            // Look for a single variable that is likely the one being solved for.
            // If multiple, try to find one that appears in a high-degree polynomial context or is the only one not assigned.
            var candidateVars = variables.Where(v => !IgnoredIdentifiers.Contains(v) && !IsAssignedConstant(v, constraintLines)).ToList();
            
            string? bestVar = null;
            if (candidateVars.Count == 1)
            {
                bestVar = candidateVars[0];
            }
            else if (candidateVars.Count > 1)
            {
                // Heuristic: pick the one that appears with powers like x^2, x^3 or Pow(x, ...) or Pow(x-..., ...)
                bestVar = candidateVars.FirstOrDefault(v => constraintLines.Any(l => 
                    Regex.IsMatch(l, $@"\b{Regex.Escape(v)}\s*\^") || 
                    Regex.IsMatch(l, $@"\bPow\s*\([^,]*\b{Regex.Escape(v)}\b")
                ));
            }

            if (bestVar != null)
            {
                targetName = BuildUniqueName(isSum ? "sum_of_solutions" : "product_of_solutions", existing);
                targetConstraint = isSum ? $"{targetName} == Sum(roots({bestVar}))" : $"{targetName} == Product(roots({bestVar}))";
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildCountOfSolutionsTarget(
        string wordProblem,
        IReadOnlyList<string> variables,
        IReadOnlyList<string> constraintLines,
        out string targetName,
        out string targetConstraint)
    {
        targetName = string.Empty;
        targetConstraint = string.Empty;

        if (string.IsNullOrWhiteSpace(wordProblem)) return false;

        var lastSentence = ExtractLastSentence(wordProblem);
        bool asksForCount = Regex.IsMatch(lastSentence, @"\b(number\s+of\s+solutions|how\s+many\s+solutions|count\s+of\s+solutions|how\s+many\s+values|number\s+of\s+distinct|how\s+many\s+distinct|number\s+of\s+real\s+solutions|number\s+of\s+integers|how\s+many\s+integers|how\s+many\s+different|how\s+many\s+ways|number\s+of\s+ways)\b", RegexOptions.IgnoreCase);

        if (asksForCount)
        {
            var existing = new HashSet<string>(variables, StringComparer.Ordinal);
            foreach (var line in constraintLines)
            {
                if (TrySplitEquality(line, out var lhs) && IsStandaloneIdentifier(lhs))
                {
                    existing.Add(lhs);
                }
            }

            // Find the most likely variable being solved for.
            var candidateVars = variables.Where(v => !IgnoredIdentifiers.Contains(v) && !IsAssignedConstant(v, constraintLines)).ToList();
            
            string? bestVar = null;
            if (candidateVars.Count == 1)
            {
                bestVar = candidateVars[0];
            }
            else if (candidateVars.Count > 1)
            {
                // Heuristic: pick the one mentioned in the last sentence
                bestVar = candidateVars.FirstOrDefault(v => WordProblemMentionsVariable(lastSentence, v));
                // Fallback: pick the last unassigned one
                bestVar ??= candidateVars.Last();
            }

            if (bestVar != null)
            {
                targetName = BuildUniqueName("solution_count", existing);
                targetConstraint = $"{targetName} == Length({bestVar})";
                return true;
            }
        }

        return false;
    }

    private static bool IsAssignedConstant(string variable, IEnumerable<string> lines)
    {
        return lines.Any(l => Regex.IsMatch(l, $@"^\s*{Regex.Escape(variable)}\s*={{1,2}}\s*\d+(\.\d+)?\s*;?\s*$"));
    }

    private static bool TryBuildUnitsDigitTarget(
        string wordProblem,
        IReadOnlyList<string> variables,
        IReadOnlyList<string> constraintLines,
        out string targetName,
        out string targetConstraint)
    {
        targetName = string.Empty;
        targetConstraint = string.Empty;

        if (string.IsNullOrWhiteSpace(wordProblem)) return false;

        if (Regex.IsMatch(wordProblem, @"\bunits?\s+digit\b", RegexOptions.IgnoreCase))
        {
            var existing = new HashSet<string>(variables, StringComparer.Ordinal);
            foreach (var line in constraintLines)
            {
                if (TrySplitEquality(line, out var lhs) && IsStandaloneIdentifier(lhs))
                {
                    existing.Add(lhs);
                }
            }

            // Find the most likely candidate expression to take the units digit of.
            // If there's a trailing standalone identifier like 'e', use it.
            string? baseExpr = null;
            for (int i = constraintLines.Count - 1; i >= 0; i--)
            {
                var line = constraintLines[i].Trim().TrimEnd(';');
                if (line.Length == 0) continue;
                if (IsStandaloneIdentifier(line))
                {
                    baseExpr = line;
                    break;
                }
                if (!line.Contains('=') && IsExpression(line))
                {
                    baseExpr = line;
                    break;
                }
            }

            if (baseExpr != null)
            {
                targetName = BuildUniqueName("units_digit", existing);
                targetConstraint = $"{targetName} == mod({baseExpr}, 10)";
                return true;
            }
        }

        return false;
    }

    private static bool TryInterpretCompositeName(string identifier, IReadOnlyList<string> variables, out string? constraint)
    {
        constraint = null;
        if (string.IsNullOrWhiteSpace(identifier) || variables.Count == 0) return false;

        var lowerId = identifier.ToLowerInvariant();
        var ops = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plus"] = " + ",
            ["minus"] = " - ",
            ["times"] = " * ",
            ["over"] = " / ",
            ["sum"] = " + ",
            ["product"] = " * ",
            ["average"] = "avg", // Special handling
            ["ratio"] = " / "
        };

        foreach (var op in ops.Keys)
        {
            var delimiter = "_" + op + "_";
            if (lowerId.Contains(delimiter, StringComparison.Ordinal))
            {
                var parts = identifier.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var left = parts[0].Trim();
                    var right = parts[1].Trim();

                    var variableLookup = variables
                        .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                    if (variableLookup.TryGetValue(left, out var actualLeft) && variableLookup.TryGetValue(right, out var actualRight))
                    {
                        if (ops[op] == "avg")
                        {
                            constraint = $"{identifier} == ({actualLeft} + {actualRight}) / 2";
                        }
                        else
                        {
                            constraint = $"{identifier} == {actualLeft}{ops[op]}{actualRight}";
                        }
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsExpression(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains('+') || text.Contains('/') || text.Contains('*') || text.Contains('-') || text.Contains("Pow") || text.Contains("Abs"))
        {
            return true;
        }

        // Treat function invocations or grouped expressions as expressions.
        if (text.Contains('(') && text.Contains(')'))
        {
            return true;
        }

        return false;
    }

    public static Dictionary<string, object>? InferSolverOptions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = NormalizeWordProblemText(text);

        if (Regex.IsMatch(normalized, @"\bexpand\b", RegexOptions.IgnoreCase))
        {
            // If expanding, disable factorization to prevent undoing the expansion.
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "DisableStrategies", new List<string> { "FactorizationStrategy" } }
            };
        }

        return null;
    }

    public static bool HasStandaloneTarget(IEnumerable<string> lines, string target)
        => lines.Any(line => string.Equals(line.Trim(), target, StringComparison.Ordinal));

    public static string? InferRootSelectionMode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = NormalizeWordProblemText(text);

        if (Regex.IsMatch(normalized, @"\b(largest|greatest|maximum|max|highest|most)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(normalized, @"\b(how\s+(?:many|much))\b", RegexOptions.IgnoreCase))
        {
            return "max";
        }

        if (Regex.IsMatch(normalized, @"\b(smallest|least|minimum|min|fewest|lowest|first)\b", RegexOptions.IgnoreCase))
        {
            return "min";
        }

        return null;
    }

    private static bool IsStandaloneIdentifier(string text)
        => text.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

    private static HashSet<string> GetAssignedVariables(IEnumerable<string> constraintLines)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in constraintLines)
        {
            if (TrySplitEquality(line, out var lhs) && IsStandaloneIdentifier(lhs))
            {
                assigned.Add(lhs);
            }
        }
        return assigned;
    }

    private static string? GetLastAssignedIdentifier(IEnumerable<string> constraintLines)
    {
        string? last = null;
        foreach (var line in constraintLines)
        {
            if (TrySplitEquality(line, out var lhs) && IsStandaloneIdentifier(lhs))
            {
                last = lhs;
            }
        }
        return last;
    }

    private static bool TrySplitEquality(string line, out string lhs)
    {
        lhs = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Contains("==", StringComparison.Ordinal))
        {
            var parts = trimmed.Split("==", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                lhs = parts[0].Trim();
                return lhs.Length > 0;
            }
            return false;
        }

        if (trimmed.Contains(">=", StringComparison.Ordinal) ||
            trimmed.Contains("<=", StringComparison.Ordinal) ||
            trimmed.Contains("!=", StringComparison.Ordinal) ||
            trimmed.Contains("=>", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Contains("=", StringComparison.Ordinal))
        {
            var parts = trimmed.Split("=", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                lhs = parts[0].Trim();
                return lhs.Length > 0;
            }
        }

        return false;
    }

    private static string? FindExplicitTargetAssignment(IReadOnlyList<string> constraintLines)
    {
        for (int i = constraintLines.Count - 1; i >= 0; i--)
        {
            var line = constraintLines[i].Trim().TrimEnd(';');
            if (!TrySplitEquality(line, out var lhs)) continue;
            if (!IsStandaloneIdentifier(lhs)) continue;

            if (ExplicitTargetAliases.Contains(lhs))
            {
                return lhs;
            }
        }

        return null;
    }

    private static bool TryBuildKeywordTarget(
        string? wordProblem,
        IReadOnlyList<string> variables,
        IReadOnlyList<string> constraintLines,
        out string targetName,
        out string targetConstraint)
    {
        targetName = string.Empty;
        targetConstraint = string.Empty;

        if (string.IsNullOrWhiteSpace(wordProblem) || variables.Count == 0)
        {
            return false;
        }

        var variableLookup = variables
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (variableLookup.Count == 0)
        {
            return false;
        }

        var lastSentence = ExtractLastSentence(wordProblem);
        var existing = new HashSet<string>(variables, StringComparer.Ordinal);
        var assignedVariables = GetAssignedVariables(constraintLines);
        foreach (var line in constraintLines)
        {
            if (TrySplitEquality(line, out var lhs) && IsStandaloneIdentifier(lhs))
            {
                existing.Add(lhs);
            }
        }

        // Keyword based compound (Sum, Product, Mean)
        var keywords = new[] { "sum", "product", "mean", "average", "how many" };
        foreach (var kw in keywords)
        {
            // Ignore if negated
            if (Regex.IsMatch(lastSentence, $@"\bnot\s+(?:the\s+)?{kw}\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

                            if (Regex.IsMatch(lastSentence, $"\\b{kw}\\b", RegexOptions.IgnoreCase) || (kw == "how many" && wordProblem.StartsWith("How many", StringComparison.OrdinalIgnoreCase)))

                            {

                                if (kw == "how many")

                                {

                                    // Look for Divisors pattern

                                                            if (Regex.IsMatch(wordProblem, @"\bdivisors\b", RegexOptions.IgnoreCase))

                                                            {

                                                                var nVar = variables.FirstOrDefault(v => Regex.IsMatch(wordProblem, $@"\b{v}\b") && assignedVariables.Contains(v));

                                                                

                                                                // If nVar is null, try even ignored identifiers if they are assigned AND mentioned in word problem

                                                                if (nVar is null)

                                                                {

                                                                     nVar = assignedVariables.FirstOrDefault(v => Regex.IsMatch(wordProblem, $@"\b{v}\b"));

                                                                }

                                    

                                                                if (nVar is null)

                                                                {

                                                                     // Last ditch: check word problem for any assigned variable

                                                                     nVar = assignedVariables.FirstOrDefault(v => WordProblemMentionsVariable(wordProblem, v));

                                                                }

                                    

            
                        {
                            // Fallback: word problem might mention a literal (e.g., 24) rather than the variable name (e.g., n).
                            // Try to find an assignment like n == 24 where 24 appears in the word problem.
                            var mentionedNumbers = Regex.Matches(wordProblem, @"\b\d+\b")
                                .Select(m => m.Value)
                                .Distinct(StringComparer.Ordinal)
                                .ToHashSet(StringComparer.Ordinal);

                            foreach (var line in constraintLines)
                            {
                                var t = line.Trim().TrimEnd(';');
                                var m = Regex.Match(t, @"^(?<lhs>[a-zA-Z_][a-zA-Z0-9_]*)\s*={1,2}\s*(?<rhs>\d+)$");
                                if (!m.Success) continue;
                                var lhs = m.Groups["lhs"].Value;
                                var rhs = m.Groups["rhs"].Value;
                                if (mentionedNumbers.Contains(rhs) && assignedVariables.Contains(lhs))
                                {
                                    nVar = lhs;
                                    break;
                                }
                            }

                            // Final fallback: if there is exactly one assigned variable, use it.
                            if (nVar is null)
                            {
                                var assigned = variables.Where(v => assignedVariables.Contains(v)).ToList();
                                if (assigned.Count == 1)
                                {
                                    nVar = assigned[0];
                                }
                            }
                        }
                        if (nVar != null)
                        {
                            targetName = BuildUniqueName("divisor_count", existing);
                            targetConstraint = $"{targetName} == Length(Divisors({nVar}))";
                            return true;
                        }
                    }
                }

                // Check if any existing variable already contains this keyword as a semantic marker
                if (variables.Any(v => v.Contains(kw, StringComparison.OrdinalIgnoreCase) && !v.Equals(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Find all variables mentioned in the problem
                var mentioned = variables.Where(v => WordProblemMentionsVariable(wordProblem, v)).ToList();
                
                // Fallback: if none mentioned but exactly 2 or 3 exist, use them all
                if (mentioned.Count < 2 && variables.Count >= 2 && variables.Count <= 3)
                {
                    mentioned = variables.ToList();
                }
                else if (mentioned.Count < 2)
                {
                    var caseSensitiveMentions = variables
                        .Where(v => v.Length == 1 && char.IsLetter(v[0]) && Regex.IsMatch(wordProblem, $@"\\b{Regex.Escape(v)}\\b"))
                        .ToList();
                    if (caseSensitiveMentions.Count >= 2)
                    {
                        mentioned = caseSensitiveMentions;
                    }
                }

                if (mentioned.Count >= 2)
                {
                    if (kw == "sum")
                    {
                        targetName = BuildUniqueName("sum_result", existing);
                        targetConstraint = $"{targetName} == {string.Join(" + ", mentioned)}";
                        return true;
                    }
                    if (kw == "product")
                    {
                        targetName = BuildUniqueName("product_result", existing);
                        targetConstraint = $"{targetName} == {string.Join(" * ", mentioned)}";
                        return true;
                    }
                    if (kw == "mean" || kw == "average")
                    {
                        targetName = BuildUniqueName("mean_result", existing);
                        targetConstraint = $"{targetName} == ({string.Join(" + ", mentioned)}) / {mentioned.Count}";
                        return true;
                    }
                }
                else
                {
                    Console.WriteLine($"DEBUG: Keyword matched but not enough variables. Count: {mentioned.Count}");
                }
            }
        }

        return false;
    }

    private static bool TryBuildImplicitSumTarget(
        string? wordProblem,
        IReadOnlyList<string> variables,
        IReadOnlyList<string> constraintLines,
        out string targetName,
        out string targetConstraint)
    {
        targetName = string.Empty;
        targetConstraint = string.Empty;

        if (string.IsNullOrWhiteSpace(wordProblem) || variables.Count == 0)
        {
            return false;
        }

        var variableLookup = variables
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (variableLookup.Count == 0)
        {
            return false;
        }

        var lastSentence = ExtractLastSentence(wordProblem);
        var existing = new HashSet<string>(variables, StringComparer.Ordinal);
        foreach (var line in constraintLines)
        {
            if (TrySplitEquality(line, out var lhs) && IsStandaloneIdentifier(lhs))
            {
                existing.Add(lhs);
            }
        }

        // Explicit sum/product/mean of specifically mentioned variables (prefer last sentence)
        var matches = Regex.Matches(lastSentence, @"\b[a-zA-Z][a-zA-Z0-9_]*(?:\s*\+\s*[a-zA-Z][a-zA-Z0-9_]*)+\b(?!\s*=)");
        foreach (Match match in matches)
        {
            var parts = match.Value.Split('+', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (parts.Count >= 2)
            {
                var mapped = new List<string>();
                foreach (var part in parts)
                {
                    if (variableLookup.TryGetValue(part, out var actual)) mapped.Add(actual);
                    else { mapped.Clear(); break; }
                }

                if (mapped.Count >= 2)
                {
                    targetName = BuildUniqueName("sum_result", existing);
                    targetConstraint = $"{targetName} == {string.Join(" + ", mapped)}";
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryBuildSumTarget(
        string? wordProblem,
        IReadOnlyList<string> variables,
        IReadOnlyList<string> constraintLines,
        out string targetName,
        out string targetConstraint)
    {
        // This is now handled by TryBuildImplicitSumTarget
        targetName = string.Empty;
        targetConstraint = string.Empty;
        return false;
    }

    private static string BuildUniqueName(string baseName, HashSet<string> existing)
    {
        var name = baseName;
        var index = 1;
        while (existing.Contains(name))
        {
            name = $"{baseName}_{index++}";
        }
        return name;
    }

    private static string? ScoreVariablesFromWordProblem(string? wordProblem, IReadOnlyList<string> variables, IReadOnlyList<string> constraintLines)
    {
        if (string.IsNullOrWhiteSpace(wordProblem) || variables.Count == 0)
        {
            return null;
        }

        var lastSentence = ExtractLastSentence(wordProblem);
        var fullTokens = ExtractWordTokens(wordProblem);
        var lastTokens = ExtractWordTokens(lastSentence);

        var scored = new List<(string name, int score, int lastSentenceMatches)>();

        foreach (var variable in variables)
        {
            var tokens = SplitIdentifierTokens(variable).Where(IsScorableToken).ToList();
            if (tokens.Count == 0) continue;

            var score = 0;
            var lastSentenceMatches = 0;
            foreach (var token in tokens)
            {
                if (TokenMatches(token, lastTokens))
                {
                    score += 200; // Increased from 100
                    lastSentenceMatches++;
                }
                if (TokenMatches(token, fullTokens)) score += 1;
            }

            // Bonus if the variable name appears in a "find X", "what is X", "how many X" context
            // Also check for token-based matches in the same context
            var interrogativePattern = $@"\b(?:find|solve|value|is|many|much|calculate|determine|what|how|evaluate|identify|express|sum|total|area|distance|length|count|old|age|salary|price|cost|costing|spent|spend|earned|earn|wage|make|compute|invest|invested)\s+(?:the\s+)?(?:[A-Za-z0-9_]+\s+)*(?:{Regex.Escape(variable)}|{string.Join("|", tokens)})\b";
            if (Regex.IsMatch(lastSentence, interrogativePattern, RegexOptions.IgnoreCase))
            {
                score += 5000; // Increased from 2000
            }

            // High bonus if the exact variable name is in the last sentence
            if (Regex.IsMatch(lastSentence, $@"\b{Regex.Escape(variable)}\b", RegexOptions.IgnoreCase))
            {
                score += 1000; // Increased from 500
            }

            // BONUS: if the variable is UNASSIGNED in the constraints, it's more likely to be the target
            if (!constraintLines.Any(l => Regex.IsMatch(l, $@"^\s*{Regex.Escape(variable)}\s*==\s*")))
            {
                score += 300;
            }

            // PENALTY: if the variable is assigned a constant number in the constraints, it's less likely to be the target
            if (constraintLines.Any(l => Regex.IsMatch(l, $@"^\s*{Regex.Escape(variable)}\s*==\s*\d+(\.\d+)?\s*;?\s*$")))
            {
                score -= 200;
            }

            if (score > 0)
            {
                scored.Add((variable, score, lastSentenceMatches));
            }
        }

        if (scored.Count == 0)
        {
            return null;
        }

        var best = scored
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.lastSentenceMatches)
            .ThenBy(x => x.name.Length) // Prefer shorter names if tied
            .First();

        // Check for tie in top score
        if (scored.Count(x => x.score == best.score && x.lastSentenceMatches == best.lastSentenceMatches && x.name.Length == best.name.Length) > 1)
        {
            return null;
        }

        return best.name;
    }

    private static string ExtractLastSentence(string text)
    {
        var parts = text.Split(new[] { '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var trimmed = parts[i].Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return text.Trim();
    }

    private static HashSet<string> ExtractWordTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text ?? string.Empty, @"[A-Za-z]+"))
        {
            var token = match.Value.ToLowerInvariant();
            if (token.Length > 0)
            {
                tokens.Add(token);
            }
        }
        return tokens;
    }

    private static IEnumerable<string> SplitIdentifierTokens(string name)
    {
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            foreach (Match match in Regex.Matches(part, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+"))
            {
                var token = match.Value.ToLowerInvariant();
                if (token.Length > 0)
                {
                    yield return token;
                }
            }
        }
    }

    private static bool TokenMatches(string token, HashSet<string> tokens)
    {
        if (tokens.Contains(token))
        {
            return true;
        }

        // Handle common pluralization
        if (tokens.Contains(token + "s") || tokens.Contains(token + "es"))
        {
            return true;
        }

        // Handle 'y' to 'ies' pluralization
        if (token.EndsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            var plural = token[..^1] + "ies";
            if (tokens.Contains(plural))
            {
                return true;
            }
        }

        // Handle 'ies' to 'y' singularization
        if (token.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
        {
            var singular = token[..^3] + "y";
            if (tokens.Contains(singular))
            {
                return true;
            }
        }

        // Handle 'vertices' -> 'vertex'
        if (string.Equals(token, "vertex", StringComparison.OrdinalIgnoreCase) && tokens.Contains("vertices")) return true;
        if (string.Equals(token, "vertices", StringComparison.OrdinalIgnoreCase) && tokens.Contains("vertex")) return true;

        // Handle 'cents' -> 'cent'
        if (string.Equals(token, "cent", StringComparison.OrdinalIgnoreCase) && tokens.Contains("cents")) return true;
        if (string.Equals(token, "cents", StringComparison.OrdinalIgnoreCase) && tokens.Contains("cent")) return true;

        // Handle 'hourly' -> 'hour'
        if (string.Equals(token, "hourly", StringComparison.OrdinalIgnoreCase) && tokens.Contains("hour")) return true;
        if (string.Equals(token, "hour", StringComparison.OrdinalIgnoreCase) && tokens.Contains("hourly")) return true;

        // Handle 'daily' -> 'day'
        if (string.Equals(token, "daily", StringComparison.OrdinalIgnoreCase) && tokens.Contains("day")) return true;
        if (string.Equals(token, "day", StringComparison.OrdinalIgnoreCase) && tokens.Contains("daily")) return true;

        // Handle general singularization
        if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = token[..^1];
            if (trimmed.Length > 0 && tokens.Contains(trimmed))
            {
                return true;
            }
            if (token.EndsWith("es", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = token[..^2];
                if (trimmed.Length > 0 && tokens.Contains(trimmed))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> GetCandidateVariables(ProblemStruct problem, IReadOnlyList<string> constraintLines)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        
        foreach (var v in problem.Variables)
        {
            if (!string.IsNullOrWhiteSpace(v.VariableName))
                variables.Add(v.VariableName.Trim());
        }

        foreach (var line in constraintLines)
        {
            foreach (Match match in Regex.Matches(line ?? string.Empty, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
            {
                var name = match.Value.Trim();
                if (name.Length == 0) continue;
                if (IgnoredIdentifiers.Contains(name)) continue;
                if (Regex.IsMatch(name, @"^Eq\d+$", RegexOptions.IgnoreCase)) continue;
                variables.Add(name);
            }
        }

        return variables.Where(v => !Regex.IsMatch(v, @"^Eq\d+$", RegexOptions.IgnoreCase)).OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    private static string? TryExtractExplicitTarget(string? wordProblem, IReadOnlyList<string> variables)
    {
        if (string.IsNullOrWhiteSpace(wordProblem))
        {
            return null;
        }

        var lookup = variables
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        
        // Handle explicit "find [expression]" or "what is [expression]"
        var prefixPatterns = new[]
        {
            @"\bfind\s+(?:the\s+)?",
            @"\bsolve\s+for\s+(?:the\s+)?",
            @"\bvalue\s+of\s+(?:the\s+)?",
            @"\bwhat\s+is\s+(?:the\s+)?",
            @"\bhow\s+many\s+(?:the\s+)?",
            @"\bcalculate\s+(?:the\s+)?",
            @"\bdetermine\s+(?:the\s+)?",
            @"\bevaluate\s+(?:the\s+)?",
            @"\bidentify\s+(?:the\s+)?",
            @"\bexpress\s+(?:the\s+)?",
            @"\bold\s+is\s+(?:the\s+)?"
        };

        var lastSentence = ExtractLastSentence(wordProblem);

        foreach (var prefix in prefixPatterns)
        {
            // Stop at common sentence delimiters OR specific keywords OR colon
            var match = Regex.Match(lastSentence, prefix + @"(?<target>.+?)(?:\s+\b(?:when|if|for|where|is|are|such|expressed|can|as|years|old)\b|[.:?]|$)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                // Fallback to simpler match if keywords not found
                match = Regex.Match(lastSentence, prefix + @"(?<target>[A-Za-z][A-Za-z0-9_ ]*)(?:\s+|[.:?]|$)", RegexOptions.IgnoreCase);
            }
            if (!match.Success) continue;

            var captured = match.Groups["target"].Value.Trim().TrimEnd('.').TrimEnd(':');
            if (captured.Length == 0) continue;

            // Basic cleanup of common words in the captured target
            var cleaned = captured;
            var commonPrefixes = new[] { "value of ", "the ", "its ", "that ", "all ", "possible ", "values of " };
            bool cleanedSomething = true;
            while (cleanedSomething)
            {
                cleanedSomething = false;
                foreach (var cp in commonPrefixes)
                {
                    if (cleaned.StartsWith(cp, StringComparison.OrdinalIgnoreCase))
                    {
                        cleaned = cleaned[cp.Length..].Trim();
                        cleanedSomething = true;
                    }
                }
            }

            if (cleaned.Length == 0) continue;

            // Strict validation: Target name should be a valid identifier or simple composite.
            // Reject if it contains spaces but is not a known composite pattern
            if (cleaned.Contains(' ') && !IsExpression(cleaned))
            {
                 // Check if it's a known variable with spaces (rare, usually underscored)
                 // or if it matches "units digit" etc.
                 if (!lookup.ContainsKey(cleaned) && !cleaned.Contains(" digit", StringComparison.OrdinalIgnoreCase))
                 {
                     // Reject rambling targets like "50-segment millipedes" unless "millipedes" is a variable.
                     continue;
                 }
            }

            cleaned = ExpandImplicitMultiplication(cleaned, variables);

            // If it's a known variable, return it.
            if (lookup.TryGetValue(cleaned, out var actual))
            {
                return actual;
            }

            // If it's a simple multi-word variable that might have been underscored in ProblemScript
            var underscored = cleaned.Replace(" ", "_");
            if (lookup.TryGetValue(underscored, out var actualUnderscored))
            {
                return actualUnderscored;
            }

            // If it's an expression like "a + b" or "n / p" or "Abs(a - b)"
            if (cleaned.Contains('+') || cleaned.Contains('/') || cleaned.Contains('*') || cleaned.Contains('-') || cleaned.Contains("Pow") || cleaned.Contains("Abs"))
            {
                return cleaned;
            }
        }

        return null;
    }

    private static string? TryMatchOperationTarget(string? wordProblem, IReadOnlyList<string> variables)
    {
        if (string.IsNullOrWhiteSpace(wordProblem) || variables.Count == 0)
        {
            return null;
        }

        if (Regex.IsMatch(wordProblem, @"\bexpand\b", RegexOptions.IgnoreCase))
        {
            return FindVariableByToken(variables, "expand", "expanded", "expansion", "expandedexpression") ??
                   FindVariableByToken(variables, "expression");
        }

        if (Regex.IsMatch(wordProblem, @"\bfactor\b", RegexOptions.IgnoreCase))
        {
            return FindVariableByToken(variables, "factor", "factored", "factoredexpression") ??
                   FindVariableByToken(variables, "expression");
        }

        if (Regex.IsMatch(wordProblem, @"\bsimplify\b", RegexOptions.IgnoreCase))
        {
            return FindVariableByToken(variables, "simplify", "simplified", "simplifiedexpression") ??
                   FindVariableByToken(variables, "expression");
        }

        return null;
    }

    private static string? TryMatchKeywordTarget(string? wordProblem, IReadOnlyList<string> variables)
    {
        if (string.IsNullOrWhiteSpace(wordProblem) || variables.Count == 0)
        {
            return null;
        }

        if (Regex.IsMatch(wordProblem, @"\bproduct\b", RegexOptions.IgnoreCase))
        {
            var candidate = FindVariableByToken(variables, "product", "prod", "multiplied");
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        }

        if (Regex.IsMatch(wordProblem, @"\bmean\b", RegexOptions.IgnoreCase))
        {
            var candidate = FindVariableByToken(variables, "mean", "average", "avg");
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        }

        if (Regex.IsMatch(wordProblem, @"\barea\b", RegexOptions.IgnoreCase))
        {
            var candidate = FindVariableByToken(variables, "area");
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        }

        if (Regex.IsMatch(wordProblem, @"\bdistance\b", RegexOptions.IgnoreCase))
        {
            var candidate = FindVariableByToken(variables, "distance", "dist", "length");
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        }

        if (Regex.IsMatch(wordProblem, @"\bsum\b", RegexOptions.IgnoreCase))
        {
            var candidate = FindVariableByToken(variables, "sum", "total");
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        }

        return null;
    }

    private static string? FindVariableByToken(IReadOnlyList<string> variables, params string[] tokens)
    {
        if (variables.Count == 0 || tokens.Length == 0)
        {
            return null;
        }

        var tokenSet = new HashSet<string>(tokens.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.ToLowerInvariant()));
        var matches = new List<string>();

        foreach (var variable in variables)
        {
            var variableTokens = SplitIdentifierTokens(variable).ToList();
            if (variableTokens.Any(t => tokenSet.Contains(t)))
            {
                matches.Add(variable);
            }
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        return null;
    }

    private static readonly HashSet<string> IgnoredIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "pow", "sqrt", "log", "abs", "sin", "cos", "tan", "exp", "floor", "ceiling", "sum", "product", "mean", "average",
        "vector", "matrix", "mod", "and", "or", "not", "implies", "iff", "forall", "exists", "interval", "count", "filter",
        "length", "lt", "le", "gt", "ge", "ne", "integer", "issquare", "true", "false",
        "i", "pi", "pi_val", "deg", "degree", "radians", "ans", "result", "answer", "expression", "equations", "system",
        "sum_of_roots", "product_of_roots", "roots",
        "label", "dot", "draw", "fill", "filldraw", "drawanglemark", "drawrightanglemark", "anglemark", "rightanglemark", "shipout", "clip"
    };

    private static readonly HashSet<string> ExplicitTargetAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "target",
        "result",
        "answer",
        "ans"
    };

    private static bool WordProblemMentionsVariable(string? wordProblem, string variable)
    {
        if (string.IsNullOrWhiteSpace(wordProblem) || string.IsNullOrWhiteSpace(variable))
        {
            return false;
        }

        var pattern = $"\\b{Regex.Escape(variable)}\\b";
        if (Regex.IsMatch(wordProblem, pattern, RegexOptions.IgnoreCase)) return true;

        // Try token-based matching if exact match fails
        var tokens = SplitIdentifierTokens(variable).Where(IsScorableToken).ToList();
        if (tokens.Count > 0)
        {
            var wordTokens = ExtractWordTokens(wordProblem);
            if (tokens.All(t => TokenMatches(t, wordTokens)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScorableToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (token.Length <= 1) return false;
        return !StopWordTokens.Contains(token);
    }

    private static string NormalizeWordProblemText(string? wordProblem)
    {
        if (string.IsNullOrWhiteSpace(wordProblem))
        {
            return string.Empty;
        }

        var normalized = wordProblem.Replace("$", string.Empty);
        normalized = Regex.Replace(normalized, @"\\text\{([^}]*)\}", "$1", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\\(left|right)\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = normalized.Replace("{ ", " ").Replace(" }", " ");
        normalized = normalized.Replace("\\", string.Empty);
        return normalized;
    }

    private static string ExpandImplicitMultiplication(string text, IReadOnlyList<string> variables)
    {
        if (string.IsNullOrWhiteSpace(text) || variables.Count == 0)
        {
            return text;
        }

        var variableSet = new HashSet<string>(variables, StringComparer.OrdinalIgnoreCase);
        return Regex.Replace(text, @"\b([A-Za-z]{2,})\b", match =>
        {
            var token = match.Value;
            if (variableSet.Contains(token))
            {
                return token;
            }

            if (token.All(ch => char.IsLetter(ch) && variableSet.Contains(ch.ToString())))
            {
                return string.Join(" * ", token.Select(ch => ch.ToString()));
            }

            return token;
        });
    }

    private static readonly HashSet<string> StopWordTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "to", "in", "on", "for", "by", "at", "is", "are", "be", "with", "from", "as", "and",
        "or", "if", "then", "than", "between", "each", "value", "number", "many", "how", "does", "do", "what", "when",
        "per"
    };
}

// Copyright Warren Harding 2026
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;

namespace SymSolvers.CSharpAnalysis
{
    public class CSharpBugSearchStrategy
    {
        public List<CSharpMathBugFinding> Search(
            List<(IExpression Expression, CSharpExpressionMetadata Metadata)> inputs,
            CSharpMathBugAnalyzerOptions options,
            IReadOnlyDictionary<string, string> sourceTextByPath,
            CancellationToken ct,
            SemanticModel? semanticModel = null)
        {
            var preparedInputs = EnrichInputsWithLocalPropagation(inputs, ct);
            var egraph = new EGraph();
            var initialMetadata = new Dictionary<int, List<CSharpExpressionMetadata>>();

            // 1. Ingest
            foreach (var (expr, meta) in preparedInputs)
            {
                int id = egraph.Add(expr);
                if (!initialMetadata.TryGetValue(id, out var list))
                {
                    list = new List<CSharpExpressionMetadata>();
                    initialMetadata[id] = list;
                }
                list.Add(meta);
            }
            egraph.Rebuild(ct);

            // 2. Saturate
            var rules = CSharpBugRuleLibrary.GetRules();
            
            int iterations = 0;
            bool changed = true;
            var history = new MatchHistory();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            double timeoutSeconds = options.SaturationTimeoutSeconds;

            while (changed && iterations < options.MaxSaturationIterations)
            {
                ct.ThrowIfCancellationRequested();
                if (sw.Elapsed.TotalSeconds > timeoutSeconds) break;
                
                changed = false;
                iterations++;

                // Matches
                var matches = EGraphMatcher.FindMatches(egraph, rules, history, 1, ct);

                if (sw.Elapsed.TotalSeconds > timeoutSeconds) break;
                
                // Apply
                foreach (var match in matches)
                {
                    ct.ThrowIfCancellationRequested();
                    if (sw.Elapsed.TotalSeconds > timeoutSeconds) break;

                    // Materialize bindings for Condition/Transform
                    var materializedBindings = ImmutableDictionary.CreateBuilder<string, IExpression>();
                    foreach (var kvp in match.Bindings)
                    {
                        // Use default cost (null)
                        var bestExpr = EGraphExtract.ExtractBest(egraph, kvp.Value, null, null, ct);
                        if (bestExpr != null) {
                            // Console.WriteLine($"DEBUG: Extracted {bestExpr.ToDisplayString()} for {kvp.Key}");
                            materializedBindings.Add(kvp.Key, bestExpr);
                        }
                    }
                    var bindingsExpressions = materializedBindings.ToImmutable();

                    if (match.Rule.Condition != null) {
                        bool cond = match.Rule.Condition(bindingsExpressions);
                        // if (cond) Console.WriteLine($"DEBUG: Rule {match.Rule.Name} matched!");
                        if (!cond) continue;
                    }
                    
                    int newClassId;
                    if (match.Rule.Transform != null)
                    {
                        var transformed = match.Rule.Transform(bindingsExpressions);
                        if (transformed == null) continue;
                        newClassId = egraph.Add(transformed.Canonicalize());
                    }
                    else
                    {
                        // Instantiate uses int bindings
                        newClassId = EGraphInstantiator.Instantiate(egraph, match.Rule.Replacement, match.Bindings);
                    }
                    
                    if (egraph.Find(match.RootClassId) != egraph.Find(newClassId))
                    {
                        egraph.Union(match.RootClassId, newClassId);
                        changed = true;
                    }
                }

                if (changed) egraph.Rebuild(ct);
            }

            // 3. Analyze
            var findings = new List<CSharpMathBugFinding>();
            
            foreach (var kvp in initialMetadata)
            {
                int originalId = kvp.Key;
                var metadataList = kvp.Value;
                
                int currentId = egraph.Find(originalId);
                var eclass = egraph.GetClass(currentId);
                
                // Scan for bug markers
                foreach (var node in eclass.Nodes)
                {
                    string head = node.Head;
                    if (head.StartsWith("Func:")) head = head.Substring(5);

                    if (head.StartsWith("cs_bug_"))
                    {
                        if (!CSharpBugCatalog.TryMapBugNodeToId(head, out var bugId))
                        {
                            continue;
                        }

                        // Reconstruct arguments for display
                        var args = new List<string>();
                        foreach (var childId in node.Children)
                        {
                            var bestExpr = EGraphExtract.ExtractBest(egraph, childId, null, null, ct);
                            args.Add(bestExpr?.ToDisplayString() ?? "?");
                        }

                        string message = CSharpBugCatalog.GetMessage(bugId, args.ToArray());
                        string severityStr = CSharpBugCatalog.GetSeverity(bugId);
                        CSharpMathBugSeverity severity = ParseSeverity(severityStr);
                        CSharpSecurityRisk securityRisk = CSharpBugCatalog.GetSecurityRisk(bugId);
                        
                        CSharpMathBugConfidence confidence = CSharpSemanticsEvaluator.EvaluateConfidence(egraph, node);
                        double confidenceScore = ConfidenceToScore(confidence);

                        foreach (var meta in metadataList)
                        {
                            // Filter false positives in real projects, but allow in UnitTests for verification.
                            if (bugId == "CSSEC001" && 
                                meta.FilePath != null &&
                                (meta.FilePath.Contains("Test", StringComparison.OrdinalIgnoreCase) || meta.FilePath.Contains("Generator", StringComparison.OrdinalIgnoreCase)) &&
                                !meta.FilePath.Contains("UnitTests", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // EGraph-based guard suppression
                            if (bugId == "CSMATH023" || bugId == "CSSEC012")
                            {
                                if (node.Children.Count > 1 && HasGuard(egraph.GetClass(node.Children[1]), "cs_guard_nonzero")) continue;
                            }
                            if (bugId == "CSSEC011")
                            {
                                if (node.Children.Count > 0 && HasGuard(egraph.GetClass(node.Children[0]), "cs_guard_non_negative")) continue;
                            }
                            if (bugId == "CSSEC021")
                            {
                                // Subtraction is child[0] - child[1]. Rule usually matches the sub itself.
                                // If the bug node class itself has a guard, it means the sub is safe.
                                if (HasGuard(eclass, "cs_guard_non_negative")) continue;
                            }

                            // Suppress certain findings when an explicit nearby branch guard proves safety.
                            // Example: if (T.Zero.Equals(value)) return false; then T.One / value.
                            if ((bugId == "CSMATH023" || bugId == "CSSEC012" || bugId == "CSSEC011" || bugId == "CSSEC021" || bugId == "CSSEC024" || bugId == "CSSEC025" || bugId == "CSSEC009" || bugId == "CSSEC008" || bugId == "CSSEC026" || bugId == "CSSEC029" || bugId == "CSSEC030" || bugId == "CSMATH015" || bugId == "CSMATH018") &&
                                IsBugMarkerGuarded(bugId, meta, sourceTextByPath, args, semanticModel, options, ct))
                            {
                                continue;
                            }

                            if (confidenceScore < options.ConfidenceThreshold)
                            {
                                continue;
                            }

                            findings.Add(new CSharpMathBugFinding(
                                BugId: bugId,
                                Title: message,
                                Severity: severity,
                                SecurityRisk: securityRisk,
                                Confidence: confidence,
                                ConfidenceScore: confidenceScore,
                                Message: message,
                                Suggestion: string.Empty,
                                Expression: meta.OriginalText,
                                SourceSpan: new CSharpMathBugSourceSpan(meta.FilePath ?? "", meta.Line, meta.Column, meta.Line, meta.Column + meta.OriginalText.Length),
                                Evidence: new List<string>(),
                                WitnessAssignments: null
                            ));
                        }
                    }
                }
            }

            var deduplicated = findings
                .GroupBy(ComposeFindingIdentityKey, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(f => f.ConfidenceScore)
                    .ThenByDescending(f => f.Confidence)
                    .First());

            return SuppressOverlappingFindings(deduplicated)
                .OrderByDescending(f => f.SecurityRisk)
                .ThenByDescending(f => f.Severity)
                .ThenByDescending(f => f.Confidence)
                .ThenByDescending(f => f.ConfidenceScore)
                .ThenBy(f => f.SourceSpan?.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.SourceSpan?.StartLine ?? 0)
                .Take(options.MaxFindings)
                .ToList();
        }

        private static IEnumerable<CSharpMathBugFinding> SuppressOverlappingFindings(IEnumerable<CSharpMathBugFinding> findings)
        {
            var materialized = findings.ToList();
            var indexOverflowKeys = new HashSet<string>(
                materialized.Where(f => f.BugId == "CSMATH015").Select(ComposeLocationExpressionKey),
                StringComparer.Ordinal);
            var accumulatorKeys = new HashSet<string>(
                materialized.Where(f => f.BugId == "CSMATH016").Select(ComposeLocationExpressionKey),
                StringComparer.Ordinal);

            foreach (var finding in materialized)
            {
                var key = ComposeLocationExpressionKey(finding);
                if (finding.BugId == "CSMATH018" && indexOverflowKeys.Contains(key))
                {
                    continue;
                }

                if (finding.BugId == "CSMATH025" && accumulatorKeys.Contains(key))
                {
                    continue;
                }

                yield return finding;
            }
        }

        private static string ComposeFindingIdentityKey(CSharpMathBugFinding finding)
        {
            var span = finding.SourceSpan;
            var path = (span?.FilePath ?? string.Empty).ToUpperInvariant();
            var line = span?.StartLine ?? 0;
            var column = span?.StartColumn ?? 0;
            return string.Create(
                path.Length + finding.BugId.Length + finding.Expression.Length + 24,
                (finding.BugId, path, line, column, finding.Expression),
                static (buffer, state) =>
                {
                    var idx = 0;
                    state.BugId.AsSpan().CopyTo(buffer[idx..]);
                    idx += state.BugId.Length;
                    buffer[idx++] = '|';
                    state.path.AsSpan().CopyTo(buffer[idx..]);
                    idx += state.path.Length;
                    buffer[idx++] = '|';
                    state.line.TryFormat(buffer[idx..], out var writtenLine);
                    idx += writtenLine;
                    buffer[idx++] = '|';
                    state.column.TryFormat(buffer[idx..], out var writtenColumn);
                    idx += writtenColumn;
                    buffer[idx++] = '|';
                    state.Expression.AsSpan().CopyTo(buffer[idx..]);
                });
        }

        private static string ComposeLocationExpressionKey(CSharpMathBugFinding finding)
        {
            var span = finding.SourceSpan;
            var path = (span?.FilePath ?? string.Empty).ToUpperInvariant();
            var line = span?.StartLine ?? 0;
            var column = span?.StartColumn ?? 0;
            return string.Create(
                path.Length + finding.Expression.Length + 18,
                (path, line, column, finding.Expression),
                static (buffer, state) =>
                {
                    var idx = 0;
                    state.path.AsSpan().CopyTo(buffer[idx..]);
                    idx += state.path.Length;
                    buffer[idx++] = '|';
                    state.line.TryFormat(buffer[idx..], out var writtenLine);
                    idx += writtenLine;
                    buffer[idx++] = '|';
                    state.column.TryFormat(buffer[idx..], out var writtenColumn);
                    idx += writtenColumn;
                    buffer[idx++] = '|';
                    state.Expression.AsSpan().CopyTo(buffer[idx..]);
                });
        }

        private static List<(IExpression Expression, CSharpExpressionMetadata Metadata)> EnrichInputsWithLocalPropagation(
            List<(IExpression Expression, CSharpExpressionMetadata Metadata)> inputs,
            CancellationToken ct)
        {
            if (inputs.Count == 0)
            {
                return inputs;
            }

            // Safety valve: local propagation can become very expensive on large real-world repos.
            // Skip it when inputs are large to avoid timeouts/hangs.
            const int MaxPropagationInputs = 1500;
            if (inputs.Count > MaxPropagationInputs)
            {
                return inputs;
            }

            var enriched = new List<(IExpression Expression, CSharpExpressionMetadata Metadata)>(inputs.Count * 2);

            foreach (var fileGroup in inputs
                .GroupBy(i => i.Metadata.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var ordered = fileGroup
                    .OrderBy(i => i.Metadata.Line)
                    .ThenBy(i => i.Metadata.Column)
                    .ToList();

                var localBindings = new Dictionary<string, IExpression>(StringComparer.Ordinal);

                foreach (var item in ordered)
                {
                    ct.ThrowIfCancellationRequested();
                    var expr = item.Expression;
                    enriched.Add(item);

                    var propagated = SubstituteLocals(expr, localBindings, depthLimit: 8, ct);
                    if (ShouldAddPropagatedVariant(expr, propagated))
                    {
                        enriched.Add((propagated, item.Metadata));
                    }

                    if (TryGetAssignment(expr, out var targetName, out var assignedExpr))
                    {
                        var resolvedAssignedExpr = SubstituteLocals(assignedExpr, localBindings, depthLimit: 8, ct);
                        if (ReferencesSymbol(resolvedAssignedExpr, targetName))
                        {
                            // Self-referential assignments (e.g., x = x + 1) break direct substitution; clear stale value.
                            localBindings.Remove(targetName);
                        }
                        else
                        {
                            localBindings[targetName] = resolvedAssignedExpr;
                        }
                        continue;
                    }

                    if (TryGetMutationTarget(expr, out var mutatedTarget))
                    {
                        localBindings.Remove(mutatedTarget);
                    }
                }
            }

            return enriched;
        }

        private static bool TryGetAssignment(IExpression expr, out string targetName, out IExpression assignedExpr)
        {
            if (expr is Function { Name: "cs_assign" } assign &&
                assign.Arguments.Count == 2 &&
                assign.Arguments[0] is Symbol target)
            {
                targetName = target.Name;
                assignedExpr = assign.Arguments[1];
                return true;
            }

            targetName = string.Empty;
            assignedExpr = null!;
            return false;
        }

        private static bool TryGetMutationTarget(IExpression expr, out string targetName)
        {
            if (expr is Function f &&
                f.Arguments.Count == 1 &&
                f.Arguments[0] is Symbol target &&
                (f.Name.StartsWith("cs_inc_", StringComparison.Ordinal) || f.Name.StartsWith("cs_dec_", StringComparison.Ordinal)))
            {
                targetName = target.Name;
                return true;
            }

            targetName = string.Empty;
            return false;
        }

        private static IExpression SubstituteLocals(IExpression expr, IReadOnlyDictionary<string, IExpression> bindings, int depthLimit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (depthLimit <= 0)
            {
                return expr;
            }

            if (expr is Symbol symbol && bindings.TryGetValue(symbol.Name, out var replacement))
            {
                return SubstituteLocals(replacement, bindings, depthLimit - 1, ct);
            }

            if (expr is not Operation op || op.Arguments.Count == 0)
            {
                return expr;
            }

            bool changed = false;
            var rewrittenArgs = ImmutableList.CreateBuilder<IExpression>();
            foreach (var arg in op.Arguments)
            {
                var rewrittenArg = SubstituteLocals(arg, bindings, depthLimit - 1, ct);
                rewrittenArgs.Add(rewrittenArg);
                if (!ReferenceEquals(rewrittenArg, arg))
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                return expr;
            }

            return op.WithArguments(rewrittenArgs.ToImmutable()).Canonicalize();
        }

        private static bool ReferencesSymbol(IExpression expr, string symbolName)
        {
            if (expr is Symbol s)
            {
                return string.Equals(s.Name, symbolName, StringComparison.Ordinal);
            }

            if (expr is not Operation op)
            {
                return false;
            }

            foreach (var arg in op.Arguments)
            {
                if (ReferencesSymbol(arg, symbolName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldAddPropagatedVariant(IExpression original, IExpression propagated)
        {
            if (original.InternalEquals(propagated))
            {
                return false;
            }

            if (original is not Function f)
            {
                return false;
            }

            // Keep propagation focused on security-relevant sinks and guard expressions.
            // This avoids unintentionally upgrading confidence for generic math findings.
            return IsSecurityPropagationRoot(f.Name);
        }

        private static bool IsSecurityPropagationRoot(string name)
        {
            return name == "cs_call" ||
                   name == "cs_new_array" ||
                   name == "cs_array_get" ||
                   name.StartsWith("cs_lt_", StringComparison.Ordinal) ||
                   name.StartsWith("cs_lte_", StringComparison.Ordinal) ||
                   name.StartsWith("cs_gt_", StringComparison.Ordinal) ||
                   name.StartsWith("cs_gte_", StringComparison.Ordinal);
        }

        private static double ConfidenceToScore(CSharpMathBugConfidence confidence)
        {
            return confidence switch
            {
                CSharpMathBugConfidence.Confirmed => 1.0,
                CSharpMathBugConfidence.High => 0.75,
                CSharpMathBugConfidence.Medium => 0.5,
                _ => 0.25
            };
        }

        private static bool HasGuard(EClass eclass, string guardPrefix)
        {
            foreach (var node in eclass.Nodes)
            {
                var head = node.Head;
                if (head.StartsWith("Func:", StringComparison.Ordinal)) head = head.Substring(5);
                if (head.StartsWith(guardPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsBugMarkerGuarded(
            string bugId,
            CSharpExpressionMetadata meta,
            IReadOnlyDictionary<string, string> sourceTextByPath,
            IReadOnlyList<string>? bugArgs = null,
            SemanticModel? semanticModel = null,
            CSharpMathBugAnalyzerOptions? options = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(meta.FilePath) || meta.Line <= 0)
            {
                return false;
            }

            // Prefer operation-based subject keys when possible (stable across formatting and aligns with EGraph guard subjects).
            HashSet<string>? operationSubjectKeys = null;
            string? legacySubjectName = null;

            if (bugId is "CSSEC011" or "CSSEC021" or "CSSEC024" or "CSSEC008" or "CSMATH023" or "CSSEC012" or "CSSEC026" or "CSSEC029" or "CSSEC030" or "CSMATH015" or "CSMATH018")
            {
                legacySubjectName = bugId switch
                {
                    "CSMATH023" or "CSSEC012" => (bugArgs != null && bugArgs.Count > 1)
                        ? bugArgs[1]
                        : (TryExtractDivisorIdentifier(meta.OriginalText) ?? TryExtractDivisorFromArgs(bugArgs)),
                    "CSSEC011" => (bugArgs != null && bugArgs.Count > 0)
                        ? bugArgs[0]
                        : (TryExtractModuloDividend(meta.OriginalText) ?? (bugArgs?.Count > 0 ? bugArgs[0] : null)),
                    "CSSEC021" or "CSSEC024" => (bugArgs != null && bugArgs.Count > 0) ? bugArgs[0] : meta.OriginalText,
                    "CSSEC008" => (bugArgs != null && bugArgs.Count > 0) ? bugArgs[0] : null,
                    "CSSEC026" or "CSSEC030" => (bugArgs != null && bugArgs.Count > 0) ? bugArgs[bugArgs.Count - 1] : null,
                    "CSSEC029" => (bugArgs != null && bugArgs.Count > 1) ? bugArgs[1] : null, // (a, b) -> b is the shift amount
                    "CSMATH015" or "CSMATH018" => (bugArgs != null && bugArgs.Count > 0) ? bugArgs[0] : null, // First arg is usually the index math
                    _ => null
                };
            }

            if (semanticModel != null && (options?.EnableGuardProver ?? true))
            {
                try
                {
                    var root = semanticModel.SyntaxTree.GetRoot(ct);
                    var text = semanticModel.SyntaxTree.GetText(ct);
                    if (meta.Line <= text.Lines.Count)
                    {
                        var line = text.Lines[meta.Line - 1];
                        var pos = line.Start + Math.Max(0, meta.Column - 1);
                        var node = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(pos, 0));
                        var op = node != null ? FindBestOperationForBug(node, bugId, semanticModel, ct) : null;

                        if (op != null)
                        {
                            if (bugId == "CSSEC009" && IsStaticInitializationWriteSite(node!, semanticModel, ct))
                            {
                                return true;
                            }

                            var prover = new CSharpGuardProver(options);

                            // Many guard/idiom patterns are only recognizable at the *enclosing* operation level
                            // (e.g. range-check comparisons, x&(x-1) bit-twiddling). Prove guards against the
                            // closest useful parent operation instead of only the inner sub-expression.
                            var opForFacts = SelectOperationForGuardProving(bugId, op);

                            // Compute subject keys for the bug at the current syntax position.
                            var subjectOp = TryGetBugSubjectOperation(bugId, op);
                            if (subjectOp != null)
                            {
                                operationSubjectKeys = BuildOperationSubjectKeys(subjectOp, semanticModel, ct);
                            }

                            // Non-branch, intra-procedural proof for subjects that are local temps:
                            // If the subject is a local temp that was just assigned a safe expression, treat it as guarded.
                            if (subjectOp is ILocalReferenceOperation)
                            {
                                if (TryResolveSimpleLocalValue(subjectOp.Syntax, semanticModel, ct, out var resolvedValueOp) &&
                                    resolvedValueOp != null)
                                {
                                    var facts = prover.DeriveExpressionFacts(resolvedValueOp, semanticModel, diagnostics: null, ct: ct);
                                    var lowered = new CSharpSemanticLowerer().LowerExpression(resolvedValueOp, null);
                                    if (lowered != null)
                                    {
                                        var key = lowered.Canonicalize().ToDisplayString().Trim();
                                        if (IsProtectedByFacts(bugId, facts, new HashSet<string>(StringComparer.Ordinal) { key }, legacySubjectName))
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }

                            // Check facts derived from the expression itself (including preceding assertions)
                            var exprFacts = prover.DeriveExpressionFacts(opForFacts, semanticModel, diagnostics: null, ct: ct);
                            if (IsProtectedByFacts(bugId, exprFacts, operationSubjectKeys, legacySubjectName))
                            {
                                return true;
                            }

                            var current = op;
                            while (current != null)
                            {
                                ct.ThrowIfCancellationRequested();
                                if (current.Parent is IConditionalOperation conditional)
                                {
                                    // If the suspicious expression participates in the condition itself (common for
                                    // range-check idioms like (uint)(x - min) < range), derive facts from the condition.
                                    // This allows suppression even though the expression is not in either branch body.
                                    var isInCondition = false;
                                    var walk = current;
                                    while (walk != null && walk is not IConditionalOperation)
                                    {
                                        if (walk == conditional.Condition)
                                        {
                                            isInCondition = true;
                                            break;
                                        }

                                        walk = walk.Parent;
                                    }

                                    if (isInCondition)
                                    {
                                        var conditionFacts = prover.DeriveExpressionFacts(conditional.Condition, semanticModel, diagnostics: null, ct: ct);
                                        if (IsProtectedByFacts(bugId, conditionFacts, operationSubjectKeys, legacySubjectName))
                                        {
                                            return true;
                                        }
                                    }

                                    var isTrueBranch = IsInBranch(current, conditional.WhenTrue);
                                    var isFalseBranch = IsInBranch(current, conditional.WhenFalse);

                                    if (isTrueBranch || isFalseBranch)
                                    {
                                        var facts = prover.DeriveBranchFacts(conditional.Condition, isTrueBranch, semanticModel, diagnostics: null, ct: ct);

                                        if (IsProtectedByFacts(bugId, facts, operationSubjectKeys, legacySubjectName))
                                        {
                                            return true;
                                        }
                                    }
                                }
                                current = current.Parent;
                            }

                            // Interprocedural proof (closed-world within the current compilation):
                            // if the bug is on a parameter-derived subject and every call site provides a proven-safe argument,
                            // treat it as guarded.
                            if (options != null &&
                                (options.SecurityFlowMode == CSharpSecurityFlowMode.InterproceduralIfds ||
                                 options.SecurityFlowMode == CSharpSecurityFlowMode.InterproceduralIde))
                            {
                                if (IsInterprocedurallyGuarded(bugId, op, semanticModel, options, ct))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Fall back to text-based heuristic on failure.
                }
            }

            if (string.IsNullOrWhiteSpace(legacySubjectName))
            {
                return false;
            }

            if (!sourceTextByPath.TryGetValue(meta.FilePath, out var sourceText) || string.IsNullOrWhiteSpace(sourceText))
            {
                return false;
            }

            var lines = sourceText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var lineIndex = Math.Clamp(meta.Line - 1, 0, Math.Max(0, lines.Length - 1));
            var windowStart = Math.Max(0, lineIndex - 20);

            for (int i = windowStart; i <= lineIndex; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (bugId == "CSMATH023" || bugId == "CSSEC012")
                {
                    // Generic math guard patterns
                    if (line.Contains($"Zero.Equals({legacySubjectName})", StringComparison.Ordinal) ||
                        line.Contains($"Zero.Equals({legacySubjectName} ", StringComparison.Ordinal) ||
                        line.Contains($"T.Zero.Equals({legacySubjectName})", StringComparison.Ordinal) ||
                        line.Contains($"{legacySubjectName}.Equals(T.Zero)", StringComparison.Ordinal) ||
                        line.Contains($"{legacySubjectName} == 0", StringComparison.Ordinal) ||
                        line.Contains($"0 == {legacySubjectName}", StringComparison.Ordinal) ||
                        line.Contains($"{legacySubjectName} == T.Zero", StringComparison.Ordinal) ||
                        line.Contains($"T.Zero == {legacySubjectName}", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IOperation SelectOperationForGuardProving(string bugId, IOperation operation)
        {
            // The metadata location for a bug often points at a small sub-expression (e.g. a subtraction inside
            // an idiom), but guard proving needs enough context to match the guard rules.
            // Keep this conservative and local: only walk a few parents and only for specific bug IDs.
            const int MaxParentHops = 6;

            if (bugId == "CSSEC024")
            {
                var current = operation;
                for (var i = 0; i < MaxParentHops && current.Parent != null; i++)
                {
                    if (current.Parent is IBinaryOperation bin &&
                        (bin.OperatorKind == BinaryOperatorKind.LessThan || bin.OperatorKind == BinaryOperatorKind.LessThanOrEqual))
                    {
                        return bin;
                    }
                    current = current.Parent;
                }
            }

            if (bugId == "CSSEC021")
            {
                var current = operation;
                for (var i = 0; i < MaxParentHops && current.Parent != null; i++)
                {
                    if (current.Parent is IBinaryOperation bin &&
                        (bin.OperatorKind == BinaryOperatorKind.And ||
                         bin.OperatorKind == BinaryOperatorKind.Or ||
                         bin.OperatorKind == BinaryOperatorKind.ExclusiveOr))
                    {
                        return bin;
                    }

                    // The idiom might also be inside a comparison (e.g. (uint)(x-min) <= ...).
                    if (current.Parent is IBinaryOperation cmp &&
                        (cmp.OperatorKind == BinaryOperatorKind.LessThan || cmp.OperatorKind == BinaryOperatorKind.LessThanOrEqual))
                    {
                        return cmp;
                    }

                    current = current.Parent;
                }
            }

            return operation;
        }

        private static IOperation? FindBestOperationForBug(
            SyntaxNode node,
            string bugId,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            // The metadata column often points at the start token of an expression (e.g., identifier),
            // but guard proving needs the surrounding operation that contains the bug subject (% dividend, subtraction, etc).
            IOperation? fallback = null;
            var current = node;
            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                ct.ThrowIfCancellationRequested();

                var candidate = semanticModel.GetOperation(current, ct);
                if (candidate != null)
                {
                    fallback ??= candidate;
                    if (TryGetBugSubjectOperation(bugId, candidate) != null)
                    {
                        return candidate;
                    }
                }

                current = current.Parent;
            }

            return fallback;
        }

        private static bool TryResolveSimpleLocalValue(
            SyntaxNode localSyntax,
            SemanticModel semanticModel,
            CancellationToken ct,
            out IOperation? resolvedValue)
        {
            resolvedValue = null;

            // We only resolve from identifier syntax inside a statement block.
            if (localSyntax is not IdentifierNameSyntax id)
            {
                return false;
            }

            // Follow a short chain of local temps (e.g., a = b; b = x & mask;).
            const int MaxDepth = 4;
            ExpressionSyntax? expr = id;
            for (int depth = 0; depth < MaxDepth && expr is IdentifierNameSyntax curId; depth++)
            {
                ct.ThrowIfCancellationRequested();
                expr = TryResolveIdentifierValueSyntax(curId, semanticModel, ct);
                if (expr is null)
                {
                    return false;
                }
            }

            resolvedValue = expr != null ? semanticModel.GetOperation(expr, ct) : null;
            return resolvedValue != null;
        }

        private static IOperation? TryGetBugSubjectOperation(string bugId, IOperation operation)
        {
            // For these bug IDs, we want the specific sub-expression that guard rules talk about.
            // - CSSEC011: the modulo dividend (left operand of %)
            // - CSSEC021: the subtraction expression itself (a - b)
            if (bugId == "CSSEC011")
            {
                var mod = FindFirst(operation, op => op is IBinaryOperation { OperatorKind: BinaryOperatorKind.Remainder }) as IBinaryOperation;
                return mod?.LeftOperand;
            }

            if (bugId == "CSSEC021")
            {
                var sub = FindFirst(operation, op => op is IBinaryOperation { OperatorKind: BinaryOperatorKind.Subtract }) as IBinaryOperation;
                return sub;
            }

            if (bugId == "CSSEC024")
            {
                var conv = FindFirst(operation, op => op is IConversionOperation { IsChecked: false } c && FindFirst(c.Operand, o => o is IBinaryOperation { OperatorKind: BinaryOperatorKind.Subtract }) != null) as IConversionOperation;
                var sub = conv != null ? FindFirst(conv.Operand, o => o is IBinaryOperation { OperatorKind: BinaryOperatorKind.Subtract }) : null;
                return sub;
            }

            if (bugId == "CSSEC008")
            {
                var eq = FindFirst(operation, op => op is IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals }) as IBinaryOperation;
                if (eq != null)
                {
                    // For null checks, the non-null operand is our subject.
                    return CSharpGuardProver.IsNullLiteral(eq.LeftOperand) ? eq.RightOperand : eq.LeftOperand;
                }

                var inv = FindFirst(operation, op => op is IInvocationOperation { TargetMethod.Name: "Equals" }) as IInvocationOperation;
                if (inv != null)
                {
                    return inv.Instance ?? (inv.Arguments.Length > 0 ? inv.Arguments[0].Value : null);
                }
            }

            if (bugId == "CSSEC025")
            {
                var conv = FindFirst(operation, op =>
                    op is IConversionOperation c &&
                    c.Type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32 &&
                    c.Operand.Type?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64 &&
                    !c.IsChecked) as IConversionOperation;

                return conv?.Operand;
            }

            if (bugId == "CSMATH023" || bugId == "CSSEC012")
            {
                // Division/modulo guard subject is typically the divisor (right operand).
                var div = FindFirst(operation, op => op is IBinaryOperation { OperatorKind: BinaryOperatorKind.Divide }) as IBinaryOperation;
                return div?.RightOperand;
            }

            if (bugId == "CSSEC026" || bugId == "CSSEC030")
            {
                var inv = FindFirst(operation, op => op is IInvocationOperation) as IInvocationOperation;
                if (inv != null && inv.Arguments.Length > 0)
                {
                    return inv.Arguments[inv.Arguments.Length - 1].Value;
                }
            }

            if (bugId == "CSSEC029")
            {
                var newArr = FindFirst(operation, op => op is IArrayCreationOperation) as IArrayCreationOperation;
                if (newArr != null && newArr.DimensionSizes.Length > 0)
                {
                    var dim = newArr.DimensionSizes[0];
                    if (dim is IBinaryOperation bin && (bin.OperatorKind == BinaryOperatorKind.LeftShift || bin.OperatorKind == BinaryOperatorKind.RightShift))
                    {
                        return bin.RightOperand;
                    }
                    if (dim is IBinaryOperation binAnd && (binAnd.OperatorKind == BinaryOperatorKind.And || binAnd.OperatorKind == BinaryOperatorKind.Or))
                    {
                        return dim; // fact will be derived on the whole expression
                    }
                    return dim;
                }
            }

            if (bugId == "CSMATH015" || bugId == "CSMATH018")
            {
                var arrGet = FindFirst(operation, op => op is IPropertyReferenceOperation { Property.IsIndexer: true } || op is IArrayElementReferenceOperation);
                if (arrGet is IPropertyReferenceOperation propRef && propRef.Arguments.Length > 0) return propRef.Arguments[0].Value;
                if (arrGet is IArrayElementReferenceOperation arrRef && arrRef.Indices.Length > 0) return arrRef.Indices[0];
            }

            return null;
        }

        private static IOperation? FindFirst(IOperation root, Func<IOperation, bool> predicate)
        {
            var stack = new Stack<IOperation>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (predicate(current))
                {
                    return current;
                }

                foreach (var child in current.ChildOperations)
                {
                    stack.Push(child);
                }
            }

            return null;
        }

        private static HashSet<string> BuildOperationSubjectKeys(IOperation operation, SemanticModel semanticModel, CancellationToken ct)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);

            var lowerer = new CSharpSemanticLowerer();
            var lowered = lowerer.LowerExpression(operation, null);
            if (lowered != null)
            {
                keys.Add(lowered.Canonicalize().ToDisplayString().Trim());
            }

            var stack = new Stack<IOperation>();
            stack.Push(operation);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = stack.Pop();
                switch (current)
                {
                    case ILocalReferenceOperation local:
                        keys.Add(local.Local.Name);
                        break;
                    case IParameterReferenceOperation parameter:
                        keys.Add(parameter.Parameter.Name);
                        break;
                    case IFieldReferenceOperation field:
                        keys.Add(field.Field.Name);
                        break;
                    case IPropertyReferenceOperation property:
                        keys.Add(property.Property.Name);
                        break;
                }

                foreach (var child in current.ChildOperations)
                {
                    stack.Push(child);
                }
            }

            keys.RemoveWhere(string.IsNullOrWhiteSpace);
            return keys;
        }

        private static bool IsInterprocedurallyGuarded(
            string bugId,
            IOperation bugSiteOperation,
            SemanticModel semanticModel,
            CSharpMathBugAnalyzerOptions options,
            CancellationToken ct)
        {
            if (bugId is not ("CSSEC011" or "CSSEC021"))
            {
                return false;
            }

            var subjectOp = TryGetBugSubjectOperation(bugId, bugSiteOperation);
            if (subjectOp is null)
            {
                return false;
            }

            // Only attempt interprocedural proof when the bug subject depends on parameters.
            // Otherwise, intra-procedural guard rules/substitution should already handle it.
            var enclosing = semanticModel.GetEnclosingSymbol(bugSiteOperation.Syntax.SpanStart, ct) as IMethodSymbol;
            if (enclosing is null)
            {
                return false;
            }

            var compilation = semanticModel.Compilation;
            if (compilation is null)
            {
                return false;
            }

            var prover = new CSharpGuardProver(options);

            // Recognize a derived-underflow idiom in the callee that is commonly guarded in the caller:
            // (param >> 16) - 1 under a call-site guard arg > 0xFFFF.
            // Example: UCS4→UTF16 surrogate conversion.
            var shiftSubPattern = bugId == "CSSEC021"
                ? TryGetShiftSub16MinusOnePattern(subjectOp)
                : null;

            int callSiteCount = 0;
            const int MaxCallSitesToScan = 64;

            foreach (var tree in compilation.SyntaxTrees)
            {
                ct.ThrowIfCancellationRequested();

                var root = tree.GetRoot(ct);
                var model = compilation.GetSemanticModel(tree);

                foreach (var invocationSyntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    ct.ThrowIfCancellationRequested();

                    var invocationOp = model.GetOperation(invocationSyntax, ct) as IInvocationOperation;
                    if (invocationOp is null)
                    {
                        continue;
                    }

                    if (!SymbolEqualityComparer.Default.Equals(invocationOp.TargetMethod.OriginalDefinition, enclosing.OriginalDefinition))
                    {
                        continue;
                    }

                    callSiteCount++;
                    if (callSiteCount > MaxCallSitesToScan)
                    {
                        return false;
                    }

                    if (bugId == "CSSEC011")
                    {
                        if (!TryProveNonNegativeModuloArgumentAtCallSite(invocationSyntax, invocationOp, model, prover, ct))
                        {
                            return false;
                        }
                    }
                    else if (bugId == "CSSEC021")
                    {
                        if (shiftSubPattern != null)
                        {
                            if (!TryProveShiftSub16MinusOneGuardAtCallSite(invocationSyntax, invocationOp, model, prover, shiftSubPattern.Value, ct))
                            {
                                return false;
                            }
                        }
                        else if (!TryProveUnsignedSubGuardAtCallSite(invocationSyntax, invocationOp, model, prover, ct))
                        {
                            return false;
                        }
                    }
                }
            }

            return callSiteCount > 0;
        }

        private readonly record struct ShiftSub16MinusOnePattern(int ParameterOrdinal, string NumericSuffix);

        private static ShiftSub16MinusOnePattern? TryGetShiftSub16MinusOnePattern(IOperation subjectOp)
        {
            // Match: (param >> 16) - 1 where param is a method parameter.
            if (subjectOp is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Subtract } sub)
            {
                return null;
            }

            // We want subtract-by-1 specifically.
            if (!sub.RightOperand.ConstantValue.HasValue) return null;
            try
            {
                if (Convert.ToDecimal(sub.RightOperand.ConstantValue.Value) != 1m) return null;
            }
            catch
            {
                return null;
            }

            if (sub.LeftOperand is not IBinaryOperation { OperatorKind: BinaryOperatorKind.RightShift } shr)
            {
                return null;
            }

            if (!shr.RightOperand.ConstantValue.HasValue) return null;
            try
            {
                if (Convert.ToDecimal(shr.RightOperand.ConstantValue.Value) != 16m) return null;
            }
            catch
            {
                return null;
            }

            if (shr.LeftOperand is not IParameterReferenceOperation param)
            {
                return null;
            }

            var suffix = GetNumericSuffix(param.Type);
            if (suffix is not "u32" and not "u64")
            {
                return null;
            }

            return new ShiftSub16MinusOnePattern(param.Parameter.Ordinal, suffix);
        }

        private static bool TryProveShiftSub16MinusOneGuardAtCallSite(
            InvocationExpressionSyntax invocationSyntax,
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CSharpGuardProver prover,
            ShiftSub16MinusOnePattern pattern,
            CancellationToken ct)
        {
            if (pattern.ParameterOrdinal < 0 || pattern.ParameterOrdinal >= invocationOperation.Arguments.Length)
            {
                return false;
            }

            var argSyntax = invocationSyntax.ArgumentList.Arguments[pattern.ParameterOrdinal].Expression;
            var resolvedArgOp = ResolveInvocationArgument(argSyntax, invocationOperation.Arguments[pattern.ParameterOrdinal].Value, semanticModel, ct);
            if (resolvedArgOp is null)
            {
                return false;
            }

            var subjectKey = BuildShiftSub16MinusOneSubjectKey(resolvedArgOp, pattern.NumericSuffix, ct);
            if (string.IsNullOrWhiteSpace(subjectKey))
            {
                return false;
            }

            // Prove the call occurs under a branch guard that implies NonNegative((arg >> 16) - 1).
            var current = (IOperation)invocationOperation;
            while (current != null)
            {
                ct.ThrowIfCancellationRequested();
                if (current.Parent is IConditionalOperation conditional)
                {
                    var isTrueBranch = IsInBranch(current, conditional.WhenTrue);
                    var isFalseBranch = IsInBranch(current, conditional.WhenFalse);
                    if (isTrueBranch || isFalseBranch)
                    {
                        var facts = prover.DeriveBranchFacts(conditional.Condition, isTrueBranch, semanticModel, diagnostics: null, ct: ct);
                        if (facts.Any(f => f.Kind == CSharpGuardKind.NonNegative && string.Equals(f.Subject, subjectKey, StringComparison.Ordinal)))
                        {
                            return true;
                        }
                    }
                }

                current = current.Parent;
            }

            return false;
        }

        private static string? BuildShiftSub16MinusOneSubjectKey(IOperation arg, string suffix, CancellationToken ct)
        {
            _ = ct;
            var lowerer = new CSharpSemanticLowerer();
            var argExpr = lowerer.LowerExpression(arg, null);
            if (argExpr is null)
            {
                return null;
            }

            // Build: (arg >> 16) - 1
            var shr = new Function($"cs_shr_{suffix}", new IExpression[] { argExpr, new Number(16m) });
            var sub = new Function($"cs_sub_{suffix}_unchecked", new IExpression[] { shr, new Number(1m) });
            return sub.Canonicalize().ToDisplayString().Trim();
        }

        private static bool TryProveNonNegativeModuloArgumentAtCallSite(
            InvocationExpressionSyntax invocationSyntax,
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CSharpGuardProver prover,
            CancellationToken ct)
        {
            if (invocationOperation.Arguments.Length < 1)
            {
                return false;
            }

            var argSyntax = invocationSyntax.ArgumentList.Arguments[0].Expression;
            var resolvedArgOp = ResolveInvocationArgument(argSyntax, invocationOperation.Arguments[0].Value, semanticModel, ct);
            if (resolvedArgOp is null)
            {
                return false;
            }

            var facts = prover.DeriveExpressionFacts(resolvedArgOp, semanticModel, diagnostics: null, ct: ct);

            var lowerer = new CSharpSemanticLowerer();
            var lowered = lowerer.LowerExpression(resolvedArgOp, null);
            if (lowered is null)
            {
                return false;
            }

            var key = lowered.Canonicalize().ToDisplayString().Trim();
            return facts.Any(f => f.Kind == CSharpGuardKind.NonNegative && string.Equals(f.Subject, key, StringComparison.Ordinal));
        }

        private static bool TryProveUnsignedSubGuardAtCallSite(
            InvocationExpressionSyntax invocationSyntax,
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CSharpGuardProver prover,
            CancellationToken ct)
        {
            if (invocationOperation.Arguments.Length < 2)
            {
                return false;
            }

            var arg0Syntax = invocationSyntax.ArgumentList.Arguments[0].Expression;
            var arg1Syntax = invocationSyntax.ArgumentList.Arguments[1].Expression;

            var resolvedLeft = ResolveInvocationArgument(arg0Syntax, invocationOperation.Arguments[0].Value, semanticModel, ct);
            var resolvedRight = ResolveInvocationArgument(arg1Syntax, invocationOperation.Arguments[1].Value, semanticModel, ct);
            if (resolvedLeft is null || resolvedRight is null)
            {
                return false;
            }

            // Prove the call occurs under a branch guard that implies NonNegative(left - right).
            var current = (IOperation)invocationOperation;
            while (current != null)
            {
                ct.ThrowIfCancellationRequested();
                if (current.Parent is IConditionalOperation conditional)
                {
                    var isTrueBranch = IsInBranch(current, conditional.WhenTrue);
                    var isFalseBranch = IsInBranch(current, conditional.WhenFalse);
                    if (isTrueBranch || isFalseBranch)
                    {
                        var facts = prover.DeriveBranchFacts(conditional.Condition, isTrueBranch, semanticModel, diagnostics: null, ct: ct);
                        var subjectKey = BuildUnsignedSubSubjectKey(resolvedLeft, resolvedRight, semanticModel, ct);
                        if (subjectKey != null && facts.Any(f => f.Kind == CSharpGuardKind.NonNegative && string.Equals(f.Subject, subjectKey, StringComparison.Ordinal)))
                        {
                            return true;
                        }
                    }
                }

                current = current.Parent;
            }

            return false;
        }

        private static string? BuildUnsignedSubSubjectKey(
            IOperation left,
            IOperation right,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            _ = semanticModel;
            var lowerer = new CSharpSemanticLowerer();
            var leftExpr = lowerer.LowerExpression(left, null);
            var rightExpr = lowerer.LowerExpression(right, null);
            if (leftExpr is null || rightExpr is null)
            {
                return null;
            }

            var suffix = GetNumericSuffix(left.Type) ?? GetNumericSuffix(right.Type);
            if (suffix is null)
            {
                return null;
            }

            var sub = new Function($"cs_sub_{suffix}_unchecked", new IExpression[] { leftExpr, rightExpr });
            return sub.Canonicalize().ToDisplayString().Trim();
        }

        private static string? GetNumericSuffix(ITypeSymbol? type)
        {
            if (type is null)
            {
                return null;
            }

            return type.SpecialType switch
            {
                SpecialType.System_Int32 => "i32",
                SpecialType.System_UInt32 => "u32",
                SpecialType.System_Int64 => "i64",
                SpecialType.System_UInt64 => "u64",
                SpecialType.System_Single => "f32",
                SpecialType.System_Double => "f64",
                SpecialType.System_Decimal => "dec",
                SpecialType.System_IntPtr => "nint",
                SpecialType.System_UIntPtr => "nuint",
                _ => null
            };
        }

        private static IOperation? ResolveInvocationArgument(
            ExpressionSyntax argumentSyntax,
            IOperation argumentOperation,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            // Try to resolve simple local temporaries immediately before the call.
            // Conservative: only handles direct assignments in the same block.
            const int MaxResolutionDepth = 4;
            var currentSyntax = argumentSyntax;

            for (int depth = 0; depth < MaxResolutionDepth; depth++)
            {
                ct.ThrowIfCancellationRequested();

                if (currentSyntax is IdentifierNameSyntax id)
                {
                    var resolvedSyntax = TryResolveIdentifierValueSyntax(id, semanticModel, ct);
                    if (resolvedSyntax is null)
                    {
                        break;
                    }

                    currentSyntax = resolvedSyntax;
                    continue;
                }

                break;
            }

            var resolvedOp = semanticModel.GetOperation(currentSyntax, ct);
            return resolvedOp ?? argumentOperation;
        }

        private static ExpressionSyntax? TryResolveIdentifierValueSyntax(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            var statement = identifier.FirstAncestorOrSelf<StatementSyntax>();
            if (statement is null)
            {
                return null;
            }

            if (statement.Parent is not BlockSyntax block)
            {
                return null;
            }

            var statementIndex = block.Statements.IndexOf(statement);
            if (statementIndex < 0)
            {
                return null;
            }

            var name = identifier.Identifier.ValueText;
            for (int i = statementIndex - 1; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                var prior = block.Statements[i];

                if (prior is LocalDeclarationStatementSyntax localDecl)
                {
                    foreach (var variable in localDecl.Declaration.Variables)
                    {
                        if (variable.Identifier.ValueText == name && variable.Initializer?.Value is ExpressionSyntax init)
                        {
                            return init;
                        }
                    }
                }
                else if (prior is ExpressionStatementSyntax exprStmt && exprStmt.Expression is AssignmentExpressionSyntax assign)
                {
                    if (assign.Left is IdentifierNameSyntax leftId && leftId.Identifier.ValueText == name)
                    {
                        return assign.Right;
                    }
                }
            }

            return null;
        }


        private static bool IsStaticInitializationWriteSite(SyntaxNode node, SemanticModel semanticModel, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var ctor = node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();
            if (ctor != null && ctor.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                return true;
            }

            // Static field initializer: static int x = ...;
            var fieldDecl = node.FirstAncestorOrSelf<FieldDeclarationSyntax>();
            if (fieldDecl != null && fieldDecl.Modifiers.Any(SyntaxKind.StaticKeyword) && node.FirstAncestorOrSelf<EqualsValueClauseSyntax>() != null)
            {
                return true;
            }

            // Static property initializer: static int P { get; } = ...;
            var propDecl = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
            if (propDecl != null && propDecl.Modifiers.Any(SyntaxKind.StaticKeyword) && propDecl.Initializer != null)
            {
                return true;
            }

            // Fallback: if Roslyn reports we're inside a static constructor symbol, treat as type init.
            var enclosing = semanticModel.GetEnclosingSymbol(node.SpanStart, ct);
            if (enclosing is IMethodSymbol { MethodKind: MethodKind.StaticConstructor })
            {
                return true;
            }

            return false;
        }


        private static string? TryExtractModuloDividend(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return null;
            var match = Regex.Match(originalText, @"(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*%");
            if (!match.Success) return null;
            return match.Groups["id"].Value;
        }

        private static bool IsInBranch(IOperation current, IOperation? branch)
        {
            if (branch == null) return false;
            var node = current;
            while (node != null)
            {
                if (node == branch) return true;
                node = node.Parent!;
                if (node is IConditionalOperation) break;
            }
            return false;
        }

        private static string? TryExtractDivisorFromArgs(IReadOnlyList<string>? bugArgs)
        {
            if (bugArgs == null || bugArgs.Count == 0)
            {
                return null;
            }

            // Division/modulo rules place the divisor as the last argument.
            var candidate = bugArgs[bugArgs.Count - 1];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            var match = Regex.Match(candidate, @"^(?<id>[A-Za-z_][A-Za-z0-9_]*)$");
            if (!match.Success)
            {
                return null;
            }

            return match.Groups["id"].Value;
        }

        private static string? TryExtractDivisorIdentifier(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return null;

            // Heuristic: capture a simple identifier immediately after '/'.
            // Examples: "T.One / value" -> value, "a/b" -> b
            var match = Regex.Match(originalText, @"/\s*(?<id>[A-Za-z_][A-Za-z0-9_]*)");
            if (!match.Success) return null;
            return match.Groups["id"].Value;
        }

        private CSharpMathBugSeverity ParseSeverity(string s) => s switch {
            "Error" => CSharpMathBugSeverity.Error,
            "Warning" => CSharpMathBugSeverity.Warning,
            "Info" => CSharpMathBugSeverity.Info,
            _ => CSharpMathBugSeverity.Warning
        };

        private static bool IsProtectedByFacts(
            string bugId,
            IReadOnlyList<CSharpGuardFact> facts,
            HashSet<string>? operationSubjectKeys,
            string? legacySubjectName)
        {
            if (facts.Count == 0) return false;

            bool MatchesAnySubject(CSharpGuardFact fact)
            {
                if (operationSubjectKeys != null && operationSubjectKeys.Count > 0)
                {
                    return operationSubjectKeys.Contains(fact.Subject);
                }

                return legacySubjectName != null && string.Equals(fact.Subject, legacySubjectName, StringComparison.Ordinal);
            }

            return bugId switch
            {
                "CSMATH023" or "CSSEC012" => facts.Any(f => f.Kind == CSharpGuardKind.NonZero && MatchesAnySubject(f)),
                "CSSEC011" => facts.Any(f => f.Kind == CSharpGuardKind.NonNegative && MatchesAnySubject(f)) || facts.Any(f => f.Kind == CSharpGuardKind.SafeModuloResult && MatchesAnySubject(f)),
                // CSSEC021 (unsigned underflow) must NOT be suppressed merely because the expression's type is unsigned.
                // The result of unsigned subtraction is always non-negative by type, even when it wraps.
                // Only suppress when NonNegative is proven by a semantic guard (e.g. a >= b) or by a recognized idiom.
                "CSSEC021" => facts.Any(f => f.Kind == CSharpGuardKind.NonNegative &&
                                            !f.Evidence.Contains("Type is unsigned", StringComparison.OrdinalIgnoreCase) &&
                                            MatchesAnySubject(f))
                             || facts.Any(f => f.Kind == CSharpGuardKind.InRangeIdiom && MatchesAnySubject(f))
                             || facts.Any(f => f.Kind == CSharpGuardKind.SafeUnsignedWrap && MatchesAnySubject(f)),
                "CSSEC024" => facts.Any(f => f.Kind == CSharpGuardKind.InRangeIdiom && MatchesAnySubject(f)),
                "CSSEC025" => facts.Any(f => f.Kind == CSharpGuardKind.InInt32Range && MatchesAnySubject(f)),
                "CSSEC008" => facts.Any(f => f.Kind == CSharpGuardKind.IsConstantTimeEquivalent && MatchesAnySubject(f)),
                "CSSEC026" => facts.Any(f => (f.Kind == CSharpGuardKind.NonNegative || f.Kind == CSharpGuardKind.InInt32Range) && MatchesAnySubject(f)),
                "CSSEC029" => facts.Any(f => (f.Kind == CSharpGuardKind.ValidShiftAmount || f.Kind == CSharpGuardKind.InInt32Range) && MatchesAnySubject(f)),
                "CSSEC030" => facts.Any(f => f.Kind == CSharpGuardKind.NonNegative && MatchesAnySubject(f)),
                "CSMATH015" or "CSMATH018" => facts.Any(f => (f.Kind == CSharpGuardKind.NonNegative || f.Kind == CSharpGuardKind.InInt32Range) && MatchesAnySubject(f)),
                _ => false
            };
        }
    }
}

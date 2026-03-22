// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Operations;
using SymSolvers;
using SymSolvers.Numerics;
using SymSolvers.Stability;
using SymSolvers.Validation;
using SymRules;

using CoreRule = Sym.Core.Rule;

namespace SymSolvers.StableForms;

/// <summary>
/// Opt-in strategy that searches for numerically stable rewrites of a loss expression and ranks candidates by stability.
/// </summary>
public sealed class StableLossSynthesisStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        var config = StableLossSynthesisConfig.FromAdditionalData(context.AdditionalData);
        var original = problem.Canonicalize();
        var symbols = SymbolCollector.CollectSymbolsList(original);

        var rules = BuildStabilityRules();
        var candidates = GenerateCandidates(original, rules, config.CandidateBudget);
        var counterFinder = new CounterexampleFinder(new Float64Model(), config.EquivalenceTolerance);
        var scorer = new StabilityScorer(config.Models, sampleCount: config.SampleBudget);

        var artifacts = new List<(IExpression Expr, StabilityScoreResult Score, string Status, string? Counter)>();

        foreach (var cand in candidates.Take(config.CandidateBudget))
        {
            if (!cand.InternalEquals(original))
            {
                var ce = counterFinder.Find(original, cand, symbols, maxSamples: config.SampleBudget);
                if (ce is not null)
                {
                    artifacts.Add((cand, new StabilityScoreResult(double.PositiveInfinity, Array.Empty<StabilityMetrics>()), "Counterexample", ce.Message));
                    continue;
                }
            }

            var score = scorer.Score(cand);
            artifacts.Add((cand, score, "Validated", null));
        }

        var best = artifacts
            .Where(a => a.Status == "Validated")
            .OrderBy(a => a.Score.Score)
            .ThenBy(a => a.Expr.ToDisplayString(), StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Expr is null)
        {
            var counter = artifacts.FirstOrDefault();
            var msg = counter.Expr is null
                ? "Stable loss synthesis produced no candidates."
                : $"Stable loss synthesis rejected all candidates. Example rejection: {counter.Counter ?? "counterexample"}.";
            return SolveResult.Failure(original, msg);
        }

        var guards = ExtractGuards(best.Expr);
        var resultPayload = new StableLossSynthesisResult
        {
            OriginalExpression = original.ToDisplayString(),
            StableExpression = best.Expr.ToDisplayString(),
            Guards = guards,
            EquivalenceStatus = "Validated",
            Counterexample = best.Counter,
            StabilityMetrics = best.Score.Metrics,
            SuggestedUnitTests = BuildTestHints(original, best.Expr, guards),
            Notes = new List<string> { $"Score={best.Score.Score:F4}", $"Models={string.Join(",", config.Models.Select(m => m.Name))}" }
        };

        var message = $"Stable loss synthesis selected candidate with score {best.Score.Score:F4}. StableExpression={resultPayload.StableExpression} Guards={resultPayload.Guards.Count} Models={resultPayload.StabilityMetrics.Count}";
        return SolveResult.Success(best.Expr, message, trace: context.EnableTracing ? ImmutableList.Create(original, best.Expr) : null);
    }

    private static List<IExpression> GenerateCandidates(IExpression start, ImmutableList<CoreRule> stabilityRules, int budget)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IExpression>();

        void Add(IExpression expr)
        {
            var key = expr.ToDisplayString();
            if (seen.Add(key))
            {
                result.Add(expr);
            }
        }

        Add(start);

        // Single-pass stability rewrite
        var rewritten = Rewriter.RewriteFully(start, stabilityRules, maxInternalIterations: 4);
        Add(rewritten.RewrittenExpression.Canonicalize());

        // Attempt stabilization + simplification chain
        var simplifier = new SymSolvers.EGraphSolverStrategy();
        var simpResult = simplifier.Solve(rewritten.RewrittenExpression, new SolveContext());
        if (simpResult.ResultExpression is not null)
        {
            Add(simpResult.ResultExpression.Canonicalize());

            // Re-apply stability rewrites after algebraic simplification, because simplification can
            // transform the expression into a form where stability patterns (e.g., expm1/log1p)
            // become detectable.
            var rewrittenAfterSimp = Rewriter.RewriteFully(simpResult.ResultExpression, stabilityRules, maxInternalIterations: 4);
            Add(rewrittenAfterSimp.RewrittenExpression.Canonicalize());
        }

        // Beam search removed in favor of deterministic rewrite + e-graph.

        return result;
    }

    private static ImmutableList<CoreRule> BuildStabilityRules()
    {
        return StabilityRuleLibrary.Rules;
    }

    private static List<string> ExtractGuards(IExpression expr)
    {
        var guards = new List<string>();
        void Walk(IExpression e)
        {
            switch (e)
            {
                case Function fn when string.Equals(fn.Name, "log", StringComparison.OrdinalIgnoreCase):
                    if (fn.Arguments.Count > 0)
                    {
                        guards.Add($"log arg > 0: {fn.Arguments[0].ToDisplayString()} > 0");
                    }
                    break;
                case Divide div:
                    guards.Add($"denominator != 0: {div.Denominator.ToDisplayString()} != 0");
                    break;
            }
            if (e is Operation op)
            {
                foreach (var a in op.Arguments) Walk(a);
            }
        }
        Walk(expr);
        return guards;
    }

    private static IReadOnlyList<string> BuildTestHints(IExpression original, IExpression candidate, IReadOnlyList<string> guards)
    {
        var hints = new List<string>
        {
            $"Verify equivalence: {original.ToDisplayString()} vs {candidate.ToDisplayString()} under guards.",
            $"Check NaN/Inf reduction with FP16/BF16 stress inputs."
        };
        if (guards.Count > 0)
        {
            hints.Add("Ensure guards enforced: " + string.Join("; ", guards));
        }
        return hints;
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using SymSolvers;
using SymSolvers.Numerics;

namespace SymSolvers.Stability;

/// <summary>
/// Evaluates expressions under multiple precision models and produces stability metrics + aggregate score.
/// </summary>
public sealed class StabilityScorer
{
    private readonly IReadOnlyList<IFloatingPointModel> _models;
    private readonly int _sampleCount;
    private readonly int _seed;

    public StabilityScorer(IReadOnlyList<IFloatingPointModel> models, int sampleCount = 16, int seed = 7)
    {
        _models = models;
        _sampleCount = Math.Max(4, sampleCount);
        _seed = seed;
    }

    public StabilityScoreResult Score(IExpression expression)
    {
        var symbols = SymbolCollector.CollectSymbolsList(expression);
        var rng = new Random(_seed);
        var assignments = BuildSamples(symbols, _sampleCount, rng).ToList();

        var reports = new List<StabilityMetrics>();
        double aggregatePenalty = 0;

        var fp64 = _models.OfType<Float64Model>().FirstOrDefault() ?? new Float64Model();
        var referenceValues = EvaluateAll(expression, assignments, fp64);

        foreach (var model in _models)
        {
            var values = EvaluateAll(expression, assignments, model);
            var metrics = ComputeMetrics(model.Name, referenceValues, values);
            reports.Add(metrics);

            // Simple aggregate: weight NaN/Inf heavily, then abs error.
            aggregatePenalty += metrics.NaNCount * 10;
            aggregatePenalty += metrics.InfCount * 5;
            aggregatePenalty += metrics.MaxAbsErrorVsFp64;
            aggregatePenalty += metrics.MaxRelErrorVsFp64;
        }

        var risk = ExpressionRiskAnalyzer.Score(expression);
        var finalScore = aggregatePenalty - risk.Net;

        return new StabilityScoreResult(finalScore, reports);
    }

    private static IReadOnlyList<double?> EvaluateAll(IExpression expression, IReadOnlyList<Dictionary<string, double>> assignments, IFloatingPointModel model)
    {
        var results = new List<double?>(assignments.Count);
        foreach (var assn in assignments)
        {
            if (PrecisionExpressionEvaluator.TryEvaluate(expression, assn, model, out var val, out _))
            {
                results.Add(val);
            }
            else
            {
                results.Add(null);
            }
        }
        return results;
    }

    private static StabilityMetrics ComputeMetrics(string model, IReadOnlyList<double?> reference, IReadOnlyList<double?> candidate)
    {
        int nan = 0, inf = 0, underflow = 0;
        var absErrors = new List<double>();
        var relErrors = new List<double>();

        for (int i = 0; i < candidate.Count; i++)
        {
            var refVal = reference[i];
            var val = candidate[i];

            // If the reference is undefined (NaN/Inf) skip penalties for this sample.
            if (refVal.HasValue && (double.IsNaN(refVal.Value) || double.IsInfinity(refVal.Value)))
            {
                continue;
            }

            if (val is null)
            {
                if (refVal.HasValue) nan++;
                continue;
            }
            if (double.IsNaN(val.Value))
            {
                if (refVal.HasValue) nan++;
                continue;
            }
            if (double.IsInfinity(val.Value))
            {
                if (refVal.HasValue) inf++;
                continue;
            }

            if (refVal.HasValue)
            {
                var absErr = Math.Abs(val.Value - refVal.Value);
                absErrors.Add(absErr);

                var denom = Math.Abs(refVal.Value) < 1e-12 ? 1e-12 : Math.Abs(refVal.Value);
                relErrors.Add(absErr / denom);

                if (Math.Abs(refVal.Value) > 0 && Math.Abs(val.Value) == 0)
                {
                    underflow++;
                }
            }
        }

        double Max(IReadOnlyList<double> list) => list.Count == 0 ? 0 : list.Max();
        double Pct(IReadOnlyList<double> list, double pct)
        {
            if (list.Count == 0) return 0;
            var ordered = list.OrderBy(x => x).ToList();
            var idx = (int)Math.Clamp(Math.Round((pct / 100.0) * (ordered.Count - 1)), 0, ordered.Count - 1);
            return ordered[idx];
        }

        return new StabilityMetrics(
            model,
            candidate.Count,
            nan,
            inf,
            underflow,
            Max(absErrors),
            Pct(absErrors, 95),
            Max(relErrors),
            Pct(relErrors, 95));
    }

    private static IEnumerable<Dictionary<string, double>> BuildSamples(IReadOnlyList<Symbol> symbols, int count, Random rng)
    {
        if (symbols.Count == 0)
        {
            yield return new Dictionary<string, double>();
            yield break;
        }

        var core = new[] { -10d, -3d, -1d, -0.5d, -0.1d, 0d, 0.1d, 0.5d, 1d, 3d, 10d };
        for (int i = 0; i < count; i++)
        {
            var dict = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var sym in symbols)
            {
                var v = core[(i + sym.Name.Length) % core.Length];
                v += rng.NextDouble() * 0.01; // deterministic offset for variety
                dict[sym.Name] = v;
            }
            yield return dict;
        }
    }

}

public sealed record StabilityScoreResult(double Score, IReadOnlyList<StabilityMetrics> Metrics);

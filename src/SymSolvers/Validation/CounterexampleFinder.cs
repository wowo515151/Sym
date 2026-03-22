using System;
using System.Collections.Generic;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using SymSolvers.Numerics;

namespace SymSolvers.Validation;

public sealed class Counterexample
{
    public Dictionary<string, double> Assignments { get; }
    public string Message { get; }

    public Counterexample(Dictionary<string, double> assignments, string message)
    {
        Assignments = assignments;
        Message = message;
    }
}

/// <summary>
/// Generates counterexamples for inequivalent expressions using targeted numeric sampling.
/// </summary>
public sealed class CounterexampleFinder
{
    private readonly IFloatingPointModel _model;
    private readonly double _tolerance;
    private readonly int _seed;

    public CounterexampleFinder(IFloatingPointModel model, double tolerance = 1e-6, int seed = 17)
    {
        _model = model;
        _tolerance = tolerance;
        _seed = seed;
    }

    public Counterexample? Find(IExpression left, IExpression right, IReadOnlyList<Symbol> symbols, int maxSamples = 64)
    {
        if (symbols.Count == 0)
        {
            if (TryDiff(left, right, new Dictionary<string, double>()))
            {
                return new Counterexample(new Dictionary<string, double>(), "Mismatch on constant evaluation.");
            }
            return null;
        }

        var rng = new Random(_seed);
        var probeValues = BuildProbeValues();

        int attempts = 0;
        foreach (var sample in EnumerateSamples(symbols, probeValues, rng))
        {
            attempts++;
            if (attempts > maxSamples) break;

            if (TryDiff(left, right, sample))
            {
                return new Counterexample(new Dictionary<string, double>(sample, StringComparer.Ordinal), $"Mismatch at sample {Format(sample)}");
            }
        }

        return null;
    }

    private bool TryDiff(IExpression left, IExpression right, IReadOnlyDictionary<string, double> sample)
    {
        if (!PrecisionExpressionEvaluator.TryEvaluate(left, sample, _model, out var lv, out _)) return true;
        if (!PrecisionExpressionEvaluator.TryEvaluate(right, sample, _model, out var rv, out _)) return true;

        if (double.IsNaN(lv) != double.IsNaN(rv) || double.IsInfinity(lv) != double.IsInfinity(rv))
        {
            return true;
        }

        return Math.Abs(lv - rv) > _tolerance;
    }

    private static IEnumerable<Dictionary<string, double>> EnumerateSamples(IReadOnlyList<Symbol> symbols, IReadOnlyList<double> probes, Random rng)
    {
        foreach (var p in probes)
        {
            var dict = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var s in symbols)
            {
                dict[s.Name] = p;
            }
            yield return dict;
        }

        for (int i = 0; i < probes.Count; i++)
        {
            var dict = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var s in symbols)
            {
                var jitter = (rng.NextDouble() - 0.5) * 0.1;
                dict[s.Name] = probes[i] + jitter;
            }
            yield return dict;
        }
    }

    private static List<double> BuildProbeValues()
    {
        return new List<double> { -10, -3, -1, -0.5, -0.1, 0, 0.1, 0.5, 1, 3, 10 };
    }

    private static string Format(IReadOnlyDictionary<string, double> sample)
    {
        return string.Join(", ", sample.Select(kv => $"{kv.Key}={kv.Value:F4}"));
    }
}

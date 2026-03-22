// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;

namespace SymSolvers.Validation;

/// <summary>
/// Attempts to shrink counterexamples into simpler assignments for readability.
/// </summary>
public static class CounterexampleMinimizer
{
    public static Dictionary<string, double> Minimize(Dictionary<string, double> original, Func<IReadOnlyDictionary<string, double>, bool> stillFails)
    {
        var current = new Dictionary<string, double>(original, StringComparer.Ordinal);

        foreach (var key in original.Keys.ToList())
        {
            var value = current[key];
            foreach (var cand in EnumerateCandidates(value))
            {
                if (Math.Abs(cand - value) < 1e-12) continue;
                current[key] = cand;
                if (stillFails(current))
                {
                    value = cand;
                    break;
                }
            }

            current[key] = value;
        }

        return current;
    }

    private static IEnumerable<double> EnumerateCandidates(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            yield return value;
            yield break;
        }

        var sign = Math.Sign(value);
        if (sign == 0) sign = 1;

        yield return 0d;
        yield return sign * 1d;
        yield return sign * 0.5d;
        yield return sign * 2d;
        yield return sign * 0.25d;
        yield return sign * 4d;

        var rounded = Math.Round(value);
        yield return rounded;
        yield return Math.Round(value, 1);
        yield return Math.Round(value, 2);

        // As a final fallback, return the original.
        yield return value;
    }
}

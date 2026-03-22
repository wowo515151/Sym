using System;
using System.Collections.Generic;
using System.Linq;

namespace SymSolvers.Numerics;

public static class IntegerSearch
{
    public static IEnumerable<Dictionary<string, T>> GenerateCombinations<T>(
        IReadOnlyList<string> variables,
        Func<string, IEnumerable<T>> valueProvider,
        int maxCombinations = 100000)
    {
        if (variables.Count == 0)
        {
            yield return new Dictionary<string, T>(StringComparer.Ordinal);
            yield break;
        }

        var count = 0;
        foreach (var combo in GenerateRecursive(variables, 0, valueProvider))
        {
            if (++count > maxCombinations) yield break;
            yield return combo;
        }
    }

    private static IEnumerable<Dictionary<string, T>> GenerateRecursive<T>(
        IReadOnlyList<string> variables,
        int index,
        Func<string, IEnumerable<T>> valueProvider)
    {
        if (index == variables.Count)
        {
            yield return new Dictionary<string, T>(StringComparer.Ordinal);
            yield break;
        }

        var variable = variables[index];
        var values = valueProvider(variable);

        foreach (var val in values)
        {
            foreach (var rest in GenerateRecursive(variables, index + 1, valueProvider))
            {
                rest[variable] = val;
                yield return rest;
            }
        }
    }
}

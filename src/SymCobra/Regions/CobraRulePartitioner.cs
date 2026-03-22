// Copyright Warren Harding 2026
using System.Collections.Generic;
using Sym.Core;

namespace SymCobra.Regions;

internal static class CobraRulePartitioner
{
    internal static IReadOnlyDictionary<int, IReadOnlyList<Rule>> BuildCompatibilityRulesByClass(
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        CobraDirectMatchPlan directPlan)
    {
        var result = new Dictionary<int, IReadOnlyList<Rule>>();

        foreach (var (classId, classRules) in rulesByClass)
        {
            var compatibilityRules = new List<Rule>();
            bool hasDirectRules = directPlan.PairsByClass.TryGetValue(classId, out var directRulesByClass);
            IReadOnlySet<Rule>? handledRulesByClass = null;
            bool hasHandledRules = directPlan.ExhaustivelyHandledRulesByClass is not null &&
                                   directPlan.ExhaustivelyHandledRulesByClass.TryGetValue(classId, out handledRulesByClass);
            foreach (var rule in classRules)
            {
                if (hasHandledRules && handledRulesByClass!.Contains(rule))
                {
                    continue;
                }

                if (!hasDirectRules ||
                    !directRulesByClass!.TryGetValue(rule, out var directPairs) ||
                    directPairs.Count == 0)
                {
                    compatibilityRules.Add(rule);
                }
            }

            if (compatibilityRules.Count > 0)
            {
                result[classId] = compatibilityRules;
            }
        }

        return result;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Sym.Core;
using Sym.Core.EGraph;
using SymCobra.Regions;

namespace SymCobra.Core;

public static class CobraMatcher
{
    internal static IReadOnlyDictionary<int, IReadOnlyList<Rule>> FilterRulesByPositiveCandidates(
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass)
    {
        var result = new Dictionary<int, IReadOnlyList<Rule>>();

        foreach (var (classId, rules) in rulesByClass)
        {
            if (!eligibleNodesByClass.TryGetValue(classId, out var eligibleRules) || eligibleRules.Count == 0)
            {
                continue;
            }

            var filteredRules = new List<Rule>();
            foreach (var rule in rules)
            {
                if (eligibleRules.TryGetValue(rule, out var eligibleNodes) && eligibleNodes.Count > 0)
                {
                    filteredRules.Add(rule);
                }
            }

            if (filteredRules.Count > 0)
            {
                result[classId] = filteredRules;
            }
        }

        return result;
    }

    public static List<Match> FindMatches(
        CobraGraphState graphState,
        EGraph legacyGraph, // temporary during migration
        IEnumerable<Rule> rules,
        IReadOnlyList<int> frontierClassIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass,
        MatchHistory history,
        int maxConcurrency,
        CancellationToken ct,
        CobraDiagnostics diagnostics,
        out List<Match> directMatches,
        out List<CobraDirectRuleApplication> simpleDirectApplications,
        CobraDirectMatchPlan directMatchPlan)
    {
        return FindMatches(
            graphState,
            legacyGraph,
            rules,
            frontierClassIds,
            rulesByClass,
            eligibleNodesByClass,
            history,
            maxConcurrency,
            ct,
            diagnostics,
            out directMatches,
            out simpleDirectApplications,
            directMatchPlan,
            remainingRulesByClass: null);
    }

    public static List<Match> FindMatches(
        CobraGraphState graphState,
        EGraph legacyGraph, // temporary during migration
        IEnumerable<Rule> rules,
        IReadOnlyList<int> frontierClassIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass,
        MatchHistory history,
        int maxConcurrency,
        CancellationToken ct,
        CobraDiagnostics diagnostics,
        out List<Match> directMatches,
        out List<CobraDirectRuleApplication> simpleDirectApplications,
        CobraDirectMatchPlan directMatchPlan,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>>? remainingRulesByClass)
    {
        // 1. Fast Path (Direct Matches via CUDA)
        var directExecution = CobraMatchFastPath.FindDirectMatches(legacyGraph, directMatchPlan);
        directMatches = directExecution.ManagedMatches.ToList();
        simpleDirectApplications = directExecution.SimpleApplications.ToList();
        diagnostics?.RecordPhaseSource(CobraPhase.Match, $"DirectExecution:{directMatchPlan.Source}", directMatchPlan.Source == CobraDirectMatchSource.Cuda);
        
        // 2. Compatibility Path (Managed CPU Matcher for complex/nested patterns)
        var activeRemainingRulesByClass = remainingRulesByClass ?? ExcludeDirectRules(rulesByClass, directMatchPlan);
        bool requiresCompatibilityPath = RequiresCompatibilityPath(activeRemainingRulesByClass, eligibleNodesByClass);

        if (requiresCompatibilityPath)
        {
            diagnostics?.RecordFallback(CobraPhase.Match, Telemetry.CobraFallbackReason.MatchCompatibilityPath);
            diagnostics?.RecordPhaseSource(CobraPhase.Match, "CompatibilityExecution:CpuMatcher", isGpuBacked: false);
        }
        else
        {
            diagnostics?.RecordPhaseSource(
                CobraPhase.Match,
                directMatchPlan.Source == CobraDirectMatchSource.Cuda ? "MatchOutcome:GpuDirectOnly" : "MatchOutcome:CpuDirectOnly",
                isGpuBacked: directMatchPlan.Source == CobraDirectMatchSource.Cuda);
        }

        var rawMatches = requiresCompatibilityPath
            ? CobraMatchCompatibilityPath.FindMatches(
                graphState,
                legacyGraph,
                rules,
                frontierClassIds,
                activeRemainingRulesByClass,
                eligibleNodesByClass,
                history,
                maxConcurrency,
                ct)
            : new List<Match>();

        if (requiresCompatibilityPath)
        {
            diagnostics?.RecordPhaseSource(CobraPhase.Match, "MatchOutcome:HybridFallback", isGpuBacked: false);
        }

        rawMatches.AddRange(directMatches);
        return rawMatches;
    }

    internal static bool RequiresCompatibilityPath(
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> remainingRulesByClass,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass)
    {
        foreach (var (classId, rules) in remainingRulesByClass)
        {
            if (rules.Count == 0)
            {
                continue;
            }

            if (!eligibleNodesByClass.TryGetValue(classId, out var eligibleRules) || eligibleRules.Count == 0)
            {
                continue;
            }

            foreach (var rule in rules)
            {
                if (eligibleRules.TryGetValue(rule, out var eligibleNodes) && eligibleNodes.Count > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static IReadOnlyDictionary<int, IReadOnlyList<Rule>> ExcludeDirectRules(
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> allRules,
        CobraDirectMatchPlan directPlan)
    {
        var result = new Dictionary<int, IReadOnlyList<Rule>>();
        foreach (var kvp in allRules)
        {
            int classId = kvp.Key;
            var classRules = kvp.Value;
            IReadOnlySet<Rule>? handledRulesForClass = null;
            bool hasHandledRules = directPlan.ExhaustivelyHandledRulesByClass is not null &&
                                   directPlan.ExhaustivelyHandledRulesByClass.TryGetValue(classId, out handledRulesForClass);
            
            if (!directPlan.PairsByClass.TryGetValue(classId, out var directRulesMap))
            {
                result[classId] = hasHandledRules
                    ? classRules.Where(rule => !handledRulesForClass!.Contains(rule)).ToList()
                    : classRules;
                continue;
            }

            var filtered = new List<Rule>();
            foreach (var rule in classRules)
            {
                if (hasHandledRules && handledRulesForClass!.Contains(rule))
                {
                    continue;
                }

                if (!directRulesMap.TryGetValue(rule, out var pairs) || pairs.Count == 0)
                {
                    filtered.Add(rule);
                }
            }
            result[classId] = filtered;
        }
        return result;
    }
}

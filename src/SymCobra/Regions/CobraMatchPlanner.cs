using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraMatchPlanner
{
    public static CobraMatchPriorityResult Prioritize(IEnumerable<Match> matches, CobraRegionPlan? plan)
    {
        var matchList = matches.ToList();
        if (plan is null || plan.Regions.Count == 0)
        {
            return new CobraMatchPriorityResult(matchList, CobraMatchPrioritySource.CpuHeuristic);
        }

        var hotFlags = matchList.Select(m => plan.HotClassIds.Contains(m.RootClassId) ? 1 : 0).ToArray();
        var boundaryFlags = matchList.Select(m => plan.BoundaryClassIds.Contains(m.RootClassId) ? 1 : 0).ToArray();
        var suppressedFlags = matchList.Select(m => plan.SuppressedClassIds.Contains(m.RootClassId) ? 1 : 0).ToArray();
        var ruleArities = matchList.Select(m => m.Rule.Pattern is Sym.Core.Operation op ? op.Arguments.Count : 0).ToArray();
        var directFlags = matchList.Select(m => m.Rule.Pattern is Sym.Core.Operation op && op.Arguments.All(arg => arg is not Sym.Core.Operation) ? 1 : 0).ToArray();
        var nestedFlags = matchList.Select(m => m.Rule.Pattern is Sym.Core.Operation op && op.Arguments.Any(static arg => arg is Sym.Core.Operation) ? 1 : 0).ToArray();

        if (CobraCudaNative.TryScoreMatchPriorityV3(hotFlags, boundaryFlags, suppressedFlags, ruleArities, directFlags, nestedFlags, out var scores))
        {
            var prioritized = matchList
                .Select((match, index) => (match, score: scores[index], index))
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.match.Rule.Name ?? string.Empty)
                .ThenBy(x => x.match.RootClassId)
                .ThenBy(x => x.index)
                .Select(x => x.match)
                .ToList();

            return new CobraMatchPriorityResult(prioritized, CobraMatchPrioritySource.Cuda);
        }

        if (CobraCudaNative.TryScoreMatchPriorityV2(hotFlags, boundaryFlags, suppressedFlags, ruleArities, directFlags, out scores))
        {
            var prioritized = matchList
                .Select((match, index) => (match, score: scores[index], index))
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.match.Rule.Name ?? string.Empty)
                .ThenBy(x => x.match.RootClassId)
                .ThenBy(x => x.index)
                .Select(x => x.match)
                .ToList();

            return new CobraMatchPriorityResult(prioritized, CobraMatchPrioritySource.Cuda);
        }

        var fallback = matchList
            .OrderByDescending(m => plan.HotClassIds.Contains(m.RootClassId))
            .ThenByDescending(m => plan.BoundaryClassIds.Contains(m.RootClassId))
            .ThenByDescending(m => m.Rule.Pattern is Sym.Core.Operation op && op.Arguments.Any(static arg => arg is Sym.Core.Operation))
            .ThenByDescending(m => m.Rule.Pattern is Sym.Core.Operation op ? op.Arguments.Count : 0)
            .ThenBy(m => plan.SuppressedClassIds.Contains(m.RootClassId))
            .ThenBy(m => m.Rule.Name ?? string.Empty)
            .ThenBy(m => m.RootClassId)
            .ToList();

        return new CobraMatchPriorityResult(fallback, CobraMatchPrioritySource.CpuHeuristic);
    }
}

using System.Collections.Generic;
using System.Threading;
using Sym.Core;
using Sym.Core.EGraph;

namespace SymCobra.Core;

public static class CobraMatchCompatibilityPath
{
    public static List<Match> FindMatches(
        CobraGraphState graphState,
        EGraph legacyGraph,
        IEnumerable<Rule> rules,
        IReadOnlyList<int> frontierClassIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass,
        MatchHistory history,
        int maxConcurrency,
        CancellationToken ct)
    {
        return CobraGraphMatcher.FindMatches(
            graphState,
            rules,
            frontierClassIds,
            rulesByClass,
            eligibleNodesByClass,
            history,
            ct);
    }
}

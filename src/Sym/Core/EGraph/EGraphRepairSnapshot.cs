using System.Collections.Generic;

namespace Sym.Core.EGraph
{
    public sealed record EGraphRepairSnapshot(
        int[] Parents,
        int[] ChildStarts,
        int[] ChildCounts,
        int[] ChildIds,
        IReadOnlyList<EGraphRepairCandidate> Candidates);
}

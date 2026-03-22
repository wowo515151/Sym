using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Sym.Core.EGraph;

namespace SymCobra.Core;

public static class CobraRebuildEngine
{
    public static void Rebuild(
        CobraGraphState graphState,
        EGraph legacyGraph,
        IReadOnlyList<int> orderedClassIds,
        IReadOnlyList<IReadOnlyList<EGraphRepairCandidate>> repairGroups,
        CancellationToken ct)
    {
        graphState.Rebuild(orderedClassIds, repairGroups, ct);
        graphState.SyncLegacyGraphFromCobra(legacyGraph, forceFull: true);
        legacyGraph.Rebuild(ct);
    }
}

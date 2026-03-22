using System;
using System.Collections.Generic;
using System.Threading;
using Sym.Core;
using Sym.Core.EGraph;

namespace SymCobra.Core;

public static class CobraExtractor
{
    public static IExpression ExtractBestEffort(
        CobraGraphState graphState,
        int rootId,
        Func<ENode, bool>? filter,
        Func<ENode, long> costFunction,
        IReadOnlyList<int> orderedClassIds,
        IReadOnlyDictionary<int, IReadOnlyList<int>> orderedNodesByClass,
        CancellationToken? softCt,
        CancellationToken hardCt,
        CobraDiagnostics diagnostics)
    {
        return CobraGraphExtractor.ExtractBestEffort(
            graphState,
            rootId,
            filter,
            costFunction,
            orderedClassIds,
            orderedNodesByClass,
            softCt ?? default,
            hardCt);
    }

    public static IExpression ExtractBest(
        CobraGraphState graphState,
        int classId,
        CancellationToken ct,
        CobraDiagnostics diagnostics)
    {
        return CobraGraphExtractor.ExtractBest(graphState, classId, ct: ct);
    }
}

// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using SymCobra.Regions;

namespace SymCobra.Core;

public static class CobraMatchFastPath
{
    public static CobraDirectMatchExecutionResult FindDirectMatches(EGraph legacyGraph, CobraDirectMatchPlan directMatchPlan)
    {
        return CobraDirectMatchMaterializer.MaterializeForExecution(legacyGraph, directMatchPlan);
    }
}
